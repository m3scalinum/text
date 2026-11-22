//! A minimal but real `.xlsx` writer, for the tests only.
//!
//! The crate ships no binary fixture: the package is written here, from the same parts Excel writes,
//! so a test states the workbook it means and the bytes follow from that. The parts are the ones
//! `calamine` requires — content types, both relationship files, the workbook, its sheets, a shared
//! string table and a style table with one date format and one duration format.

use std::io::{Cursor, Write};

use zip::write::SimpleFileOptions;

/// A workbook under construction.
#[derive(Debug, Default)]
pub struct Fixture {
    /// The sheets, in workbook order.
    pub sheets: Vec<Sheet>,
}

/// One sheet and the cells placed on it.
#[derive(Debug)]
pub struct Sheet {
    /// The tab name.
    pub name: String,
    /// The cells, in no particular order.
    pub cells: Vec<Cell>,
}

/// One cell and its value.
#[derive(Debug, Clone)]
pub struct Cell {
    /// Zero-based sheet row.
    pub row: u32,
    /// Zero-based sheet column.
    pub column: u32,
    /// What the cell holds.
    pub value: Value,
}

/// What a cell holds, as the fixture writes it.
#[derive(Debug, Clone)]
pub enum Value {
    /// An entry of the shared string table.
    Text(String),
    /// A number.
    Number(f64),
    /// A boolean.
    Boolean(bool),
    /// A serial date, in days since the 1900 epoch, formatted as a date.
    Date(f64),
    /// A serial duration, in days, formatted as `[h]:mm:ss`.
    Duration(f64),
    /// An error value.
    Error(String),
    /// A formula and, when the workbook computed it, its cached result.
    Formula {
        /// The formula, without its leading `=`.
        text: String,
        /// The cached result, when the package holds one.
        cached: Option<f64>,
    },
}

impl Cell {
    /// A text cell.
    pub fn text(row: u32, column: u32, text: &str) -> Self {
        Self {
            row,
            column,
            value: Value::Text(text.to_string()),
        }
    }

    /// A text cell, placed explicitly: the name of the constructor says where the cell sits.
    pub fn at(row: u32, column: u32, text: &str) -> Self {
        Self::text(row, column, text)
    }

    /// A number cell.
    pub fn number(row: u32, column: u32, number: f64) -> Self {
        Self {
            row,
            column,
            value: Value::Number(number),
        }
    }

    /// A boolean cell.
    pub fn boolean(row: u32, column: u32, value: bool) -> Self {
        Self {
            row,
            column,
            value: Value::Boolean(value),
        }
    }

    /// A date cell, from a serial day number.
    pub fn date(row: u32, column: u32, serial: f64) -> Self {
        Self {
            row,
            column,
            value: Value::Date(serial),
        }
    }

    /// A duration cell, from a serial number of days.
    pub fn duration(row: u32, column: u32, days: f64) -> Self {
        Self {
            row,
            column,
            value: Value::Duration(days),
        }
    }

    /// An error cell.
    pub fn error(row: u32, column: u32, error: &str) -> Self {
        Self {
            row,
            column,
            value: Value::Error(error.to_string()),
        }
    }

    /// A formula cell, with the result the workbook cached for it when it computed one.
    pub fn formula(row: u32, column: u32, text: &str, cached: Option<f64>) -> Self {
        Self {
            row,
            column,
            value: Value::Formula {
                text: text.to_string(),
                cached,
            },
        }
    }
}

impl Fixture {
    /// An empty workbook.
    pub fn new() -> Self {
        Self::default()
    }

    /// Adds a sheet with the given cells.
    pub fn sheet<const N: usize>(mut self, name: &str, cells: [Cell; N]) -> Self {
        self.sheets.push(Sheet {
            name: name.to_string(),
            cells: cells.into_iter().collect(),
        });
        self
    }

    /// Adds a sheet with no cell at all.
    pub fn empty_sheet(mut self, name: &str) -> Self {
        self.sheets.push(Sheet {
            name: name.to_string(),
            cells: Vec::new(),
        });
        self
    }

    /// Writes the package.
    pub fn build(&self) -> Vec<u8> {
        let strings = self.strings();
        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options =
            SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);

        let mut write = |name: &str, content: String| {
            writer
                .start_file(name, options)
                .expect("the fixture writes a zip entry");
            writer
                .write_all(content.as_bytes())
                .expect("the fixture writes the entry's bytes");
        };

        write("[Content_Types].xml", content_types(self.sheets.len()));
        write("_rels/.rels", package_relationships());
        write("xl/workbook.xml", self.workbook_xml());
        write("xl/_rels/workbook.xml.rels", self.workbook_relationships());
        write("xl/styles.xml", styles());
        write("xl/sharedStrings.xml", shared_strings(&strings));

        for (index, sheet) in self.sheets.iter().enumerate() {
            write(
                &format!("xl/worksheets/sheet{}.xml", index + 1),
                sheet_xml(sheet, &strings),
            );
        }

        writer
            .finish()
            .expect("the fixture closes its zip")
            .into_inner()
    }

    /// Every text cell, in sheet order, as the shared string table holds it.
    fn strings(&self) -> Vec<String> {
        self.sheets
            .iter()
            .flat_map(|sheet| sheet.cells.iter())
            .filter_map(|cell| match &cell.value {
                Value::Text(text) => Some(text.clone()),
                _ => None,
            })
            .collect()
    }

    fn workbook_xml(&self) -> String {
        const HEAD: &str = concat!(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ",
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">",
            "<sheets>"
        );

        let sheets: String = self
            .sheets
            .iter()
            .enumerate()
            .map(|(index, sheet)| {
                format!(
                    "<sheet name=\"{}\" sheetId=\"{}\" r:id=\"rId{}\"/>",
                    escape(&sheet.name),
                    index + 1,
                    index + 1
                )
            })
            .collect();

        format!("{HEAD}{sheets}</sheets></workbook>")
    }

    fn workbook_relationships(&self) -> String {
        const HEAD: &str = concat!(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        );
        const WORKSHEET: &str =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

        let sheets: String = self
            .sheets
            .iter()
            .enumerate()
            .map(|(index, _)| {
                format!(
                    "<Relationship Id=\"rId{}\" Type=\"{WORKSHEET}\" \
                     Target=\"worksheets/sheet{}.xml\"/>",
                    index + 1,
                    index + 1
                )
            })
            .collect();

        let styles_id = self.sheets.len() + 1;
        let strings_id = self.sheets.len() + 2;

        format!(
            concat!(
                "{HEAD}{sheets}",
                "<Relationship Id=\"rId{styles_id}\" ",
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" ",
                "Target=\"styles.xml\"/>",
                "<Relationship Id=\"rId{strings_id}\" ",
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" ",
                "Target=\"sharedStrings.xml\"/>",
                "</Relationships>"
            ),
            HEAD = HEAD,
            sheets = sheets,
            styles_id = styles_id,
            strings_id = strings_id,
        )
    }
}

fn content_types(sheets: usize) -> String {
    const HEAD: &str = concat!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">",
        "<Default Extension=\"rels\" ",
        "ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>",
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>",
        "<Override PartName=\"/xl/workbook.xml\" ",
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>",
        "<Override PartName=\"/xl/styles.xml\" ",
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>",
        "<Override PartName=\"/xl/sharedStrings.xml\" ",
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
    );

    let worksheets: String = (1..=sheets)
        .map(|index| {
            format!(
                "<Override PartName=\"/xl/worksheets/sheet{index}.xml\" \
                 ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
            )
        })
        .collect();

    format!("{HEAD}{worksheets}</Types>")
}

fn package_relationships() -> String {
    concat!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">",
        "<Relationship Id=\"rId1\" ",
        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" ",
        "Target=\"xl/workbook.xml\"/>",
        "</Relationships>"
    )
    .to_string()
}

/// Three cell formats: the default, a date (`numFmtId` 14, the built-in `mm-dd-yy`) and one custom
/// duration format, which is how `calamine` tells an elapsed time from a timestamp.
fn styles() -> String {
    concat!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">",
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"[h]:mm:ss\"/></numFmts>",
        "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>",
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>",
        "<fill><patternFill patternType=\"gray125\"/></fill></fills>",
        "<borders count=\"1\"><border/></borders>",
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>",
        "<cellXfs count=\"3\">",
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>",
        "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>",
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>",
        "</cellXfs>",
        "</styleSheet>"
    )
    .to_string()
}

fn shared_strings(strings: &[String]) -> String {
    let items: String = strings
        .iter()
        .map(|text| format!("<si><t xml:space=\"preserve\">{}</t></si>", escape(text)))
        .collect();

    format!(
        concat!(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
            "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ",
            "count=\"{count}\" uniqueCount=\"{count}\">{items}</sst>"
        ),
        count = strings.len(),
        items = items
    )
}

fn sheet_xml(sheet: &Sheet, strings: &[String]) -> String {
    const HEAD: &str = concat!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
        "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
    );

    let dimension = match dimension(sheet) {
        Some(reference) => format!("<dimension ref=\"{reference}\"/>"),
        None => String::new(),
    };

    let mut rows: Vec<String> = Vec::new();
    let mut row_index: Option<u32> = None;
    let mut cells = String::new();

    let mut sorted: Vec<&Cell> = sheet.cells.iter().collect();
    sorted.sort_by_key(|cell| (cell.row, cell.column));

    for cell in sorted {
        if row_index != Some(cell.row) {
            if let Some(row) = row_index {
                rows.push(format!("<row r=\"{}\">{cells}</row>", row + 1));
            }
            row_index = Some(cell.row);
            cells = String::new();
        }

        cells.push_str(&cell_xml(cell, strings));
    }

    if let Some(row) = row_index {
        rows.push(format!("<row r=\"{}\">{cells}</row>", row + 1));
    }

    format!(
        "{HEAD}{dimension}<sheetData>{}</sheetData></worksheet>",
        rows.concat()
    )
}

fn dimension(sheet: &Sheet) -> Option<String> {
    let mut min_row = u32::MAX;
    let mut min_column = u32::MAX;
    let mut max_row = 0;
    let mut max_column = 0;

    for cell in &sheet.cells {
        min_row = min_row.min(cell.row);
        min_column = min_column.min(cell.column);
        max_row = max_row.max(cell.row);
        max_column = max_column.max(cell.column);
    }

    if sheet.cells.is_empty() {
        return None;
    }

    let start = reference(min_row, min_column);
    let end = reference(max_row, max_column);

    Some(if start == end {
        start
    } else {
        format!("{start}:{end}")
    })
}

fn cell_xml(cell: &Cell, strings: &[String]) -> String {
    let at = reference(cell.row, cell.column);

    match &cell.value {
        Value::Text(text) => {
            let index = strings
                .iter()
                .position(|entry| entry == text)
                .expect("every text cell is in the shared string table");
            format!("<c r=\"{at}\" t=\"s\"><v>{index}</v></c>")
        }
        Value::Number(number) => format!("<c r=\"{at}\"><v>{number}</v></c>"),
        Value::Boolean(value) => {
            let flag = if *value { 1 } else { 0 };
            format!("<c r=\"{at}\" t=\"b\"><v>{flag}</v></c>")
        }
        Value::Date(serial) => format!("<c r=\"{at}\" s=\"1\"><v>{serial}</v></c>"),
        Value::Duration(days) => format!("<c r=\"{at}\" s=\"2\"><v>{days}</v></c>"),
        Value::Error(error) => format!("<c r=\"{at}\" t=\"e\"><v>{}</v></c>", escape(error)),
        Value::Formula { text, cached } => match cached {
            Some(value) => format!("<c r=\"{at}\"><f>{}</f><v>{value}</v></c>", escape(text)),
            None => format!("<c r=\"{at}\"><f>{}</f></c>", escape(text)),
        },
    }
}

/// `A1`-style reference for a zero-based row and column.
fn reference(row: u32, column: u32) -> String {
    let mut letters = String::new();
    let mut column = column + 1;

    while column > 0 {
        let remainder = (column - 1) % 26;
        letters.insert(0, char::from(b'A' + remainder as u8));
        column = (column - 1) / 26;
    }

    format!("{letters}{}", row + 1)
}

fn escape(text: &str) -> String {
    let mut escaped = String::with_capacity(text.len());

    for character in text.chars() {
        match character {
            '&' => escaped.push_str("&amp;"),
            '<' => escaped.push_str("&lt;"),
            '>' => escaped.push_str("&gt;"),
            '"' => escaped.push_str("&quot;"),
            _ => escaped.push(character),
        }
    }

    escaped
}
