//! FFI primitives: status codes, owned buffers, error objects, panic guard.
//!
//! Everything in this module is `#[repr(C)]` and must stay in sync with
//! `docs/specifications/20-barcodes.md` §4.

use std::ffi::c_char;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;

pub const BT_OK: i32 = 0;
pub const BT_ERR_INVALID_ARGUMENT: i32 = 1;
pub const BT_ERR_INVALID_HANDLE: i32 = 2;
pub const BT_ERR_PANIC: i32 = 3;
pub const BT_ERR_ENCODE: i32 = 4;
pub const BT_ERR_UNSUPPORTED: i32 = 5;
pub const BT_ERR_DECODE: i32 = 6;
pub const BT_ERR_ABI_MISMATCH: i32 = 7;
pub const BT_ERR_LIMIT: i32 = 8;
/// The GS1 element string is malformed: an unknown Application Identifier, a value of the wrong
/// length, a check digit that does not match, or a Digital Link that is not one.
pub const BT_ERR_GS1: i32 = 9;

/// A buffer allocated by Rust and freed by `bt_buffer_free`.
#[repr(C)]
pub struct BtBuffer {
    pub ptr: *mut u8,
    pub len: usize,
}

impl BtBuffer {
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
    /// `self` must come from `BtBuffer::from_vec` and must not be freed twice.
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
#[derive(Debug, PartialEq, Eq)]
pub struct BtError {
    pub code: i32,
    pub json: String,
    pub traceback: String,
}

impl BtError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        let json = format!(
            "{{\"code\":\"{}\",\"message\":\"{}\"}}",
            code_name(code),
            escape_json(&message.into())
        );
        Self {
            code,
            json,
            traceback: String::new(),
        }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_INVALID_ARGUMENT, message)
    }

    pub fn unsupported(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_UNSUPPORTED, message)
    }

    pub fn encode(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_ENCODE, message)
    }

    pub fn decode(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_DECODE, message)
    }

    pub fn limit(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_LIMIT, message)
    }

    pub fn gs1(message: impl Into<String>) -> Self {
        Self::new(BT_ERR_GS1, message)
    }
}

pub fn code_name(code: i32) -> &'static str {
    match code {
        BT_OK => "ok",
        BT_ERR_INVALID_ARGUMENT => "invalid_argument",
        BT_ERR_INVALID_HANDLE => "invalid_handle",
        BT_ERR_PANIC => "panic",
        BT_ERR_ENCODE => "encode",
        BT_ERR_UNSUPPORTED => "unsupported",
        BT_ERR_DECODE => "decode",
        BT_ERR_ABI_MISMATCH => "abi_mismatch",
        BT_ERR_LIMIT => "limit",
        BT_ERR_GS1 => "gs1",
        _ => "unknown",
    }
}

/// Escapes the two characters that can break the error payload: `"` and `\`, plus control
/// characters, which a symbology message can carry when the payload is what failed.
pub(crate) fn escape_json(value: &str) -> String {
    let mut out = String::with_capacity(value.len() + 8);
    for c in value.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out
}

/// Writes `error` into `out_error` when the caller asked for it.
///
/// # Safety
/// `out_error` is either null or a valid pointer to a slot owned by the caller.
pub unsafe fn set_error(out_error: *mut *mut BtError, error: BtError) {
    if out_error.is_null() {
        return;
    }
    unsafe { *out_error = Box::into_raw(Box::new(error)) };
}

fn panic_error(payload: Box<dyn std::any::Any + Send>) -> BtError {
    let message = if let Some(s) = payload.downcast_ref::<&str>() {
        (*s).to_string()
    } else if let Some(s) = payload.downcast_ref::<String>() {
        s.clone()
    } else {
        "panic in native code".to_string()
    };
    BtError::new(BT_ERR_PANIC, message)
}

/// Runs `f`, converting a panic into `BT_ERR_PANIC` instead of unwinding into C.
///
/// # Safety
/// `out_error` follows the same contract as [`set_error`].
pub unsafe fn guard_status<F>(out_error: *mut *mut BtError, f: F) -> i32
where
    F: FnOnce() -> Result<(), BtError>,
{
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(())) => BT_OK,
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
pub unsafe fn guard_ptr<T, F>(out_error: *mut *mut BtError, f: F) -> *mut T
where
    F: FnOnce() -> Result<Box<T>, BtError>,
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
pub unsafe fn read_str<'a>(ptr: *const c_char, what: &str) -> Result<&'a str, BtError> {
    if ptr.is_null() {
        return Err(BtError::invalid_argument(format!(
            "{what} must not be null"
        )));
    }
    unsafe { std::ffi::CStr::from_ptr(ptr) }
        .to_str()
        .map_err(|_| BtError::invalid_argument(format!("{what} is not valid UTF-8")))
}

/// Copies a caller-owned byte range into an owned `Vec`.
///
/// # Safety
/// `ptr` must be valid for `len` bytes, or null when `len` is 0.
pub unsafe fn read_bytes(ptr: *const u8, len: usize, what: &str) -> Result<Vec<u8>, BtError> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        return Err(BtError::invalid_argument(format!(
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
        let buffer = BtBuffer::from_vec(Vec::new());
        assert!(buffer.ptr.is_null());
        assert_eq!(buffer.len, 0);
        unsafe { buffer.free() };
    }

    #[test]
    fn buffer_preserves_content() {
        let buffer = BtBuffer::from_vec(b"hello".to_vec());
        assert_eq!(buffer.len, 5);
        let slice = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        assert_eq!(slice, b"hello");
        unsafe { buffer.free() };
    }

    #[test]
    fn status_names_are_stable() {
        assert_eq!(code_name(BT_OK), "ok");
        assert_eq!(code_name(BT_ERR_ENCODE), "encode");
        assert_eq!(code_name(BT_ERR_PANIC), "panic");
    }

    #[test]
    fn quotes_in_a_message_do_not_break_the_payload() {
        let error = BtError::encode("no \"start\" character");
        assert!(error.json.contains("\\\"start\\\""));
    }

    #[test]
    fn null_string_is_rejected() {
        let result = unsafe { read_str(std::ptr::null(), "payload") };
        assert!(result.is_err());
    }
}
