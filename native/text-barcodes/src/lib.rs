//! `sharp_text_barcodes`: the C ABI over the rxing barcode engine.
//!
//! The managed package `M3scalinum.Text.Barcodes` is the only caller. The contract is
//! `docs/specifications/20-barcodes.md` §4: owned buffers freed by `bt_buffer_free`, opaque
//! handles freed by their own `*_free`, no borrowed pointers, and a panic at any boundary converted
//! into `BT_ERR_PANIC` instead of unwinding into C.

pub mod decode;
pub mod encode;
pub mod ffi;
pub mod symbology;

use std::ffi::{CString, c_char};
use std::sync::atomic::{AtomicUsize, Ordering};

use ffi::{
    BT_ERR_INVALID_HANDLE, BtBuffer, BtError, guard_ptr, guard_status, read_bytes, read_str,
};

// ---------------------------------------------------------------------------
// Live handles
// ---------------------------------------------------------------------------

/// Handles alive right now, so a host can prove that every one it created was released.
///
/// The process working set cannot answer that: it is process-wide and every platform retains freed
/// native blocks differently. The handle count is the property this library owns, and it is exact.
static LIVE_BARCODES: AtomicUsize = AtomicUsize::new(0);
static LIVE_DECODES: AtomicUsize = AtomicUsize::new(0);

/// One encoded symbol, held open until the caller frees it.
pub struct BtBarcode {
    inner: encode::Encoded,
}

/// One decode attempt, held open until the caller frees it.
pub struct BtDecode {
    inner: decode::Decoded,
}

impl Drop for BtBarcode {
    fn drop(&mut self) {
        LIVE_BARCODES.fetch_sub(1, Ordering::Relaxed);
    }
}

impl Drop for BtDecode {
    fn drop(&mut self) {
        LIVE_DECODES.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a barcode handle is created: counting it here is what keeps the count exact.
fn barcode_handle(inner: encode::Encoded) -> Box<BtBarcode> {
    LIVE_BARCODES.fetch_add(1, Ordering::Relaxed);
    Box::new(BtBarcode { inner })
}

/// The only way a decode handle is created. See [`barcode_handle`].
fn decode_handle(inner: decode::Decoded) -> Box<BtDecode> {
    LIVE_DECODES.fetch_add(1, Ordering::Relaxed);
    Box::new(BtDecode { inner })
}

/// Number of barcode handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn bt_barcode_live_count() -> u32 {
    LIVE_BARCODES.load(Ordering::Relaxed) as u32
}

/// Number of decode handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn bt_decode_live_count() -> u32 {
    LIVE_DECODES.load(Ordering::Relaxed) as u32
}

// ---------------------------------------------------------------------------
// Version handshake
// ---------------------------------------------------------------------------

#[unsafe(no_mangle)]
pub extern "C" fn bt_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn bt_abi_version_string() -> *const c_char {
    static VERSION: std::sync::OnceLock<CString> = std::sync::OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("1.0.0").expect("literal has no interior NUL"))
        .as_ptr()
}

/// The barcode engine and its version, for example `rxing 0.9.4`.
#[unsafe(no_mangle)]
pub extern "C" fn bt_engine_version() -> *const c_char {
    static VERSION: std::sync::OnceLock<CString> = std::sync::OnceLock::new();
    VERSION
        .get_or_init(|| CString::new(format!("rxing {}", engine_version())).expect("no NUL"))
        .as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn bt_build_info() -> *const c_char {
    static INFO: std::sync::OnceLock<CString> = std::sync::OnceLock::new();
    INFO.get_or_init(|| {
        let info = format!(
            "{{\"target\":\"{}\",\"profile\":\"{}\",\"engine\":\"rxing\",\"engineVersion\":\"{}\"}}",
            std::env::consts::ARCH,
            if cfg!(debug_assertions) {
                "debug"
            } else {
                "release"
            },
            engine_version()
        );
        CString::new(info).expect("no interior NUL")
    })
    .as_ptr()
}

fn engine_version() -> &'static str {
    "0.9.4"
}

/// Every symbology name this library writes and reads, as a JSON array of strings.
///
/// # Safety
/// The returned buffer is owned by Rust: free it with `bt_buffer_free`. Nothing else is unsafe
/// about the call, but every entry point that returns an owned buffer is `unsafe` by policy so the
/// ownership rule sits next to the function.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_symbologies() -> BtBuffer {
    let names: Vec<String> = symbology::ALL
        .iter()
        .map(|s| format!("\"{}\"", s.name()))
        .collect();

    BtBuffer::from_vec(format!("[{}]", names.join(",")).into_bytes())
}

// ---------------------------------------------------------------------------
// Errors and buffers
// ---------------------------------------------------------------------------

/// Frees an error object. Passing null is allowed.
///
/// # Safety
/// `error` must come from this library and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_error_free(error: *mut BtError) {
    if error.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(error) });
}

/// The status code carried by an error object; `BT_ERR_INVALID_HANDLE` for null.
///
/// # Safety
/// `error` must be null or a live error object.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_error_code(error: *const BtError) -> i32 {
    if error.is_null() {
        return BT_ERR_INVALID_HANDLE;
    }
    unsafe { &*error }.code
}

/// The JSON payload of an error object, as an owned buffer.
///
/// # Safety
/// `error` must be null or a live error object; free the returned buffer with `bt_buffer_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_error_json(error: *const BtError) -> BtBuffer {
    if error.is_null() {
        return BtBuffer::empty();
    }
    BtBuffer::from_vec(unsafe { &*error }.json.clone().into_bytes())
}

/// The panic traceback of an error object, empty when there is none.
///
/// # Safety
/// `error` must be null or a live error object; free the returned buffer with `bt_buffer_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_error_traceback(error: *const BtError) -> BtBuffer {
    if error.is_null() {
        return BtBuffer::empty();
    }
    BtBuffer::from_vec(unsafe { &*error }.traceback.clone().into_bytes())
}

/// Frees a buffer returned by this library. Passing the empty buffer is allowed.
///
/// # Safety
/// `buffer` must come from this library and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_buffer_free(buffer: BtBuffer) {
    unsafe { buffer.free() };
}

// ---------------------------------------------------------------------------
// Encoding
// ---------------------------------------------------------------------------

/// Encodes `payload` as `symbology` and returns an owned handle.
///
/// `quiet_zone` and `bar_height` are in modules; a negative value takes the symbology default.
/// `error_correction` may be null and is passed to rxing as-is.
///
/// # Safety
/// Every pointer argument follows the contract in `docs/specifications/20-barcodes.md` §4.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_encode(
    symbology_name: *const c_char,
    payload: *const c_char,
    quiet_zone: i32,
    bar_height: i32,
    error_correction: *const c_char,
    out_error: *mut *mut BtError,
) -> *mut BtBarcode {
    unsafe {
        guard_ptr(out_error, || {
            let symbology = symbology::Symbology::parse(read_str(symbology_name, "symbology")?)?;
            let payload = read_str(payload, "payload")?;

            let error_correction = if error_correction.is_null() {
                None
            } else {
                let value = read_str(error_correction, "error correction")?;
                if value.is_empty() {
                    None
                } else {
                    Some(value.to_string())
                }
            };

            let options = encode::EncodeOptions {
                quiet_zone: (quiet_zone >= 0).then_some(quiet_zone as u32),
                bar_height: (bar_height > 0).then_some(bar_height as u32),
                error_correction,
            };

            Ok(barcode_handle(encode::encode(
                symbology, payload, &options,
            )?))
        })
    }
}

/// Writes the SVG rendering of `barcode` into `out_svg`.
///
/// # Safety
/// `barcode` must be a live handle and `out_svg` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_svg(
    barcode: *const BtBarcode,
    out_svg: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let barcode = barcode_ref(barcode)?;
            set_buffer(out_svg, encode::to_svg(&barcode.inner).into_bytes())?;
            Ok(())
        })
    }
}

/// Writes the PNG rendering of `barcode`, `scale` pixels per module, into `out_png`.
///
/// # Safety
/// `barcode` must be a live handle and `out_png` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_png(
    barcode: *const BtBarcode,
    scale: u32,
    out_png: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let barcode = barcode_ref(barcode)?;
            let png = encode::to_png(&barcode.inner, scale)?;
            set_buffer(out_png, png)?;
            Ok(())
        })
    }
}

/// Width of the module matrix, in modules.
///
/// # Safety
/// `barcode` must be a live handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_width(barcode: *const BtBarcode) -> u32 {
    unsafe { barcode_ref(barcode) }
        .map(|barcode| barcode.inner.width())
        .unwrap_or(0)
}

/// Height of the module matrix, in modules.
///
/// # Safety
/// `barcode` must be a live handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_height(barcode: *const BtBarcode) -> u32 {
    unsafe { barcode_ref(barcode) }
        .map(|barcode| barcode.inner.height())
        .unwrap_or(0)
}

/// Frees a barcode handle. Passing null is allowed.
///
/// # Safety
/// `barcode` must come from `bt_barcode_encode` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_barcode_free(barcode: *mut BtBarcode) {
    if barcode.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(barcode) });
}

// ---------------------------------------------------------------------------
// Decoding
// ---------------------------------------------------------------------------

/// Decodes the first barcode in a PNG and returns an owned handle.
///
/// A PNG without a barcode is a successful call: the handle reports `success == 0`. Only bytes that
/// are not a readable PNG are an error.
///
/// # Safety
/// `data` must be valid for `len` bytes, or null when `len` is 0.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_png(
    data: *const u8,
    len: usize,
    out_error: *mut *mut BtError,
) -> *mut BtDecode {
    unsafe {
        guard_ptr(out_error, || {
            let bytes = read_bytes(data, len, "image")?;
            Ok(decode_handle(decode::decode_png(&bytes)?))
        })
    }
}

/// Whether the decode found a symbol. `0` means no barcode was found; see `bt_decode_error`.
///
/// # Safety
/// `decoded` must be a live handle.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_success(decoded: *const BtDecode) -> i32 {
    unsafe { decode_ref(decoded) }
        .map(|decoded| i32::from(decoded.inner.success))
        .unwrap_or(0)
}

/// Writes the decoded text into `out_text`.
///
/// # Safety
/// `decoded` must be a live handle and `out_text` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_text(
    decoded: *const BtDecode,
    out_text: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let decoded = decode_ref(decoded)?;
            set_buffer(out_text, decoded.inner.text.clone().into_bytes())?;
            Ok(())
        })
    }
}

/// Writes the detected symbology name into `out_name`, empty when the format is outside the set
/// this package exposes. `bt_decode_format` names it either way.
///
/// # Safety
/// `decoded` must be a live handle and `out_name` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_symbology(
    decoded: *const BtDecode,
    out_name: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let decoded = decode_ref(decoded)?;
            let name = decoded
                .inner
                .symbology
                .map(|symbology| symbology.name())
                .unwrap_or_default();
            set_buffer(out_name, name.as_bytes().to_vec())?;
            Ok(())
        })
    }
}

/// Writes the format rxing detected into `out_format`, in rxing's spelling, empty when nothing was
/// found.
///
/// # Safety
/// `decoded` must be a live handle and `out_format` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_format(
    decoded: *const BtDecode,
    out_format: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let decoded = decode_ref(decoded)?;
            set_buffer(out_format, decoded.inner.format.clone().into_bytes())?;
            Ok(())
        })
    }
}

/// Writes the corner points as JSON, `[{"x":12.5,"y":30.0}]`, into `out_points`.
///
/// # Safety
/// `decoded` must be a live handle and `out_points` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_points(
    decoded: *const BtDecode,
    out_points: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let decoded = decode_ref(decoded)?;
            let points: Vec<String> = decoded
                .inner
                .points
                .iter()
                .map(|point| format!("{{\"x\":{},\"y\":{}}}", point.x, point.y))
                .collect();
            set_buffer(out_points, format!("[{}]", points.join(",")).into_bytes())?;
            Ok(())
        })
    }
}

/// Writes why nothing was decoded into `out_message`, empty on success.
///
/// # Safety
/// `decoded` must be a live handle and `out_message` a valid pointer to a slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_error(
    decoded: *const BtDecode,
    out_message: *mut BtBuffer,
    out_error: *mut *mut BtError,
) -> i32 {
    unsafe {
        guard_status(out_error, || {
            let decoded = decode_ref(decoded)?;
            let message = decoded.inner.error.clone().unwrap_or_default();
            set_buffer(out_message, message.into_bytes())?;
            Ok(())
        })
    }
}

/// Frees a decode handle. Passing null is allowed.
///
/// # Safety
/// `decoded` must come from `bt_decode_png` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn bt_decode_free(decoded: *mut BtDecode) {
    if decoded.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(decoded) });
}

// ---------------------------------------------------------------------------
// Pointer helpers
// ---------------------------------------------------------------------------

unsafe fn barcode_ref<'a>(barcode: *const BtBarcode) -> Result<&'a BtBarcode, BtError> {
    if barcode.is_null() {
        return Err(BtError::new(
            BT_ERR_INVALID_HANDLE,
            "the barcode handle is null",
        ));
    }
    Ok(unsafe { &*barcode })
}

unsafe fn decode_ref<'a>(decoded: *const BtDecode) -> Result<&'a BtDecode, BtError> {
    if decoded.is_null() {
        return Err(BtError::new(
            BT_ERR_INVALID_HANDLE,
            "the decode handle is null",
        ));
    }
    Ok(unsafe { &*decoded })
}

/// Hands `bytes` to the caller as an owned buffer.
///
/// # Safety
/// `slot` must be a valid pointer to a `BtBuffer` the caller owns.
unsafe fn set_buffer(slot: *mut BtBuffer, bytes: Vec<u8>) -> Result<(), BtError> {
    if slot.is_null() {
        return Err(BtError::invalid_argument(
            "the output slot must not be null",
        ));
    }
    unsafe { *slot = BtBuffer::from_vec(bytes) };
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ptr;
    use std::sync::Mutex;

    /// The two live-count tests create handles, so they must not run beside each other: the count
    /// is process-wide, and an exact assertion is the point.
    static HANDLE_LOCK: Mutex<()> = Mutex::new(());

    #[test]
    fn the_handshake_is_stable() {
        assert_eq!(bt_abi_version(), 1);
        assert_eq!(engine_version(), "0.9.4");
    }

    #[test]
    fn an_unknown_symbology_sets_an_error_and_returns_null() {
        let name = CString::new("Code128X").unwrap();
        let payload = CString::new("HELLO").unwrap();
        let mut error = ptr::null_mut();

        let handle = unsafe {
            bt_barcode_encode(
                name.as_ptr(),
                payload.as_ptr(),
                -1,
                -1,
                ptr::null(),
                &mut error,
            )
        };

        assert!(handle.is_null());
        assert!(!error.is_null());
        assert_eq!(unsafe { bt_error_code(error) }, ffi::BT_ERR_UNSUPPORTED);
        unsafe { bt_error_free(error) };
    }

    #[test]
    fn a_null_payload_is_an_invalid_argument() {
        let name = CString::new("QRCode").unwrap();
        let mut error = ptr::null_mut();

        let handle = unsafe {
            bt_barcode_encode(name.as_ptr(), ptr::null(), -1, -1, ptr::null(), &mut error)
        };

        assert!(handle.is_null());
        assert_eq!(
            unsafe { bt_error_code(error) },
            ffi::BT_ERR_INVALID_ARGUMENT
        );
        unsafe { bt_error_free(error) };
    }

    #[test]
    fn a_null_handle_is_reported_without_a_crash() {
        assert_eq!(unsafe { bt_barcode_width(ptr::null()) }, 0);
        assert_eq!(unsafe { bt_barcode_height(ptr::null()) }, 0);
        assert_eq!(unsafe { bt_decode_success(ptr::null()) }, 0);

        let mut svg = BtBuffer::empty();
        let mut error = ptr::null_mut();
        let status = unsafe { bt_barcode_svg(ptr::null(), &mut svg, &mut error) };
        assert_eq!(status, BT_ERR_INVALID_HANDLE);
        unsafe { bt_error_free(error) };
        unsafe { svg.free() };
    }

    #[test]
    fn the_live_count_tracks_creation_and_release() {
        let _guard = HANDLE_LOCK
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let before = bt_barcode_live_count();

        let name = CString::new("QRCode").unwrap();
        let payload = CString::new("LIVE COUNT").unwrap();
        let mut error = ptr::null_mut();

        let handle = unsafe {
            bt_barcode_encode(
                name.as_ptr(),
                payload.as_ptr(),
                -1,
                -1,
                ptr::null(),
                &mut error,
            )
        };
        assert!(!handle.is_null(), "encode failed");
        assert_eq!(bt_barcode_live_count(), before + 1);

        unsafe { bt_barcode_free(handle) };
        assert_eq!(bt_barcode_live_count(), before);
    }

    #[test]
    fn the_decode_live_count_tracks_creation_and_release() {
        let _guard = HANDLE_LOCK
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let before = bt_decode_live_count();

        let name = CString::new("QRCode").unwrap();
        let payload = CString::new("DECODE LIVE").unwrap();
        let mut error = ptr::null_mut();

        let barcode = unsafe {
            bt_barcode_encode(
                name.as_ptr(),
                payload.as_ptr(),
                -1,
                -1,
                ptr::null(),
                &mut error,
            )
        };
        let mut png = BtBuffer::empty();
        let status = unsafe { bt_barcode_png(barcode, 2, &mut png, &mut error) };
        assert_eq!(status, ffi::BT_OK);

        let decoded = unsafe { bt_decode_png(png.ptr, png.len, &mut error) };
        unsafe { png.free() };
        assert!(!decoded.is_null());
        assert_eq!(bt_decode_live_count(), before + 1);
        assert_eq!(unsafe { bt_decode_success(decoded) }, 1);

        unsafe { bt_decode_free(decoded) };
        unsafe { bt_barcode_free(barcode) };
        assert_eq!(bt_decode_live_count(), before);
    }

    #[test]
    fn the_symbology_list_is_json() {
        let buffer = unsafe { bt_symbologies() };
        let json = unsafe { std::slice::from_raw_parts(buffer.ptr, buffer.len) };
        let json = std::str::from_utf8(json).expect("utf-8");
        assert!(json.starts_with('['));
        assert!(json.contains("\"QRCode\""));
        assert!(json.contains("\"Codabar\""));
        unsafe { buffer.free() };
    }
}
