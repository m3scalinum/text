//! FFI primitives: status codes, owned buffers, error objects, panic guard.
//!
//! The conventions are those of `docs/specifications/02-native-abi.md` and of the other crates'
//! `ffi.rs`; this crate repeats the handful of shapes it needs rather than depending on another
//! crate, so the libraries stay independent.

use std::ffi::c_char;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

/// Codes shared with `02-native-abi.md` keep their numbers, so a host that already knows the
/// compiler's codes reads these the same way.
pub const TP_OK: i32 = 0;
pub const TP_ERR_INVALID_ARGUMENT: i32 = 1;
pub const TP_ERR_PANIC: i32 = 3;
/// The page would have rendered past the pixel or memory ceiling the caller set.
pub const TP_ERR_LIMIT: i32 = 9;
/// The page asks for something this renderer does not draw.
pub const TP_ERR_UNSUPPORTED: i32 = 10;
/// The bytes are not a PDF, or the document cannot be read.
pub const TP_ERR_NOT_A_PDF: i32 = 11;
/// The document is encrypted, and this crate does not decrypt.
pub const TP_ERR_ENCRYPTED: i32 = 12;
/// The page number is outside `1..=page_count`.
pub const TP_ERR_PAGE_OUT_OF_RANGE: i32 = 13;
pub const TP_ERR_INTERNAL: i32 = 14;

/// A buffer allocated by Rust and freed by `tp_buffer_free`.
#[repr(C)]
pub struct TpBuffer {
    pub ptr: *mut u8,
    pub len: usize,
}

impl TpBuffer {
    pub fn empty() -> Self {
        Self {
            ptr: ptr::null_mut(),
            len: 0,
        }
    }

    pub fn from_vec(bytes: Vec<u8>) -> Self {
        if bytes.is_empty() {
            return Self::empty();
        }
        let boxed = bytes.into_boxed_slice();
        let len = boxed.len();
        let ptr = Box::into_raw(boxed) as *mut u8;
        Self { ptr, len }
    }

    /// # Safety
    /// `self` must come from [`TpBuffer::from_vec`] and must not be freed twice.
    pub unsafe fn free(self) {
        if self.ptr.is_null() || self.len == 0 {
            return;
        }
        let slice = ptr::slice_from_raw_parts_mut(self.ptr, self.len);
        drop(unsafe { Box::from_raw(slice) });
    }
}

/// An error crossing the ABI. `json` carries `{"code","message"}`.
#[repr(C)]
#[derive(Debug)]
pub struct TpError {
    pub code: i32,
    pub json: String,
}

impl TpError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        let json = serde_json::json!({
            "code": code_name(code),
            "message": message.into(),
        })
        .to_string();
        Self { code, json }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(TP_ERR_INVALID_ARGUMENT, message)
    }
}

pub fn code_name(code: i32) -> &'static str {
    match code {
        TP_OK => "ok",
        TP_ERR_INVALID_ARGUMENT => "invalid_argument",
        TP_ERR_PANIC => "panic",
        TP_ERR_LIMIT => "limit",
        TP_ERR_UNSUPPORTED => "unsupported",
        TP_ERR_NOT_A_PDF => "not_a_pdf",
        TP_ERR_ENCRYPTED => "encrypted",
        TP_ERR_PAGE_OUT_OF_RANGE => "page_out_of_range",
        TP_ERR_INTERNAL => "internal",
        _ => "unknown",
    }
}

/// Writes `error` into `out_error` when the caller asked for it.
///
/// # Safety
/// `out_error` is either null or a valid pointer to a slot owned by the caller.
pub unsafe fn set_error(out_error: *mut *mut TpError, error: TpError) {
    if out_error.is_null() {
        return;
    }
    unsafe { *out_error = Box::into_raw(Box::new(error)) };
}

fn panic_error(payload: Box<dyn std::any::Any + Send>) -> TpError {
    let message = if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "panic in native code".to_string()
    };
    TpError::new(TP_ERR_PANIC, message)
}

/// Runs `f`, converting a panic into `TP_ERR_PANIC` instead of unwinding into C.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_status<F>(out_error: *mut *mut TpError, f: F) -> i32
where
    F: FnOnce() -> Result<(), TpError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(())) => TP_OK,
        Ok(Err(error)) => {
            let code = error.code;
            unsafe { set_error(out_error, error) };
            code
        }
        Err(payload) => {
            let error = panic_error(payload);
            let code = error.code;
            unsafe { set_error(out_error, error) };
            code
        }
    }
}

/// Same as [`guard_status`] for a function that returns an owned value rather than a status.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_value<T, F>(out_error: *mut *mut TpError, f: F) -> Option<T>
where
    F: FnOnce() -> Result<T, TpError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(value)) => Some(value),
        Ok(Err(error)) => {
            unsafe { set_error(out_error, error) };
            None
        }
        Err(payload) => {
            unsafe { set_error(out_error, panic_error(payload)) };
            None
        }
    }
}

/// Reads a NUL-terminated UTF-8 string, rejecting null and invalid UTF-8.
///
/// # Safety
/// `ptr` is either null or a valid NUL-terminated C string.
pub unsafe fn read_str<'a>(ptr: *const c_char, what: &str) -> Result<Option<&'a str>, TpError> {
    if ptr.is_null() {
        return Ok(None);
    }
    unsafe { std::ffi::CStr::from_ptr(ptr) }
        .to_str()
        .map(Some)
        .map_err(|_| TpError::invalid_argument(format!("{what} is not valid UTF-8")))
}

/// Copies a caller-owned byte range into an owned `Vec`.
///
/// # Safety
/// `ptr` must be valid for `len` bytes, or null when `len` is 0.
pub unsafe fn read_bytes(ptr: *const u8, len: usize, what: &str) -> Result<Vec<u8>, TpError> {
    if len == 0 {
        return Err(TpError::invalid_argument(format!(
            "{what} must not be empty"
        )));
    }
    if ptr.is_null() {
        return Err(TpError::invalid_argument(format!(
            "{what} must not be null when len > 0"
        )));
    }
    Ok(unsafe { std::slice::from_raw_parts(ptr, len) }.to_vec())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_buffer_round_trips() {
        let buffer = TpBuffer::from_vec(Vec::new());
        assert!(buffer.ptr.is_null());
        assert_eq!(buffer.len, 0);
        unsafe { buffer.free() };
    }

    #[test]
    fn buffer_preserves_content() {
        let buffer = TpBuffer::from_vec(b"png".to_vec());
        let slice = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        assert_eq!(slice, b"png");
        unsafe { buffer.free() };
    }

    #[test]
    fn status_names_are_stable() {
        assert_eq!(code_name(TP_OK), "ok");
        assert_eq!(code_name(TP_ERR_ENCRYPTED), "encrypted");
        assert_eq!(code_name(TP_ERR_PAGE_OUT_OF_RANGE), "page_out_of_range");
    }

    #[test]
    fn null_string_is_absent_not_an_error() {
        assert_eq!(unsafe { read_str(ptr::null(), "options") }.unwrap(), None);
    }

    #[test]
    fn empty_input_is_an_invalid_argument() {
        let error = unsafe { read_bytes(ptr::null(), 0, "pdf") }.unwrap_err();
        assert_eq!(error.code, TP_ERR_INVALID_ARGUMENT);
    }
}
