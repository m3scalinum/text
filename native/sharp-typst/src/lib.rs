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
mod fingerprint;
mod fonts;
mod packages;
mod page_text;
mod pdf_fonts;
mod semantic;
mod world;

use std::ffi::{CString, c_char};
use std::sync::OnceLock;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Instant;

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
    metrics: CompileMetrics,
}

/// What one compilation cost, measured where the work happens.
///
/// The managed side measures the whole call — marshalling included — and this measures the part
/// only the library can see: how long Typst itself ran, and what the resulting world and document
/// are made of. The two numbers together answer "is the time in the compiler or around it".
#[derive(Debug, Clone, Copy, Default)]
struct CompileMetrics {
    /// Nanoseconds spent inside the compile: `typst::compile`, plus the bundle export when the
    /// target is a bundle, because a bundle document only exists once its files are written.
    compile_nanos: u64,
    /// Pages the layout produced. Zero for HTML and bundle documents, which have none.
    pages: u32,
    /// Fonts the world could resolve, embedded and host together.
    fonts_total: u32,
    /// How many of them are the ones embedded in the binary.
    fonts_embedded: u32,
    /// Bytes of distinct font data the world held, each buffer counted once.
    font_bytes: u64,
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
fn document_handle(
    document: Document,
    diagnostics: String,
    metrics: CompileMetrics,
) -> Box<TnDocument> {
    LIVE_DOCUMENTS.fetch_add(1, Ordering::Relaxed);
    Box::new(TnDocument {
        document,
        diagnostics,
        metrics,
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

/// The ABI revision this crate implements.
///
/// Revision 2 adds [`tn_abi_version_min`] and the negotiated range; every symbol that existed in
/// revision 1 keeps its name and its signature, so a revision 1 host still works against this
/// library.
pub const TN_ABI_VERSION: u32 = 2;

/// The oldest ABI revision this crate still answers for.
///
/// A revision stays served for two ABI revisions — its own and the next one — so revision 1 is
/// dropped when revision 3 ships. [`tn_abi_version`] and this constant together are the range a
/// host intersects with its own.
pub const TN_ABI_MIN_VERSION: u32 = 1;

/// The ABI revision this library implements.
///
/// A host reads it once, at load, and compares it with the range it was built for. The library
/// never refuses a host on its own: it cannot know which managed assembly called it.
#[unsafe(no_mangle)]
pub extern "C" fn tn_abi_version() -> u32 {
    TN_ABI_VERSION
}

/// The oldest ABI revision this library still serves.
///
/// Exported for introspection and conformance only: a managed layer that also runs against
/// revision 1 libraries must not call it, because revision 1 does not export it.
#[unsafe(no_mangle)]
pub extern "C" fn tn_abi_version_min() -> u32 {
    TN_ABI_MIN_VERSION
}

/// Human-readable form of [`tn_abi_version`], for logs and error messages.
#[unsafe(no_mangle)]
pub extern "C" fn tn_abi_version_string() -> *const c_char {
    static VERSION: OnceLock<CString> = OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("2.0.0").expect("literal has no interior NUL"))
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

            // The clock covers the compiler, not the bookkeeping around it: parsing the options
            // and resolving the root are the caller's inputs, so costing them says nothing about
            // Typst. Everything between here and the handle is the work being measured.
            let compile_started = Instant::now();

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

            let metrics = CompileMetrics {
                compile_nanos: u64::try_from(compile_started.elapsed().as_nanos())
                    .unwrap_or(u64::MAX),
                pages: page_count(&document),
                fonts_total: world.inner.font_count() as u32,
                fonts_embedded: world.inner.embedded_font_count() as u32,
                font_bytes: world.inner.font_bytes(),
            };

            Ok(document_handle(document, diagnostics, metrics))
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
    page_count(unsafe { &(*document).document })
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

/// What the compilation cost, as JSON: the time inside Typst, the pages, the fonts it resolved and
/// the bytes they occupy.
///
/// The managed layer measures the whole call; this is the part only the library can see. A host
/// reads it once per document, when the result is attached, and needs no counter of its own — a
/// process-wide counter would be shared by every compilation running at the same time, which is
/// exactly the case a metrics API has to get right (Spec 03 §12).
///
/// # Safety
/// `document` is a valid handle; `out` is writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_metrics(
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

            let metrics = (*document).metrics;
            *out = TnBuffer::from_vec(metrics_json(&metrics).into_bytes());
            Ok(())
        })
    }
}

/// One fingerprint per page of a paged document, as JSON: `{"pages":[<u64>, ...]}`.
///
/// A fingerprint is a digest of what the page draws — geometry, glyph runs, shapes, images and
/// link destinations — with source spans and element tags left out, so editing text on one page
/// does not mark the pages after it. It is the comparison a host performs to answer "which pages
/// do I have to re-render", not a content address: two fingerprints are only meaningful against
/// each other, and only on the machine that produced them (Spec 03 §15).
///
/// An HTML or bundle document has no pages and yields an empty array. The fingerprints are
/// computed here rather than at compile time, so a caller that never asks pays nothing.
///
/// # Safety
/// `document` is a valid handle; `out` is writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tn_document_page_fingerprints(
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

            let fingerprints = match &(*document).document {
                Document::Paged(paged) => fingerprint::page_fingerprints(paged),
                Document::Html(_) | Document::Bundle { .. } => Vec::new(),
            };

            let json = serde_json::json!({ "pages": fingerprints }).to_string();
            *out = TnBuffer::from_vec(json.into_bytes());
            Ok(())
        })
    }
}

/// Drops every memoized compilation result this process holds.
///
/// Typst memoizes each stage of a compilation — parsing, evaluation, layout — in a process-wide
/// table keyed by that stage's inputs, and neither `typst::compile` nor this crate ever evicts.
/// That is what makes a second compilation of an edited document cheaper than a first one, and it
/// is also why a host that compiles thousands of *different* documents keeps all of their
/// intermediate results: the table only grows. Clearing it is the valve; the next compilation of
/// any document is cold again.
///
/// Safe to call at any time: a compilation running on another thread keeps the entries it already
/// looked up and simply recomputes what it had not reached yet.
#[unsafe(no_mangle)]
pub extern "C" fn tn_cache_clear() {
    // Age zero evicts every entry, whatever its age.
    typst::comemo::evict(0);
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

/// The metrics as the JSON object the managed layer decodes.
///
/// Serialising by hand, rather than deriving it, keeps the contract in one visible place: these
/// names are read by `CompileMetricsDto` on the managed side, and a rename here without one there
/// is a silent zero.
fn metrics_json(metrics: &CompileMetrics) -> String {
    serde_json::json!({
        "compileNanos": metrics.compile_nanos,
        "pages": metrics.pages,
        "fontsTotal": metrics.fonts_total,
        "fontsEmbedded": metrics.fonts_embedded,
        "fontBytes": metrics.font_bytes,
    })
    .to_string()
}

/// Pages a document has. An HTML or bundle document has none: the caller asked for a semantic
/// tree or a set of files, not for pages.
fn page_count(document: &Document) -> u32 {
    match document {
        Document::Paged(paged) => paged.pages().len() as u32,
        Document::Html(_) | Document::Bundle { .. } => 0,
    }
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

#[cfg(test)]
mod tests {
    use std::ffi::CStr;

    use super::*;

    /// The two numbers a host negotiates with must describe a range, not a point: the current
    /// revision, the oldest one still served, and exactly two revisions between them.
    #[test]
    fn the_abi_range_is_coherent() {
        // Compile-time facts: a bad constant should fail the build, not one test.
        const { assert!(TN_ABI_MIN_VERSION >= 1, "revision 0 is not an ABI") };
        const { assert!(TN_ABI_MIN_VERSION <= TN_ABI_VERSION) };
        const {
            assert!(
                TN_ABI_VERSION - TN_ABI_MIN_VERSION + 1 == 2,
                "a revision is served for two ABI revisions: its own and the next"
            )
        };

        assert_eq!(tn_abi_version(), TN_ABI_VERSION);
        assert_eq!(tn_abi_version_min(), TN_ABI_MIN_VERSION);
    }

    /// The string form is what an error message quotes, so it must name the revision the number
    /// reports. A drift here makes every diagnostic about a mismatch misleading.
    #[test]
    fn the_version_string_names_the_current_revision() {
        let text = unsafe { CStr::from_ptr(tn_abi_version_string()) }
            .to_str()
            .expect("the version literal is utf-8");

        assert_eq!(text, format!("{TN_ABI_VERSION}.0.0"));
    }

    /// A compilation is measurable offline: the library, the embedded fonts and the layout are all
    /// in the binary, so a minimal source proves that the numbers are produced by real work rather
    /// than defaulted.
    #[test]
    fn a_compilation_reports_its_time_pages_and_fonts() {
        let mut world = NetWorld::new(WorldConfig::default()).expect("world");
        world
            .add_source("main.typ", "= Hello".to_string())
            .expect("source");
        world.set_root("main.typ").expect("root");

        let started = Instant::now();
        let (document, _diagnostics) =
            compile_as::<PagedDocument>(&world, Document::Paged).expect("compile");
        let elapsed = started.elapsed();

        assert!(elapsed > std::time::Duration::ZERO);
        assert_eq!(page_count(&document), 1, "`= Hello` is one page");
        assert!(
            world.font_count() > 0,
            "the embedded fonts are in the binary"
        );
        assert_eq!(
            world.embedded_font_count(),
            world.font_count(),
            "a default world loads no system font"
        );
        assert!(world.font_bytes() > 0, "the embedded fonts have bytes");
    }

    /// The font inventory is read back out of a PDF this crate encoded: a real document, a real
    /// subset, and numbers that must agree with the file they came from.
    #[test]
    fn the_font_inventory_reads_a_real_export() {
        let mut world = NetWorld::new(WorldConfig::default()).expect("world");
        world
            .add_source("main.typ", "= Hello\n\nBody text, a few words.".to_string())
            .expect("source");
        world.set_root("main.typ").expect("root");

        let (document, _diagnostics) =
            compile_as::<PagedDocument>(&world, Document::Paged).expect("compile");

        let pdf = export::export(&document, "pdf", "").expect("pdf");
        let report = export::export(&document, "pdf-fonts", "").expect("pdf-fonts");
        let json: serde_json::Value =
            serde_json::from_slice(&report).expect("the report is valid JSON");

        assert_eq!(pdf.len() as u64, json["bytes"].as_u64().expect("bytes"));
        assert!(
            json["fontBytes"].as_u64().expect("fontBytes") > 0,
            "a text document embeds at least one font"
        );

        let fonts = json["fonts"].as_array().expect("fonts");
        assert!(!fonts.is_empty());
        assert!(
            fonts
                .iter()
                .all(|font| font["subset"] == serde_json::Value::Bool(true)),
            "krilla subsets every face it embeds"
        );
        assert!(
            fonts.iter().any(|font| font["family"]
                .as_str()
                .is_some_and(|name| name.contains("Libertinus"))),
            "the document is set in the embedded serif"
        );
    }

    /// The six names are the ABI: the managed decoder reads them by name, and a rename that is not
    /// mirrored there turns the metric into a silent zero.
    #[test]
    fn the_metrics_json_carries_every_field_the_host_reads() {
        let metrics = CompileMetrics {
            compile_nanos: 1_500_000,
            pages: 3,
            fonts_total: 12,
            fonts_embedded: 10,
            font_bytes: 2_048_576,
        };

        let json: serde_json::Value =
            serde_json::from_str(&metrics_json(&metrics)).expect("the metrics are valid JSON");

        assert_eq!(json["compileNanos"], 1_500_000);
        assert_eq!(json["pages"], 3);
        assert_eq!(json["fontsTotal"], 12);
        assert_eq!(json["fontsEmbedded"], 10);
        assert_eq!(json["fontBytes"], 2_048_576);
    }

    /// Three pages, of which the caller can vary one sentence on the first.
    fn three_pages(sentence: &str) -> String {
        format!(
            "#set page(paper: \"a4\")\n\
             #heading(level: 1)[First]\n\n\
             {sentence}\n\n\
             #pagebreak(weak: true)\n\
             #heading(level: 1)[Second]\n\n\
             #lorem(120)\n\n\
             #pagebreak(weak: true)\n\
             #heading(level: 1)[Third]\n\n\
             #lorem(120)\n"
        )
    }

    /// The fingerprints of a compilation, through the real entry point.
    fn fingerprints_of(world: &NetWorld) -> Vec<u64> {
        let (document, _diagnostics) =
            compile_as::<PagedDocument>(world, Document::Paged).expect("compile");
        let handle = document_handle(document, String::new(), CompileMetrics::default());
        let pointer = Box::into_raw(handle);

        let mut out = TnBuffer::empty();
        let status =
            unsafe { tn_document_page_fingerprints(pointer, &mut out, std::ptr::null_mut()) };
        assert_eq!(status, 0, "the fingerprint call succeeds");

        let bytes = unsafe { std::slice::from_raw_parts(out.ptr, out.len).to_vec() };
        unsafe { out.free() };
        unsafe { tn_document_free(pointer) };

        let json: serde_json::Value = serde_json::from_slice(&bytes).expect("valid JSON");
        json["pages"]
            .as_array()
            .expect("an array of pages")
            .iter()
            .map(|value| value.as_u64().expect("a u64 fingerprint"))
            .collect()
    }

    /// The comparison `CompileChanged` performs: editing one sentence on page one moves the
    /// fingerprint of page one and of nothing else, even though every later span in the source
    /// shifted by the length of the edit.
    #[test]
    fn fingerprints_mark_only_the_page_that_changed() {
        let world = NetWorld::new(WorldConfig::default()).expect("world");

        world
            .add_source("main.typ", three_pages("The first wording."))
            .expect("add_source");
        let before = fingerprints_of(&world);
        assert_eq!(before.len(), 3, "the document has three pages");

        world
            .add_source("main.typ", three_pages("The second, longer wording."))
            .expect("add_source");
        let after = fingerprints_of(&world);

        assert_eq!(after.len(), 3);
        assert_ne!(before[0], after[0], "the edited page is marked");
        assert_eq!(before[1], after[1], "page two did not change");
        assert_eq!(before[2], after[2], "page three did not change");
    }

    /// The same source compiled twice yields the same fingerprints: the digest has no identity in
    /// it, so it can be compared with a compilation that happened on another world or another
    /// thread.
    #[test]
    fn fingerprints_of_the_same_source_are_equal() {
        let first = NetWorld::new(WorldConfig::default()).expect("world");
        first
            .add_source("main.typ", three_pages("Stable."))
            .expect("add_source");
        let one = fingerprints_of(&first);

        let second = NetWorld::new(WorldConfig::default()).expect("world");
        second
            .add_source("main.typ", three_pages("Stable."))
            .expect("add_source");
        let two = fingerprints_of(&second);

        assert_eq!(
            one, two,
            "a fingerprint is a function of the page, not of a world"
        );
    }

    /// An HTML document has no pages, so it has no page fingerprints: the array is empty rather
    /// than an error, and the managed layer reports it as "no pages to compare".
    #[test]
    fn a_document_without_pages_has_no_fingerprints() {
        let world = NetWorld::new(WorldConfig::default()).expect("world");
        world
            .add_source("main.typ", "= Hello\n\nBody.".to_string())
            .expect("add_source");

        let (document, _diagnostics) =
            compile_as::<HtmlDocument>(&world, Document::Html).expect("compile");
        let handle = Box::into_raw(document_handle(
            document,
            String::new(),
            CompileMetrics::default(),
        ));

        let mut out = TnBuffer::empty();
        let status =
            unsafe { tn_document_page_fingerprints(handle, &mut out, std::ptr::null_mut()) };
        assert_eq!(status, 0);

        let bytes = unsafe { std::slice::from_raw_parts(out.ptr, out.len).to_vec() };
        unsafe { out.free() };
        unsafe { tn_document_free(handle) };

        let json: serde_json::Value = serde_json::from_slice(&bytes).expect("valid JSON");
        assert_eq!(json["pages"].as_array().expect("an array").len(), 0);
    }
}
