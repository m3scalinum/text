//! FFI primitives: status codes, owned buffers, error objects, panic guard.
//!
//! The conventions are those of `native/sharp-typst/src/ffi.rs` and
//! `docs/specifications/02-native-abi.md`; this crate repeats the handful of shapes it needs rather
//! than depending on the compiler crate, so the two libraries stay independent.

use std::ffi::c_char;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

/// Codes shared with `02-native-abi.md` keep their numbers, so a host that already knows the
/// compiler's codes reads these the same way.
pub const TD_OK: i32 = 0;
pub const TD_ERR_INVALID_ARGUMENT: i32 = 1;
pub const TD_ERR_INVALID_HANDLE: i32 = 2;
pub const TD_ERR_PANIC: i32 = 3;
pub const TD_ERR_LIMIT: i32 = 9;
pub const TD_ERR_UNSUPPORTED: i32 = 10;
/// The bytes are not a zip package, or a part of the package is unreadable.
pub const TD_ERR_MALFORMED_PACKAGE: i32 = 11;
/// The package is a zip package, but a part this reader needs is absent.
pub const TD_ERR_MISSING_PART: i32 = 12;
pub const TD_ERR_INTERNAL: i32 = 13;
/// A part declares a document type, an external entity, or an internal entity set that would expand
/// past the reader's bound: the XML policy in `xml.rs` refused it.
pub const TD_ERR_XML_POLICY: i32 = 14;

/// A buffer allocated by Rust and freed by `td_buffer_free`.
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
    /// `self` must come from [`TnBuffer::from_vec`] and must not be freed twice.
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
pub struct TdError {
    pub code: i32,
    pub json: String,
}

impl TdError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        let json = serde_json::json!({
            "code": code_name(code),
            "message": message.into(),
        })
        .to_string();
        Self { code, json }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(TD_ERR_INVALID_ARGUMENT, message)
    }
}

pub fn code_name(code: i32) -> &'static str {
    match code {
        TD_OK => "ok",
        TD_ERR_INVALID_ARGUMENT => "invalid_argument",
        TD_ERR_INVALID_HANDLE => "invalid_handle",
        TD_ERR_PANIC => "panic",
        TD_ERR_LIMIT => "limit",
        TD_ERR_UNSUPPORTED => "unsupported",
        TD_ERR_MALFORMED_PACKAGE => "malformed_package",
        TD_ERR_MISSING_PART => "missing_part",
        TD_ERR_INTERNAL => "internal",
        TD_ERR_XML_POLICY => "xml_policy",
        _ => "unknown",
    }
}

/// Writes `error` into `out_error` when the caller asked for it.
///
/// # Safety
/// `out_error` is either null or a valid pointer to a slot owned by the caller.
pub unsafe fn set_error(out_error: *mut *mut TdError, error: TdError) {
    if out_error.is_null() {
        return;
    }
    unsafe { *out_error = Box::into_raw(Box::new(error)) };
}

fn panic_error(payload: Box<dyn std::any::Any + Send>) -> TdError {
    let message = if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "panic in native code".to_string()
    };
    TdError::new(TD_ERR_PANIC, message)
}

/// Runs `f`, converting a panic into `TD_ERR_PANIC` instead of unwinding into C.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_status<F>(out_error: *mut *mut TdError, f: F) -> i32
where
    F: FnOnce() -> Result<(), TdError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(())) => TD_OK,
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
pub unsafe fn guard_ptr<T, F>(out_error: *mut *mut TdError, f: F) -> *mut T
where
    F: FnOnce() -> Result<Box<T>, TdError>,
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

/// Same as [`guard_status`] for a function that returns an owned value rather than a status.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_value<T, F>(out_error: *mut *mut TdError, f: F) -> Option<T>
where
    F: FnOnce() -> Result<T, TdError>,
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
pub unsafe fn read_str<'a>(ptr: *const c_char, what: &str) -> Result<Option<&'a str>, TdError> {
    if ptr.is_null() {
        return Ok(None);
    }
    unsafe { std::ffi::CStr::from_ptr(ptr) }
        .to_str()
        .map(Some)
        .map_err(|_| TdError::invalid_argument(format!("{what} is not valid UTF-8")))
}

/// Copies a caller-owned byte range into an owned `Vec`.
///
/// # Safety
/// `ptr` must be valid for `len` bytes, or null when `len` is 0.
pub unsafe fn read_bytes(ptr: *const u8, len: usize, what: &str) -> Result<Vec<u8>, TdError> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        return Err(TdError::invalid_argument(format!(
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
        let buffer = TnBuffer::from_vec(b"docx".to_vec());
        let slice = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        assert_eq!(slice, b"docx");
        unsafe { buffer.free() };
    }

    #[test]
    fn status_names_are_stable() {
        assert_eq!(code_name(TD_OK), "ok");
        assert_eq!(code_name(TD_ERR_MALFORMED_PACKAGE), "malformed_package");
        assert_eq!(code_name(TD_ERR_PANIC), "panic");
    }

    #[test]
    fn null_string_is_absent_not_an_error() {
        assert_eq!(unsafe { read_str(ptr::null(), "options") }.unwrap(), None);
    }
}
