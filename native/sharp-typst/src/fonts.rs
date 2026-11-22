//! The host font index: scanned once per process, persisted between processes.
//!
//! A world that the host asked to see the machine's fonts — `AddSystemFonts` or
//! `AddFontDirectory` — used to enumerate and parse every face on every `Build()`. A service that
//! compiles continuously paid that scan per compilation. The metadata scan now happens once per
//! process: the result is a [`FontIndex`] held behind an `Arc` and handed to every world that asks
//! for the same set of sources, and the faces themselves are parsed on first use rather than at
//! scan time.
//!
//! Across processes the index is cached on disk as `(path, family, fingerprint)` per face. The
//! fingerprint is the file's length and modification time, not a hash of its contents: a hash costs
//! one full read of every font file, which is the cost the cache exists to avoid, while a stat is
//! one syscall per file and catches a replacement because writing a file changes its mtime. The
//! trade is stated where it matters: a file replaced while keeping both its length and its mtime is
//! served from the stale entry until a [`IndexOptions::refresh`] build or a new process.
//!
//! A world can always refuse the index — [`IndexOptions::refresh`] scans again and replaces the
//! process entry — which is how a font installed after the process started becomes visible.

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::UNIX_EPOCH;

use serde::{Deserialize, Serialize};
use typst::foundations::Bytes;
use typst::text::{Font, FontBook, FontInfo};
use typst::utils::LazyHash;

/// On-disk index revision. A file carrying another revision is ignored, not migrated.
const INDEX_FORMAT: u32 = 1;

/// How deep the directory walk follows subdirectories. A font tree is two or three levels; the cap
/// only stops a symlink loop from hanging the scan.
const MAX_DEPTH: usize = 16;

/// Number of metadata scans this process has performed, reused hits excluded.
static SCANS: AtomicUsize = AtomicUsize::new(0);

/// Identity of the next index, so two worlds can compare which index they hold.
static NEXT_ID: AtomicU64 = AtomicU64::new(1);

/// The set of host font sources a world asked for.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub(crate) struct HostSources {
    /// Whether the operating system's installed fonts are part of the set.
    pub system: bool,
    /// Directories scanned recursively, in the order the host gave them.
    pub directories: Vec<String>,
}

impl HostSources {
    /// True when the world asked for nothing, which is the common case and costs no scan.
    pub fn is_empty(&self) -> bool {
        !self.system && self.directories.is_empty()
    }

    /// The registry key: two worlds with the same sources share one index.
    fn key(&self) -> String {
        format!(
            "system={}|dirs={}",
            self.system,
            self.directories.join("\u{1f}")
        )
    }
}

/// How an index is obtained.
#[derive(Debug, Clone, Copy, Default)]
pub(crate) struct IndexOptions<'a> {
    /// Scan again instead of reusing the process index, and replace it.
    pub refresh: bool,
    /// Where the persistent index lives. `None` disables it.
    pub cache_dir: Option<&'a Path>,
}

/// Number of metadata scans this process has performed.
///
/// A test that only builds worlds cannot see a rescan any other way, and a measurement wants the
/// absolute figure rather than a duration.
#[cfg(test)]
pub(crate) fn scan_count() -> usize {
    SCANS.load(Ordering::Relaxed)
}

/// Forgets every index this process holds.
///
/// Only the tests need it: two tests that share a source key would otherwise see each other's
/// index. Nothing in the library calls it.
#[cfg(test)]
pub(crate) fn clear_registry() {
    registry()
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
        .clear();
}

/// Identifies the bytes an entry was built from, without reading them.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
struct Fingerprint {
    /// File length in bytes.
    size: u64,
    /// Modification time in nanoseconds since the Unix epoch; `0` when the host cannot report one.
    modified: i64,
}

impl Fingerprint {
    fn of(meta: &std::fs::Metadata) -> Self {
        let modified = meta
            .modified()
            .ok()
            .and_then(|time| time.duration_since(UNIX_EPOCH).ok())
            .map(|since| since.as_nanos() as i64)
            .unwrap_or(0);

        Self {
            size: meta.len(),
            modified,
        }
    }
}

/// One face of a font file, as the cache stores it.
#[derive(Debug, Clone, Serialize, Deserialize)]
struct CachedFace {
    /// Index inside the file: non-zero for a collection (`ttc`/`otc`).
    index: u32,
    info: FontInfo,
}

/// One font file and the faces it exposed when it was last read.
#[derive(Debug, Clone, Serialize, Deserialize)]
struct CachedFile {
    path: PathBuf,
    size: u64,
    modified: i64,
    faces: Vec<CachedFace>,
}

impl CachedFile {
    fn matches(&self, fingerprint: Fingerprint) -> bool {
        self.size == fingerprint.size && self.modified == fingerprint.modified
    }
}

/// The persisted index: where it came from, and one entry per font file.
#[derive(Debug, Clone, Serialize, Deserialize)]
struct CachedIndex {
    format: u32,
    compiler: String,
    /// The directories walked. For the system source these are learned from the first scan.
    roots: Vec<PathBuf>,
    files: Vec<CachedFile>,
}

/// The result of a metadata scan, before it becomes a [`FontIndex`].
struct Scanned {
    roots: Vec<PathBuf>,
    files: Vec<CachedFile>,
    parsed: usize,
    reused: usize,
    from_cache: bool,
    cache_path: Option<PathBuf>,
}

impl Scanned {
    fn face_count(&self) -> usize {
        self.files.iter().map(|file| file.faces.len()).sum()
    }

    /// Writes the index back, unless the run read a complete one and found nothing new.
    fn persist(&self) {
        let Some(path) = &self.cache_path else {
            return;
        };

        if (self.from_cache && self.parsed == 0) || self.files.is_empty() {
            return;
        }

        let index = CachedIndex {
            format: INDEX_FORMAT,
            compiler: env!("CARGO_PKG_VERSION").to_string(),
            roots: self.roots.clone(),
            files: self.files.clone(),
        };

        let Some(directory) = path.parent() else {
            return;
        };

        if std::fs::create_dir_all(directory).is_err() {
            return;
        }

        let Ok(json) = serde_json::to_vec(&index) else {
            return;
        };

        // Written beside the target and renamed over it: a process killed mid-write leaves the
        // previous index readable instead of a truncated one.
        let temporary = path.with_extension("json.tmp");

        if std::fs::write(&temporary, json).is_ok() {
            let _ = std::fs::rename(&temporary, path);
        }
    }
}

/// One face of the index, parsed on first use.
struct Slot {
    path: PathBuf,
    index: u32,
    /// Length of the file when the index recorded it, so the byte total needs no syscall.
    size: u64,
    font: OnceLock<Option<Font>>,
}

impl Slot {
    /// Parses the face, once.
    ///
    /// The file is re-read here rather than at scan time, which is what makes a warm index cheap.
    /// If the file changed since the scan, the bytes on disk win: the face still resolves, and its
    /// entry in the book is the one the scan recorded until a refresh rebuilds the index.
    fn load(&self) -> Option<Font> {
        self.font
            .get_or_init(|| {
                let data = std::fs::read(&self.path).ok()?;
                Font::new(Bytes::new(data), self.index)
            })
            .clone()
    }
}

/// The process-wide host font index for one set of sources.
pub(crate) struct FontIndex {
    /// Identity of the index. Read by the tests and by nothing else.
    #[cfg_attr(not(test), allow(dead_code))]
    id: u64,
    slots: Vec<Slot>,
    /// The embedded fonts, then every host face, in the order the slots are indexed.
    book: Arc<LazyHash<FontBook>>,
    /// What the scan that built this index did. Read by the tests that pin the cache.
    #[cfg_attr(not(test), allow(dead_code))]
    parsed: usize,
    #[cfg_attr(not(test), allow(dead_code))]
    reused: usize,
    #[cfg_attr(not(test), allow(dead_code))]
    from_cache: bool,
}

impl FontIndex {
    fn build(scanned: &Scanned, embedded: &LazyHash<FontBook>) -> Self {
        let mut book = (**embedded).clone();
        let mut slots = Vec::with_capacity(scanned.face_count());

        for file in &scanned.files {
            for face in &file.faces {
                book.push(face.info.clone());
                slots.push(Slot {
                    path: file.path.clone(),
                    index: face.index,
                    size: file.size,
                    font: OnceLock::new(),
                });
            }
        }

        Self {
            id: NEXT_ID.fetch_add(1, Ordering::Relaxed),
            slots,
            book: Arc::new(LazyHash::new(book)),
            parsed: scanned.parsed,
            reused: scanned.reused,
            from_cache: scanned.from_cache,
        }
    }

    /// How many host faces the index holds.
    pub fn len(&self) -> usize {
        self.slots.len()
    }

    /// The font book of every world built from this index: the embedded fonts and the host faces.
    pub fn book(&self) -> &Arc<LazyHash<FontBook>> {
        &self.book
    }

    /// The host face at `index`, parsed on first use.
    pub fn font(&self, index: usize) -> Option<Font> {
        self.slots.get(index)?.load()
    }

    /// Bytes of font data the host files carry, each file counted once.
    pub fn file_bytes(&self) -> u64 {
        let mut seen: std::collections::HashSet<&Path> = std::collections::HashSet::new();
        let mut bytes = 0u64;

        for slot in &self.slots {
            if seen.insert(slot.path.as_path()) {
                bytes += slot.size;
            }
        }

        bytes
    }

    /// Identity of this index, so a test can tell a shared index from an equal one.
    #[cfg(test)]
    pub fn id(&self) -> u64 {
        self.id
    }

    /// How many font files this run parsed, and how many it served from the cache.
    #[cfg(test)]
    pub fn counts(&self) -> (usize, usize, bool) {
        (self.parsed, self.reused, self.from_cache)
    }
}

/// The registry of process-wide indices, keyed by source set.
fn registry() -> &'static Mutex<HashMap<String, Arc<FontIndex>>> {
    static REGISTRY: OnceLock<Mutex<HashMap<String, Arc<FontIndex>>>> = OnceLock::new();

    REGISTRY.get_or_init(|| Mutex::new(HashMap::new()))
}

/// The index for this source set, scanning only when the process does not hold one yet.
///
/// The registry lock is held across the scan on purpose: two threads that both miss would
/// otherwise scan the same directories at the same time, which is the cost this exists to remove.
pub(crate) fn shared_index(
    source: &HostSources,
    embedded: &LazyHash<FontBook>,
    options: IndexOptions<'_>,
) -> Arc<FontIndex> {
    let key = source.key();
    let mut indices = registry()
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());

    if !options.refresh
        && let Some(hit) = indices.get(&key)
    {
        return Arc::clone(hit);
    }

    SCANS.fetch_add(1, Ordering::Relaxed);

    let scanned = scan(source, options.cache_dir, options.refresh);

    scanned.persist();

    let index = Arc::new(FontIndex::build(&scanned, embedded));

    indices.insert(key, Arc::clone(&index));

    index
}

/// Scans the sources, reusing the persisted entries whose files are unchanged.
///
/// `refresh` skips reading the persisted index — that is what "scan again" means — but still
/// writes the result, so the next process starts from the scan this one paid for.
fn scan(source: &HostSources, cache_dir: Option<&Path>, refresh: bool) -> Scanned {
    let cache_path = cache_dir.map(|dir| dir.join(cache_name(source)));

    let cached = if refresh {
        None
    } else {
        cache_path.as_deref().and_then(read_cache)
    };

    let mut scanned = match cached {
        Some(index)
            if index.format == INDEX_FORMAT
                && index.compiler == env!("CARGO_PKG_VERSION")
                && !index.roots.is_empty()
                && index.roots.iter().all(|root| root.is_dir()) =>
        {
            scan_cached(&index)
        }
        _ => scan_fresh(source),
    };

    scanned.cache_path = cache_path;

    scanned
}

/// A full scan through `typst-kit`, which is what discovers the sources and their roots.
fn scan_fresh(source: &HostSources) -> Scanned {
    let mut roots = Vec::new();
    let mut files = Vec::new();

    if source.system {
        let mut by_path: BTreeMap<PathBuf, Vec<CachedFace>> = BTreeMap::new();

        for (path, info) in typst_kit::fonts::system() {
            if let Some(parent) = path.path.parent() {
                roots.push(parent.to_path_buf());
            }

            by_path
                .entry(path.path.clone())
                .or_default()
                .push(CachedFace {
                    index: path.index,
                    info,
                });
        }

        roots.sort();
        roots.dedup();

        files.extend(
            by_path
                .into_iter()
                .filter_map(|(path, faces)| cached_file(path, faces)),
        );
    }

    for directory in &source.directories {
        let root = PathBuf::from(directory);

        let mut by_path: BTreeMap<PathBuf, Vec<CachedFace>> = BTreeMap::new();

        for (path, info) in typst_kit::fonts::scan(&root) {
            by_path
                .entry(path.path.clone())
                .or_default()
                .push(CachedFace {
                    index: path.index,
                    info,
                });
        }

        files.extend(
            by_path
                .into_iter()
                .filter_map(|(path, faces)| cached_file(path, faces)),
        );

        roots.push(root);
    }

    // Sorted so a cold scan and a warm one expose the faces in the same order: the fallback chain
    // breaks ties by position, and a process that read the cache must not lay out differently from
    // one that did not.
    files.sort_by(|left, right| left.path.cmp(&right.path));

    let parsed = files.len();

    Scanned {
        roots,
        files,
        parsed,
        reused: 0,
        from_cache: false,
        cache_path: None,
    }
}

/// A scan that walks the recorded roots and only parses the files the cache cannot vouch for.
fn scan_cached(cached: &CachedIndex) -> Scanned {
    let known: HashMap<&Path, &CachedFile> = cached
        .files
        .iter()
        .map(|file| (file.path.as_path(), file))
        .collect();

    let mut files = Vec::new();
    let mut parsed = 0usize;
    let mut reused = 0usize;

    for path in walk(&cached.roots) {
        let Ok(meta) = std::fs::metadata(&path) else {
            continue;
        };

        let fingerprint = Fingerprint::of(&meta);

        if let Some(hit) = known
            .get(path.as_path())
            .filter(|hit| hit.matches(fingerprint))
        {
            files.push((*hit).clone());
            reused += 1;
            continue;
        }

        let Ok(data) = std::fs::read(&path) else {
            continue;
        };

        let faces: Vec<CachedFace> = FontInfo::iter(&data)
            .enumerate()
            .map(|(index, info)| CachedFace {
                index: index as u32,
                info,
            })
            .collect();

        if faces.is_empty() {
            continue;
        }

        parsed += 1;

        files.push(CachedFile {
            path,
            size: fingerprint.size,
            modified: fingerprint.modified,
            faces,
        });
    }

    files.sort_by(|left, right| left.path.cmp(&right.path));

    Scanned {
        roots: cached.roots.clone(),
        files,
        parsed,
        reused,
        from_cache: true,
        cache_path: None,
    }
}

/// Records a file's fingerprint now that its faces are known.
fn cached_file(path: PathBuf, faces: Vec<CachedFace>) -> Option<CachedFile> {
    if faces.is_empty() {
        return None;
    }

    let fingerprint = std::fs::metadata(&path)
        .map(|meta| Fingerprint::of(&meta))
        .ok()?;

    Some(CachedFile {
        path,
        size: fingerprint.size,
        modified: fingerprint.modified,
        faces,
    })
}

/// Every font file under the roots, sorted and deduplicated.
fn walk(roots: &[PathBuf]) -> Vec<PathBuf> {
    let mut files = Vec::new();

    for root in roots {
        collect(root, 0, &mut files);
    }

    files.sort();
    files.dedup();

    files
}

fn collect(directory: &Path, depth: usize, files: &mut Vec<PathBuf>) {
    if depth > MAX_DEPTH {
        return;
    }

    let Ok(entries) = std::fs::read_dir(directory) else {
        return;
    };

    for entry in entries.flatten() {
        let path = entry.path();

        // `metadata` follows a symlink, which is what a font directory installed by a package
        // manager looks like; the depth cap stops a cycle from hanging the scan.
        let Ok(meta) = std::fs::metadata(&path) else {
            continue;
        };

        if meta.is_dir() {
            collect(&path, depth + 1, files);
        } else if meta.is_file() && is_font_file(&path) {
            files.push(path);
        }
    }
}

fn is_font_file(path: &Path) -> bool {
    matches!(
        path.extension()
            .and_then(|extension| extension.to_str())
            .map(|extension| extension.to_ascii_lowercase())
            .as_deref(),
        Some("ttf" | "otf" | "ttc" | "otc")
    )
}

/// The cache file of one source set: a stable FNV-1a of everything that defines it.
fn cache_name(source: &HostSources) -> String {
    let mut hash = 0xcbf2_9ce4_8422_2325u64;

    let seed = format!(
        "{}|{}|{}",
        INDEX_FORMAT,
        env!("CARGO_PKG_VERSION"),
        source.key()
    );

    for byte in seed.as_bytes() {
        hash ^= u64::from(*byte);
        hash = hash.wrapping_mul(0x0000_0100_0000_01b3);
    }

    format!("index-{hash:016x}.json")
}

fn read_cache(path: &Path) -> Option<CachedIndex> {
    let data = std::fs::read(path).ok()?;

    serde_json::from_slice(&data).ok()
}

/// The directory the persistent index lives in when the host did not name one.
///
/// It follows the platform's cache convention; a host that needs it elsewhere sets
/// `fonts.cacheDir`, and `fonts.cache = false` disables the file entirely.
pub(crate) fn default_cache_dir() -> Option<PathBuf> {
    let base = if cfg!(windows) {
        std::env::var_os("LOCALAPPDATA").map(PathBuf::from)
    } else if cfg!(target_os = "macos") {
        std::env::var_os("HOME").map(|home| PathBuf::from(home).join("Library/Caches"))
    } else {
        std::env::var_os("XDG_CACHE_HOME")
            .map(PathBuf::from)
            .or_else(|| std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".cache")))
    }?;

    Some(base.join("m3scalinum").join("text").join("fonts"))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A directory that removes itself, and a font or two to put in it, without a dependency.
    struct TempDir(PathBuf);

    impl TempDir {
        fn new(label: &str) -> Self {
            static COUNTER: AtomicUsize = AtomicUsize::new(0);

            let path = std::env::temp_dir().join(format!(
                "sharp-typst-fonts-{}-{}-{}",
                std::process::id(),
                COUNTER.fetch_add(1, Ordering::Relaxed),
                label
            ));

            std::fs::create_dir_all(&path).expect("temp dir");

            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }

        fn write(&self, name: &str, bytes: &[u8]) -> PathBuf {
            let path = self.0.join(name);
            std::fs::write(&path, bytes).expect("write font");
            path
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    /// Two single-face fonts with different families, taken from the ones embedded in the binary:
    /// the tests need real font files and must not depend on what the host has installed.
    fn two_fonts() -> (Vec<u8>, Vec<u8>, String, String) {
        let mut first: Option<(Vec<u8>, String)> = None;
        let mut second: Option<(Vec<u8>, String)> = None;

        for (font, info) in typst_kit::fonts::embedded() {
            let bytes = font.data().as_slice().to_vec();

            match &first {
                None => first = Some((bytes, info.family.clone())),
                Some((_, family)) if *family != info.family => {
                    second = Some((bytes, info.family.clone()));
                    break;
                }
                Some(_) => {}
            }
        }

        let (a, family_a) = first.expect("an embedded font");
        let (b, family_b) = second.expect("a second embedded family");

        (a, b, family_a, family_b)
    }

    fn source(directory: &Path) -> HostSources {
        HostSources {
            system: false,
            directories: vec![directory.to_string_lossy().into_owned()],
        }
    }

    #[test]
    fn a_source_set_is_scanned_once_per_process() {
        clear_registry();

        let fonts = TempDir::new("shared");
        let (first, _, family, _) = two_fonts();
        fonts.write("first.otf", &first);

        let embedded = LazyHash::new(FontBook::new());
        let source = source(fonts.path());
        let options = IndexOptions::default();

        let one = shared_index(&source, &embedded, options);
        let two = shared_index(&source, &embedded, options);

        assert!(Arc::ptr_eq(&one, &two), "both worlds must share one index");
        assert_eq!(one.id(), two.id());
        assert!(one.len() >= 1, "the directory holds a font");
        assert!(
            one.book().families().any(|(name, _)| name == family),
            "the font's family is in the book"
        );
    }

    #[test]
    fn a_refresh_world_scans_again_and_replaces_the_index() {
        clear_registry();

        let fonts = TempDir::new("refresh");
        let (first, _, family, _) = two_fonts();
        fonts.write("first.otf", &first);

        let embedded = LazyHash::new(FontBook::new());
        let source = source(fonts.path());

        let before = shared_index(&source, &embedded, IndexOptions::default());
        let after = shared_index(
            &source,
            &embedded,
            IndexOptions {
                refresh: true,
                cache_dir: None,
            },
        );

        assert_ne!(before.id(), after.id(), "a refresh replaces the entry");
        assert!(
            after.book().families().any(|(name, _)| name == family),
            "the refreshed index still resolves the family"
        );
    }

    #[test]
    fn a_refresh_world_sees_a_font_installed_after_the_first_scan() {
        clear_registry();

        let fonts = TempDir::new("installed");
        let (first, second, _, family_b) = two_fonts();
        fonts.write("first.otf", &first);

        let embedded = LazyHash::new(FontBook::new());
        let source = source(fonts.path());

        let before = shared_index(&source, &embedded, IndexOptions::default());
        let faces_before = before.len();

        fonts.write("second.otf", &second);

        let stale = shared_index(&source, &embedded, IndexOptions::default());
        assert_eq!(
            stale.id(),
            before.id(),
            "a world that accepts the index does not see a font added after the scan"
        );

        let fresh = shared_index(
            &source,
            &embedded,
            IndexOptions {
                refresh: true,
                cache_dir: None,
            },
        );

        assert!(fresh.len() > faces_before, "the refresh build sees it");
        assert!(
            fresh.book().families().any(|(name, _)| name == family_b),
            "the new family is resolvable"
        );
    }

    #[test]
    fn the_persisted_index_is_reloaded_without_parsing_a_font() {
        let fonts = TempDir::new("persisted");
        let cache = TempDir::new("persisted-cache");
        let (first, _, family, _) = two_fonts();
        fonts.write("first.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        assert_eq!(cold.parsed, 1, "the first run reads the font file");
        assert_eq!(cold.reused, 0);
        assert!(!cold.from_cache);

        cold.persist();

        let warm = scan(&source, Some(cache.path()), false);
        assert_eq!(warm.parsed, 0, "the second run parses nothing");
        assert_eq!(warm.reused, 1);
        assert!(warm.from_cache);
        assert_eq!(warm.face_count(), cold.face_count());
        assert_eq!(
            warm.files[0].faces[0].info.family, family,
            "the family survives the round trip"
        );
    }

    #[test]
    fn replacing_a_font_file_invalidates_its_entry() {
        let fonts = TempDir::new("replaced");
        let cache = TempDir::new("replaced-cache");
        let (first, second, family_a, family_b) = two_fonts();
        let path = fonts.write("font.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        cold.persist();
        assert_eq!(cold.files[0].faces[0].info.family, family_a);

        // A different font under the same name: a font upgrade, replacing the file in place.
        std::fs::write(&path, &second).expect("replace font");

        let warm = scan(&source, Some(cache.path()), false);

        assert_eq!(warm.parsed, 1, "the changed file is parsed again");
        assert_eq!(warm.reused, 0);
        assert_eq!(
            warm.files[0].faces[0].info.family, family_b,
            "the new bytes are what the index reports"
        );
    }

    #[test]
    fn a_font_file_added_to_a_cached_directory_is_seen() {
        let fonts = TempDir::new("added");
        let cache = TempDir::new("added-cache");
        let (first, second, _, family_b) = two_fonts();
        fonts.write("first.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        cold.persist();
        assert_eq!(cold.face_count(), cold.files[0].faces.len());

        fonts.write("second.otf", &second);

        let warm = scan(&source, Some(cache.path()), false);

        assert_eq!(warm.reused, 1, "the known file is not parsed again");
        assert_eq!(warm.parsed, 1, "the new file is");
        assert_eq!(warm.files.len(), 2);
        assert!(
            warm.files
                .iter()
                .flat_map(|file| &file.faces)
                .any(|face| face.info.family == family_b),
            "the added family is in the index"
        );
    }

    #[test]
    fn a_refresh_scan_ignores_the_persisted_index() {
        let fonts = TempDir::new("refresh-cache");
        let cache = TempDir::new("refresh-cache-dir");
        let (first, _, _, _) = two_fonts();
        fonts.write("first.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        cold.persist();

        let warm = scan(&source, Some(cache.path()), false);
        assert_eq!(warm.parsed, 0, "an ordinary scan trusts the cache");

        let refreshed = scan(&source, Some(cache.path()), true);
        assert!(!refreshed.from_cache, "a refresh reads no cache");
        assert_eq!(refreshed.parsed, 1, "a refresh parses the font again");
    }

    #[test]
    fn an_index_written_for_another_compiler_is_ignored() {
        let fonts = TempDir::new("compiler");
        let cache = TempDir::new("compiler-cache");
        let (first, _, _, _) = two_fonts();
        fonts.write("first.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        cold.persist();

        let path = cache.path().join(cache_name(&source));
        let mut index: CachedIndex =
            serde_json::from_slice(&std::fs::read(&path).expect("index file")).expect("parse");
        index.compiler = "0.0.0".to_string();
        std::fs::write(&path, serde_json::to_vec(&index).expect("encode")).expect("write");

        let warm = scan(&source, Some(cache.path()), false);

        assert!(!warm.from_cache, "a foreign index is not trusted");
        assert_eq!(warm.parsed, 1);
    }

    /// The price of the scan the index removes, on this host. Ignored: it is a measurement.
    #[test]
    #[ignore = "measurement: run with --ignored --nocapture --test-threads=1"]
    fn one_system_scan_costs_what_the_index_replaces() {
        use typst_kit::fonts::FontSource;

        let started = std::time::Instant::now();
        let faces = typst_kit::fonts::system().count();
        let elapsed = started.elapsed();

        println!("{faces} system faces scanned in {elapsed:?}");

        // What `NetWorld::new` used to do for every world that asked for the host's fonts.
        let started = std::time::Instant::now();
        let mut book = FontBook::new();
        let mut fonts = Vec::new();

        for (path, info) in typst_kit::fonts::system() {
            if let Some(font) = path.load() {
                book.push(info);
                fonts.push(font);
            }
        }

        println!(
            "the pre-index cost per world: {:?} ({} faces, {} bytes)",
            started.elapsed(),
            fonts.len(),
            book.families().count()
        );
    }

    #[test]
    fn a_missing_root_falls_back_to_a_fresh_scan() {
        let fonts = TempDir::new("missing");
        let cache = TempDir::new("missing-cache");
        let (first, _, _, _) = two_fonts();
        fonts.write("first.otf", &first);

        let source = source(fonts.path());

        let cold = scan(&source, Some(cache.path()), false);
        cold.persist();

        std::fs::remove_dir_all(fonts.path()).expect("remove directory");

        let warm = scan(&source, Some(cache.path()), false);

        assert!(
            !warm.from_cache,
            "a root that vanished invalidates the entry"
        );
        assert_eq!(warm.files.len(), 0);
    }
}
