//! The workbook tree this reader produces and hands to the host as JSON.
//!
//! The shape is specified in `docs/specifications/28-xlsx.md`. Coordinates are zero-based and
//! relative to the used region of the sheet: `(0, 0)` is the top-left cell Excel reports as the
//! start of the range, and `row_offset`/`column_offset` say where that is on the sheet.

use serde::Serialize;

/// A workbook: one entry per sheet, in workbook order.
#[derive(Debug, PartialEq, Serialize)]
pub struct WorkbookAst {
    /// The sheets, in the order the workbook declares them.
    pub sheets: Vec<SheetAst>,
}

/// One sheet and the cells that hold something.
#[derive(Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SheetAst {
    /// The sheet name as the workbook tab shows it.
    pub name: String,
    /// Rows in the used region.
    pub rows: usize,
    /// Columns in the used region.
    pub columns: usize,
    /// The zero-based sheet row the used region starts at.
    pub row_offset: usize,
    /// The zero-based sheet column the used region starts at.
    pub column_offset: usize,
    /// The cells that hold a value or a formula, row-major.
    pub cells: Vec<CellAst>,
}

/// One cell: where it is, what it holds, and how it got there.
#[derive(Debug, PartialEq, Serialize)]
pub struct CellAst {
    /// Row within the used region.
    pub row: usize,
    /// Column within the used region.
    pub column: usize,
    /// The value, tagged by `type`.
    #[serde(flatten)]
    pub value: CellValue,
    /// The formula text with its leading `=`, when the cell holds a formula.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub formula: Option<String>,
}

/// What a cell holds, as Excel types it.
///
/// A formula cell carries its cached value when the workbook saved one: Excel writes the result of
/// the last calculation into the package, and that is what `calamine` reads. A formula with no
/// cached value is [`CellValue::Blank`] with a `formula`, which says "the workbook never computed
/// this" rather than "the cell is empty".
#[derive(Debug, PartialEq, Serialize)]
#[serde(
    tag = "type",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum CellValue {
    /// A string, from a shared-string table or an inline string.
    Text {
        /// The characters.
        text: String,
    },
    /// A number, integer or floating point.
    Number {
        /// The value. Excel stores every number as a double.
        number: f64,
    },
    /// A boolean.
    Boolean {
        /// The value.
        value: bool,
    },
    /// A date, a time, or both.
    Date {
        /// The value as ISO 8601 without a zone: `2026-01-15` or `2026-01-15T10:30:00`.
        iso: String,
        /// True when the time part is midnight, so a host can print a date rather than a timestamp.
        date_only: bool,
    },
    /// An elapsed time, from a `[h]:mm:ss`-style format.
    Duration {
        /// The length in seconds.
        seconds: f64,
    },
    /// An error value such as `#DIV/0!`.
    Error {
        /// The error as Excel prints it, including the `#`.
        error: String,
    },
    /// No value. Present only when the cell carries a formula whose result was never cached.
    Blank,
}
