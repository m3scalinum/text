//! World configuration, as received from the managed layer.

use serde::Deserialize;

use crate::ffi::{TN_ERR_INVALID_ARGUMENT, TnError};

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorldConfig {
    #[serde(default)]
    pub clock: ClockConfig,
    #[serde(default)]
    pub limits: Limits,
    #[serde(default)]
    pub packages: PackagesConfig,
    #[serde(default)]
    pub fonts: FontsConfig,
}

/// Where fonts come from, beyond the ones embedded in the binary.
///
/// Both are opt-in: a compile never reads the host's font directories unless the caller asks.
/// Once it does, the metadata scan is shared by every world of the process (see
/// [`crate::fonts`]), and the switch that bypasses it is here.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct FontsConfig {
    /// Directories scanned recursively for font files.
    pub directories: Vec<String>,
    /// Whether to load the operating system's installed fonts.
    pub system: bool,
    /// Scans the sources again instead of reusing the process-wide index, and replaces it.
    ///
    /// This is how a font installed after the process started becomes visible: every other world
    /// accepts the index it finds, and one `refresh` world rebuilds it for all of them.
    pub refresh: bool,
    /// Whether the persistent index is read and written. On by default.
    pub cache: bool,
    /// Where the persistent index lives. `None` or empty means the user's cache directory.
    pub cache_dir: Option<String>,
}

impl Default for FontsConfig {
    fn default() -> Self {
        Self {
            directories: Vec::new(),
            system: false,
            refresh: false,
            cache: true,
            cache_dir: None,
        }
    }
}

impl FontsConfig {
    /// The directory the persistent index is read from and written to, if it is enabled.
    pub fn cache_path(&self) -> Option<std::path::PathBuf> {
        if !self.cache {
            return None;
        }

        match self.cache_dir.as_deref() {
            None | Some("") => crate::fonts::default_cache_dir(),
            Some(directory) => Some(std::path::PathBuf::from(directory)),
        }
    }
}

/// Where `@namespace/name:version` packages come from.
///
/// The default is offline: a compile never touches the network unless the host asks for it.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct PackagesConfig {
    /// Directory for downloaded packages. `None` uses the system cache directory.
    pub cache_dir: Option<String>,
    /// Directory for local packages. `None` uses the system data directory.
    pub data_dir: Option<String>,
    /// Whether a package missing from both directories may be fetched from the registry.
    pub download: bool,
    /// Registry base URL. `None` uses the official Typst Universe.
    pub registry: Option<String>,
    /// Directory of a package mirror: a `mirror.json` listing every package and every file's
    /// SHA-256, plus the packages themselves.
    ///
    /// Setting it switches the world to cache-only: the mirror is the only source, downloads are
    /// refused, and the manifest is verified before the world is built. See
    /// `docs/specifications/03-compilation.md` §15.
    pub mirror: Option<String>,
}

impl PackagesConfig {
    /// Validates and normalises a mirror, in place.
    ///
    /// The mirror becomes both the cache and the data directory, so nothing outside it can satisfy
    /// a package, and `download` is refused rather than silently ignored: a host that asked for a
    /// verified mirror and kept downloads on would get packages that verification never saw.
    ///
    /// With no mirror configured this does nothing at all — the resolution path, the defaults and
    /// the failure messages stay exactly what they were.
    pub fn normalize_mirror(&mut self) -> Result<(), TnError> {
        let Some(mirror) = self.mirror.clone() else {
            return Ok(());
        };

        if mirror.trim().is_empty() {
            return Err(TnError::invalid_argument(
                "packages.mirror must not be empty",
            ));
        }

        if self.download {
            return Err(TnError::invalid_argument(
                "packages.mirror is cache-only: set packages.download to false",
            ));
        }

        for (name, directory) in [("cacheDir", &self.cache_dir), ("dataDir", &self.data_dir)] {
            if let Some(directory) = directory
                && directory != &mirror
            {
                return Err(TnError::invalid_argument(format!(
                    "packages.{name} is `{directory}`, which is not the mirror `{mirror}`: a \
                     mirrored world resolves packages only from its mirror"
                )));
            }
        }

        crate::packages::Mirror::open(&mirror)
            .and_then(|mirror| mirror.verify())
            .map_err(|error| error.to_tn_error())?;

        self.cache_dir = Some(mirror.clone());
        self.data_dir = Some(mirror);

        Ok(())
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum ClockConfig {
    /// Deterministic clock: the default.
    Fixed {
        #[serde(default = "default_year")]
        year: i32,
        #[serde(default = "default_month")]
        month: u8,
        #[serde(default = "default_day")]
        day: u8,
        #[serde(default)]
        hour: u8,
        #[serde(default)]
        minute: u8,
        #[serde(default)]
        second: u8,
    },
    /// Real clock: opt-in, non-reproducible.
    System,
}

impl Default for ClockConfig {
    fn default() -> Self {
        Self::Fixed {
            year: default_year(),
            month: default_month(),
            day: default_day(),
            hour: 0,
            minute: 0,
            second: 0,
        }
    }
}

fn default_year() -> i32 {
    2024
}
fn default_month() -> u8 {
    1
}
fn default_day() -> u8 {
    1
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Limits {
    #[serde(default = "default_max_source_bytes")]
    pub max_source_bytes: usize,
    #[serde(default = "default_max_asset_bytes")]
    pub max_asset_bytes: usize,
    #[serde(default = "default_max_font_bytes")]
    pub max_font_bytes: usize,
    #[serde(default = "default_max_sources")]
    pub max_sources: usize,
    #[serde(default = "default_max_fonts")]
    pub max_fonts: usize,
}

impl Default for Limits {
    fn default() -> Self {
        Self {
            max_source_bytes: default_max_source_bytes(),
            max_asset_bytes: default_max_asset_bytes(),
            max_font_bytes: default_max_font_bytes(),
            max_sources: default_max_sources(),
            max_fonts: default_max_fonts(),
        }
    }
}

fn default_max_source_bytes() -> usize {
    16 * 1024 * 1024
}
fn default_max_asset_bytes() -> usize {
    64 * 1024 * 1024
}
fn default_max_font_bytes() -> usize {
    64 * 1024 * 1024
}
fn default_max_sources() -> usize {
    1024
}
fn default_max_fonts() -> usize {
    256
}

impl WorldConfig {
    pub fn parse(json: &str) -> Result<Self, TnError> {
        if json.trim().is_empty() {
            return Ok(Self::default());
        }
        let mut config: Self = serde_json::from_str(json).map_err(|e| {
            TnError::new(
                TN_ERR_INVALID_ARGUMENT,
                format!("invalid world config: {e}"),
            )
        })?;

        // A package mirror is resolved and verified here, at the configuration boundary: a world is
        // never built over a mirror that does not match its manifest, so no compilation can read a
        // file the manifest does not describe.
        config.packages.normalize_mirror()?;

        Ok(config)
    }
}

/// Compilation options, as received from the managed layer.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CompileOptions {
    #[serde(default = "default_root")]
    pub root: String,
    #[serde(default = "default_document")]
    pub document: String,
}

impl Default for CompileOptions {
    fn default() -> Self {
        Self {
            root: default_root(),
            document: default_document(),
        }
    }
}

fn default_root() -> String {
    "main.typ".to_string()
}

fn default_document() -> String {
    "paged".to_string()
}

impl CompileOptions {
    pub fn parse(json: &str) -> Result<Self, TnError> {
        if json.trim().is_empty() {
            return Ok(Self::default());
        }
        serde_json::from_str(json).map_err(|e| {
            TnError::new(
                TN_ERR_INVALID_ARGUMENT,
                format!("invalid compile options: {e}"),
            )
        })
    }
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    use super::*;
    use crate::ffi::TN_ERR_FILE;
    use crate::packages::{FORMAT, Mirror, sha256_hex};

    /// A mirror on disk, removed when the test ends.
    struct MirrorDir {
        root: PathBuf,
    }

    impl MirrorDir {
        fn new(label: &str) -> Self {
            use std::sync::atomic::{AtomicUsize, Ordering};

            static COUNTER: AtomicUsize = AtomicUsize::new(0);
            let root = std::env::temp_dir().join(format!(
                "sharp-typst-config-{}-{}-{label}",
                std::process::id(),
                COUNTER.fetch_add(1, Ordering::Relaxed)
            ));
            let _ = std::fs::remove_dir_all(&root);
            std::fs::create_dir_all(&root).expect("mirror root");

            Self { root }
        }

        fn package(&self, spec: &str, file: &str, text: &str) -> PathBuf {
            let (namespace, rest) = spec
                .trim_start_matches('@')
                .split_once('/')
                .expect("a name");
            let (name, version) = rest.split_once(':').expect("a version");
            let directory = self.root.join(namespace).join(name).join(version);
            std::fs::create_dir_all(&directory).expect("package dir");
            let path = directory.join(file);
            std::fs::write(&path, text).expect("write");
            path
        }

        fn manifest(&self, entries: &[(&str, &str, &Path)]) {
            let mut packages = serde_json::Map::new();

            for (spec, file, path) in entries {
                let bytes = std::fs::read(path).expect("read");
                packages.insert(
                    spec.to_string(),
                    serde_json::json!([{
                        "path": file,
                        "sha256": sha256_hex(&bytes),
                        "size": bytes.len(),
                    }]),
                );
            }

            std::fs::write(
                Mirror::manifest_path(&self.root),
                serde_json::json!({ "format": FORMAT, "packages": packages }).to_string(),
            )
            .expect("write manifest");
        }

        /// The world configuration JSON, with the mirror set and `extra` merged into `packages`.
        fn json_with(&self, extra: serde_json::Value) -> String {
            let mut packages = serde_json::json!({ "mirror": self.root.display().to_string() });

            if let (Some(target), Some(source)) = (packages.as_object_mut(), extra.as_object()) {
                for (key, value) in source {
                    target.insert(key.clone(), value.clone());
                }
            }

            serde_json::json!({ "packages": packages }).to_string()
        }

        fn json(&self) -> String {
            self.json_with(serde_json::json!({}))
        }
    }

    impl Drop for MirrorDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.root);
        }
    }

    #[test]
    fn a_verified_mirror_becomes_the_only_package_source() {
        let mirror = MirrorDir::new("only-source");
        let file = mirror.package("@preview/cetz:0.3.1", "lib.typ", "#let cetz = 1\n");
        mirror.manifest(&[("@preview/cetz:0.3.1", "lib.typ", &file)]);

        let config = WorldConfig::parse(&mirror.json()).expect("a verified mirror is accepted");
        let root = mirror.root.display().to_string();

        assert_eq!(config.packages.cache_dir.as_deref(), Some(root.as_str()));
        assert_eq!(config.packages.data_dir.as_deref(), Some(root.as_str()));
        assert!(!config.packages.download);
    }

    #[test]
    fn a_tampered_mirror_is_refused_at_the_configuration_boundary() {
        let mirror = MirrorDir::new("tampered");
        let file = mirror.package("@preview/cetz:0.3.1", "lib.typ", "#let cetz = 1\n");
        mirror.manifest(&[("@preview/cetz:0.3.1", "lib.typ", &file)]);
        std::fs::write(&file, "#let cetz = 2\n").expect("tamper");

        let error = WorldConfig::parse(&mirror.json()).expect_err("a tampered mirror is refused");

        assert_eq!(error.code, TN_ERR_FILE);
        assert!(error.json.contains("@preview/cetz:0.3.1"), "{}", error.json);
        assert!(error.json.contains("lib.typ"), "{}", error.json);
    }

    #[test]
    fn a_mirror_and_downloads_are_mutually_exclusive() {
        let mirror = MirrorDir::new("download");
        let file = mirror.package("@preview/cetz:0.3.1", "lib.typ", "#let cetz = 1\n");
        mirror.manifest(&[("@preview/cetz:0.3.1", "lib.typ", &file)]);

        let json = mirror.json_with(serde_json::json!({ "download": true }));

        let error = WorldConfig::parse(&json).expect_err("downloads are refused with a mirror");

        assert_eq!(error.code, TN_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("cache-only"), "{}", error.json);
    }

    #[test]
    fn a_cache_directory_outside_the_mirror_is_refused() {
        let mirror = MirrorDir::new("elsewhere");
        let file = mirror.package("@preview/cetz:0.3.1", "lib.typ", "#let cetz = 1\n");
        mirror.manifest(&[("@preview/cetz:0.3.1", "lib.typ", &file)]);

        let json = mirror.json_with(serde_json::json!({ "cacheDir": "/somewhere/else" }));

        let error = WorldConfig::parse(&json).expect_err("a second package source is refused");

        assert_eq!(error.code, TN_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("cacheDir"), "{}", error.json);
    }

    #[test]
    fn a_mirror_without_a_manifest_is_refused() {
        let mirror = MirrorDir::new("no-manifest");
        mirror.package("@preview/cetz:0.3.1", "lib.typ", "#let cetz = 1\n");

        let error = WorldConfig::parse(&mirror.json()).expect_err("an unlisted mirror is refused");

        assert_eq!(error.code, TN_ERR_FILE);
        assert!(error.json.contains("mirror.json"), "{}", error.json);
    }

    /// The regression that matters for every existing host: no `mirror` key, and the configuration
    /// is the one it always was.
    #[test]
    fn a_configuration_without_a_mirror_is_untouched() {
        let config = WorldConfig::parse(r#"{"packages":{"download":true}}"#).expect("parsed");

        assert_eq!(config.packages.mirror, None);
        assert_eq!(config.packages.cache_dir, None);
        assert_eq!(config.packages.data_dir, None);
        assert!(config.packages.download);
    }

    #[test]
    fn the_mirror_key_is_optional_in_the_json() {
        let config: PackagesConfig = serde_json::from_str("{}").expect("parsed");

        assert_eq!(config.mirror, None);
        assert!(!config.download);
    }

    /// An empty mirror path is a typo, not a request for the system directories.
    #[test]
    fn an_empty_mirror_path_is_refused() {
        let error = WorldConfig::parse(r#"{"packages":{"mirror":"  "}}"#)
            .expect_err("an empty mirror is refused");

        assert_eq!(error.code, TN_ERR_INVALID_ARGUMENT);
    }
}
