//! The compact AST handed to the managed side as JSON.
//!
//! Shape, as documented in `docs/specifications/22-docx.md` §4:
//!
//! ```json
//! {
//!   "blocks": [
//!     {"type": "paragraph", "heading": 1, "runs": [{"type": "text", "text": "Report"}]},
//!     {"type": "paragraph", "list": {"numberingId": 1, "level": 0, "ordered": false},
//!      "runs": [{"type": "text", "text": "First finding"}]},
//!     {"type": "paragraph", "runs": [
//!       {"type": "image", "name": "word/media/image1.png", "mimeType": "image/png", "bytesHandle": 0}]},
//!     {"type": "table", "columns": 3, "rows": [
//!       {"cells": [{"colspan": 2, "rowspan": 1, "blocks": []}]}]}
//!   ],
//!   "page": {"widthPt": 595.3, "heightPt": 841.9, "marginLeftPt": 85.05},
//!   "images": [{"name": "word/media/image1.png", "mimeType": "image/png", "bytesHandle": 0, "length": 74}]
//! }
//! ```
//!
//! `bytesHandle` is the index a second call, `td_document_image`, turns into bytes.

use serde::Serialize;

/// A parsed document: its body, its page setup when the file states one, and its media.
#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DocumentAst {
    /// Body content, in document order.
    pub blocks: Vec<Block>,
    /// Page setup, absent when the document only carries Word's defaults.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub page: Option<PageSetup>,
    /// The media table. Empty when images were not extracted.
    pub images: Vec<ImageRef>,
}

/// One block of the body.
#[derive(Debug, Clone, Serialize)]
#[serde(
    tag = "type",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum Block {
    /// A paragraph of runs.
    Paragraph {
        /// Heading level, 1 to 6, when the paragraph carries a heading style.
        #[serde(skip_serializing_if = "Option::is_none")]
        heading: Option<u8>,
        /// List membership, when the paragraph carries numbering.
        #[serde(skip_serializing_if = "Option::is_none")]
        list: Option<ListInfo>,
        /// Inline content, in document order.
        runs: Vec<Run>,
    },
    /// A table.
    Table {
        /// Number of columns the table declares in its grid.
        columns: usize,
        /// Rows in document order.
        rows: Vec<TableRow>,
    },
}

/// Where a paragraph sits in a list.
#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ListInfo {
    /// The `numId` the paragraph points at.
    pub numbering_id: i64,
    /// Nesting level, zero-based.
    pub level: u8,
    /// Whether the level's format counts: `false` for a bullet.
    pub ordered: bool,
}

/// A table row.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TableRow {
    /// Cells, left to right. A cell a row above spans into this row is not repeated here.
    pub cells: Vec<TableCell>,
}

/// A table cell.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TableCell {
    /// Columns the cell covers.
    pub colspan: u32,
    /// Rows the cell covers.
    pub rowspan: u32,
    /// The cell body.
    pub blocks: Vec<Block>,
}

/// Inline content of a paragraph.
#[derive(Debug, Clone, Serialize)]
#[serde(
    tag = "type",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum Run {
    /// A run of text and its formatting.
    Text {
        /// The characters, with Word's `xml:space` handling applied.
        text: String,
        #[serde(default, skip_serializing_if = "is_false")]
        bold: bool,
        #[serde(default, skip_serializing_if = "is_false")]
        italic: bool,
        /// The underline style Word names, for example `single`.
        #[serde(skip_serializing_if = "Option::is_none")]
        underline: Option<String>,
        #[serde(default, skip_serializing_if = "is_false")]
        strike: bool,
        /// `superscript` or `subscript`.
        #[serde(skip_serializing_if = "Option::is_none")]
        script: Option<String>,
        /// `RRGGBB`, as Word writes it.
        #[serde(skip_serializing_if = "Option::is_none")]
        color: Option<String>,
        /// Font size in points, from Word's half-points.
        #[serde(skip_serializing_if = "Option::is_none")]
        size_pt: Option<f64>,
    },
    /// An embedded image. Its bytes come from `td_document_image`.
    Image {
        /// The part name inside the package, with forward slashes.
        name: String,
        /// Declared from the part's extension.
        mime_type: String,
        /// Index into the media table.
        bytes_handle: u64,
    },
    /// A line break inside a paragraph.
    Break,
}

/// The media table entry: what the run references and how large it is.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ImageRef {
    /// The part name inside the package, with forward slashes.
    pub name: String,
    /// Declared from the part's extension.
    pub mime_type: String,
    /// Index `td_document_image` takes.
    pub bytes_handle: u64,
    /// Byte count, so a host can size its buffer before asking for the bytes.
    pub length: u64,
}

/// Page geometry, in points (1/72 inch).
#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PageSetup {
    /// Page width.
    pub width_pt: f64,
    /// Page height.
    pub height_pt: f64,
    /// Which edge Word calls the top; a landscape section already has width and height swapped.
    pub landscape: bool,
    /// Top margin.
    pub margin_top_pt: f64,
    /// Right margin.
    pub margin_right_pt: f64,
    /// Bottom margin.
    pub margin_bottom_pt: f64,
    /// Left margin.
    pub margin_left_pt: f64,
}

/// `serde` helper: a false flag is left out of the JSON, which is what makes the AST compact.
fn is_false(value: &bool) -> bool {
    !*value
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_plain_paragraph_omits_every_false_flag() {
        let ast = DocumentAst {
            blocks: vec![Block::Paragraph {
                heading: None,
                list: None,
                runs: vec![Run::Text {
                    text: "hello".to_owned(),
                    bold: false,
                    italic: false,
                    underline: None,
                    strike: false,
                    script: None,
                    color: None,
                    size_pt: None,
                }],
            }],
            page: None,
            images: Vec::new(),
        };

        let json = serde_json::to_string(&ast).unwrap();

        assert_eq!(
            json,
            r#"{"blocks":[{"type":"paragraph","runs":[{"type":"text","text":"hello"}]}],"images":[]}"#
        );
    }

    #[test]
    fn a_heading_keeps_its_level_and_its_list_stays_out_of_the_way() {
        let block = Block::Paragraph {
            heading: Some(2),
            list: None,
            runs: Vec::new(),
        };

        let json = serde_json::to_string(&block).unwrap();

        assert!(json.contains(r#""heading":2"#));
        assert!(!json.contains("list"));
    }

    #[test]
    fn a_cell_keeps_both_spans() {
        let cell = TableCell {
            colspan: 2,
            rowspan: 1,
            blocks: Vec::new(),
        };

        let json = serde_json::to_string(&cell).unwrap();

        assert_eq!(json, r#"{"colspan":2,"rowspan":1,"blocks":[]}"#);
    }
}
