//! The `typst::World` implementation handed to the compiler.
//!
//! All inputs are explicit: sources, assets and fonts are registered by the caller,
//! there is no ambient file system access.

use std::any::Any;
use std::collections::HashMap;
use std::io::Read;
use std::sync::{Arc, OnceLock, RwLock, RwLockReadGuard, RwLockWriteGuard};

use typst::diag::{FileError, FileResult};
use typst::foundations::{Bytes, Datetime, Duration};
use typst::syntax::{DiagSpan, FileId, RootedPath, Source, VirtualPath, VirtualRoot};
use typst::text::{Font, FontBook};
use typst::utils::LazyHash;
use typst::{Feature, Features, Library, LibraryExt, World, WorldExt};
use typst_kit::downloader::{Downloader, SystemDownloader};
use typst_kit::fonts::FontSource;
use typst_kit::packages::{FsPackages, SystemPackages, UniversePackages};

use crate::config::{ClockConfig, Limits, PackagesConfig, WorldConfig};
use crate::ffi::{TN_ERR_LIMIT, TnError};

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

pub struct NetWorld {
    library: Arc<LazyHash<Library>>,
    book: Arc<LazyHash<FontBook>>,
    fonts: Arc<Vec<Font>>,
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
/// Host fonts are opt-in: a compile never reads the file system unless the caller asks. Only such a
/// world copies the embedded set; the common case shares it.
fn fonts_for(config: &crate::config::FontsConfig) -> (Arc<LazyHash<FontBook>>, Arc<Vec<Font>>) {
    let shared = shared();

    if !config.system && config.directories.is_empty() {
        return (Arc::clone(&shared.book), Arc::clone(&shared.fonts));
    }

    let mut book = (**shared.book).clone();
    let mut fonts = (*shared.fonts).clone();

    if config.system {
        for (path, info) in typst_kit::fonts::system() {
            if let Some(font) = path.load() {
                book.push(info);
                fonts.push(font);
            }
        }
    }

    for directory in &config.directories {
        for (path, info) in typst_kit::fonts::scan(std::path::Path::new(directory)) {
            if let Some(font) = path.load() {
                book.push(info);
                fonts.push(font);
            }
        }
    }

    (Arc::new(LazyHash::new(book)), Arc::new(fonts))
}

impl NetWorld {
    pub fn new(config: WorldConfig) -> Result<Self, TnError> {
        let shared = shared();
        let (book, fonts) = fonts_for(&config.fonts);

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
            fonts,
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
        if self.fonts.len() >= self.limits.max_fonts {
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
            Arc::make_mut(&mut self.fonts).push(font);
            added += 1;
        } else {
            // Not a single-face font: walk the collection (`ttc`/`otc`).
            for font in Font::iter(data) {
                book.push(font.info().clone());
                Arc::make_mut(&mut self.fonts).push(font);
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

    pub fn source_count(&self) -> usize {
        read(&self.sources).len()
    }

    pub fn font_count(&self) -> usize {
        self.fonts.len()
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
        self.fonts.get(index).cloned()
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
}
