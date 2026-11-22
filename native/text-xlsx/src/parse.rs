//! Reading a workbook with `calamine` and shaping it into the AST of [`crate::ast`].
//!
//! `calamine` is the reader; this module decides what of a workbook survives. Three decisions are
//! worth stating here rather than in the specification:
//!
//! - the used region is normalised: cells are coordinates inside it, and the offsets say where it
//!   sits on the sheet, so a sheet whose data starts at `C5` does not produce four empty rows and
//!   two empty columns in every table a host renders;
//! - a formula's cached result is its value: Excel writes the result of its last calculation into
//!   the package, and refusing it would mean refusing every workbook Excel ever saved;
//! - a formula whose result was never cached is a blank cell that carries the formula, which is the
//!   only honest reading of a package that says `=SUM(B2:B9)` and no value.

use std::io::Cursor;

use calamine::{Data, ExcelDateTime, Range, Reader, Xlsx};

use crate::ast::{CellAst, CellValue, SheetAst, WorkbookAst};

/// The largest workbook, in bytes, this reader opens unless told otherwise.
pub const DEFAULT_MAX_BYTES: u64 = 64 * 1024 * 1024;

/// The largest used region, in cells, this reader shapes into an AST unless told otherwise.
///
/// Excel's own limit is a little over seventeen billion cells, and a sheet that claims it is either
/// broken or hostile; a million is already more rows than any invoice a person typed.
pub const DEFAULT_MAX_CELLS: u64 = 1_000_000;

/// The most sheets one workbook may hold unless told otherwise.
///
/// A workbook is a collection of tabs a person named: the record is in the hundreds, and a package
/// that declares thousands is a generator. The cap exists because `calamine` opens every sheet the
/// workbook part lists, and each one is a part of the zip that has already been expanded.
pub const DEFAULT_MAX_SHEETS: usize = 256;

/// The most bytes one cell's text may hold unless told otherwise.
///
/// Excel refuses a cell over 32 767 characters, so twice that is already a package no producer
/// writes. The cap stops one shared string — the table `calamine` builds before this reader sees a
/// cell — from becoming the whole workbook's memory.
pub const DEFAULT_MAX_CELL_TEXT_BYTES: u64 = 64 * 1024;

/// The most text, in bytes, one workbook may put in its cells unless told otherwise.
///
/// The cells' cap bounds how many cells there are and the cell-text cap bounds each one; this is
/// the product of the two, and the reason a workbook of a million cells each just under the per-cell
/// cap is refused rather than shaped into a gigabyte of `String`.
pub const DEFAULT_MAX_TEXT_BYTES: u64 = 64 * 1024 * 1024;

/// How one call reads a workbook.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Options {
    /// The largest workbook, in bytes, this call will open.
    pub max_bytes: u64,
    /// The largest used region, in cells, this call will shape into an AST.
    pub max_cells: u64,
    /// The most sheets this call will read.
    pub max_sheets: usize,
    /// The most bytes one cell's text may hold.
    pub max_cell_text_bytes: u64,
    /// The most text, in bytes, this call will shape from all the cells together.
    pub max_text_bytes: u64,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            max_bytes: DEFAULT_MAX_BYTES,
            max_cells: DEFAULT_MAX_CELLS,
            max_sheets: DEFAULT_MAX_SHEETS,
            max_cell_text_bytes: DEFAULT_MAX_CELL_TEXT_BYTES,
            max_text_bytes: DEFAULT_MAX_TEXT_BYTES,
        }
    }
}

/// Why a workbook could not be read.
#[derive(Debug, PartialEq, Eq)]
pub enum ParseError {
    /// Not a zip package, or a part of it does not parse.
    MalformedPackage(String),
    /// Larger than the configured ceiling, or wider than the reader will shape.
    Limit(String),
}

impl ParseError {
    /// The status code this failure crosses the ABI as.
    pub fn code(&self) -> i32 {
        match self {
            Self::MalformedPackage(_) => crate::ffi::TX_ERR_MALFORMED_PACKAGE,
            Self::Limit(_) => crate::ffi::TX_ERR_LIMIT,
        }
    }

    /// The sentence a host shows.
    pub fn message(&self) -> &str {
        match self {
            Self::MalformedPackage(message) | Self::Limit(message) => message,
        }
    }
}

/// Reads a workbook into the AST.
///
/// # Errors
/// [`ParseError`] when the bytes are not an `.xlsx` package, a part of it does not parse, or the
/// workbook crosses one of the ceilings in [`Options`].
pub fn extract(bytes: &[u8], options: &Options) -> Result<WorkbookAst, ParseError> {
    if bytes.len() as u64 > options.max_bytes {
        return Err(ParseError::Limit(format!(
            "the workbook is {} bytes, over the {}-byte limit",
            bytes.len(),
            options.max_bytes
        )));
    }

    if bytes.is_empty() {
        return Err(ParseError::MalformedPackage(
            "the workbook is empty".to_string(),
        ));
    }

    let mut workbook: Xlsx<Cursor<&[u8]>> =
        Xlsx::new(Cursor::new(bytes)).map_err(|error| malformed("the package", &error))?;

    let names = workbook.sheet_names();

    if names.len() > options.max_sheets {
        return Err(ParseError::Limit(format!(
            "the workbook declares {} sheets, over the {}-sheet limit",
            names.len(),
            options.max_sheets
        )));
    }

    let mut sheets = Vec::with_capacity(names.len());
    let mut text_bytes: u64 = 0;

    for name in names {
        let range = workbook
            .worksheet_range(&name)
            .map_err(|error| malformed(&format!("the sheet '{name}'"), &error))?;

        let formulas = workbook
            .worksheet_formula(&name)
            .map_err(|error| malformed(&format!("the formulas of '{name}'"), &error))?;

        sheets.push(sheet(name, &range, &formulas, options, &mut text_bytes)?);
    }

    Ok(WorkbookAst { sheets })
}

fn malformed(what: &str, error: &calamine::XlsxError) -> ParseError {
    ParseError::MalformedPackage(format!("{what} could not be read: {error:?}"))
}

/// One sheet: its used region, and the cells that hold a value or a formula.
fn sheet(
    name: String,
    range: &Range<Data>,
    formulas: &Range<String>,
    options: &Options,
    text_bytes: &mut u64,
) -> Result<SheetAst, ParseError> {
    let (rows, columns) = range.get_size();

    if rows as u64 * columns as u64 > options.max_cells {
        return Err(ParseError::Limit(format!(
            "the sheet '{name}' covers {rows} rows by {columns} columns, over the {}-cell limit",
            options.max_cells
        )));
    }

    let (row_offset, column_offset) = range.start().unwrap_or((0, 0));
    let (formula_row_offset, formula_column_offset) = formulas.start().unwrap_or((0, 0));

    let mut absolute: Vec<(u32, u32, CellValue, Option<String>)> = Vec::new();

    for (row, column, value) in range.cells() {
        let Some(value) = convert(value) else {
            continue;
        };

        charge(
            &value,
            &name,
            row_offset + row as u32,
            column_offset + column as u32,
            options,
            text_bytes,
        )?;

        let formula = formulas
            .get_value((row_offset + row as u32, column_offset + column as u32))
            .and_then(|text| formula_text(text));

        if let Some(formula) = &formula {
            charge_text(
                formula.len() as u64,
                "a formula",
                &name,
                row_offset + row as u32,
                column_offset + column as u32,
                options,
                text_bytes,
            )?;
        }

        absolute.push((
            row_offset + row as u32,
            column_offset + column as u32,
            value,
            formula,
        ));
    }

    // Excel writes a formula and, separately, the value it last computed. A formula cell with no
    // value is absent from the range entirely, and dropping it would hide the one thing the sheet
    // says about that cell.
    let mut valued: Vec<(u32, u32)> = absolute
        .iter()
        .map(|(row, column, _, _)| (*row, *column))
        .collect();
    valued.sort_unstable();

    for (row, column, text) in formulas.cells() {
        let absolute_row = formula_row_offset + row as u32;
        let absolute_column = formula_column_offset + column as u32;

        let Some(formula) = formula_text(text) else {
            continue;
        };

        if valued
            .binary_search(&(absolute_row, absolute_column))
            .is_ok()
        {
            continue;
        }

        charge_text(
            formula.len() as u64,
            "a formula",
            &name,
            absolute_row,
            absolute_column,
            options,
            text_bytes,
        )?;

        absolute.push((
            absolute_row,
            absolute_column,
            CellValue::Blank,
            Some(formula),
        ));
    }

    absolute.sort_by_key(|(row, column, _, _)| (*row, *column));

    let cells = absolute
        .into_iter()
        .map(|(row, column, value, formula)| CellAst {
            row: row as usize - row_offset as usize,
            column: column as usize - column_offset as usize,
            value,
            formula,
        })
        .collect();

    Ok(SheetAst {
        name,
        rows,
        columns,
        row_offset: row_offset as usize,
        column_offset: column_offset as usize,
        cells,
    })
}

/// Charges one cell's text against the per-cell ceiling and the workbook's text budget.
///
/// A number, a boolean or a duration costs nothing: it is a fixed few bytes in the AST. The strings
/// a cell can carry — its text, its date, its error name — are what a hostile package inflates.
fn charge(
    value: &CellValue,
    sheet: &str,
    row: u32,
    column: u32,
    options: &Options,
    spent: &mut u64,
) -> Result<(), ParseError> {
    let bytes = match value {
        CellValue::Text { text } => text.len(),
        CellValue::Date { iso, .. } => iso.len(),
        CellValue::Error { error } => error.len(),
        CellValue::Number { .. }
        | CellValue::Boolean { .. }
        | CellValue::Duration { .. }
        | CellValue::Blank => 0,
    };

    charge_text(bytes as u64, "the text", sheet, row, column, options, spent)
}

/// Refuses a string past the per-cell ceiling, or past the workbook's text budget.
///
/// The two ceilings answer different attacks: the per-cell one stops one shared string from being
/// the workbook, and the budget stops a million cells each just under it from being a gigabyte of
/// `String` the caller never asked for.
fn charge_text(
    bytes: u64,
    what: &str,
    sheet: &str,
    row: u32,
    column: u32,
    options: &Options,
    spent: &mut u64,
) -> Result<(), ParseError> {
    if bytes > options.max_cell_text_bytes {
        return Err(ParseError::Limit(format!(
            "{what} of cell {}{} on the sheet '{sheet}' is {bytes} bytes, over the {}-byte limit of one cell",
            column_name(column),
            row.saturating_add(1),
            options.max_cell_text_bytes
        )));
    }

    *spent = spent.saturating_add(bytes);

    if *spent > options.max_text_bytes {
        return Err(ParseError::Limit(format!(
            "the workbook holds more than the {}-byte limit of cell text",
            options.max_text_bytes
        )));
    }

    Ok(())
}

/// A column number as Excel names it, so the refusal names the cell a person can find.
fn column_name(mut column: u32) -> String {
    let mut name = Vec::new();

    loop {
        name.push(b'A' + (column % 26) as u8);

        if column < 26 {
            break;
        }

        column = column / 26 - 1;
    }

    name.reverse();

    String::from_utf8(name).unwrap_or_default()
}

/// A formula as the sheet writes it. `calamine` hands back the `<f>` text without the `=`, which is
/// how Excel stores it; a host reads `=SUM(B2:B9)`, so the sign goes back on.
fn formula_text(text: &str) -> Option<String> {
    let text = text.trim();
    if text.is_empty() {
        return None;
    }
    Some(if text.starts_with('=') {
        text.to_string()
    } else {
        format!("={text}")
    })
}

/// One `calamine` value as the AST types it, or `None` when there is nothing to report.
fn convert(value: &Data) -> Option<CellValue> {
    match value {
        Data::Empty => None,
        Data::Int(number) => Some(CellValue::Number {
            number: *number as f64,
        }),
        Data::Float(number) => number
            .is_finite()
            .then_some(CellValue::Number { number: *number }),
        Data::String(text) => Some(CellValue::Text { text: text.clone() }),
        Data::Bool(value) => Some(CellValue::Boolean { value: *value }),
        Data::DateTime(datetime) => Some(date(datetime)),
        Data::DateTimeIso(iso) => Some(CellValue::Date {
            date_only: !iso.contains('T'),
            iso: iso.clone(),
        }),
        // A duration with no serial behind it: the sheet wrote ISO 8601 text and no number, so the
        // text is what a host gets.
        Data::DurationIso(text) => Some(CellValue::Text { text: text.clone() }),
        Data::Error(error) => Some(CellValue::Error {
            error: error.to_string(),
        }),
    }
}

/// A serial date as ISO 8601, or a duration in seconds.
fn date(datetime: &ExcelDateTime) -> CellValue {
    if datetime.is_duration() {
        return CellValue::Duration {
            seconds: datetime.as_f64() * 86_400.0,
        };
    }

    let (year, month, day, hour, minute, second, millisecond) = datetime.to_ymd_hms_milli();
    let date_only = hour == 0 && minute == 0 && second == 0 && millisecond == 0;

    let iso = if date_only {
        format!("{year:04}-{month:02}-{day:02}")
    } else if millisecond == 0 {
        format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}")
    } else {
        format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}.{millisecond:03}")
    };

    CellValue::Date { iso, date_only }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::fixture::{self, Cell, Fixture, Value};

    fn read(fixture: &Fixture) -> WorkbookAst {
        extract(&fixture.build(), &Options::default()).expect("the fixture is a readable workbook")
    }

    #[test]
    fn a_text_cell_is_text() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::text(0, 0, "Total")]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Text {
                text: "Total".to_string()
            }
        );
    }

    #[test]
    fn a_number_cell_is_a_number() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::number(0, 1, 1234.5)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Number { number: 1234.5 }
        );
    }

    #[test]
    fn a_boolean_cell_is_a_boolean() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::boolean(0, 0, true)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Boolean { value: true }
        );
    }

    #[test]
    fn a_date_cell_is_a_date() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::date(0, 0, 45943.0)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Date {
                iso: "2025-10-13".to_string(),
                date_only: true,
            }
        );
    }

    #[test]
    fn a_date_with_a_time_keeps_its_time() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::date(0, 0, 45943.541)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Date {
                iso: "2025-10-13T12:59:02.400".to_string(),
                date_only: false,
            }
        );
    }

    #[test]
    fn a_duration_cell_is_a_duration() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::duration(0, 0, 1.5)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Duration { seconds: 129_600.0 }
        );
    }

    #[test]
    fn an_error_cell_is_an_error() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::error(0, 0, "#DIV/0!")]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Error {
                error: "#DIV/0!".to_string()
            }
        );
    }

    #[test]
    fn a_formula_cell_carries_its_formula_and_its_cached_value() {
        let workbook = read(&Fixture::new().sheet(
            "Facture",
            [
                Cell::number(0, 0, 2.0),
                Cell::formula(1, 0, "A1*2", Some(4.0)),
            ],
        ));

        let cell = &workbook.sheets[0].cells[1];

        assert_eq!(cell.formula.as_deref(), Some("=A1*2"));
        assert_eq!(cell.value, CellValue::Number { number: 4.0 });
    }

    #[test]
    fn a_formula_with_no_cached_value_is_blank_and_keeps_its_formula() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::formula(0, 0, "1+1", None)]));

        let cell = &workbook.sheets[0].cells[0];

        assert_eq!(cell.formula.as_deref(), Some("=1+1"));
        assert_eq!(cell.value, CellValue::Blank);
    }

    #[test]
    fn a_sheet_reports_its_used_region() {
        let workbook = read(
            &Fixture::new().sheet("Facture", [Cell::text(0, 0, "a"), Cell::number(2, 3, 1.0)]),
        );

        let sheet = &workbook.sheets[0];

        assert_eq!(sheet.rows, 3);
        assert_eq!(sheet.columns, 4);
        assert_eq!(sheet.cells.len(), 2);
        assert_eq!((sheet.cells[1].row, sheet.cells[1].column), (2, 3));
    }

    #[test]
    fn an_offset_region_reports_where_it_starts() {
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::at(4, 2, "C5")]));

        let sheet = &workbook.sheets[0];

        assert_eq!(sheet.row_offset, 4);
        assert_eq!(sheet.column_offset, 2);
        assert_eq!((sheet.cells[0].row, sheet.cells[0].column), (0, 0));
        assert_eq!(
            sheet.cells[0].value,
            CellValue::Text {
                text: "C5".to_string()
            }
        );
    }

    #[test]
    fn a_second_sheet_is_read_too() {
        let workbook = read(
            &Fixture::new()
                .sheet("Facture", [Cell::text(0, 0, "first")])
                .sheet("Annexe", [Cell::text(0, 0, "second")]),
        );

        assert_eq!(workbook.sheets.len(), 2);
        assert_eq!(workbook.sheets[0].name, "Facture");
        assert_eq!(workbook.sheets[1].name, "Annexe");
        assert_eq!(
            workbook.sheets[1].cells[0].value,
            CellValue::Text {
                text: "second".to_string()
            }
        );
    }

    #[test]
    fn an_empty_sheet_is_a_sheet_with_no_cells() {
        let workbook = read(&Fixture::new().empty_sheet("Vide"));

        assert_eq!(workbook.sheets.len(), 1);
        assert_eq!(workbook.sheets[0].rows, 0);
        assert_eq!(workbook.sheets[0].columns, 0);
        assert!(workbook.sheets[0].cells.is_empty());
    }

    #[test]
    fn bytes_that_are_not_a_package_are_refused() {
        let error = extract(b"not a package", &Options::default()).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_MALFORMED_PACKAGE);
        assert!(error.message().contains("could not be read"));
    }

    #[test]
    fn an_empty_workbook_is_refused() {
        let error = extract(&[], &Options::default()).unwrap_err();

        assert_eq!(
            error,
            ParseError::MalformedPackage("the workbook is empty".to_string())
        );
    }

    #[test]
    fn a_workbook_over_the_byte_limit_is_refused_before_it_is_opened() {
        let bytes = fixture::Fixture::new()
            .sheet("Facture", [Cell::text(0, 0, "a")])
            .build();

        let options = Options {
            max_bytes: 16,
            ..Options::default()
        };

        let error = extract(&bytes, &options).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_LIMIT);
        assert!(error.message().contains("16-byte limit"));
    }

    #[test]
    fn a_region_over_the_cell_limit_is_refused() {
        // The ceiling measures the used region, not the cells that hold a value: a hundred cells in
        // a ten-by-ten region is a table no host should be asked to build either.
        let bytes = fixture::Fixture::new()
            .sheet(
                "Facture",
                [Cell::number(0, 0, 1.0), Cell::number(9, 9, 2.0)],
            )
            .build();

        let options = Options {
            max_cells: 50,
            ..Options::default()
        };

        let error = extract(&bytes, &options).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_LIMIT);
        assert!(error.message().contains("50-cell limit"));
    }

    #[test]
    fn a_workbook_over_the_sheet_limit_is_refused() {
        // A workbook is tabs a person named. Past the cap the package is a generator, and every
        // sheet it declares is a part the zip has already expanded, so the refusal comes before the
        // first one is read.
        let mut fixture = Fixture::new();

        for index in 0..=DEFAULT_MAX_SHEETS {
            fixture = fixture.empty_sheet(&format!("Feuille{index}"));
        }

        let error = extract(&fixture.build(), &Options::default()).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_LIMIT);
        assert!(error.message().contains("257 sheets"));
        assert!(error.message().contains("256-sheet limit"));
    }

    #[test]
    fn a_text_cell_over_the_per_cell_limit_is_refused() {
        // Excel refuses a cell over 32 767 characters; this one is twice the suite's own ceiling, so
        // no producer writes it and the shared-string table is what it costs to find out.
        let text = "x".repeat(DEFAULT_MAX_CELL_TEXT_BYTES as usize + 1);
        let bytes = Fixture::new()
            .sheet("Facture", [Cell::text(0, 0, &text)])
            .build();

        let error = extract(&bytes, &Options::default()).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_LIMIT);
        assert!(error.message().contains("cell A1"));
        assert!(error.message().contains("65537 bytes"));
        assert!(error.message().contains("65536-byte limit of one cell"));
    }

    #[test]
    fn a_text_cell_at_the_per_cell_limit_is_read() {
        // The control: the ceiling refuses what is past it, not what reaches it.
        let text = "x".repeat(DEFAULT_MAX_CELL_TEXT_BYTES as usize);
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::text(0, 0, &text)]));

        assert_eq!(workbook.sheets[0].cells[0].value, CellValue::Text { text });
    }

    #[test]
    fn a_workbook_over_the_text_budget_is_refused() {
        // Each cell is well under its own ceiling; together they are more text than the caller asked
        // for. The budget is what stops a million legal cells from being a gigabyte of `String`.
        let bytes = Fixture::new()
            .sheet(
                "Facture",
                [Cell::text(0, 0, "aaaa"), Cell::text(0, 1, "bbbb")],
            )
            .build();

        let options = Options {
            max_text_bytes: 6,
            ..Options::default()
        };

        let error = extract(&bytes, &options).unwrap_err();

        assert_eq!(error.code(), crate::ffi::TX_ERR_LIMIT);
        assert!(error.message().contains("6-byte limit of cell text"));
    }

    #[test]
    fn a_rarer_quarter_is_not_a_date() {
        // 1.75 formatted as a plain number stays a number, whatever the date code does.
        let workbook = read(&Fixture::new().sheet("Facture", [Cell::number(0, 0, 1.75)]));

        assert_eq!(
            workbook.sheets[0].cells[0].value,
            CellValue::Number { number: 1.75 }
        );
    }

    #[test]
    fn the_fixture_values_are_the_ones_the_sheet_declares() {
        let fixture = Fixture::new().sheet(
            "Facture",
            [
                Cell::text(0, 0, "Facture"),
                Cell::number(0, 1, 42.0),
                Cell::boolean(0, 2, false),
            ],
        );

        let value = match &fixture.sheets[0].cells[0].value {
            Value::Text(text) => text,
            other => panic!("expected text, got {other:?}"),
        };

        assert_eq!(*value, "Facture");
    }
}
