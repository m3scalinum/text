//! C ABI over a PDF page rasteriser.
//!
//! The contract is specified in `docs/specifications/35-pdf-raster.md`; the conventions — status
//! codes, owned buffers, an owned error object, `catch_unwind` at every entry point — are those of
//! `02-native-abi.md`. The symbols are prefixed `tp_` so a process that loads this library together
//! with `sharp_typst` and the other format crates never has to wonder which crate answered.

mod ffi;
mod raster;

pub use ffi::{TpBuffer, TpError};

use std::ffi::c_char;
use std::sync::OnceLock;

use serde::Deserialize;

use crate::ffi::{
    TP_ERR_INTERNAL, TP_ERR_INVALID_ARGUMENT, guard_status, guard_value, read_bytes, read_str,
};
use crate::raster::Options;

/// The ABI version this library implements.
#[unsafe(no_mangle)]
pub extern "C" fn tp_abi_version() -> u32 {
    1
}

/// What this library was built from, as JSON, valid for the life of the process.
///
/// The renderer is the interesting field: a consumer reporting a rendering defect should be able to
/// say which hayro produced it without guessing from the package version.
#[unsafe(no_mangle)]
pub extern "C" fn tp_build_info() -> *const c_char {
    static INFO: OnceLock<std::ffi::CString> = OnceLock::new();

    INFO.get_or_init(|| {
        let json = serde_json::json!({
            "crate": env!("CARGO_PKG_NAME"),
            "version": env!("CARGO_PKG_VERSION"),
            "renderer": format!("hayro {HAYRO_SERIES}"),
            "format": "png",
        })
        .to_string();

        // The string holds no interior NUL: it is built from literals and a version number.
        std::ffi::CString::new(json).unwrap_or_default()
    })
    .as_ptr()
}

/// The `hayro` series this crate depends on, as the manifest names it. `Cargo.lock` pins the exact
/// patch release; the series is what a consumer needs to reproduce a rendering defect.
const HAYRO_SERIES: &str = "0.9";

// ---------------------------------------------------------------------------
// Buffers and errors
// ---------------------------------------------------------------------------

/// Frees a buffer this library returned.
///
/// # Safety
/// `buffer` must come from one of the `tp_` entry points and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tp_buffer_free(buffer: TpBuffer) {
    unsafe { buffer.free() }
}

/// Frees an error object.
///
/// # Safety
/// `error` is null or a pointer this library handed out, and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tp_error_free(error: *mut TpError) {
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
pub unsafe extern "C" fn tp_error_code(error: *const TpError) -> i32 {
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
pub unsafe extern "C" fn tp_error_json(error: *const TpError) -> TpBuffer {
    if error.is_null() {
        return TpBuffer::empty();
    }
    TpBuffer::from_vec(unsafe { &*error }.json.clone().into_bytes())
}

// ---------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------

/// The number of pages in the document, or `-1` on failure with `out_error` naming the reason.
///
/// # Safety
/// `data` is valid for `len` bytes, and `out_error` is null or a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tp_page_count(
    data: *const u8,
    len: usize,
    out_error: *mut *mut TpError,
) -> i64 {
    unsafe {
        guard_value(out_error, || {
            let bytes = read_bytes(data, len, "pdf")?;
            Ok(i64::from(raster::page_count(&bytes)?))
        })
        .unwrap_or(-1)
    }
}

/// Renders one page as PNG.
///
/// `page` is 1-based, the way a reader numbers pages. `options_json` is optional;
/// `{"dpi": 300, "maxPixels": 20000000, "maxBytes": 67108864}` renders at 300 DPI under lower
/// ceilings.
///
/// On success `out_image` holds the PNG and `out_json` the metadata document
/// `{"page","pageCount","format","width","height","dpi","effectiveDpi","bytes"}`. On failure both
/// are empty and `out_error` names the reason.
///
/// # Safety
/// `data` is valid for `len` bytes, the two out-slots are valid, and `out_error` is null or a valid
/// slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tp_page_render(
    data: *const u8,
    len: usize,
    page: u32,
    options_json: *const c_char,
    out_image: *mut TpBuffer,
    out_json: *mut TpBuffer,
    out_error: *mut *mut TpError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            if out_image.is_null() || out_json.is_null() {
                return Err(TpError::invalid_argument(
                    "out_image and out_json must not be null",
                ));
            }

            *out_image = TpBuffer::empty();
            *out_json = TpBuffer::empty();

            let bytes = read_bytes(data, len, "pdf")?;
            let options = parse_options(read_str(options_json, "options")?)?;

            let rendered = raster::render_page(&bytes, page, &options)?;

            let metadata = serde_json::json!({
                "page": rendered.page,
                "pageCount": rendered.page_count,
                "format": "png",
                "width": rendered.width,
                "height": rendered.height,
                "dpi": rendered.dpi,
                "effectiveDpi": rendered.effective_dpi,
                "bytes": rendered.png.len(),
            });

            let json = serde_json::to_vec(&metadata).map_err(|error| {
                TpError::new(
                    TP_ERR_INTERNAL,
                    format!("the render metadata could not be serialised: {error}"),
                )
            })?;

            *out_image = TpBuffer::from_vec(rendered.png);
            *out_json = TpBuffer::from_vec(json);

            Ok(())
        })
    }
}

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct RawOptions {
    dpi: Option<u32>,
    max_pixels: Option<u64>,
    max_bytes: Option<u64>,
}

/// Builds the options, deferring every range check to [`Options::validate`] so the two entry points
/// and the unit tests all refuse the same combinations with the same message.
fn parse_options(json: Option<&str>) -> Result<Options, TpError> {
    let defaults = Options::default();

    let Some(json) = json else {
        return Ok(defaults);
    };

    if json.trim().is_empty() {
        return Ok(defaults);
    }

    let raw = serde_json::from_str::<RawOptions>(json).map_err(|error| {
        TpError::new(
            TP_ERR_INVALID_ARGUMENT,
            format!("options are not valid JSON: {error}"),
        )
    })?;

    Ok(Options {
        dpi: raw.dpi.unwrap_or(defaults.dpi),
        max_pixels: raw.max_pixels.unwrap_or(defaults.max_pixels),
        max_bytes: raw.max_bytes.unwrap_or(defaults.max_bytes),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    use std::ffi::CStr;

    use crate::ffi::set_error;

    #[test]
    fn absent_options_are_the_defaults() {
        let options = parse_options(None).unwrap();
        assert_eq!(options.dpi, raster::DEFAULT_DPI);
        assert_eq!(options.max_pixels, raster::DEFAULT_MAX_PIXELS);
        assert_eq!(options.max_bytes, raster::DEFAULT_MAX_BYTES);
    }

    #[test]
    fn options_are_read_from_json() {
        let options =
            parse_options(Some(r#"{"dpi": 300, "maxPixels": 1000, "maxBytes": 4096}"#)).unwrap();
        assert_eq!(options.dpi, 300);
        assert_eq!(options.max_pixels, 1000);
        assert_eq!(options.max_bytes, 4096);
    }

    #[test]
    fn options_that_are_not_json_are_refused() {
        let error = parse_options(Some("{")).unwrap_err();
        assert_eq!(error.code, TP_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn the_abi_version_is_one() {
        assert_eq!(tp_abi_version(), 1);
    }

    #[test]
    fn the_build_info_names_the_renderer() {
        let info = unsafe { CStr::from_ptr(tp_build_info()) }.to_str().unwrap();
        assert!(info.contains("\"renderer\":\"hayro"));
        assert!(info.contains("\"format\":\"png\""));
    }

    #[test]
    fn a_null_buffer_and_a_null_slot_are_handled() {
        unsafe {
            let mut error: *mut TpError = std::ptr::null_mut();
            let status = tp_page_render(
                std::ptr::null(),
                0,
                1,
                std::ptr::null(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                &mut error,
            );
            assert_eq!(status, TP_ERR_INVALID_ARGUMENT);
            assert!(!error.is_null());
            // The error object is the caller's to free, and must be readable before it is.
            assert_eq!(tp_error_code(error), TP_ERR_INVALID_ARGUMENT);
            let json = tp_error_json(error);
            assert!(json.len > 0);
            tp_buffer_free(json);
            tp_error_free(error);
        }
    }

    #[test]
    fn a_null_error_slot_is_not_written_through() {
        // A host that does not want the error must not have its null dereferenced.
        unsafe {
            set_error(
                std::ptr::null_mut(),
                TpError::new(TP_ERR_INTERNAL, "ignored"),
            );
        }
    }
}
