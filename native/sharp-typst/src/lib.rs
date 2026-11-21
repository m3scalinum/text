//! C ABI over the Typst compiler.
//!
//! The contract is specified in `docs/specifications/02-native-abi.md`. Every entry point
//! is `extern "C"`, never unwinds, and reports failures through a trailing `TnError**`.

mod bundle;
mod config;
mod diag;
mod evaluate;
mod export;
mod ffi;
mod page_text;
mod semantic;
mod world;

use std::ffi::{CString, c_char};
use std::sync::OnceLock;
use std::sync::atomic::{AtomicUsize, Ordering};

use typst::foundations::Output;
use typst_html::HtmlDocument;
use typst_layout::PagedDocument;

use crate::config::{CompileOptions, WorldConfig};
use crate::ffi::{
    TN_ERR_COMPILATION, TN_ERR_EXPORT, TN_ERR_INVALID_HANDLE, TN_ERR_UNSUPPORTED, TnBuffer,
    TnError, guard_ptr, guard_status, read_bytes, read_str,
};
use crate::world::NetWorld;

/// Opaque world handle.
pub struct TnWorld {
    inner: NetWorld,
}

/// A compiled document, in whichever layout pass produced it.
pub enum Document {
    Paged(PagedDocument),
    Html(HtmlDocument),
    /// A bundle, already exported into its files.
    Bundle {
        files: typst_bundle::VirtualFs,
    },
}

/// Opaque compiled-document handle.
pub struct TnDocument {
    document: Document,
    diagnostics: String,
}

// ---------------------------------------------------------------------------
// Live handles
// ---------------------------------------------------------------------------

/// Handles alive right now, so a host can prove that every one it created was released.
///
/// The process working set cannot witness that: it is process-wide, and every platform retains
/// freed native blocks differently — a soak that was flat on x64 grew by 492 MiB on Windows x64
/// and by 78 MiB on linux-arm64 for the same code. The handle count is the property this library
/// owns, and it is exact.
static LIVE_WORLDS: AtomicUsize = AtomicUsize::new(0);
static LIVE_DOCUMENTS: AtomicUsize = AtomicUsize::new(0);

impl Drop for TnWorld {
    fn drop(&mut self) {
        LIVE_WORLDS.fetch_sub(1, Ordering::Relaxed);
    }
}

impl Drop for TnDocument {
    fn drop(&mut self) {
        LIVE_DOCUMENTS.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a world handle is created: counting it here is what keeps the count exact.
fn world_handle(inner: NetWorld) -> Box<TnWorld> {
    LIVE_WORLDS.fetch_add(1, Ordering::Relaxed);
    Box::new(TnWorld { inner })
}

/// The only way a document handle is created. See [`world_handle`].
fn document_handle(document: Document, diagnostics: String) -> Box<TnDocument> {
    LIVE_DOCUMENTS.fetch_add(1, Ordering::Relaxed);
    Box::new(TnDocument {
        document,
        diagnostics,
    })
}

/// Number of world handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn tn_world_live_count() -> u32 {
    LIVE_WORLDS.load(Ordering::Relaxed) as u32
}

/// Number of document handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn tn_document_live_count() -> u32 {
    LIVE_DOCUMENTS.load(Ordering::Relaxed) as u32
}

// ---------------------------------------------------------------------------
// Version handshake
// ---------------------------------------------------------------------------

#[unsafe(no_mangle)]
pub extern "C" fn tn_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn tn_abi_version_string() -> *const c_char {
    static VERSION: OnceLock<CString> = OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("1.0.0").expect("literal has no interior NUL"))
        .as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn tn_typst_version() -> *const c_char {
    static VERSION: OnceLock<CString> = OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("0.15.1").expect("literal has no interior NUL"))
        .as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn tn_build_info() -> *const c_char {
    static INFO: OnceLock<CString> = OnceLock::new();
    INFO.get_or_init(|| {
        let json = serde_json::json!({
            "target": std::env::consts::ARCH.to_string() + "-" + std::env::consts::OS,
            "profile": if cfg!(debug_assertions) { "debug" } else { "release" },
            "typst": "0.15.1",
        })
        .to_string();
        CString::new(json).expect("build info has no interior NUL")
    })
    .as_ptr()
}

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

/// # Safety
/// `error` must come from this library and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_error_free(error: *mut TnError) {
    if error.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(error) });
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_error_code(error: *const TnError) -> i32 {
    if error.is_null() {
        return 0;
    }
    unsafe { (*error).code }
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_error_json(error: *const TnError) -> TnBuffer {
    if error.is_null() {
        return TnBuffer::empty();
    }
    TnBuffer::from_vec(unsafe { (*error).json.clone() }.into_bytes())
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_error_traceback(error: *const TnError) -> TnBuffer {
    if error.is_null() {
        return TnBuffer::empty();
    }
    TnBuffer::from_vec(unsafe { (*error).traceback.clone() }.into_bytes())
}

// ---------------------------------------------------------------------------
// Buffers
// ---------------------------------------------------------------------------

/// # Safety
/// `buffer` must come from this library and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_buffer_free(buffer: TnBuffer) {
    unsafe { buffer.free() };
}

// ---------------------------------------------------------------------------
// World
// ---------------------------------------------------------------------------

/// # Safety
/// `config_json` is null or a NUL-terminated UTF-8 string; `out_error` is null or writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_create(
    config_json: *const c_char,
    out_error: *mut *mut TnError,
) -> *mut TnWorld {
    unsafe {
        guard_ptr(out_error, || {
            let json = if config_json.is_null() {
                ""
            } else {
                read_str(config_json, "config")?
            };
            let config = WorldConfig::parse(json)?;
            let inner = NetWorld::new(config)?;
            Ok(world_handle(inner))
        })
    }
}

/// # Safety
/// `world` must come from `tn_world_create` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_free(world: *mut TnWorld) {
    if world.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(world) });
}

/// # Safety
/// `world` is a valid handle; `path` a NUL-terminated UTF-8 string; `data` valid for `len`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_add_source(
    world: *mut TnWorld,
    path: *const c_char,
    data: *const u8,
    len: usize,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let world = world_mut(world)?;
            let path = read_str(path, "path")?;
            let bytes = read_bytes(data, len, "source")?;
            let text = String::from_utf8(bytes)
                .map_err(|_| TnError::invalid_argument("source is not valid UTF-8"))?;
            world.inner.add_source(path, text)
        })
    }
}

/// # Safety
/// Same contract as [`tn_world_add_source`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_add_asset(
    world: *mut TnWorld,
    path: *const c_char,
    data: *const u8,
    len: usize,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let world = world_mut(world)?;
            let path = read_str(path, "path")?;
            let bytes = read_bytes(data, len, "asset")?;
            world.inner.add_asset(path, bytes)
        })
    }
}

/// # Safety
/// Same contract as [`tn_world_add_source`]; `index` addresses a face in a collection.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_add_font(
    world: *mut TnWorld,
    data: *const u8,
    len: usize,
    index: u32,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let world = world_mut(world)?;
            let bytes = read_bytes(data, len, "font")?;
            world.inner.add_font(bytes, index)
        })
    }
}

/// # Safety
/// `world` is a valid handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_clear_sources(
    world: *mut TnWorld,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let world = world_mut(world)?;
            world.inner.clear_sources();
            Ok(())
        })
    }
}

/// # Safety
/// `world` is null or a valid handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_font_count(world: *const TnWorld) -> u32 {
    if world.is_null() {
        return 0;
    }
    unsafe { (*world).inner.font_count() as u32 }
}

/// # Safety
/// `world` is null or a valid handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_source_count(world: *const TnWorld) -> u32 {
    if world.is_null() {
        return 0;
    }
    unsafe { (*world).inner.source_count() as u32 }
}

// ---------------------------------------------------------------------------
// Compilation
// ---------------------------------------------------------------------------

/// # Safety
/// `world` is a valid handle; `options_json` null or NUL-terminated UTF-8.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_compile(
    world: *mut TnWorld,
    options_json: *const c_char,
    out_error: *mut *mut TnError,
) -> *mut TnDocument {
    unsafe {
        guard_ptr(out_error, || {
            let world = world_mut(world)?;
            let json = if options_json.is_null() {
                ""
            } else {
                read_str(options_json, "options")?
            };
            let options = CompileOptions::parse(json)?;

            world.inner.set_root(&options.root)?;

            let (document, diagnostics) = match options.document.as_str() {
                "paged" => compile_as::<PagedDocument>(&world.inner, Document::Paged)?,
                "html" => compile_as::<HtmlDocument>(&world.inner, Document::Html)?,
                "bundle" => {
                    let warned = typst::compile::<typst_bundle::Bundle>(&world.inner);
                    let diagnostics = diag::to_json(&world.inner, &warned.warnings, "ok");

                    let bundle = match warned.output {
                        Ok(bundle) => bundle,
                        Err(errors) => {
                            return Err(TnError::with_json(
                                TN_ERR_COMPILATION,
                                diag::to_json(&world.inner, &errors, "compilation"),
                            ));
                        }
                    };

                    let files = bundle::export(&bundle).map_err(|errors| {
                        TnError::with_json(
                            TN_ERR_EXPORT,
                            diag::to_json(&world.inner, &errors, "export"),
                        )
                    })?;

                    (Document::Bundle { files }, diagnostics)
                }
                other => {
                    return Err(TnError::new(
                        TN_ERR_UNSUPPORTED,
                        format!("document kind `{other}` is not supported"),
                    ));
                }
            };

            Ok(document_handle(document, diagnostics))
        })
    }
}

/// # Safety
/// `document` must come from `tn_compile` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_free(document: *mut TnDocument) {
    if document.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(document) });
}

/// # Safety
/// `document` is null or a valid handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_page_count(document: *const TnDocument) -> u32 {
    if document.is_null() {
        return 0;
    }
    match unsafe { &(*document).document } {
        Document::Paged(paged) => paged.pages().len() as u32,
        // An HTML or bundle document has no pages; the caller asked for a semantic tree.
        Document::Html(_) | Document::Bundle { .. } => 0,
    }
}

/// # Safety
/// `document` is a valid handle; `out_json` is writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_diagnostics(
    document: *const TnDocument,
    out_json: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out_json.is_null() {
                return Err(TnError::invalid_argument("out_json must not be null"));
            }
            let json = (*document).diagnostics.clone();
            *out_json = TnBuffer::from_vec(json.into_bytes());
            Ok(())
        })
    }
}

/// # Safety
/// `document` is a valid handle; `format` a NUL-terminated UTF-8 string; `out` writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_export(
    document: *const TnDocument,
    format: *const c_char,
    options_json: *const c_char,
    out: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }
            let format = read_str(format, "format")?;
            let options = if options_json.is_null() {
                ""
            } else {
                read_str(options_json, "options")?
            };
            let bytes = export::export(&(*document).document, format, options)?;
            *out = TnBuffer::from_vec(bytes);
            Ok(())
        })
    }
}

/// A pull reader over the bytes of one export.
///
/// The export is encoded once, in Rust, and copied out in caller-sized chunks, so a large PDF
/// never crosses the ABI in a single buffer and the managed side never holds a second full copy.
pub struct TnExportReader {
    bytes: Vec<u8>,
    offset: usize,
}

/// Opens a reader over the exported bytes. The document must outlive the reader; the managed
/// layer keeps its handle referenced until `tn_export_reader_free`.
///
/// # Safety
/// `document` is a valid handle; `format` a NUL-terminated UTF-8 string; `out` writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_export_reader(
    document: *const TnDocument,
    format: *const c_char,
    options_json: *const c_char,
    out: *mut *mut TnExportReader,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }
            let format = read_str(format, "format")?;
            let options = if options_json.is_null() {
                ""
            } else {
                read_str(options_json, "options")?
            };
            let bytes = export::export(&(*document).document, format, options)?;
            *out = Box::into_raw(Box::new(TnExportReader { bytes, offset: 0 }));
            Ok(())
        })
    }
}

/// Copies up to `capacity` bytes into `buffer` and reports how many were written. A `written`
/// of 0 means the reader is exhausted.
///
/// # Safety
/// `reader` is a valid handle; `buffer` holds `capacity` writable bytes; `written` writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_export_reader_read(
    reader: *mut TnExportReader,
    buffer: *mut u8,
    capacity: usize,
    written: *mut usize,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if reader.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "export reader handle is null",
                ));
            }
            if written.is_null() {
                return Err(TnError::invalid_argument("written must not be null"));
            }
            if capacity > 0 && buffer.is_null() {
                return Err(TnError::invalid_argument("buffer must not be null"));
            }

            let reader = &mut (*reader);
            let remaining = reader.bytes.len() - reader.offset;
            let count = remaining.min(capacity);

            if count > 0 {
                std::ptr::copy_nonoverlapping(
                    reader.bytes.as_ptr().add(reader.offset),
                    buffer,
                    count,
                );
                reader.offset += count;
            }

            *written = count;
            Ok(())
        })
    }
}

/// Frees the reader and the encoded export it holds.
///
/// # Safety
/// `reader` is null or a handle from `tn_document_export_reader`, freed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_export_reader_free(reader: *mut TnExportReader) {
    if reader.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(reader) });
}

/// Clones a world, sharing the library, the font book and the fonts.
///
/// # Safety
/// `world` must be a live handle from `tn_world_create`; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_clone(
    world: *const TnWorld,
    out: *mut *mut TnWorld,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if world.is_null() {
                return Err(TnError::new(TN_ERR_INVALID_HANDLE, "world handle is null"));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }

            let clone = (*world).inner.clone_world()?;
            *out = Box::into_raw(world_handle(clone));
            Ok(())
        })
    }
}

/// The font families a world can resolve, as a JSON array of strings.
///
/// # Safety
/// `world` must be a live handle from `tn_world_create`; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_world_font_families(
    world: *const TnWorld,
    out: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if world.is_null() {
                return Err(TnError::new(TN_ERR_INVALID_HANDLE, "world handle is null"));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }

            let families = (*world).inner.font_families();
            let json = serde_json::to_vec(&families)
                .map_err(|e| TnError::new(TN_ERR_EXPORT, format!("font families failed: {e}")))?;

            *out = TnBuffer::from_vec(json);
            Ok(())
        })
    }
}

/// Evaluates the document's introspection data: outline, labels and metadata, as JSON.
///
/// # Safety
/// `document` must be a live handle from `tn_compile`; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_evaluate(
    document: *const TnDocument,
    out: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }
            let bytes = evaluate::evaluate(&(*document).document)?;
            *out = TnBuffer::from_vec(bytes);
            Ok(())
        })
    }
}

/// The text of one page, read from the paged layout in frame order. `page` is zero-based.
///
/// # Safety
/// `document` must be a live handle from `tn_compile`; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_page_text(
    document: *const TnDocument,
    page: u32,
    out: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }
            let text = page_text::page_text(&(*document).document, page as usize)?;
            *out = TnBuffer::from_vec(text.into_bytes());
            Ok(())
        })
    }
}

/// # Safety
/// `document` is a valid handle; `out` is writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_bundle_asset(
    document: *const TnDocument,
    index: u32,
    out: *mut TnBuffer,
    out_error: *mut *mut TnError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if document.is_null() {
                return Err(TnError::new(
                    TN_ERR_INVALID_HANDLE,
                    "document handle is null",
                ));
            }
            if out.is_null() {
                return Err(TnError::invalid_argument("out must not be null"));
            }
            let bytes = export::bundle_asset(&(*document).document, index as usize)?;
            *out = TnBuffer::from_vec(bytes);
            Ok(())
        })
    }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// # Safety
/// `world` is null or a valid handle.
unsafe fn world_mut<'a>(world: *mut TnWorld) -> Result<&'a mut TnWorld, TnError> {
    if world.is_null() {
        return Err(TnError::new(TN_ERR_INVALID_HANDLE, "world handle is null"));
    }
    Ok(unsafe { &mut *world })
}

/// Runs one layout pass and wraps the output, keeping warnings on success.
fn compile_as<T: Output>(
    world: &NetWorld,
    wrap: impl FnOnce(T) -> Document,
) -> Result<(Document, String), TnError> {
    let warned = typst::compile::<T>(world);
    let diagnostics = diag::to_json(world, &warned.warnings, "ok");

    match warned.output {
        Ok(output) => Ok((wrap(output), diagnostics)),
        Err(errors) => Err(TnError::with_json(
            TN_ERR_COMPILATION,
            diag::to_json(world, &errors, "compilation"),
        )),
    }
}
