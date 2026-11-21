//! Turns a `.docx` package into [`DocumentAst`].
//!
//! `docx-rs` reads the package and exposes the document tree; its serde representation is the
//! documented JSON API of that crate, and reading it is how this module sees run properties whose
//! fields are private. Everything else — headings, list formats, span arithmetic, the media table —
//! is decided here, so the managed package receives a tree it does not have to interpret again.

use std::collections::HashMap;
use std::io::Cursor;

use docx_rs::{Docx, ReadDocxOptions, read_docx_with_options};
use serde::Deserialize;
use serde_json::Value;

use crate::ast::{Block, DocumentAst, ImageRef, ListInfo, PageSetup, Run, TableCell, TableRow};

/// The ceiling on a document this reader will open. A range request gone wrong should fail with a
/// clear error, not by exhausting the host's memory.
pub const DEFAULT_MAX_BYTES: u64 = 64 * 1024 * 1024;

/// Word's default page, which `docx-rs` reports even for a document that states no `<w:sectPr>`.
const DEFAULT_PAGE_WIDTH: i64 = 11906;
const DEFAULT_PAGE_HEIGHT: i64 = 16838;

/// What the caller asked for.
#[derive(Debug, Clone, Copy)]
pub struct Options {
    /// Whether media parts are read and image runs appear in the AST at all.
    pub images: bool,
    /// The largest document, in bytes, this call will open.
    pub max_bytes: u64,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            images: true,
            max_bytes: DEFAULT_MAX_BYTES,
        }
    }
}

/// Why a package could not be read.
#[derive(Debug)]
pub enum ParseError {
    /// Not a zip package, or a part of it does not parse.
    MalformedPackage(String),
    /// A part this reader needs is absent.
    MissingPart(String),
    /// Larger than the configured ceiling.
    Limit(String),
}

impl ParseError {
    /// The status code `td_document_parse` returns for this failure.
    pub fn code(&self) -> i32 {
        match self {
            Self::MalformedPackage(_) => crate::ffi::TD_ERR_MALFORMED_PACKAGE,
            Self::MissingPart(_) => crate::ffi::TD_ERR_MISSING_PART,
            Self::Limit(_) => crate::ffi::TD_ERR_LIMIT,
        }
    }

    /// The sentence a host shows its user.
    pub fn message(&self) -> String {
        match self {
            Self::MalformedPackage(message) | Self::MissingPart(message) | Self::Limit(message) => {
                message.clone()
            }
        }
    }
}

/// One media part, held by the document handle until `td_document_image` hands it back.
#[derive(Debug, Clone)]
pub struct Media {
    /// Part name inside the package, forward slashes.
    pub name: String,
    /// Declared from the part's extension.
    pub mime_type: String,
    /// The bytes, exactly as they were stored.
    pub bytes: Vec<u8>,
}

/// A parsed document and the media its image runs point at.
pub struct Extracted {
    /// The tree the managed side reads.
    pub ast: DocumentAst,
    /// Indexed by `bytesHandle`.
    pub media: Vec<Media>,
}

/// Reads a package into an AST.
pub fn extract(bytes: &[u8], options: &Options) -> Result<Extracted, ParseError> {
    if bytes.len() as u64 > options.max_bytes {
        return Err(ParseError::Limit(format!(
            "the document is {} bytes, over the {}-byte limit",
            bytes.len(),
            options.max_bytes
        )));
    }

    check_package(bytes)?;

    let docx = read_docx_with_options(bytes, ReadDocxOptions::default().with_image_previews(false))
        .map_err(|error| {
            ParseError::MalformedPackage(format!("the package could not be read: {error}"))
        })?;

    let (media, by_rel) = read_media(&docx, options.images);

    let styles = style_hints(&docx);
    let numbering = numbering_formats(&docx);

    let document = read_document(&docx)?;
    let context = Context {
        styles: &styles,
        numbering: &numbering,
        media: &media,
        media_by_rel: &by_rel,
    };

    let ast = DocumentAst {
        blocks: convert_blocks(&document.children, &context),
        page: document.section_property.as_ref().and_then(page_setup),
        images: media
            .iter()
            .enumerate()
            .map(|(index, part)| ImageRef {
                name: part.name.clone(),
                mime_type: part.mime_type.clone(),
                bytes_handle: index as u64,
                length: part.bytes.len() as u64,
            })
            .collect(),
    };

    Ok(Extracted { ast, media })
}

/// Rejects the two failures a reader must name precisely: bytes that are not a package at all, and a
/// package that lacks a part every DOCX must carry.
fn check_package(bytes: &[u8]) -> Result<(), ParseError> {
    let mut archive = zip::ZipArchive::new(Cursor::new(bytes)).map_err(|error| {
        ParseError::MalformedPackage(format!("the bytes are not a zip package: {error}"))
    })?;

    for part in ["[Content_Types].xml", "_rels/.rels", "word/document.xml"] {
        if archive.by_name(part).is_err() {
            return Err(ParseError::MissingPart(format!(
                "the package has no {part}"
            )));
        }
    }

    Ok(())
}

/// Reads every media part the document's relationships name, and maps each relationship id to the
/// index the AST and `td_document_image` use.
///
/// Two relationships to the same part share one handle: the AST names parts, not relationships, so a
/// picture reused twice must not become two assets on the Typst side.
fn read_media(docx: &Docx, extract: bool) -> (Vec<Media>, HashMap<String, u64>) {
    let mut media: Vec<Media> = Vec::new();
    let mut by_rel: HashMap<String, u64> = HashMap::new();

    if !extract {
        return (media, by_rel);
    }

    let mut by_name: HashMap<String, u64> = HashMap::new();

    for (rel_id, path, image, _) in &docx.images {
        let name = normalise_part_name(path);
        let handle = match by_name.get(&name) {
            Some(handle) => *handle,
            None => {
                let handle = media.len() as u64;
                media.push(Media {
                    mime_type: mime_type(&name).to_owned(),
                    name: name.clone(),
                    bytes: image.0.clone(),
                });
                by_name.insert(name, handle);
                handle
            }
        };

        by_rel.insert(rel_id.clone(), handle);
    }

    (media, by_rel)
}

/// `docx-rs` reports part names with the separators the zip stores, which on Windows can be a
/// backslash. Every consumer of the name — Typst asset paths, the other readers — wants `/`.
fn normalise_part_name(path: &str) -> String {
    path.replace('\\', "/")
}

/// The MIME type of a media part, from its extension only.
fn mime_type(name: &str) -> &'static str {
    let extension = name.rsplit('.').next().unwrap_or_default().to_lowercase();

    match extension.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "tif" | "tiff" => "image/tiff",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "emf" => "image/x-emf",
        "wmf" => "image/x-wmf",
        _ => "application/octet-stream",
    }
}

// ---------------------------------------------------------------------------
// The shapes `docx-rs` serialises
// ---------------------------------------------------------------------------

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawDocument {
    #[serde(default)]
    children: Vec<RawNode>,
    #[serde(default)]
    section_property: Option<RawSection>,
}

/// A node of the serialised tree: a discriminator and the payload that goes with it.
#[derive(Debug, Deserialize)]
struct RawNode {
    #[serde(rename = "type", default)]
    kind: String,
    #[serde(default)]
    data: Value,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawParagraph {
    #[serde(default)]
    children: Vec<RawNode>,
    #[serde(default)]
    property: RawParagraphProperty,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawParagraphProperty {
    #[serde(default)]
    style: Option<String>,
    #[serde(default)]
    numbering_property: Option<RawNumbering>,
    #[serde(default)]
    outline_lvl: Option<u8>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawNumbering {
    #[serde(default)]
    id: i64,
    #[serde(default)]
    level: u8,
}

/// A hyperlink or a tracked insertion: both hold runs, and both are flattened to their text.
#[derive(Debug, Deserialize)]
struct RawContainer {
    #[serde(default)]
    children: Vec<RawNode>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawRun {
    #[serde(default)]
    children: Vec<RawNode>,
    #[serde(default)]
    run_property: RawRunProperty,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawRunProperty {
    #[serde(default)]
    bold: Option<bool>,
    #[serde(default)]
    italic: Option<bool>,
    #[serde(default)]
    strike: Option<bool>,
    #[serde(default)]
    dstrike: Option<bool>,
    #[serde(default)]
    underline: Option<Value>,
    #[serde(default)]
    vert_align: Option<Value>,
    #[serde(default)]
    color: Option<Value>,
    #[serde(default)]
    sz: Option<f64>,
}

#[derive(Debug, Deserialize)]
struct RawText {
    #[serde(default)]
    text: String,
}

#[derive(Debug, Deserialize)]
struct RawDrawing {
    #[serde(rename = "type", default)]
    kind: String,
    #[serde(default)]
    data: Value,
}

#[derive(Debug, Deserialize)]
struct RawPicture {
    #[serde(default)]
    id: String,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawTable {
    #[serde(default)]
    grid: Vec<u64>,
    #[serde(default)]
    rows: Vec<RawNode>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawTableRow {
    #[serde(default)]
    cells: Vec<RawNode>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawTableCell {
    #[serde(default)]
    children: Vec<RawNode>,
    #[serde(default)]
    property: RawCellProperty,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawCellProperty {
    #[serde(default)]
    grid_span: Option<u32>,
    #[serde(default)]
    vertical_merge: Option<Value>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawSection {
    #[serde(default)]
    page_size: Option<RawPageSize>,
    #[serde(default)]
    page_margin: Option<RawPageMargin>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawPageSize {
    #[serde(default)]
    w: i64,
    #[serde(default)]
    h: i64,
    #[serde(default)]
    orient: Option<String>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawPageMargin {
    #[serde(default)]
    top: i64,
    #[serde(default)]
    right: i64,
    #[serde(default)]
    bottom: i64,
    #[serde(default)]
    left: i64,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawStyles {
    #[serde(default)]
    styles: Vec<RawStyle>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawStyle {
    #[serde(default)]
    style_id: String,
    #[serde(default)]
    name: String,
    #[serde(default)]
    paragraph_property: RawParagraphProperty,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawNumberings {
    #[serde(default)]
    abstract_nums: Vec<RawAbstractNumbering>,
    #[serde(default)]
    numberings: Vec<RawNumberingDefinition>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawAbstractNumbering {
    #[serde(default)]
    id: i64,
    #[serde(default)]
    levels: Vec<RawLevel>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawLevel {
    #[serde(default)]
    level: u8,
    #[serde(default)]
    format: String,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawNumberingDefinition {
    #[serde(default)]
    id: i64,
    #[serde(default)]
    abstract_num_id: i64,
}

// ---------------------------------------------------------------------------
// Document scaffolding
// ---------------------------------------------------------------------------

fn read_document(docx: &Docx) -> Result<RawDocument, ParseError> {
    let value = serde_json::to_value(&docx.document).map_err(|error| {
        ParseError::MalformedPackage(format!("the document tree could not be read: {error}"))
    })?;
    serde_json::from_value(value).map_err(|error| {
        ParseError::MalformedPackage(format!(
            "the document tree has an unexpected shape: {error}"
        ))
    })
}

/// What a style says about headings: its outline level, and its name.
#[derive(Debug, Clone)]
struct StyleHint {
    outline: Option<u8>,
    name: String,
}

fn style_hints(docx: &Docx) -> HashMap<String, StyleHint> {
    let Ok(value) = serde_json::to_value(&docx.styles) else {
        return HashMap::new();
    };
    let Ok(styles) = serde_json::from_value::<RawStyles>(value) else {
        return HashMap::new();
    };

    styles
        .styles
        .into_iter()
        .filter(|style| !style.style_id.is_empty())
        .map(|style| {
            let hint = StyleHint {
                outline: style.paragraph_property.outline_lvl,
                name: style.name,
            };
            (style.style_id, hint)
        })
        .collect()
}

/// For each `numId`, whether each of its levels counts. A bullet is not ordered.
fn numbering_formats(docx: &Docx) -> HashMap<i64, Vec<bool>> {
    let Ok(value) = serde_json::to_value(&docx.numberings) else {
        return HashMap::new();
    };
    let Ok(numberings) = serde_json::from_value::<RawNumberings>(value) else {
        return HashMap::new();
    };

    let mut by_abstract: HashMap<i64, Vec<bool>> = HashMap::new();

    for abstract_numbering in numberings.abstract_nums {
        let mut levels = vec![false; abstract_numbering.levels.len()];
        for level in abstract_numbering.levels {
            let ordered = !matches!(level.format.as_str(), "bullet" | "none");
            if let Some(slot) = levels.get_mut(level.level as usize) {
                *slot = ordered;
            }
        }
        by_abstract.insert(abstract_numbering.id, levels);
    }

    let mut by_num_id: HashMap<i64, Vec<bool>> = HashMap::new();
    for numbering in numberings.numberings {
        if let Some(levels) = by_abstract.get(&numbering.abstract_num_id) {
            by_num_id.insert(numbering.id, levels.clone());
        }
    }

    by_num_id
}

fn page_setup(section: &RawSection) -> Option<PageSetup> {
    let size = section.page_size.as_ref()?;
    let margin = section.page_margin.clone().unwrap_or_default();

    // `docx-rs` fills Word's defaults whether the file states them or not, so a section that equals
    // the defaults is reported as "no page setup stated" instead of asserting what it cannot know.
    let is_default = size.w == DEFAULT_PAGE_WIDTH
        && size.h == DEFAULT_PAGE_HEIGHT
        && margin
            == RawPageMargin {
                top: 1985,
                right: 1701,
                bottom: 1701,
                left: 1701,
            };
    if is_default {
        return None;
    }

    Some(PageSetup {
        width_pt: twips_to_points(size.w),
        height_pt: twips_to_points(size.h),
        landscape: size.orient.as_deref() == Some("landscape"),
        margin_top_pt: twips_to_points(margin.top),
        margin_right_pt: twips_to_points(margin.right),
        margin_bottom_pt: twips_to_points(margin.bottom),
        margin_left_pt: twips_to_points(margin.left),
    })
}

/// A twip is a twentieth of a point; Word measures pages and margins in twips.
fn twips_to_points(twips: i64) -> f64 {
    (twips as f64 / 20.0 * 100.0).round() / 100.0
}

// ---------------------------------------------------------------------------
// Blocks
// ---------------------------------------------------------------------------

struct Context<'a> {
    styles: &'a HashMap<String, StyleHint>,
    numbering: &'a HashMap<i64, Vec<bool>>,
    media: &'a [Media],
    media_by_rel: &'a HashMap<String, u64>,
}

fn convert_blocks(nodes: &[RawNode], context: &Context<'_>) -> Vec<Block> {
    let mut blocks = Vec::new();

    for node in nodes {
        match node.kind.as_str() {
            "paragraph" => {
                if let Some(block) =
                    decode::<RawParagraph>(node).and_then(|p| paragraph(&p, context))
                {
                    blocks.push(block);
                }
            }
            "table" => {
                if let Some(table) = decode::<RawTable>(node) {
                    blocks.push(Block::Table {
                        columns: table.grid.len(),
                        rows: table_rows(&table, context),
                    });
                }
            }
            _ => {}
        }
    }

    blocks
}

fn paragraph(paragraph: &RawParagraph, context: &Context<'_>) -> Option<Block> {
    let runs = paragraph_runs(&paragraph.children, context);

    if runs.is_empty() {
        // An empty paragraph is Word's vertical space; carrying it into Typst would be a paragraph
        // of nothing, so it is dropped instead.
        return None;
    }

    Some(Block::Paragraph {
        heading: heading_level(paragraph, context),
        list: list_info(paragraph, context),
        runs,
    })
}

fn paragraph_runs(nodes: &[RawNode], context: &Context<'_>) -> Vec<Run> {
    let mut runs = Vec::new();
    collect_runs(nodes, context, &mut runs);
    runs
}

/// Runs live in a paragraph, in a hyperlink, and in a tracked insertion; all three are walked the
/// same way, which is why a hyperlink's text survives and its target does not.
fn collect_runs(nodes: &[RawNode], context: &Context<'_>, runs: &mut Vec<Run>) {
    for node in nodes {
        match node.kind.as_str() {
            "run" => {
                if let Some(run) = decode::<RawRun>(node) {
                    run_children(&run, context, runs);
                }
            }
            "hyperlink" | "insert" => {
                if let Some(container) = decode::<RawContainer>(node) {
                    collect_runs(&container.children, context, runs);
                }
            }
            _ => {}
        }
    }
}

fn run_children(run: &RawRun, context: &Context<'_>, runs: &mut Vec<Run>) {
    for child in &run.children {
        match child.kind.as_str() {
            "text" => {
                if let Some(text) = decode::<RawText>(child)
                    && !text.text.is_empty()
                {
                    runs.push(text_run(&text.text, &run.run_property));
                }
            }
            "drawing" => {
                if let Some(drawing) = decode::<RawDrawing>(child)
                    && drawing.kind == "pic"
                    && let Ok(picture) = serde_json::from_value::<RawPicture>(drawing.data)
                    && let Some(handle) = context.media_by_rel.get(&picture.id)
                    && let Some(part) = context.media.get(*handle as usize)
                {
                    runs.push(Run::Image {
                        name: part.name.clone(),
                        mime_type: part.mime_type.clone(),
                        bytes_handle: *handle,
                    });
                }
            }
            "break" | "carriageReturn" => runs.push(Run::Break),
            // A tab is a layout instruction, not text; Typst would have to reproduce Word's tab
            // stops to honour it, so it is dropped rather than guessed at.
            _ => {}
        }
    }
}

fn text_run(text: &str, property: &RawRunProperty) -> Run {
    Run::Text {
        text: text.to_owned(),
        bold: property.bold.unwrap_or(false),
        italic: property.italic.unwrap_or(false),
        underline: property.underline.as_ref().and_then(string_value),
        strike: property.strike.unwrap_or(false) || property.dstrike.unwrap_or(false),
        script: property.vert_align.as_ref().and_then(string_value),
        color: property.color.as_ref().and_then(string_value),
        size_pt: property.sz.map(|half_points| half_points / 2.0),
    }
}

/// A property that serialises either as a bare string or as an object with a `val` field.
fn string_value(value: &Value) -> Option<String> {
    match value {
        Value::String(text) => Some(text.clone()),
        Value::Object(map) => map.get("val").and_then(Value::as_str).map(str::to_owned),
        _ => None,
    }
}

fn heading_level(paragraph: &RawParagraph, context: &Context<'_>) -> Option<u8> {
    if let Some(style_id) = &paragraph.property.style {
        if let Some(level) = heading_in_text(style_id) {
            return Some(level);
        }

        if let Some(hint) = context.styles.get(style_id) {
            if let Some(level) = heading_in_text(&hint.name) {
                return Some(level);
            }

            if let Some(outline) = hint.outline {
                return Some(clamp_level(outline as u16 + 1));
            }
        }
    }

    paragraph
        .property
        .outline_lvl
        .map(|level| clamp_level(level as u16 + 1))
}

/// Reads `Heading1`, `heading 2`, `Heading 3` — the localised style id and the style name Word
/// writes both reduce to the digits that follow the word.
fn heading_in_text(text: &str) -> Option<u8> {
    let lower = text.trim().to_lowercase();
    let rest = lower.strip_prefix("heading")?.trim_start();

    if lower.len() == rest.len() {
        return None;
    }

    if rest.is_empty() {
        return None;
    }

    rest.parse::<u16>().ok().map(clamp_level)
}

fn clamp_level(level: u16) -> u8 {
    level.clamp(1, 6) as u8
}

fn list_info(paragraph: &RawParagraph, context: &Context<'_>) -> Option<ListInfo> {
    let numbering = paragraph.property.numbering_property.as_ref()?;

    // `numId` 0 is Word's way of saying "this paragraph is not in a list".
    if numbering.id == 0 {
        return None;
    }

    let ordered = context
        .numbering
        .get(&numbering.id)
        .and_then(|levels| levels.get(numbering.level as usize))
        .copied()
        .unwrap_or(false);

    Some(ListInfo {
        numbering_id: numbering.id,
        level: numbering.level,
        ordered,
    })
}

// ---------------------------------------------------------------------------
// Tables
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum VerticalMerge {
    None,
    Restart,
    Continue,
}

struct DraftCell {
    colspan: u32,
    rowspan: u32,
    merged_away: bool,
    merge: VerticalMerge,
    blocks: Vec<Block>,
}

fn table_rows(table: &RawTable, context: &Context<'_>) -> Vec<TableRow> {
    let mut draft: Vec<Vec<DraftCell>> = Vec::new();

    for row in &table.rows {
        if row.kind != "tableRow" {
            continue;
        }

        let Some(raw_row) = decode::<RawTableRow>(row) else {
            continue;
        };

        let mut cells = Vec::new();
        for cell in &raw_row.cells {
            if cell.kind != "tableCell" {
                continue;
            }

            let Some(raw_cell) = decode::<RawTableCell>(cell) else {
                continue;
            };

            cells.push(DraftCell {
                colspan: raw_cell.property.grid_span.unwrap_or(1).max(1),
                rowspan: 1,
                merged_away: false,
                merge: vertical_merge(raw_cell.property.vertical_merge.as_ref()),
                blocks: convert_blocks(&raw_cell.children, context),
            });
        }

        draft.push(cells);
    }

    resolve_row_spans(&mut draft);

    draft
        .into_iter()
        .map(|cells| TableRow {
            cells: cells
                .into_iter()
                .filter(|cell| !cell.merged_away)
                .map(|cell| TableCell {
                    colspan: cell.colspan,
                    rowspan: cell.rowspan,
                    blocks: cell.blocks,
                })
                .collect(),
        })
        .collect()
}

fn vertical_merge(value: Option<&Value>) -> VerticalMerge {
    match value {
        None | Some(Value::Null) => VerticalMerge::None,
        // `<w:vMerge/>` with no value means "continue the merge above"; only `restart` opens one.
        Some(Value::Bool(false)) => VerticalMerge::None,
        Some(Value::String(text)) if text == "restart" => VerticalMerge::Restart,
        Some(_) => VerticalMerge::Continue,
    }
}

/// Word writes a vertically merged cell as a cell that restarts the merge and, in each row below, a
/// cell that continues it. Typst wants one cell with `rowspan`, so the continuations are counted and
/// then dropped.
fn resolve_row_spans(rows: &mut [Vec<DraftCell>]) {
    let columns: Vec<Vec<u32>> = rows
        .iter()
        .map(|cells| {
            let mut column = 0;
            cells
                .iter()
                .map(|cell| {
                    let start = column;
                    column += cell.colspan;
                    start
                })
                .collect()
        })
        .collect();

    for row in 0..rows.len() {
        for index in 0..rows[row].len() {
            if rows[row][index].merge != VerticalMerge::Restart {
                continue;
            }

            let start = columns[row][index];

            for below in (row + 1)..rows.len() {
                let found = columns[below].iter().position(|column| *column == start);

                match found {
                    Some(position) if rows[below][position].merge == VerticalMerge::Continue => {
                        rows[row][index].rowspan += 1;
                        rows[below][position].merged_away = true;
                    }
                    _ => break,
                }
            }
        }
    }
}

fn decode<T>(node: &RawNode) -> Option<T>
where
    T: for<'de> Deserialize<'de>,
{
    serde_json::from_value(node.data.clone()).ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_heading_style_id_yields_its_level() {
        assert_eq!(heading_in_text("Heading1"), Some(1));
        assert_eq!(heading_in_text("heading 3"), Some(3));
        assert_eq!(heading_in_text("Heading 9"), Some(6));
    }

    #[test]
    fn a_body_style_is_not_a_heading() {
        assert_eq!(heading_in_text("Normal"), None);
        assert_eq!(heading_in_text("heading"), None);
        assert_eq!(heading_in_text("Subtitle"), None);
    }

    #[test]
    fn a_twip_becomes_a_point() {
        assert_eq!(twips_to_points(11906), 595.3);
        assert_eq!(twips_to_points(1440), 72.0);
    }

    #[test]
    fn a_media_part_name_loses_its_backslashes() {
        assert_eq!(
            normalise_part_name("word\\media/image1.png"),
            "word/media/image1.png"
        );
    }

    #[test]
    fn a_png_is_named_by_its_extension() {
        assert_eq!(mime_type("word/media/image1.PNG"), "image/png");
        assert_eq!(
            mime_type("word/media/movie.mp4"),
            "application/octet-stream"
        );
    }
}
