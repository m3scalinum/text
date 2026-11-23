//! The `typst::World` implementation handed to the compiler.
//!
//! All inputs are explicit: sources, assets and fonts are registered by the caller,
//! there is no ambient file system access.

use std::any::Any;
use std::collections::{HashMap, HashSet};
use std::io::Read;
use std::sync::{Arc, OnceLock, RwLock, RwLockReadGuard, RwLockWriteGuard};

use serde::Serialize;
use typst::diag::{FileError, FileResult};
use typst::foundations::{Bytes, Datetime, Duration};
use typst::syntax::{DiagSpan, FileId, RootedPath, Source, VirtualPath, VirtualRoot};
use typst::text::{Font, FontBook, FontFlags, FontStyle};
use typst::utils::LazyHash;
use typst::{Feature, Features, Library, LibraryExt, World, WorldExt};
use typst_kit::downloader::{Downloader, SystemDownloader};
use typst_kit::packages::{FsPackages, SystemPackages, UniversePackages};

use crate::config::{ClockConfig, Limits, PackagesConfig, WorldConfig};
use crate::ffi::{TN_ERR_LIMIT, TnError};
use crate::fonts::{FontIndex, HostSources, IndexOptions, shared_index};

/// A downloader that refuses everything: the offline default.
///
/// `SystemPackages::obtain` falls back to the downloader only when the package is in neither the
/// data nor the cache directory, so refusing here means "cached packages work, missing ones fail".
struct NoDownloader;

/// Reads a lock, recovering from poisoning.
///
/// A poisoned lock still holds valid data: the panic happened in another thread, not in the map.
/// Recovering keeps the world usable instead of turning one panic into a permanently broken world.
fn read<T>(lock: &RwLock<T>) -> RwLockReadGuard<'_, T> {
    lock.read().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Writes a lock, recovering from poisoning. See [`read`].
fn write<T>(lock: &RwLock<T>) -> RwLockWriteGuard<'_, T> {
    lock.write()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

impl Downloader for NoDownloader {
    fn stream(
        &self,
        _key: &dyn Any,
        _url: &str,
    ) -> std::io::Result<(Option<usize>, Box<dyn Read>)> {
        Err(std::io::Error::new(
            std::io::ErrorKind::NotFound,
            "package downloads are disabled; set packages.download to true",
        ))
    }
}

/// Builds the package resolver from the host's configuration.
fn build_packages(config: &PackagesConfig) -> SystemPackages {
    let data = match &config.data_dir {
        Some(dir) => Some(FsPackages::new(dir)),
        None => FsPackages::system_data(),
    };

    let cache = match &config.cache_dir {
        Some(dir) => Some(FsPackages::new(dir)),
        None => FsPackages::system_cache(),
    };

    let universe = if config.download {
        let downloader = SystemDownloader::new(format!("SharpTypst/{}", env!("CARGO_PKG_VERSION")));
        match &config.registry {
            Some(url) => UniversePackages::with_url(downloader, url.clone()),
            None => UniversePackages::new(downloader),
        }
    } else {
        match &config.registry {
            Some(url) => UniversePackages::with_url(NoDownloader, url.clone()),
            None => UniversePackages::new(NoDownloader),
        }
    };

    SystemPackages::from_parts(data, cache, universe)
}

/// One face a world can resolve, with the variation axes it carries.
///
/// The distinction this exposes is the one that decides whether `#set text(weight: 350)` does
/// anything: a face with a `wght` axis interpolates to 350, a static face has no axis and the
/// compiler serves the nearest cut instead.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct FontFaceInfo {
    /// The family name Typst resolves against.
    pub family: String,
    /// The weight of the default instance, 100-900.
    pub weight: u16,
    /// The width of the default instance, as a percentage of normal — `100.0` is normal.
    pub stretch: f32,
    /// `normal`, `italic` or `oblique`.
    pub style: &'static str,
    /// Whether the face carries an `fvar` table, and therefore axes that can be interpolated.
    pub variable: bool,
    /// Every axis the face carries, sorted by tag. Empty for a static face.
    pub axes: Vec<FontAxisInfo>,
}

/// One variation axis of a face: its tag, and the range the font allows.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct FontAxisInfo {
    /// The OpenType tag, e.g. `wght`, `wdth`, `opsz` or a custom one.
    pub tag: String,
    /// The smallest value the face accepts.
    pub min: f32,
    /// The largest value the face accepts.
    pub max: f32,
    /// The value the face was designed around, used when nothing asks for another one.
    pub default: f32,
}

pub struct NetWorld {
    library: Arc<LazyHash<Library>>,
    book: Arc<LazyHash<FontBook>>,
    /// The fonts embedded in the binary: never copied, shared by every world.
    fonts: Arc<Vec<Font>>,
    /// Faces the host registered with `add_font`, after the embedded ones.
    extra: Vec<Font>,
    /// The host's indexed fonts, shared by every world that asked for the same sources.
    host: Option<Arc<FontIndex>>,
    /// How many of `fonts` are the ones embedded in the binary — the rest are the host's.
    embedded_fonts: usize,
    sources: RwLock<HashMap<FileId, Source>>,
    files: RwLock<HashMap<FileId, Bytes>>,
    main: FileId,
    today: Option<Datetime>,
    limits: Limits,
    packages: SystemPackages,
    packages_config: PackagesConfig,
}

/// The parts every world shares: the standard library and the fonts embedded in the binary.
///
/// Both are immutable and identical for every world, and building them is the expensive half of
/// creating one — the library registers every standard definition, and the embedded fonts cost
/// about a megabyte of parsing. `clone_world` already shares them; a fresh world does too, so
/// building N worlds costs one library and one font scan, not N.
struct Shared {
    library: Arc<LazyHash<Library>>,
    book: Arc<LazyHash<FontBook>>,
    fonts: Arc<Vec<Font>>,
}

fn shared() -> &'static Shared {
    static SHARED: OnceLock<Shared> = OnceLock::new();

    SHARED.get_or_init(|| {
        // Every feature is enabled unconditionally: they only add definitions to the standard
        // library, and a world must be able to serve every export target without being rebuilt.
        // `Html` adds `html.*`, `A11yExtras` adds `pdf.data-cell`/`header-cell`/`table-summary`,
        // `Bundle` adds the bundle target.
        let library = Library::builder()
            .with_features(Features::from_iter(Feature::all()))
            .build();

        // Embedded fonts only: system fonts are an explicit opt-in in the spec and are
        // not wired into the ABI yet, so a build never depends on the host.
        let mut book = FontBook::new();
        let mut fonts = Vec::new();
        for (font, info) in typst_kit::fonts::embedded() {
            book.push(info);
            fonts.push(font);
        }

        Shared {
            library: Arc::new(LazyHash::new(library)),
            book: Arc::new(LazyHash::new(book)),
            fonts: Arc::new(fonts),
        }
    })
}

/// The font book and fonts of a world: the shared embedded set, plus whatever the host asked for.
///
/// Host fonts are opt-in: a compile never reads the file system unless the caller asks. The
/// metadata scan is not per world — [`crate::fonts::shared_index`] returns the process-wide index
/// for the same sources, so the second world and the two-thousandth cost a lookup, and the faces
/// are parsed when a compilation first uses them.
fn fonts_for(
    config: &crate::config::FontsConfig,
    shared: &Shared,
) -> (Arc<LazyHash<FontBook>>, Option<Arc<FontIndex>>) {
    let source = HostSources {
        system: config.system,
        directories: config.directories.clone(),
    };

    if source.is_empty() {
        return (Arc::clone(&shared.book), None);
    }

    let cache = config.cache_path();

    let index = shared_index(
        &source,
        &shared.book,
        IndexOptions {
            refresh: config.refresh,
            cache_dir: cache.as_deref(),
        },
    );

    (Arc::clone(index.book()), Some(index))
}

impl NetWorld {
    pub fn new(config: WorldConfig) -> Result<Self, TnError> {
        let shared = shared();
        let embedded_fonts = shared.fonts.len();
        let (book, host) = fonts_for(&config.fonts, shared);

        let main_path = VirtualPath::new("main.typ")
            .map_err(|e| TnError::invalid_argument(format!("invalid default root: {e}")))?;
        let main = FileId::new(RootedPath::new(VirtualRoot::Project, main_path));

        let today = match &config.clock {
            ClockConfig::Fixed {
                year,
                month,
                day,
                hour,
                minute,
                second,
            } => Datetime::from_ymd_hms(*year, *month, *day, *hour, *minute, *second),
            ClockConfig::System => None,
        };

        Ok(Self {
            library: Arc::clone(&shared.library),
            book,
            fonts: Arc::clone(&shared.fonts),
            extra: Vec::new(),
            host,
            embedded_fonts,
            sources: RwLock::new(HashMap::new()),
            files: RwLock::new(HashMap::new()),
            main,
            today,
            limits: config.limits,
            packages: build_packages(&config.packages),
            packages_config: config.packages.clone(),
        })
    }

    /// A new world sharing the expensive parts — the library, the font book and the fonts — and
    /// copying the cheap ones. This is what makes "one template world, one clone per request"
    /// affordable: a clone never re-scans fonts or re-reads packages.
    pub fn clone_world(&self) -> Result<NetWorld, TnError> {
        let sources = read(&self.sources).clone();

        let files = read(&self.files).clone();

        Ok(NetWorld {
            library: Arc::clone(&self.library),
            book: Arc::clone(&self.book),
            fonts: Arc::clone(&self.fonts),
            extra: self.extra.clone(),
            host: self.host.as_ref().map(Arc::clone),
            embedded_fonts: self.embedded_fonts,
            sources: RwLock::new(sources),
            files: RwLock::new(files),
            main: self.main,
            today: self.today,
            limits: self.limits.clone(),
            packages: build_packages(&self.packages_config),
            packages_config: self.packages_config.clone(),
        })
    }

    /// The font families this world can resolve, sorted and deduplicated.
    ///
    /// A host that gets "unknown font family" needs this to see what it actually has.
    pub fn font_families(&self) -> Vec<String> {
        let mut families: Vec<String> = self
            .book
            .families()
            .map(|(name, _)| name.to_string())
            .collect();

        families.sort();
        families.dedup();
        families
    }

    /// Every face this world can resolve, in family order, with the variation axes each carries.
    ///
    /// Metadata only: nothing is parsed, so this is cheap even for a host font set of several
    /// hundred faces. A caller uses it to find out whether a weight is reachable before asking the
    /// layout for it — a variable face advertises a `wght` range and interpolates inside it; a
    /// static family carries no axes at all and serves the nearest cut instead.
    pub fn font_faces(&self) -> Vec<FontFaceInfo> {
        let mut faces = Vec::new();

        for (_, ids) in self.book.families() {
            for id in ids {
                let Some(info) = self.book.info(id) else {
                    continue;
                };

                let style = match info.variant.style {
                    FontStyle::Normal => "normal",
                    FontStyle::Italic => "italic",
                    FontStyle::Oblique => "oblique",
                };

                let mut axes: Vec<FontAxisInfo> = info
                    .axes
                    .iter()
                    .map(|axis| FontAxisInfo {
                        tag: axis.tag.to_string(),
                        min: axis.min.0,
                        max: axis.max.0,
                        default: axis.default.0,
                    })
                    .collect();
                axes.sort_by(|left, right| left.tag.cmp(&right.tag));

                faces.push(FontFaceInfo {
                    family: info.family.clone(),
                    weight: info.variant.weight.to_number(),
                    stretch: info.variant.stretch.to_ratio().get() as f32 * 100.0,
                    style,
                    variable: info.flags.contains(FontFlags::VARIABLE),
                    axes,
                });
            }
        }

        faces
    }

    pub fn add_source(&self, path: &str, text: String) -> Result<(), TnError> {
        if text.len() > self.limits.max_source_bytes {
            return Err(TnError::new(
                TN_ERR_LIMIT,
                format!(
                    "source `{path}` exceeds maxSourceBytes ({} > {})",
                    text.len(),
                    self.limits.max_source_bytes
                ),
            ));
        }

        let id = make_id(path)?;
        let mut sources = write(&self.sources);

        if sources.len() >= self.limits.max_sources && !sources.contains_key(&id) {
            return Err(TnError::new(
                TN_ERR_LIMIT,
                format!("too many sources (max {})", self.limits.max_sources),
            ));
        }

        sources.insert(id, Source::new(id, text));
        Ok(())
    }

    pub fn add_asset(&self, path: &str, bytes: Vec<u8>) -> Result<(), TnError> {
        if bytes.len() > self.limits.max_asset_bytes {
            return Err(TnError::new(
                TN_ERR_LIMIT,
                format!(
                    "asset `{path}` exceeds maxAssetBytes ({} > {})",
                    bytes.len(),
                    self.limits.max_asset_bytes
                ),
            ));
        }
        let id = make_id(path)?;
        write(&self.files).insert(id, Bytes::new(bytes));
        Ok(())
    }

    pub fn add_font(&mut self, bytes: Vec<u8>, face_index: u32) -> Result<(), TnError> {
        if bytes.len() > self.limits.max_font_bytes {
            return Err(TnError::new(
                TN_ERR_LIMIT,
                format!(
                    "font exceeds maxFontBytes ({} > {})",
                    bytes.len(),
                    self.limits.max_font_bytes
                ),
            ));
        }
        if self.font_count() >= self.limits.max_fonts {
            return Err(TnError::new(
                TN_ERR_LIMIT,
                format!("too many fonts (max {})", self.limits.max_fonts),
            ));
        }

        let data = Bytes::new(bytes);
        let mut book = (**self.book).clone();
        let mut added = 0usize;

        if let Some(font) = Font::new(data.clone(), face_index) {
            book.push(font.info().clone());
            self.extra.push(font);
            added += 1;
        } else {
            // Not a single-face font: walk the collection (`ttc`/`otc`).
            for font in Font::iter(data) {
                book.push(font.info().clone());
                self.extra.push(font);
                added += 1;
            }
        }

        if added == 0 {
            return Err(TnError::invalid_argument(
                "font data could not be parsed as a supported font",
            ));
        }

        // Fonts are added before the world is shared, so this replaces the `Arc` rather than
        // mutating through it.
        self.book = Arc::new(LazyHash::new(book));
        Ok(())
    }

    pub fn clear_sources(&self) {
        write(&self.sources).clear();
    }

    /// The host index this world holds, if it asked for host fonts. Exposed for tests.
    #[cfg(test)]
    pub fn host_index(&self) -> Option<&Arc<FontIndex>> {
        self.host.as_ref()
    }

    pub fn source_count(&self) -> usize {
        read(&self.sources).len()
    }

    pub fn font_count(&self) -> usize {
        self.fonts.len() + self.host.as_ref().map_or(0, |index| index.len()) + self.extra.len()
    }

    /// How many of the fonts this world resolves are the ones embedded in the binary.
    ///
    /// The rest came from the host: the system font directories it asked for, or the faces it
    /// uploaded with `add_font`. The split is what tells a caller whether a slow first compile is
    /// its own font set or the host's.
    pub fn embedded_font_count(&self) -> usize {
        self.embedded_fonts
    }

    /// Bytes of font data this world can resolve, counting each buffer once.
    ///
    /// A font collection — a `ttc` or an `otc` — exposes one `Font` per face over the same buffer,
    /// so summing per face would count that file once per face. The pointer and the length identify
    /// the buffer, which is what a caller wants to compare with what it registered. A host face the
    /// compilation never used is not parsed yet, so its file's length is what counts; the answer
    /// does not depend on which faces have been touched.
    pub fn font_bytes(&self) -> u64 {
        let mut seen = HashSet::new();
        let mut bytes = 0u64;

        for font in self.fonts.iter().chain(self.extra.iter()) {
            let data = font.data().as_slice();
            if seen.insert((data.as_ptr() as usize, data.len())) {
                bytes += data.len() as u64;
            }
        }

        if let Some(host) = &self.host {
            bytes += host.file_bytes();
        }

        bytes
    }

    /// Points the compilation at `root`, registering nothing.
    pub fn set_root(&mut self, root: &str) -> Result<(), TnError> {
        if !self.has_source(root) {
            return Err(TnError::new(
                crate::ffi::TN_ERR_FILE,
                format!("root `{root}` was not registered with add_source"),
            ));
        }
        self.main = make_id(root)?;
        Ok(())
    }

    pub fn has_source(&self, path: &str) -> bool {
        make_id(path)
            .map(|id| read(&self.sources).contains_key(&id))
            .unwrap_or(false)
    }

    /// Resolves a diagnostic span into `(file, line, column, endLine, endColumn, inSource)`.
    pub fn locate(
        &self,
        span: DiagSpan,
    ) -> Option<(String, usize, usize, usize, usize, bool, String)> {
        let id = span.id()?;
        let range = self.range(span)?;

        let sources = self.sources.read().ok()?;
        let source = sources.get(&id)?;
        let lines = source.lines();

        let (line, column) = lines.byte_to_line_column(range.start)?;
        let (end_line, end_column) = lines.byte_to_line_column(range.end)?;

        // The line the span starts on, so a host can render a caret without holding the source.
        let source_line = source
            .text()
            .lines()
            .nth(line)
            .unwrap_or_default()
            .to_string();

        Some((
            path_of(id),
            line + 1,
            column + 1,
            end_line + 1,
            end_column + 1,
            true,
            source_line,
        ))
    }
}

fn make_id(path: &str) -> Result<FileId, TnError> {
    if path.is_empty() {
        return Err(TnError::invalid_argument("path must not be empty"));
    }
    if path.contains('\\') {
        return Err(TnError::invalid_argument(format!(
            "path `{path}` must use forward slashes"
        )));
    }
    if path.starts_with('/') || path.contains(':') {
        return Err(TnError::invalid_argument(format!(
            "path `{path}` must be relative"
        )));
    }
    if path.split('/').any(|segment| segment == "..") {
        return Err(TnError::invalid_argument(format!(
            "path `{path}` must not contain `..`"
        )));
    }

    let virtual_path = VirtualPath::new(path)
        .map_err(|e| TnError::invalid_argument(format!("invalid path `{path}`: {e}")))?;
    Ok(FileId::new(RootedPath::new(
        VirtualRoot::Project,
        virtual_path,
    )))
}

fn path_of(id: FileId) -> String {
    id.vpath().get_without_slash().to_string()
}

impl World for NetWorld {
    fn library(&self) -> &LazyHash<Library> {
        &self.library
    }

    fn book(&self) -> &LazyHash<FontBook> {
        &self.book
    }

    fn main(&self) -> FileId {
        self.main
    }

    fn source(&self, id: FileId) -> FileResult<Source> {
        // A package file is read from disk, not from the in-memory registry.
        if matches!(id.root(), VirtualRoot::Package(_)) {
            let bytes = self.file(id)?;
            let text = String::from_utf8(bytes.to_vec())
                .map_err(|_| FileError::Other(Some("package source is not valid UTF-8".into())))?;
            return Ok(Source::new(id, text));
        }

        self.sources
            .read()
            .map_err(|_| FileError::Other(None))?
            .get(&id)
            .cloned()
            .ok_or_else(|| FileError::NotFound(path_of(id).into()))
    }

    fn file(&self, id: FileId) -> FileResult<Bytes> {
        // `@namespace/name:version` resolves through the package storage: data directory, then
        // cache, then the registry if downloads are enabled.
        if let VirtualRoot::Package(spec) = id.root() {
            return self.packages.obtain(spec)?.load(id.vpath());
        }

        self.files
            .read()
            .map_err(|_| FileError::Other(None))?
            .get(&id)
            .cloned()
            .ok_or_else(|| FileError::NotFound(path_of(id).into()))
    }

    fn font(&self, index: usize) -> Option<Font> {
        if index < self.fonts.len() {
            return self.fonts.get(index).cloned();
        }

        let index = index - self.fonts.len();

        if let Some(host) = &self.host {
            if index < host.len() {
                return host.font(index);
            }

            return self.extra.get(index - host.len()).cloned();
        }

        self.extra.get(index).cloned()
    }

    fn today(&self, _offset: Option<Duration>) -> Option<Datetime> {
        self.today
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_backslashes() {
        assert!(make_id("partials\\a.typ").is_err());
    }

    #[test]
    fn rejects_absolute_paths() {
        assert!(make_id("/main.typ").is_err());
        assert!(make_id("C:/main.typ").is_err());
    }

    #[test]
    fn rejects_parent_segments() {
        assert!(make_id("../main.typ").is_err());
        assert!(make_id("a/../b.typ").is_err());
    }

    #[test]
    fn accepts_relative_paths() {
        assert!(make_id("main.typ").is_ok());
        assert!(make_id("partials/header.typ").is_ok());
    }

    #[test]
    fn worlds_share_one_library_and_one_font_set() {
        // Building the library and parsing the embedded fonts costs milliseconds per world; the
        // spec says both are built once per process and shared through `Arc`. A world that builds
        // its own copy is a leak of exactly that work per world, which the soak test catches as
        // working-set growth on an allocator that retains the churn.
        let first = NetWorld::new(WorldConfig::default()).expect("first world");
        let second = NetWorld::new(WorldConfig::default()).expect("second world");

        assert!(
            Arc::ptr_eq(&first.library, &second.library),
            "every world must share the process library"
        );
        assert!(
            Arc::ptr_eq(&first.book, &second.book),
            "every world must share the embedded font book"
        );
        assert!(
            Arc::ptr_eq(&first.fonts, &second.fonts),
            "every world must share the embedded fonts"
        );
    }

    #[test]
    fn a_poisoned_lock_does_not_brick_the_world() {
        let world = Arc::new(NetWorld::new(WorldConfig::default()).expect("world"));

        // Poison the source lock: panic while holding the write guard.
        let poisoned = Arc::clone(&world);
        let outcome = std::thread::spawn(move || {
            let _guard = poisoned.sources.write().expect("lock");
            panic!("poison the lock on purpose");
        })
        .join();

        assert!(outcome.is_err(), "the thread should have panicked");
        assert!(world.sources.is_poisoned(), "the lock should be poisoned");

        // The world still answers, and still accepts a source.
        assert_eq!(world.source_count(), 0);
        world
            .add_source("main.typ", "= Still usable".to_string())
            .expect("add_source after poisoning");
        assert_eq!(world.source_count(), 1);
        assert!(world.has_source("main.typ"));
    }

    /// A font directory to hand the world, and the family one of its files carries.
    ///
    /// The bytes are the ones embedded in the binary, copied to a temporary directory: the tests
    /// need real font files and must not depend on what the host has installed.
    struct FontDir {
        path: std::path::PathBuf,
        family: String,
    }

    impl FontDir {
        fn new(label: &str) -> Self {
            static COUNTER: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);

            let mut chosen: Option<(Vec<u8>, String)> = None;

            for (font, info) in typst_kit::fonts::embedded() {
                match &chosen {
                    None => chosen = Some((font.data().as_slice().to_vec(), info.family.clone())),
                    Some((_, family)) if *family != info.family => {
                        // A second family, so `new` can add one the first world has not seen.
                        break;
                    }
                    Some(_) => {}
                }
            }

            let (bytes, family) = chosen.expect("an embedded font");

            let path = std::env::temp_dir().join(format!(
                "sharp-typst-world-fonts-{}-{}-{}",
                std::process::id(),
                COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed),
                label
            ));

            std::fs::create_dir_all(&path).expect("temp dir");
            std::fs::write(path.join("host.otf"), &bytes).expect("write font");

            Self { path, family }
        }

        fn config(&self) -> WorldConfig {
            let mut config = WorldConfig::default();
            config.fonts.directories = vec![self.path.to_string_lossy().into_owned()];
            config.fonts.cache = false;
            config
        }
    }

    impl Drop for FontDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.path);
        }
    }

    #[test]
    fn every_world_shares_one_host_font_index() {
        let fonts = FontDir::new("shared");
        let first = NetWorld::new(fonts.config()).expect("first world");
        let second = NetWorld::new(fonts.config()).expect("second world");

        let one = first.host_index().expect("first world holds an index");
        let two = second.host_index().expect("second world holds an index");

        assert!(
            Arc::ptr_eq(one, two),
            "the scan is per process, not per world"
        );
        assert_eq!(one.id(), two.id());
        assert!(
            first.font_families().contains(&fonts.family),
            "the directory's family is resolvable"
        );
        assert_eq!(first.font_count(), second.font_count());
        assert_eq!(
            one.counts(),
            (1, 0, false),
            "one font file was read, none came from the persistent index"
        );
    }

    #[test]
    fn a_hundred_worlds_compile_from_one_scan() {
        let fonts = FontDir::new("hundred");
        let config = fonts.config();

        // The document changes on every iteration: Typst memoises a compilation by its inputs, and
        // a repeated one would answer from that cache instead of exercising the layout.
        let markup = |seed: usize| {
            format!(
                "#set text(font: \"{}\")\n= Title {seed}\n\n#lorem({})",
                fonts.family,
                seed + 40
            )
        };

        let template = NetWorld::new(config.clone()).expect("template world");
        let index = Arc::clone(template.host_index().expect("index"));
        let before = index.id();

        for seed in 0..100 {
            let world = NetWorld::new(config.clone()).expect("world");
            world
                .add_source("main.typ", markup(seed))
                .expect("add_source");

            assert!(
                Arc::ptr_eq(world.host_index().expect("index"), &index),
                "every world must reuse the index the process already built"
            );

            let document = typst::compile::<typst_layout::PagedDocument>(&world)
                .output
                .expect("the document compiles");

            assert_eq!(document.pages().len(), 1);
        }

        assert_eq!(index.id(), before, "no compilation rebuilt the index");
        assert_eq!(
            index.font(0).map(|font| font.info().family.clone()),
            Some(fonts.family.clone()),
            "the host face is parsed on first use"
        );
    }

    #[test]
    fn a_refresh_world_rebuilds_the_index_for_every_world_that_follows() {
        let fonts = FontDir::new("refresh-world");
        let mut refresh = fonts.config();
        refresh.fonts.refresh = true;

        let accepted = NetWorld::new(fonts.config()).expect("accepted world");
        let refreshed = NetWorld::new(refresh).expect("refresh world");
        let after = NetWorld::new(fonts.config()).expect("world after the refresh");

        let first = accepted.host_index().expect("index");
        let second = refreshed.host_index().expect("index");
        let third = after.host_index().expect("index");

        assert_ne!(first.id(), second.id(), "a refresh world rescans");
        assert!(Arc::ptr_eq(second, third), "the rebuild replaces the entry");
    }

    #[test]
    fn the_face_list_carries_every_face_and_no_axis_for_a_static_font() {
        // The inventory is what tells a caller whether `#set text(weight: 350)` can interpolate. The
        // fonts embedded in the binary are static, so every face they contribute must report an empty
        // axis list — and the list must still cover every face the world counts, or a caller would
        // read a family's absence as "no weight axis".
        let world = NetWorld::new(WorldConfig::default()).expect("world");
        let faces = world.font_faces();

        assert_eq!(faces.len(), world.font_count(), "one entry per face");
        assert!(
            faces
                .iter()
                .all(|face| !face.variable && face.axes.is_empty()),
            "the embedded faces are static"
        );
        assert!(
            faces
                .iter()
                .all(|face| (100..=900).contains(&face.weight) && face.stretch > 0.0),
            "every face reports a weight in 100-900 and a positive width"
        );
        assert!(
            faces
                .iter()
                .all(|face| matches!(face.style, "normal" | "italic" | "oblique")),
            "every face reports one of the three styles"
        );
        assert!(
            faces
                .iter()
                .all(|face| world.font_families().contains(&face.family)),
            "every face belongs to a family the world resolves"
        );
    }

    /// The measurement behind the item: two thousand compilations, one scan.
    ///
    /// Ignored because it is a measurement, not a gate — it asserts on the process-wide scan
    /// counter, which is only meaningful with one test running.
    /// `cargo test --lib --release -- --ignored --nocapture --test-threads=1`.
    #[test]
    #[ignore = "measurement: run with --ignored --nocapture --test-threads=1"]
    fn two_thousand_compilations_scan_the_fonts_once() {
        let fonts = FontDir::new("soak");
        let config = fonts.config();

        // Every iteration compiles a different document: Typst memoises by inputs, and a repeated
        // one would be answered from the memo table rather than laid out.
        let markup = |seed: usize| {
            format!(
                "#set text(font: \"{}\")\n= Title {seed}\n\n#lorem({})",
                fonts.family,
                seed % 200 + 20
            )
        };

        // What the old code paid per world: a metadata scan with nothing shared.
        let scan_started = std::time::Instant::now();
        let mut refresh = config.clone();
        refresh.fonts.refresh = true;
        let scanned = NetWorld::new(refresh).expect("scan world");
        let scan_time = scan_started.elapsed();
        drop(scanned);

        let after_scan = crate::fonts::scan_count();

        let started = std::time::Instant::now();
        let mut pages = 0usize;

        for seed in 0..2000 {
            let world = NetWorld::new(config.clone()).expect("world");
            world
                .add_source("main.typ", markup(seed))
                .expect("add_source");

            let document = typst::compile::<typst_layout::PagedDocument>(&world)
                .output
                .expect("the document compiles");

            pages += document.pages().len();
        }

        let elapsed = started.elapsed();
        let scans_after = crate::fonts::scan_count();

        println!(
            "2000 compilations in {elapsed:?} ({:.3} ms each, {pages} pages); one host font scan              costs {scan_time:?}; scans during the 2000 compilations: {}",
            elapsed.as_secs_f64() * 1000.0 / 2000.0,
            scans_after - after_scan
        );

        assert_eq!(
            scans_after, after_scan,
            "two thousand compilations must not scan the host fonts again"
        );
    }

    /// A report of `pages` pages, every page carrying a paragraph the caller can vary.
    ///
    /// The body of each paragraph is `lorem`, so the pages are dense enough that the layout, not
    /// the interpreter, is what a compilation costs.
    fn report(pages: usize, changed: &str) -> String {
        let mut markup = String::from("#set page(paper: \"a4\")\n#set text(size: 10pt)\n");

        for page in 0..pages {
            if page > 0 {
                markup.push_str("#pagebreak(weak: true)\n");
            }
            markup.push_str(&format!("#heading(level: 1)[Section {page}]\n\n"));

            for paragraph in 0..6 {
                markup.push_str(&format!(
                    "Paragraph {paragraph} of page {page}: #lorem({}) ",
                    40 + paragraph
                ));

                // One paragraph of the first page is the sentence the caller edits.
                if page == 0 && paragraph == 2 {
                    markup.push_str(changed);
                }

                markup.push_str("\n\n");
            }
        }

        markup
    }

    /// The measurement behind the item: a second compilation of an edited document reuses the
    /// state Typst memoized during the first.
    ///
    /// Cold is made honest with `comemo::evict(0)`, which drops every memoized result the process
    /// holds — the same state a fresh process starts with — instead of trusting that this test
    /// runs before any other compilation.
    ///
    /// Ignored because it is a measurement with a timing assertion, not a gate:
    /// `cargo test --lib --release -- --ignored --nocapture --test-threads=1`.
    #[test]
    #[ignore = "measurement: run with --ignored --nocapture --test-threads=1"]
    fn an_edited_document_compiles_faster_than_a_cold_one() {
        let world = NetWorld::new(WorldConfig::default()).expect("world");
        let pages = 8;

        world
            .add_source(
                "main.typ",
                report(pages, "The first wording of the sentence."),
            )
            .expect("add_source");

        typst::comemo::evict(0);
        let cold_started = std::time::Instant::now();
        let cold = typst::compile::<typst_layout::PagedDocument>(&world)
            .output
            .expect("the document compiles");
        let cold_time = cold_started.elapsed();
        assert_eq!(cold.pages().len(), pages);

        // One sentence, on page one, of an eight-page document.
        world
            .add_source(
                "main.typ",
                report(pages, "The second wording of the sentence."),
            )
            .expect("add_source");

        let warm_started = std::time::Instant::now();
        let warm = typst::compile::<typst_layout::PagedDocument>(&world)
            .output
            .expect("the document compiles");
        let warm_time = warm_started.elapsed();
        assert_eq!(warm.pages().len(), pages);

        // A second world holding the same sources: the memo table is keyed by the inputs, not by
        // the world, so a clone is as warm as the world it came from. This is what says
        // `WithSource` — one clone per edit — does not throw the reuse away.
        let clone = world.clone_world().expect("clone");
        let clone_started = std::time::Instant::now();
        let cloned = typst::compile::<typst_layout::PagedDocument>(&clone)
            .output
            .expect("the document compiles");
        let clone_time = clone_started.elapsed();
        assert_eq!(cloned.pages().len(), pages);

        let printed = crate::fingerprint::page_fingerprints(&warm);

        println!(
            "cold {cold_time:?}, warm {warm_time:?} ({:.1}x, {pages} pages), same sources in a \
             new world {clone_time:?} ({:.1}x); fingerprints {:x?}",
            cold_time.as_secs_f64() / warm_time.as_secs_f64().max(f64::MIN_POSITIVE),
            cold_time.as_secs_f64() / clone_time.as_secs_f64().max(f64::MIN_POSITIVE),
            &printed[..printed.len().min(2)]
        );

        assert!(
            warm_time < cold_time,
            "a second compilation of an edited document must be cheaper than a cold one \
             (cold {cold_time:?}, warm {warm_time:?})"
        );
        assert!(
            clone_time < cold_time,
            "a new world with the same sources must reuse the memoized results \
             (cold {cold_time:?}, clone {clone_time:?})"
        );
    }
}
