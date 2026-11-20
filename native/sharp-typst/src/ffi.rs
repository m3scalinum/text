//! FFI primitives: status codes, owned buffers, error objects, panic guard.
//!
//! Everything in this module is `#[repr(C)]` and must stay in sync with
//! `docs/specifications/02-native-abi.md`.

use std::ffi::c_char;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

pub const TN_OK: i32 = 0;
pub const TN_ERR_INVALID_ARGUMENT: i32 = 1;
pub const TN_ERR_INVALID_HANDLE: i32 = 2;
pub const TN_ERR_PANIC: i32 = 3;
pub const TN_ERR_COMPILATION: i32 = 4;
pub const TN_ERR_FILE: i32 = 5;
pub const TN_ERR_EXPORT: i32 = 6;
pub const TN_ERR_ABI_MISMATCH: i32 = 7;
pub const TN_ERR_CANCELLED: i32 = 8;
pub const TN_ERR_LIMIT: i32 = 9;
pub const TN_ERR_UNSUPPORTED: i32 = 10;

/// A buffer allocated by Rust and freed by `tn_buffer_free`.
#[repr(C)]
pub struct TnBuffer {
    pub ptr: *mut u8,
    pub len: usize,
}

impl TnBuffer {
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
    /// `self` must come from `TnBuffer::from_vec` and must not be freed twice.
    pub unsafe fn free(self) {
        if self.ptr.is_null() || self.len == 0 {
            return;
        }
        let slice = ptr::slice_from_raw_parts_mut(self.ptr, self.len);
        drop(unsafe { Box::from_raw(slice) });
    }
}

/// An error crossing the ABI. `json` is the payload described in the spec.
#[repr(C)]
#[derive(Debug)]
pub struct TnError {
    pub code: i32,
    pub json: String,
    pub traceback: String,
}

impl TnError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        let message = message.into();
        let json = serde_json::json!({
            "code": code_name(code),
            "message": message,
            "diagnostics": [],
        })
        .to_string();
        Self {
            code,
            json,
            traceback: String::new(),
        }
    }

    pub fn with_json(code: i32, json: String) -> Self {
        Self {
            code,
            json,
            traceback: String::new(),
        }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(TN_ERR_INVALID_ARGUMENT, message)
    }

    pub fn unsupported(message: impl Into<String>) -> Self {
        Self::new(TN_ERR_UNSUPPORTED, message)
    }
}

pub fn code_name(code: i32) -> &'static str {
    match code {
        TN_OK => "ok",
        TN_ERR_INVALID_ARGUMENT => "invalid_argument",
        TN_ERR_INVALID_HANDLE => "invalid_handle",
        TN_ERR_PANIC => "panic",
        TN_ERR_COMPILATION => "compilation",
        TN_ERR_FILE => "file",
        TN_ERR_EXPORT => "export",
        TN_ERR_ABI_MISMATCH => "abi_mismatch",
        TN_ERR_CANCELLED => "cancelled",
        TN_ERR_LIMIT => "limit",
        TN_ERR_UNSUPPORTED => "unsupported",
        _ => "unknown",
    }
}

/// Writes `error` into `out_error` when the caller asked for it.
///
/// # Safety
/// `out_error` is either null or a valid pointer to a slot owned by the caller.
pub unsafe fn set_error(out_error: *mut *mut TnError, error: TnError) {
    if out_error.is_null() {
        return;
    }
    unsafe { *out_error = Box::into_raw(Box::new(error)) };
}

fn panic_error(payload: Box<dyn std::any::Any + Send>) -> TnError {
    let message = if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "panic in native code".to_string()
    };
    let json = serde_json::json!({
        "code": "panic",
        "message": message,
        "diagnostics": [],
    })
    .to_string();
    TnError {
        code: TN_ERR_PANIC,
        json,
        traceback: String::new(),
    }
}

/// Runs `f`, converting a panic into `TN_ERR_PANIC` instead of unwinding into C.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_status<F>(out_error: *mut *mut TnError, f: F) -> i32
where
    F: FnOnce() -> Result<(), TnError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(())) => TN_OK,
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

/// Same as [`guard_status`] for functions returning an owned pointer.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_ptr<T, F>(out_error: *mut *mut TnError, f: F) -> *mut T
where
    F: FnOnce() -> Result<Box<T>, TnError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(value)) => Box::into_raw(value),
        Ok(Err(error)) => {
            unsafe { set_error(out_error, error) };
            ptr::null_mut()
        }
        Err(payload) => {
            unsafe { set_error(out_error, panic_error(payload)) };
            ptr::null_mut()
        }
    }
}

/// Reads a NUL-terminated UTF-8 string, rejecting null and invalid UTF-8.
///
/// # Safety
/// `ptr` is either null or a valid NUL-terminated C string.
pub unsafe fn read_str<'a>(ptr: *const c_char, what: &str) -> Result<&'a str, TnError> {
    if ptr.is_null() {
        return Err(TnError::invalid_argument(format!(
            "{what} must not be null"
        )));
    }
    unsafe { std::ffi::CStr::from_ptr(ptr) }
        .to_str()
        .map_err(|_| TnError::invalid_argument(format!("{what} is not valid UTF-8")))
}

/// Copies a caller-owned byte range into an owned `Vec`.
///
/// # Safety
/// `ptr` must be valid for `len` bytes, or null when `len` is 0.
pub unsafe fn read_bytes(ptr: *const u8, len: usize, what: &str) -> Result<Vec<u8>, TnError> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        return Err(TnError::invalid_argument(format!(
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
        let buffer = TnBuffer::from_vec(Vec::new());
        assert!(buffer.ptr.is_null());
        assert_eq!(buffer.len, 0);
        unsafe { buffer.free() };
    }

    #[test]
    fn buffer_preserves_content() {
        let buffer = TnBuffer::from_vec(b"hello".to_vec());
        assert_eq!(buffer.len, 5);
        let slice = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        assert_eq!(slice, b"hello");
        unsafe { buffer.free() };
    }

    #[test]
    fn status_names_are_stable() {
        assert_eq!(code_name(TN_OK), "ok");
        assert_eq!(code_name(TN_ERR_COMPILATION), "compilation");
        assert_eq!(code_name(TN_ERR_PANIC), "panic");
    }

    #[test]
    fn null_string_is_rejected() {
        let result = unsafe { read_str(std::ptr::null(), "path") };
        assert!(result.is_err());
    }
}
