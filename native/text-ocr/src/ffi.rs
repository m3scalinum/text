//! FFI primitives: status codes, owned buffers, error objects, panic guard.
//!
//! These are deliberate copies of `native/sharp-typst/src/ffi.rs`, with the `tn_`
//! status vocabulary kept identical so a host can read both libraries the same way.
//! Everything in this module is `#[repr(C)]`.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

pub const TO_OK: i32 = 0;
pub const TO_ERR_INVALID_ARGUMENT: i32 = 1;
pub const TO_ERR_INVALID_HANDLE: i32 = 2;
pub const TO_ERR_PANIC: i32 = 3;
pub const TO_ERR_MODEL: i32 = 4;
pub const TO_ERR_IMAGE: i32 = 5;
pub const TO_ERR_PDF: i32 = 6;
pub const TO_ERR_OCR: i32 = 7;

/// A buffer allocated by Rust and freed by `to_buffer_free`.
#[repr(C)]
pub struct ToBuffer {
    pub ptr: *mut u8,
    pub len: usize,
}

impl ToBuffer {
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
    /// `self` must come from `ToBuffer::from_vec` and must not be freed twice.
    pub unsafe fn free(self) {
        if self.ptr.is_null() || self.len == 0 {
            return;
        }
        let slice = ptr::slice_from_raw_parts_mut(self.ptr, self.len);
        drop(unsafe { Box::from_raw(slice) });
    }
}

/// An error crossing the ABI. `json` is `{"code", "message"}`.
#[repr(C)]
#[derive(Debug)]
pub struct ToError {
    pub code: i32,
    pub json: String,
    pub traceback: String,
}

impl ToError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        let json = serde_json::json!({
            "code": code_name(code),
            "message": message.into(),
        })
        .to_string();
        Self {
            code,
            json,
            traceback: String::new(),
        }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(TO_ERR_INVALID_ARGUMENT, message)
    }
}

pub fn code_name(code: i32) -> &'static str {
    match code {
        TO_OK => "ok",
        TO_ERR_INVALID_ARGUMENT => "invalid_argument",
        TO_ERR_INVALID_HANDLE => "invalid_handle",
        TO_ERR_PANIC => "panic",
        TO_ERR_MODEL => "model",
        TO_ERR_IMAGE => "image",
        TO_ERR_PDF => "pdf",
        TO_ERR_OCR => "ocr",
        _ => "unknown",
    }
}

/// Writes `error` into `out_error` when the caller asked for it.
///
/// # Safety
/// `out_error` is either null or a valid pointer to a slot owned by the caller.
pub unsafe fn set_error(out_error: *mut *mut ToError, error: ToError) {
    if out_error.is_null() {
        return;
    }
    unsafe { *out_error = Box::into_raw(Box::new(error)) };
}

fn panic_error(payload: Box<dyn std::any::Any + Send>) -> ToError {
    let message = if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "panic in native code".to_string()
    };
    ToError::new(TO_ERR_PANIC, message)
}

/// Runs `f`, converting a panic into `TO_ERR_PANIC` instead of unwinding into C.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_ptr<T, F>(out_error: *mut *mut ToError, f: F) -> *mut T
where
    F: FnOnce() -> Result<Box<T>, ToError>,
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

/// Same as [`guard_ptr`] for a function returning a status code and an out buffer.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`]; `out_buffer` is null or
/// a valid pointer to a slot owned by the caller.
pub unsafe fn guard_buffer<F>(out_error: *mut *mut ToError, out_buffer: *mut ToBuffer, f: F) -> i32
where
    F: FnOnce() -> Result<Vec<u8>, ToError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(bytes)) => {
            if !out_buffer.is_null() {
                unsafe { *out_buffer = ToBuffer::from_vec(bytes) };
            }
            TO_OK
        }
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

/// Copies a caller-owned byte range into an owned `Vec`.
///
/// # Safety
/// `ptr` must be valid for `len` bytes, or null when `len` is 0.
pub unsafe fn read_bytes(ptr: *const u8, len: usize, what: &str) -> Result<Vec<u8>, ToError> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        return Err(ToError::invalid_argument(format!(
            "{what} must not be null when len > 0"
        )));
    }
    Ok(unsafe { std::slice::from_raw_parts(ptr, len) }.to_vec())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn buffer_preserves_content() {
        let buffer = ToBuffer::from_vec(b"hello".to_vec());
        assert_eq!(buffer.len, 5);
        let slice = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        assert_eq!(slice, b"hello");
        unsafe { buffer.free() };
    }

    #[test]
    fn empty_buffer_is_null() {
        let buffer = ToBuffer::from_vec(Vec::new());
        assert!(buffer.ptr.is_null());
        unsafe { buffer.free() };
    }

    #[test]
    fn status_names_are_stable() {
        assert_eq!(code_name(TO_OK), "ok");
        assert_eq!(code_name(TO_ERR_MODEL), "model");
        assert_eq!(code_name(TO_ERR_PANIC), "panic");
    }
}
