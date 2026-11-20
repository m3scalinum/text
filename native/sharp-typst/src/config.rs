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
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct FontsConfig {
    /// Directories scanned recursively for font files.
    pub directories: Vec<String>,
    /// Whether to load the operating system's installed fonts.
    pub system: bool,
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
        serde_json::from_str(json).map_err(|e| {
            TnError::new(
                TN_ERR_INVALID_ARGUMENT,
                format!("invalid world config: {e}"),
            )
        })
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
