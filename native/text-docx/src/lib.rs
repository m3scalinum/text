//! C ABI over a DOCX reader.
//!
//! The contract is specified in `docs/specifications/22-docx.md`; the conventions — status codes,
//! owned buffers, an owned error object, `catch_unwind` at every entry point — are those of
//! `02-native-abi.md`. The symbols are prefixed `td_` rather than `tn_` so a process that loads both
//! this library and `sharp_typst` never has to wonder which crate answered.

mod ast;
mod ffi;
mod parse;

pub use ffi::{TdError, TnBuffer};

use std::ffi::c_char;
use std::sync::atomic::{AtomicUsize, Ordering};

use serde::Deserialize;

use crate::ast::DocumentAst;
use crate::ffi::{
    TD_ERR_INTERNAL, TD_ERR_INVALID_ARGUMENT, TD_ERR_INVALID_HANDLE, guard_ptr, guard_status,
    read_bytes, read_str,
};
use crate::parse::{Media, Options};

/// Opaque parsed-document handle: the tree and the media its runs point at.
pub struct TdDocument {
    ast: DocumentAst,
    media: Vec<Media>,
}

/// Handles alive right now, so a host can prove that every one it created was released. This is the
/// property the library owns; the process working set is not — every platform retains freed blocks
/// differently.
static LIVE_DOCUMENTS: AtomicUsize = AtomicUsize::new(0);

impl Drop for TdDocument {
    fn drop(&mut self) {
        LIVE_DOCUMENTS.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a document handle is created: counting it here is what keeps the count exact.
fn document_handle(ast: DocumentAst, media: Vec<Media>) -> Box<TdDocument> {
    LIVE_DOCUMENTS.fetch_add(1, Ordering::Relaxed);
    Box::new(TdDocument { ast, media })
}

/// Number of document handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn td_document_live_count() -> u32 {
    LIVE_DOCUMENTS.load(Ordering::Relaxed) as u32
}

/// The ABI version this library implements.
#[unsafe(no_mangle)]
pub extern "C" fn td_abi_version() -> u32 {
    1
}

// ---------------------------------------------------------------------------
// Buffers and errors
// ---------------------------------------------------------------------------

/// Frees a buffer this library returned.
///
/// # Safety
/// `buffer` must come from one of the `td_` entry points and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_buffer_free(buffer: TnBuffer) {
    unsafe { buffer.free() };
}

/// Frees an error object.
///
/// # Safety
/// `error` is null or a pointer this library handed out, and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_error_free(error: *mut TdError) {
    if error.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(error) });
}

/// The status code of an error object.
///
/// # Safety
/// `error` is null or a pointer this library handed out.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_error_code(error: *const TdError) -> i32 {
    if error.is_null() {
        return 0;
    }
    unsafe { &*error }.code
}

/// The JSON payload of an error object, owned by the caller.
///
/// # Safety
/// `error` is null or a pointer this library handed out.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_error_json(error: *const TdError) -> TnBuffer {
    if error.is_null() {
        return TnBuffer::empty();
    }
    TnBuffer::from_vec(unsafe { &*error }.json.clone().into_bytes())
}

// ---------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------

/// Parses a `.docx` package.
///
/// `options_json` is optional; `{"images": false, "maxBytes": 1048576}` turns image extraction off
/// and lowers the size ceiling.
///
/// # Safety
/// `data` is valid for `len` bytes, and `out_error` is null or a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_document_parse(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
    out_error: *mut *mut TdError,
) -> *mut TdDocument {
    unsafe {
        guard_ptr(out_error, || {
            let bytes = read_bytes(data, len, "document")?;
            let options = parse_options(read_str(options_json, "options")?)?;

            let extracted = parse::extract(&bytes, &options)
                .map_err(|error| TdError::new(error.code(), error.message()))?;

            Ok(document_handle(extracted.ast, extracted.media))
        })
    }
}

/// The AST as JSON, owned by the caller.
///
/// # Safety
/// `document` is a handle from [`td_document_parse`], `out_error` a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_document_ast_json(
    document: *mut TdDocument,
    out_error: *mut *mut TdError,
) -> TnBuffer {
    unsafe {
        ffi::guard_value(out_error, || {
            let document = document_ref(document)?;
            let json = serde_json::to_vec(&document.ast).map_err(|error| {
                TdError::new(
                    TD_ERR_INTERNAL,
                    format!("the AST could not be serialised: {error}"),
                )
            })?;
            Ok(TnBuffer::from_vec(json))
        })
        .unwrap_or_else(TnBuffer::empty)
    }
}

/// Number of media parts the document holds.
///
/// # Safety
/// `document` is a handle from [`td_document_parse`], or null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_document_image_count(document: *const TdDocument) -> u32 {
    if document.is_null() {
        return 0;
    }
    unsafe { &*document }.media.len() as u32
}

/// Copies one media part's bytes into `out_buffer`, owned by the caller.
///
/// # Safety
/// `document` is a handle from [`td_document_parse`]; `out_buffer` points at an empty slot that this
/// call fills; `out_error` is null or a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_document_image(
    document: *mut TdDocument,
    handle: u64,
    out_buffer: *mut TnBuffer,
    out_error: *mut *mut TdError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if out_buffer.is_null() {
                return Err(TdError::invalid_argument("out_buffer must not be null"));
            }

            let document = document_ref(document)?;
            let part = document.media.get(handle as usize).ok_or_else(|| {
                TdError::new(
                    TD_ERR_INVALID_ARGUMENT,
                    format!("no media part at handle {handle}"),
                )
            })?;

            *out_buffer = TnBuffer::from_vec(part.bytes.clone());
            Ok(())
        })
    }
}

/// Releases a document handle.
///
/// # Safety
/// `document` is a handle from [`td_document_parse`] and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn td_document_free(document: *mut TdDocument) {
    if document.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(document) });
}

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

/// # Safety
/// `document` is null or a handle from [`td_document_parse`].
unsafe fn document_ref<'a>(document: *mut TdDocument) -> Result<&'a TdDocument, TdError> {
    if document.is_null() {
        return Err(TdError::new(
            TD_ERR_INVALID_HANDLE,
            "the document handle is null",
        ));
    }
    Ok(unsafe { &*document })
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct RawOptions {
    images: Option<bool>,
    max_bytes: Option<u64>,
}

fn parse_options(json: Option<&str>) -> Result<Options, TdError> {
    let Some(json) = json else {
        return Ok(Options::default());
    };

    if json.trim().is_empty() {
        return Ok(Options::default());
    }

    let raw = serde_json::from_str::<RawOptions>(json).map_err(|error| {
        TdError::new(
            TD_ERR_INVALID_ARGUMENT,
            format!("options are not valid JSON: {error}"),
        )
    })?;

    let defaults = Options::default();

    Ok(Options {
        images: raw.images.unwrap_or(defaults.images),
        max_bytes: raw.max_bytes.unwrap_or(defaults.max_bytes),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn absent_options_are_the_defaults() {
        let options = parse_options(None).unwrap();
        assert!(options.images);
        assert_eq!(options.max_bytes, parse::DEFAULT_MAX_BYTES);
    }

    #[test]
    fn options_are_read_from_json() {
        let options = parse_options(Some(r#"{"images": false, "maxBytes": 1024}"#)).unwrap();
        assert!(!options.images);
        assert_eq!(options.max_bytes, 1024);
    }

    #[test]
    fn options_that_are_not_json_are_refused() {
        let error = parse_options(Some("{")).unwrap_err();
        assert_eq!(error.code, TD_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn a_null_handle_is_an_invalid_handle() {
        let error = match unsafe { document_ref(std::ptr::null_mut()) } {
            Ok(_) => panic!("a null handle must not resolve"),
            Err(error) => error,
        };
        assert_eq!(error.code, TD_ERR_INVALID_HANDLE);
    }
}
