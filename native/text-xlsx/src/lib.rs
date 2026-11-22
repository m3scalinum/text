//! C ABI over an XLSX reader.
//!
//! The contract is specified in `docs/specifications/28-xlsx.md`; the conventions — status codes,
//! owned buffers, an owned error object, `catch_unwind` at every entry point — are those of
//! `02-native-abi.md`. The symbols are prefixed `tx_` rather than `td_` so a process that loads this
//! library, the DOCX reader and `sharp_typst` never has to wonder which crate answered.

mod ast;
mod ffi;
mod parse;

#[cfg(test)]
mod fixture;

pub use ffi::{TxBuffer, TxError};

/// The function the fuzzing target in `fuzz/` drives, and nothing else.
///
/// `parse::extract` is where `calamine` meets bytes a stranger chose; it is private because a caller
/// has no business reaching it, so the fuzzer gets this one door. It adds no bound and changes no
/// behaviour: it calls the same function the ABI calls, with the documented defaults, and drops the
/// workbook that comes back.
#[doc(hidden)]
pub mod fuzz_api {
    use crate::parse::{self, Options};

    /// Reads a workbook under the default ceilings, discarding the AST.
    pub fn extract(bytes: &[u8]) {
        let _ = parse::extract(bytes, &Options::default());
    }
}

use std::ffi::c_char;
use std::sync::atomic::{AtomicUsize, Ordering};

use serde::Deserialize;

use crate::ast::WorkbookAst;
use crate::ffi::{
    TX_ERR_INTERNAL, TX_ERR_INVALID_ARGUMENT, TX_ERR_INVALID_HANDLE, guard_ptr, guard_value,
    read_bytes, read_str,
};
use crate::parse::Options;

/// Opaque parsed-workbook handle.
pub struct TxWorkbook {
    ast: WorkbookAst,
}

/// Handles alive right now, so a host can prove that every one it created was released.
static LIVE_WORKBOOKS: AtomicUsize = AtomicUsize::new(0);

impl Drop for TxWorkbook {
    fn drop(&mut self) {
        LIVE_WORKBOOKS.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a workbook handle is created: counting it here is what keeps the count exact.
fn workbook_handle(ast: WorkbookAst) -> Box<TxWorkbook> {
    LIVE_WORKBOOKS.fetch_add(1, Ordering::Relaxed);
    Box::new(TxWorkbook { ast })
}

/// Number of workbook handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn tx_workbook_live_count() -> u32 {
    LIVE_WORKBOOKS.load(Ordering::Relaxed) as u32
}

/// The ABI version this library implements.
#[unsafe(no_mangle)]
pub extern "C" fn tx_abi_version() -> u32 {
    1
}

// ---------------------------------------------------------------------------
// Buffers and errors
// ---------------------------------------------------------------------------

/// Frees a buffer this library returned.
///
/// # Safety
/// `buffer` must come from one of the `tx_` entry points and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tx_buffer_free(buffer: TxBuffer) {
    unsafe { buffer.free() };
}

/// Frees an error object.
///
/// # Safety
/// `error` is null or a pointer this library handed out, and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tx_error_free(error: *mut TxError) {
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
pub unsafe extern "C" fn tx_error_code(error: *const TxError) -> i32 {
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
pub unsafe extern "C" fn tx_error_json(error: *const TxError) -> TxBuffer {
    if error.is_null() {
        return TxBuffer::empty();
    }
    TxBuffer::from_vec(unsafe { &*error }.json.clone().into_bytes())
}

// ---------------------------------------------------------------------------
// Workbooks
// ---------------------------------------------------------------------------

/// Reads an `.xlsx` package.
///
/// `options_json` is optional; `{"maxBytes": 1048576, "maxCells": 10000}` lowers both ceilings.
///
/// # Safety
/// `data` is valid for `len` bytes, and `out_error` is null or a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tx_workbook_parse(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
    out_error: *mut *mut TxError,
) -> *mut TxWorkbook {
    unsafe {
        guard_ptr(out_error, || {
            let bytes = read_bytes(data, len, "workbook")?;
            let options = parse_options(read_str(options_json, "options")?)?;

            let ast = parse::extract(&bytes, &options)
                .map_err(|error| TxError::new(error.code(), error.message()))?;

            Ok(workbook_handle(ast))
        })
    }
}

/// The workbook as JSON, owned by the caller.
///
/// # Safety
/// `workbook` is a handle from [`tx_workbook_parse`], `out_error` a valid slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tx_workbook_ast_json(
    workbook: *mut TxWorkbook,
    out_error: *mut *mut TxError,
) -> TxBuffer {
    unsafe {
        guard_value(out_error, || {
            let workbook = workbook_ref(workbook)?;
            let json = serde_json::to_vec(&workbook.ast).map_err(|error| {
                TxError::new(
                    TX_ERR_INTERNAL,
                    format!("the workbook could not be serialised: {error}"),
                )
            })?;
            Ok(TxBuffer::from_vec(json))
        })
        .unwrap_or_else(TxBuffer::empty)
    }
}

/// Releases a workbook handle.
///
/// # Safety
/// `workbook` is a handle from [`tx_workbook_parse`] and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn tx_workbook_free(workbook: *mut TxWorkbook) {
    if workbook.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(workbook) });
}

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

/// # Safety
/// `workbook` is null or a handle from [`tx_workbook_parse`].
unsafe fn workbook_ref<'a>(workbook: *mut TxWorkbook) -> Result<&'a TxWorkbook, TxError> {
    if workbook.is_null() {
        return Err(TxError::new(
            TX_ERR_INVALID_HANDLE,
            "the workbook handle is null",
        ));
    }
    Ok(unsafe { &*workbook })
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct RawOptions {
    max_bytes: Option<u64>,
    max_cells: Option<u64>,
    max_sheets: Option<usize>,
    max_cell_text_bytes: Option<u64>,
    max_text_bytes: Option<u64>,
}

fn parse_options(json: Option<&str>) -> Result<Options, TxError> {
    let Some(json) = json else {
        return Ok(Options::default());
    };

    if json.trim().is_empty() {
        return Ok(Options::default());
    }

    let raw = serde_json::from_str::<RawOptions>(json).map_err(|error| {
        TxError::new(
            TX_ERR_INVALID_ARGUMENT,
            format!("options are not valid JSON: {error}"),
        )
    })?;

    let defaults = Options::default();

    Ok(Options {
        max_bytes: raw.max_bytes.unwrap_or(defaults.max_bytes),
        max_cells: raw.max_cells.unwrap_or(defaults.max_cells),
        max_sheets: raw.max_sheets.unwrap_or(defaults.max_sheets),
        max_cell_text_bytes: raw
            .max_cell_text_bytes
            .unwrap_or(defaults.max_cell_text_bytes),
        max_text_bytes: raw.max_text_bytes.unwrap_or(defaults.max_text_bytes),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ast::{CellValue, WorkbookAst};
    use crate::fixture::{Cell, Fixture};
    use std::ffi::CString;
    use std::ptr;
    use std::sync::Mutex;

    /// The two tests that read the process-wide handle count take this lock, so a handle another
    /// test opens between the two measurements cannot make them fail.
    static HANDLE_LOCK: Mutex<()> = Mutex::new(());

    fn parse(bytes: &[u8]) -> *mut TxWorkbook {
        let mut error: *mut TxError = ptr::null_mut();

        let handle = unsafe {
            tx_workbook_parse(
                bytes.as_ptr(),
                bytes.len(),
                ptr::null(),
                &mut error as *mut *mut TxError,
            )
        };

        assert!(error.is_null(), "the fixture must parse");

        handle
    }

    #[test]
    fn absent_options_are_the_defaults() {
        let options = parse_options(None).unwrap();

        assert_eq!(options.max_bytes, parse::DEFAULT_MAX_BYTES);
        assert_eq!(options.max_cells, parse::DEFAULT_MAX_CELLS);
    }

    #[test]
    fn options_are_read_from_json() {
        let options = parse_options(Some(r#"{"maxBytes": 1024, "maxCells": 8}"#)).unwrap();

        assert_eq!(options.max_bytes, 1024);
        assert_eq!(options.max_cells, 8);
    }

    #[test]
    fn options_that_are_not_json_are_refused() {
        let error = parse_options(Some("{")).unwrap_err();

        assert_eq!(error.code, TX_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn the_abi_version_is_one() {
        assert_eq!(tx_abi_version(), 1);
    }

    #[test]
    fn a_null_handle_carries_no_json() {
        let mut error: *mut TxError = ptr::null_mut();

        let buffer =
            unsafe { tx_workbook_ast_json(ptr::null_mut(), &mut error as *mut *mut TxError) };

        assert!(buffer.ptr.is_null());
        assert_eq!(unsafe { tx_error_code(error) }, TX_ERR_INVALID_HANDLE);
        unsafe { tx_error_free(error) };
    }

    #[test]
    fn a_freed_handle_lowers_the_live_count() {
        let _guard = HANDLE_LOCK.lock().unwrap();
        let before = tx_workbook_live_count();
        let bytes = Fixture::new()
            .sheet("Facture", [Cell::text(0, 0, "a")])
            .build();

        let handle = parse(&bytes);

        assert_eq!(tx_workbook_live_count(), before + 1);

        unsafe { tx_workbook_free(handle) };

        assert_eq!(tx_workbook_live_count(), before);
    }

    #[test]
    fn a_package_that_does_not_parse_returns_no_handle_and_an_error() {
        let bytes = b"definitely not a workbook";
        let mut error: *mut TxError = ptr::null_mut();

        let handle = unsafe {
            tx_workbook_parse(
                bytes.as_ptr(),
                bytes.len(),
                ptr::null(),
                &mut error as *mut *mut TxError,
            )
        };

        assert!(handle.is_null());
        assert_eq!(
            unsafe { tx_error_code(error) },
            crate::ffi::TX_ERR_MALFORMED_PACKAGE
        );

        let json = unsafe { tx_error_json(error) };

        let payload = unsafe { std::slice::from_raw_parts(json.ptr, json.len) };

        assert!(String::from_utf8_lossy(payload).contains("malformed_package"));

        unsafe {
            tx_error_free(error);
            tx_buffer_free(json);
        }
    }

    #[test]
    fn a_null_workbook_pointer_is_an_invalid_argument() {
        let mut error: *mut TxError = ptr::null_mut();

        let handle = unsafe {
            tx_workbook_parse(ptr::null(), 8, ptr::null(), &mut error as *mut *mut TxError)
        };

        assert!(handle.is_null());
        assert_eq!(unsafe { tx_error_code(error) }, TX_ERR_INVALID_ARGUMENT);

        unsafe { tx_error_free(error) };
    }

    #[test]
    fn the_ast_json_is_the_workbook_the_handle_holds() {
        let _guard = HANDLE_LOCK.lock().unwrap();
        let bytes = Fixture::new()
            .sheet(
                "Facture",
                [Cell::text(0, 0, "Total"), Cell::number(1, 0, 10.0)],
            )
            .build();

        let handle = parse(&bytes);
        let mut error: *mut TxError = ptr::null_mut();

        let json = unsafe { tx_workbook_ast_json(handle, &mut error as *mut *mut TxError) };

        assert!(error.is_null());

        let payload = unsafe { std::slice::from_raw_parts(json.ptr, json.len) };
        let ast: serde_json::Value = serde_json::from_slice(payload).expect("the AST is JSON");

        assert_eq!(ast["sheets"][0]["name"], "Facture");
        // The .NET DTOs read these names, so a rename here is a wire-contract change.
        assert_eq!(ast["sheets"][0]["rowOffset"], 0);
        assert_eq!(ast["sheets"][0]["cells"][1]["type"], "number");
        assert_eq!(ast["sheets"][0]["cells"][1]["number"], 10.0);

        unsafe {
            tx_workbook_free(handle);
            tx_buffer_free(json);
        }
    }

    #[test]
    fn the_workbook_holds_the_cells_the_sheet_declared() {
        let bytes = Fixture::new()
            .sheet(
                "Facture",
                [Cell::boolean(0, 0, false), Cell::error(1, 0, "#N/A")],
            )
            .build();

        let handle = parse(&bytes);

        let workbook: &WorkbookAst = unsafe { &(*handle).ast };
        let cells = &workbook.sheets[0].cells;

        assert_eq!(cells[0].value, CellValue::Boolean { value: false });
        assert_eq!(
            cells[1].value,
            CellValue::Error {
                error: "#N/A".to_string()
            }
        );

        unsafe { tx_workbook_free(handle) };
    }

    #[test]
    fn options_cross_the_abi_as_json() {
        let bytes = Fixture::new()
            .sheet("Facture", [Cell::text(0, 0, "a")])
            .build();
        let options = CString::new(r#"{"maxBytes": 8}"#).unwrap();
        let mut error: *mut TxError = ptr::null_mut();

        let handle = unsafe {
            tx_workbook_parse(
                bytes.as_ptr(),
                bytes.len(),
                options.as_ptr(),
                &mut error as *mut *mut TxError,
            )
        };

        assert!(handle.is_null());
        assert_eq!(unsafe { tx_error_code(error) }, crate::ffi::TX_ERR_LIMIT);

        unsafe { tx_error_free(error) };
    }
}
