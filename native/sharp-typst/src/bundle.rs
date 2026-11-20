//! Bundle export: the files produced by the `bundle` target.
//!
//! The `typst-bundle` crate supplies the `Output` implementation and the exporter; this module
//! only adapts them to the ABI's shape (a manifest plus per-file bytes).

use typst::diag::SourceResult;
use typst_bundle::{Bundle, BundleOptions, VirtualFs};

/// Exports a compiled bundle into its files, keyed by virtual path.
pub fn export(bundle: &Bundle) -> SourceResult<VirtualFs> {
    typst_bundle::export(bundle, &BundleOptions::default())
}

/// The manifest: every file path with its size, in bundle order.
pub fn manifest(files: &VirtualFs) -> Vec<u8> {
    let entries: Vec<serde_json::Value> = files
        .iter()
        .map(|(path, data)| {
            serde_json::json!({
                "path": path.get_without_slash(),
                "size": data.len(),
            })
        })
        .collect();

    serde_json::to_vec(&serde_json::json!({ "assets": entries }))
        .expect("a manifest of strings and integers always serializes")
}

/// The bytes of one file, by index into the manifest.
pub fn file(files: &VirtualFs, index: usize) -> Option<&[u8]> {
    files.get_index(index).map(|(_, data)| data.as_slice())
}

/// The main HTML document of the bundle: the first `.html` file at the root.
pub fn html(files: &VirtualFs) -> Option<&[u8]> {
    files
        .iter()
        .find(|(path, _)| {
            let name = path.get_without_slash();
            name.ends_with(".html") && !name.contains('/')
        })
        .map(|(_, data)| data.as_slice())
}
