//! C ABI over ocrs, for `M3scalinum.Text.Ocr`.
//!
//! The conventions are `native/sharp-typst`'s: a status code plus an out-parameter
//! error object, owned buffers the caller frees, one opaque handle type, a live
//! handle counter, and `catch_unwind` on every entry point so a panic cannot
//! unwind into C.
//!
//! Nothing here downloads a model or reads one from a default path: the bytes the
//! caller passes to [`to_engine_create`] are the only models this library ever
//! sees.

mod engine;
mod ffi;
mod pdf;

use std::ffi::{CString, c_char};
use std::sync::OnceLock;
use std::sync::atomic::{AtomicUsize, Ordering};

use engine::Engine;
use ffi::{ToBuffer, ToError, guard_buffer, guard_ptr, read_bytes};

/// The opaque engine handle. It owns both models.
pub struct ToEngine {
    engine: Engine,
}

// ---------------------------------------------------------------------------
// Live handles
// ---------------------------------------------------------------------------

/// Handles alive right now, so a host can prove that every one it created was
/// released. Process memory cannot witness that portably; the count is exact.
static LIVE_ENGINES: AtomicUsize = AtomicUsize::new(0);

impl Drop for ToEngine {
    fn drop(&mut self) {
        LIVE_ENGINES.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a handle is created: counting it here is what keeps the count exact.
fn engine_handle(engine: Engine) -> Box<ToEngine> {
    LIVE_ENGINES.fetch_add(1, Ordering::Relaxed);
    Box::new(ToEngine { engine })
}

/// Number of engine handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn to_engine_live_count() -> u32 {
    LIVE_ENGINES.load(Ordering::Relaxed) as u32
}

// ---------------------------------------------------------------------------
// Version handshake and build info
// ---------------------------------------------------------------------------

#[unsafe(no_mangle)]
pub extern "C" fn to_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn to_abi_version_string() -> *const c_char {
    static VERSION: OnceLock<CString> = OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("1.0.0").expect("literal has no interior NUL"))
        .as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn to_build_info() -> *const c_char {
    static INFO: OnceLock<CString> = OnceLock::new();
    INFO.get_or_init(|| {
        let json = serde_json::json!({
            "target": std::env::consts::ARCH.to_string() + "-" + std::env::consts::OS,
            "profile": if cfg!(debug_assertions) { "debug" } else { "release" },
            "ocrs": "0.14",
            "rten": "0.28",
            "hayro": "0.9",
        })
        .to_string();
        CString::new(json).expect("build info has no interior NUL")
    })
    .as_ptr()
}

// ---------------------------------------------------------------------------
// Errors and buffers
// ---------------------------------------------------------------------------

/// # Safety
/// `error` is null or a valid error pointer from this library, not yet freed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_free(error: *mut ToError) {
    if error.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(error) });
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_code(error: *const ToError) -> i32 {
    if error.is_null() {
        return 0;
    }
    unsafe { (*error).code }
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_json(error: *const ToError) -> ToBuffer {
    if error.is_null() {
        return ToBuffer::empty();
    }
    ToBuffer::from_vec(unsafe { (*error).json.clone() }.into_bytes())
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_traceback(error: *const ToError) -> ToBuffer {
    if error.is_null() {
        return ToBuffer::empty();
    }
    ToBuffer::from_vec(unsafe { (*error).traceback.clone() }.into_bytes())
}

/// # Safety
/// `buffer` comes from this library and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_buffer_free(buffer: ToBuffer) {
    unsafe { buffer.free() };
}

// ---------------------------------------------------------------------------
// Engine
// ---------------------------------------------------------------------------

/// Builds an engine from caller-supplied model bytes.
///
/// `dictionary` is the alphabet the recognition model was trained on: pass a null
/// pointer (or `dictionary_len` 0) to use the default Latin alphabet of the
/// published models.
///
/// # Safety
/// Each pointer must be valid for its length, or null with a length of 0.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_create(
    detection_model: *const u8,
    detection_len: usize,
    recognition_model: *const u8,
    recognition_len: usize,
    dictionary: *const u8,
    dictionary_len: usize,
    out_error: *mut *mut ToError,
) -> *mut ToEngine {
    unsafe {
        guard_ptr(out_error, || {
            let detection = read_bytes(detection_model, detection_len, "detection model")?;
            let recognition = read_bytes(recognition_model, recognition_len, "recognition model")?;
            let dictionary_bytes = read_bytes(dictionary, dictionary_len, "character dictionary")?;

            let dictionary = if dictionary_bytes.is_empty() {
                None
            } else {
                Some(std::str::from_utf8(&dictionary_bytes).map_err(|_| {
                    ToError::invalid_argument("character dictionary is not valid UTF-8")
                })?)
            };

            let engine = Engine::new(&detection, &recognition, dictionary)?;
            Ok(engine_handle(engine))
        })
    }
}

/// # Safety
/// `engine` must come from `to_engine_create` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_free(engine: *mut ToEngine) {
    if engine.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(engine) });
}

/// Recognises an encoded image (PNG, JPEG, BMP, GIF, TIFF or WebP).
///
/// On success `out_json` holds the result document and the return value is `TO_OK`;
/// on failure the return value is the error code and `out_error` holds the error.
///
/// # Safety
/// `engine` is a valid handle; `image` is valid for `length`; `out_json` and
/// `out_error` are null or caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let bytes = read_bytes(image, length, "image")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("image is empty"));
            }
            handle.engine.recognize_image(&bytes)
        })
    }
}

/// Renders and recognises a PDF.
///
/// `first_page` is zero-based; `page_count` 0 means "to the end".
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize_pdf(
    engine: *mut ToEngine,
    pdf: *const u8,
    length: usize,
    dpi: u32,
    first_page: u32,
    page_count: u32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let bytes = read_bytes(pdf, length, "pdf")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("pdf is empty"));
            }
            handle
                .engine
                .recognize_pdf(&bytes, dpi, first_page, page_count)
        })
    }
}

/// Reads a NUL-terminated UTF-8 string the native side owns (a version or build
/// info constant). Exposed for completeness with the compiler crate's ABI.
///
/// # Safety
/// `pointer` is null or a valid NUL-terminated string owned by this library.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_string_free(pointer: *const c_char) {
    // The strings this library returns are static; this exists so a host can
    // mirror the `tn_` lifecycle without special-casing, and does nothing.
    let _ = pointer;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn abi_version_is_one() {
        assert_eq!(to_abi_version(), 1);
    }

    #[test]
    fn live_count_is_zero_with_no_engine() {
        assert_eq!(to_engine_live_count(), 0);
    }
}
