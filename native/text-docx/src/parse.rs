//! Turns a `.docx` package into [`DocumentAst`], by reading OOXML directly.
//!
//! The container is `zip` (see `package.rs`), the XML is `roxmltree`, and everything between the two
//! — headings, list formats, span arithmetic, the media table — is decided here, so the managed
//! package receives a tree it does not have to interpret again.
//!
//! The parts read are exactly the ones the AST needs: `word/document.xml` for the body,
//! `word/_rels/document.xml.rels` for the relationships that name the media, `word/styles.xml` for
//! the outline levels a heading style may carry, `word/numbering.xml` for whether a level counts,
//! and the media parts themselves, byte for byte.
//!
//! Everything here reads hostile input. The three bounds are in one place, at the top: a depth cap,
//! a cap on the runs and cells a document may hold, and the per-part byte cap `package.rs` enforces.
//! Nothing derived from the file is unwrapped.

use std::collections::HashMap;

use roxmltree::Node;

use crate::ast::{Block, DocumentAst, ImageRef, ListInfo, PageSetup, Run, TableCell, TableRow};
use crate::package::{self, Package, Relationship};

/// The ceiling on a document this reader will open. A range request gone wrong should fail with a
/// clear error, not by exhausting the host's memory.
pub const DEFAULT_MAX_BYTES: u64 = 64 * 1024 * 1024;

/// How deeply blocks may nest. A cell can hold a table; nothing legitimate needs sixty levels of it,
/// and a document that does is a nesting bomb looking for a stack.
pub const MAX_DEPTH: usize = 64;

/// How many runs one document may hold. A body of 200 000 runs is already larger than any document
/// a person wrote; past it the reader refuses rather than build an AST of gigabytes.
pub const MAX_RUNS: usize = 200_000;

/// How many table cells one document may hold.
pub const MAX_CELLS: usize = 50_000;

/// How many media parts one document may hold.
pub const MAX_MEDIA: usize = 2048;

/// How many styles or numbering levels a part may declare.
const MAX_DECLARATIONS: usize = 8192;

const CONTENT_TYPES: &str = "[Content_Types].xml";
const PACKAGE_RELS: &str = "_rels/.rels";
const DOCUMENT: &str = "word/document.xml";
const DOCUMENT_RELS: &str = "word/_rels/document.xml.rels";

/// The relationships namespace, and its strict twin, which is where `r:embed` and `r:id` live.
const RELATIONSHIPS: &str = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
const RELATIONSHIPS_STRICT: &str = "http://purl.oclc.org/ooxml/officeDocument/relationships";

/// Word's default page, in twips, which `docx-rs` used to fill in for a document that states none.
const DEFAULT_PAGE_WIDTH: i64 = 11906;
const DEFAULT_PAGE_HEIGHT: i64 = 16838;
const DEFAULT_MARGIN_TOP: i64 = 1985;
const DEFAULT_MARGIN_SIDE: i64 = 1701;
const DEFAULT_MARGIN_BOTTOM: i64 = 1701;

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
    /// Larger than the configured ceiling, or deeper than the reader will go.
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

    let mut package = Package::open(bytes)?;

    // The three parts every package carries. Naming the missing one is the difference between "this
    // was never a document" and "this was a document and the part is gone".
    for part in [CONTENT_TYPES, PACKAGE_RELS, DOCUMENT] {
        if !package.has(part) {
            return Err(ParseError::MissingPart(format!(
                "the package has no {part}"
            )));
        }
    }

    let relationships = if package.has(DOCUMENT_RELS) {
        package::read_relationships(&package.read(DOCUMENT_RELS)?)?
    } else {
        Vec::new()
    };

    let media = if options.images {
        read_media(&mut package, &relationships)?
    } else {
        MediaTable::default()
    };

    // `word/styles.xml` and `word/numbering.xml` are optional by specification: a document with no
    // styles still reads, and a heading whose style id already names its level needs neither.
    let styles = match related_part(&relationships, "/styles", "word/styles.xml") {
        Some(name) if package.has(&name) => read_styles(&package.read(&name)?)?,
        _ => HashMap::new(),
    };

    let numbering = match related_part(&relationships, "/numbering", "word/numbering.xml") {
        Some(name) if package.has(&name) => read_numbering(&package.read(&name)?)?,
        _ => HashMap::new(),
    };

    let document_xml = package.read(DOCUMENT)?;
    let document = parse_xml(&document_xml, DOCUMENT)?;
    let root = document.root_element();
    let body = if package::is_w(&root, "body") {
        root
    } else {
        package::child(root, "body")
            .ok_or_else(|| ParseError::MalformedPackage(format!("{DOCUMENT} has no w:body")))?
    };

    let context = Context {
        styles: &styles,
        numbering: &numbering,
        media: &media.parts,
        media_by_rel: &media.by_rel,
    };

    let mut budget = Budget::default();
    let blocks = read_blocks(body, &context, 0, &mut budget)?;
    let page = page_setup(body);

    let ast = DocumentAst {
        blocks,
        page,
        images: media
            .parts
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

    Ok(Extracted {
        ast,
        media: media.parts,
    })
}

// ---------------------------------------------------------------------------
// Media
// ---------------------------------------------------------------------------

/// The media table and the relationship ids that name it.
#[derive(Default)]
struct MediaTable {
    parts: Vec<Media>,
    by_rel: HashMap<String, u64>,
}

/// Reads every media part the document's relationships name, and maps each relationship id to the
/// index the AST and `td_document_image` use.
///
/// Two relationships to the same part share one handle: the AST names parts, not relationships, so a
/// picture reused twice must not become two assets on the Typst side. Every target goes through
/// `resolve_part`, which is what keeps a relationship from naming `../../etc/whatever` or an
/// absolute name: the rule is the EPUB writer's, and it is the reason the old dependency was
/// dangerous in the first place.
fn read_media(
    package: &mut Package<'_>,
    relationships: &[Relationship],
) -> Result<MediaTable, ParseError> {
    let mut table = MediaTable::default();
    let mut by_name: HashMap<String, u64> = HashMap::new();

    for relationship in relationships {
        if relationship.external || !relationship.is("/image") {
            continue;
        }

        let name = package::resolve_part("word", &relationship.target, &["word", "media"])?;

        let handle = match by_name.get(&name) {
            Some(handle) => *handle,
            None => {
                if table.parts.len() >= MAX_MEDIA {
                    return Err(ParseError::Limit(format!(
                        "the document holds more than {MAX_MEDIA} media parts"
                    )));
                }

                let bytes = package.read(&name)?;
                let handle = table.parts.len() as u64;

                table.parts.push(Media {
                    mime_type: mime_type(&name).to_owned(),
                    name: name.clone(),
                    bytes,
                });
                by_name.insert(name, handle);
                handle
            }
        };

        table.by_rel.insert(relationship.id.clone(), handle);
    }

    Ok(table)
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

/// The part a relationship of this kind names, resolved against `word/`, or the conventional name.
fn related_part(
    relationships: &[Relationship],
    kind: &str,
    fallback: &'static str,
) -> Option<String> {
    for relationship in relationships {
        if relationship.external || !relationship.is(kind) {
            continue;
        }

        if let Ok(name) = package::resolve_part("word", &relationship.target, &["word"]) {
            return Some(name);
        }
    }

    Some(fallback.to_owned())
}

// ---------------------------------------------------------------------------
// Styles and numbering
// ---------------------------------------------------------------------------

/// What a style says about headings: its outline level, and its name.
#[derive(Debug, Clone)]
struct StyleHint {
    outline: Option<u8>,
    name: String,
}

fn read_styles(xml: &[u8]) -> Result<HashMap<String, StyleHint>, ParseError> {
    let document = parse_xml(xml, "word/styles.xml")?;
    let mut styles = HashMap::new();

    for style in document
        .descendants()
        .filter(|node| package::is_w(node, "style"))
    {
        let Some(id) = package::w_attr(style, "styleId") else {
            continue;
        };

        if id.is_empty() {
            continue;
        }

        if styles.len() >= MAX_DECLARATIONS {
            break;
        }

        let name = package::child(style, "name")
            .and_then(|node| package::w_attr(node, "val"))
            .unwrap_or_default();

        styles.insert(
            id.to_owned(),
            StyleHint {
                outline: package::path(style, &["pPr", "outlineLvl"])
                    .and_then(|node| integer(node))
                    .and_then(|level| u8::try_from(level).ok()),
                name: name.to_owned(),
            },
        );
    }

    Ok(styles)
}

/// For each `numId`, whether each of its levels counts. A bullet is not ordered.
fn read_numbering(xml: &[u8]) -> Result<HashMap<i64, Vec<bool>>, ParseError> {
    let document = parse_xml(xml, "word/numbering.xml")?;

    let mut by_abstract: HashMap<i64, Vec<bool>> = HashMap::new();

    for abstract_numbering in document
        .descendants()
        .filter(|node| package::is_w(node, "abstractNum"))
    {
        let Some(id) = package::w_attr(abstract_numbering, "abstractNumId")
            .and_then(|value| value.trim().parse::<i64>().ok())
        else {
            continue;
        };

        if by_abstract.len() >= MAX_DECLARATIONS {
            break;
        }

        let mut entries: Vec<(usize, bool)> = Vec::new();

        for level in package::children(abstract_numbering, "lvl") {
            let index = package::w_attr(level, "ilvl")
                .and_then(|value| value.trim().parse::<usize>().ok())
                .unwrap_or(entries.len());

            let format = package::child(level, "numFmt")
                .and_then(|node| package::w_attr(node, "val"))
                .unwrap_or_default();

            let ordered = !matches!(format.trim(), "bullet" | "none");
            entries.push((index, ordered));

            if entries.len() >= MAX_DECLARATIONS {
                break;
            }
        }

        let width = entries
            .iter()
            .map(|(index, _)| index.saturating_add(1))
            .max()
            .unwrap_or(0);
        let mut levels = vec![false; width];

        for (index, ordered) in entries {
            if let Some(slot) = levels.get_mut(index) {
                *slot = ordered;
            }
        }

        by_abstract.insert(id, levels);
    }

    let mut by_num_id: HashMap<i64, Vec<bool>> = HashMap::new();

    for numbering in document
        .descendants()
        .filter(|node| package::is_w(node, "num"))
    {
        let (Some(id), Some(abstract_id)) = (
            package::w_attr(numbering, "numId").and_then(|value| value.trim().parse::<i64>().ok()),
            package::child(numbering, "abstractNumId").and_then(|node| integer(node)),
        ) else {
            continue;
        };

        if by_num_id.len() >= MAX_DECLARATIONS {
            break;
        }

        if let Some(levels) = by_abstract.get(&abstract_id) {
            by_num_id.insert(id, levels.clone());
        }
    }

    Ok(by_num_id)
}

// ---------------------------------------------------------------------------
// Page setup
// ---------------------------------------------------------------------------

fn page_setup(body: Node<'_, '_>) -> Option<PageSetup> {
    // Word writes the section a body ends with as the last `w:sectPr` directly under `w:body`.
    let section = package::children(body, "sectPr").last()?;

    let size = package::child(section, "pgSz");
    let margin = package::child(section, "pgMar");

    // `w:pgSz` states `w:w`/`w:h` and `w:pgMar` its edges by name, unlike the elements that carry
    // their value in `w:val`; these are read by attribute, not through `integer`.
    let width = size
        .and_then(|node| twips(node, "w"))
        .filter(|twips| *twips > 0)
        .unwrap_or(DEFAULT_PAGE_WIDTH);
    let height = size
        .and_then(|node| twips(node, "h"))
        .filter(|twips| *twips > 0)
        .unwrap_or(DEFAULT_PAGE_HEIGHT);

    let top = margin
        .and_then(|node| twips(node, "top"))
        .unwrap_or(DEFAULT_MARGIN_TOP);
    let right = margin
        .and_then(|node| twips(node, "right"))
        .unwrap_or(DEFAULT_MARGIN_SIDE);
    let bottom = margin
        .and_then(|node| twips(node, "bottom"))
        .unwrap_or(DEFAULT_MARGIN_BOTTOM);
    let left = margin
        .and_then(|node| twips(node, "left"))
        .unwrap_or(DEFAULT_MARGIN_SIDE);

    // Word fills its defaults whether the file states them or not, so a section that equals the
    // defaults is reported as "no page setup stated" instead of asserting what it cannot know.
    let is_default = width == DEFAULT_PAGE_WIDTH
        && height == DEFAULT_PAGE_HEIGHT
        && top == DEFAULT_MARGIN_TOP
        && right == DEFAULT_MARGIN_SIDE
        && bottom == DEFAULT_MARGIN_BOTTOM
        && left == DEFAULT_MARGIN_SIDE;

    if is_default {
        return None;
    }

    Some(PageSetup {
        width_pt: twips_to_points(width),
        height_pt: twips_to_points(height),
        landscape: size
            .and_then(|node| package::w_attr(node, "orient"))
            .map(|orient| orient.eq_ignore_ascii_case("landscape"))
            .unwrap_or(false),
        margin_top_pt: twips_to_points(top),
        margin_right_pt: twips_to_points(right),
        margin_bottom_pt: twips_to_points(bottom),
        margin_left_pt: twips_to_points(left),
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

/// The counts one document has spent so far.
#[derive(Default)]
struct Budget {
    runs: usize,
    cells: usize,
}

impl Budget {
    fn run(&mut self) -> Result<(), ParseError> {
        self.runs = self.runs.saturating_add(1);

        if self.runs > MAX_RUNS {
            return Err(ParseError::Limit(format!(
                "the document holds more than {MAX_RUNS} runs"
            )));
        }

        Ok(())
    }

    fn cell(&mut self) -> Result<(), ParseError> {
        self.cells = self.cells.saturating_add(1);

        if self.cells > MAX_CELLS {
            return Err(ParseError::Limit(format!(
                "the document holds more than {MAX_CELLS} table cells"
            )));
        }

        Ok(())
    }
}

fn too_deep() -> ParseError {
    ParseError::Limit(format!(
        "the document nests elements deeper than {MAX_DEPTH} levels"
    ))
}

fn read_blocks(
    node: Node<'_, '_>,
    context: &Context<'_>,
    depth: usize,
    budget: &mut Budget,
) -> Result<Vec<Block>, ParseError> {
    if depth > MAX_DEPTH {
        return Err(too_deep());
    }

    let mut blocks = Vec::new();

    for child in node.children().filter(Node::is_element) {
        if package::is_w(&child, "p") {
            if let Some(block) = read_paragraph(child, context, depth + 1, budget)? {
                blocks.push(block);
            }
        } else if package::is_w(&child, "tbl") {
            blocks.push(read_table(child, context, depth + 1, budget)?);
        } else if package::is_w(&child, "sdt")
            && let Some(content) = package::child(child, "sdtContent")
        {
            // A content control is a wrapper, not content: Word puts its body inside
            // `w:sdtContent`, and a document built from one otherwise reads as a body of nothing.
            blocks.extend(read_blocks(content, context, depth + 1, budget)?);
        }
    }

    Ok(blocks)
}

/// The paragraph properties this reader acts on.
#[derive(Debug, Default)]
struct ParagraphProperty {
    /// `w:pStyle`'s value.
    style: Option<String>,
    /// `w:numPr`'s `numId` and `ilvl`.
    numbering: Option<(i64, u8)>,
    /// `w:outlineLvl`, zero-based as Word writes it.
    outline: Option<u8>,
}

impl ParagraphProperty {
    fn read(paragraph: Node<'_, '_>) -> Self {
        let Some(property) = package::child(paragraph, "pPr") else {
            return Self::default();
        };

        let style = package::child(property, "pStyle")
            .and_then(|node| package::w_attr(node, "val"))
            .map(str::to_owned);

        let outline = package::child(property, "outlineLvl")
            .and_then(|node| integer(node))
            .and_then(|level| u8::try_from(level).ok());

        let numbering = package::child(property, "numPr").and_then(|node| {
            let id = package::child(node, "numId").and_then(|value| integer(value))?;
            let level = package::child(node, "ilvl")
                .and_then(|value| integer(value))
                .and_then(|level| u8::try_from(level).ok())
                .unwrap_or(0);

            Some((id, level))
        });

        Self {
            style,
            numbering,
            outline,
        }
    }
}

fn read_paragraph(
    paragraph: Node<'_, '_>,
    context: &Context<'_>,
    depth: usize,
    budget: &mut Budget,
) -> Result<Option<Block>, ParseError> {
    if depth > MAX_DEPTH {
        return Err(too_deep());
    }

    let property = ParagraphProperty::read(paragraph);

    let mut runs = Vec::new();
    collect_runs(paragraph, context, depth + 1, budget, &mut runs)?;

    if runs.is_empty() {
        // An empty paragraph is Word's vertical space; carrying it into Typst would be a paragraph
        // of nothing, so it is dropped instead.
        return Ok(None);
    }

    Ok(Some(Block::Paragraph {
        heading: heading_level(&property, context),
        list: list_info(&property, context),
        runs,
    }))
}

/// Runs live in a paragraph, in a hyperlink, and in a tracked insertion; all three are walked the
/// same way, which is why a hyperlink's text survives and its target does not.
fn collect_runs(
    node: Node<'_, '_>,
    context: &Context<'_>,
    depth: usize,
    budget: &mut Budget,
    runs: &mut Vec<Run>,
) -> Result<(), ParseError> {
    if depth > MAX_DEPTH {
        return Err(too_deep());
    }

    for child in node.children().filter(Node::is_element) {
        if package::is_w(&child, "r") {
            read_run(child, context, depth + 1, budget, runs)?;
        } else if package::is_w(&child, "hyperlink")
            || package::is_w(&child, "ins")
            || package::is_w(&child, "smartTag")
        {
            collect_runs(child, context, depth + 1, budget, runs)?;
        }
    }

    Ok(())
}

/// The run properties this reader acts on.
#[derive(Debug, Default)]
struct RunProperty {
    bold: bool,
    italic: bool,
    strike: bool,
    underline: Option<String>,
    script: Option<String>,
    color: Option<String>,
    size_pt: Option<f64>,
}

impl RunProperty {
    fn read(run: Node<'_, '_>) -> Self {
        let Some(property) = package::child(run, "rPr") else {
            return Self::default();
        };

        let value = |name: &str| {
            package::child(property, name)
                .and_then(|node| package::w_attr(node, "val"))
                .map(str::to_owned)
        };

        Self {
            bold: flag(package::child(property, "b")),
            italic: flag(package::child(property, "i")),
            strike: flag(package::child(property, "strike"))
                || flag(package::child(property, "dstrike")),
            underline: value("u"),
            script: value("vertAlign"),
            // `auto` is Word's "the theme decides", which this reader cannot resolve; the field is
            // absent rather than asserting a colour the document did not state.
            color: value("color").filter(|color| !color.eq_ignore_ascii_case("auto")),
            size_pt: package::child(property, "sz")
                .and_then(|node| integer(node))
                .map(|half_points| half_points as f64 / 2.0),
        }
    }
}

/// Whether a toggle element is on. `<w:b/>` is on; `<w:b w:val="0"/>` is off.
fn flag(node: Option<Node<'_, '_>>) -> bool {
    let Some(node) = node else {
        return false;
    };

    match package::w_attr(node, "val") {
        None => true,
        Some(value) => !matches!(value.trim(), "0" | "false" | "off"),
    }
}

fn read_run(
    run: Node<'_, '_>,
    context: &Context<'_>,
    depth: usize,
    budget: &mut Budget,
    runs: &mut Vec<Run>,
) -> Result<(), ParseError> {
    if depth > MAX_DEPTH {
        return Err(too_deep());
    }

    let property = RunProperty::read(run);

    for child in run.children().filter(Node::is_element) {
        if package::is_w(&child, "t") {
            let text = text_of(child);

            if !text.is_empty() {
                budget.run()?;
                runs.push(Run::Text {
                    text,
                    bold: property.bold,
                    italic: property.italic,
                    underline: property.underline.clone(),
                    strike: property.strike,
                    script: property.script.clone(),
                    color: property.color.clone(),
                    size_pt: property.size_pt,
                });
            }
        } else if package::is_w(&child, "drawing") || package::is_w(&child, "pict") {
            if let Some(image) = image_run(child, context) {
                budget.run()?;
                runs.push(image);
            }
        } else if package::is_w(&child, "br") || package::is_w(&child, "cr") {
            budget.run()?;
            runs.push(Run::Break);
        }
        // A tab is a layout instruction, not text; Typst would have to reproduce Word's tab stops
        // to honour it, so it is dropped rather than guessed at.
    }

    Ok(())
}

/// The characters of a text-bearing element, with `xml:space="preserve"` already honoured: XML
/// parsing keeps the whitespace, and nothing trims it afterwards.
fn text_of(node: Node<'_, '_>) -> String {
    let mut text = String::new();

    for child in node.children().filter(Node::is_text) {
        if let Some(part) = child.text() {
            text.push_str(part);
        }
    }

    text
}

/// An image run, when the drawing names a picture this document has a media part for.
///
/// Both shapes are read: DrawingML keeps the relationship on `a:blip/@r:embed`, and the older VML a
/// `.doc`-era document carries keeps it on `v:imagedata/@r:id`. Anything else — a chart, a shape, a
/// text box — is not a picture and is skipped.
fn image_run(node: Node<'_, '_>, context: &Context<'_>) -> Option<Run> {
    let relationship = node.descendants().find_map(|element| {
        if package::is_local(&element, "blip") {
            relationship_id(element, "embed")
        } else if package::is_local(&element, "imagedata") {
            relationship_id(element, "id")
        } else {
            None
        }
    })?;

    let handle = *context.media_by_rel.get(relationship)?;
    let part = context.media.get(handle as usize)?;

    Some(Run::Image {
        name: part.name.clone(),
        mime_type: part.mime_type.clone(),
        bytes_handle: handle,
    })
}

fn relationship_id<'a>(node: Node<'a, '_>, name: &str) -> Option<&'a str> {
    node.attribute((RELATIONSHIPS, name))
        .or_else(|| node.attribute((RELATIONSHIPS_STRICT, name)))
}

fn heading_level(property: &ParagraphProperty, context: &Context<'_>) -> Option<u8> {
    if let Some(style_id) = &property.style {
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

    property.outline.map(|level| clamp_level(level as u16 + 1))
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

fn list_info(property: &ParagraphProperty, context: &Context<'_>) -> Option<ListInfo> {
    let (id, level) = property.numbering?;

    // `numId` 0 is Word's way of saying "this paragraph is not in a list".
    if id == 0 {
        return None;
    }

    let ordered = context
        .numbering
        .get(&id)
        .and_then(|levels| levels.get(level as usize))
        .copied()
        .unwrap_or(false);

    Some(ListInfo {
        numbering_id: id,
        level,
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

fn read_table(
    table: Node<'_, '_>,
    context: &Context<'_>,
    depth: usize,
    budget: &mut Budget,
) -> Result<Block, ParseError> {
    if depth > MAX_DEPTH {
        return Err(too_deep());
    }

    let columns = package::child(table, "tblGrid")
        .map(|grid| package::children(grid, "gridCol").count())
        .unwrap_or(0);

    let mut draft: Vec<Vec<DraftCell>> = Vec::new();

    for row in package::children(table, "tr") {
        let mut cells = Vec::new();

        for cell in package::children(row, "tc") {
            budget.cell()?;

            let property = package::child(cell, "tcPr");

            let colspan = property
                .and_then(|node| package::child(node, "gridSpan"))
                .and_then(|node| integer(node))
                .and_then(|span| u32::try_from(span).ok())
                .unwrap_or(1)
                .max(1);

            let merge = property
                .and_then(|node| package::child(node, "vMerge"))
                .map(|merge| match package::w_attr(merge, "val") {
                    Some(value) if value.trim() == "restart" => VerticalMerge::Restart,
                    // A `w:vMerge` with no value, or `continue`, continues the merge above; only
                    // `restart` opens one.
                    _ => VerticalMerge::Continue,
                })
                .unwrap_or(VerticalMerge::None);

            cells.push(DraftCell {
                colspan,
                rowspan: 1,
                merged_away: false,
                merge,
                blocks: read_blocks(cell, context, depth + 1, budget)?,
            });
        }

        draft.push(cells);
    }

    resolve_row_spans(&mut draft);

    Ok(Block::Table {
        columns,
        rows: draft
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
            .collect(),
    })
}

/// Word writes a vertically merged cell as a cell that restarts the merge and, in each row below, a
/// cell that continues it. Typst wants one cell with `rowspan`, so the continuations are counted and
/// then dropped.
fn resolve_row_spans(rows: &mut [Vec<DraftCell>]) {
    let columns: Vec<Vec<u32>> = rows
        .iter()
        .map(|cells| {
            let mut column: u32 = 0;
            cells
                .iter()
                .map(|cell| {
                    let start = column;
                    column = column.saturating_add(cell.colspan);
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
                        rows[row][index].rowspan = rows[row][index].rowspan.saturating_add(1);
                        rows[below][position].merged_away = true;
                    }
                    _ => break,
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// XML plumbing
// ---------------------------------------------------------------------------

fn parse_xml<'a>(xml: &'a [u8], what: &str) -> Result<roxmltree::Document<'a>, ParseError> {
    let text = package::xml_text(xml)?;

    roxmltree::Document::parse(text)
        .map_err(|error| ParseError::MalformedPackage(format!("{what} is not valid XML: {error}")))
}

/// The integer value of a WordprocessingML element: `<w:sz w:val="28"/>` is 28.
fn integer(node: Node<'_, '_>) -> Option<i64> {
    package::w_attr(node, "val")?.trim().parse::<i64>().ok()
}

/// The integer value of a named attribute: `<w:pgSz w:w="11906"/>` is 11906.
fn twips(node: Node<'_, '_>, name: &str) -> Option<i64> {
    package::w_attr(node, name)?.trim().parse::<i64>().ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Cursor, Write};

    const WORD: &str = package::WORD;
    const REL: &str = RELATIONSHIPS;

    fn pack(parts: &[(&str, &[u8])]) -> Vec<u8> {
        let mut buffer = Cursor::new(Vec::new());

        {
            let mut writer = zip::ZipWriter::new(&mut buffer);
            let options = zip::write::SimpleFileOptions::default()
                .compression_method(zip::CompressionMethod::Deflated);

            for (name, bytes) in parts {
                writer.start_file(*name, options).unwrap();
                writer.write_all(bytes).unwrap();
            }

            writer.finish().unwrap();
        }

        buffer.into_inner()
    }

    fn document(body: &str) -> String {
        format!(
            r#"<?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="{WORD}" xmlns:r="{REL}"><w:body>{body}</w:body></w:document>"#
        )
    }

    fn relationships(entries: &str) -> String {
        format!(
            r#"<?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            {entries}</Relationships>"#
        )
    }

    fn package(document: &str, rels: Option<&str>, extra: &[(&str, &[u8])]) -> Vec<u8> {
        let document = document.to_owned();
        let rels = rels.map(str::to_owned);

        let mut parts: Vec<(&str, &[u8])> = vec![
            (CONTENT_TYPES, b"<Types/>"),
            (PACKAGE_RELS, b"<Relationships/>"),
        ];

        parts.push((DOCUMENT, document.as_bytes()));

        if let Some(rels) = &rels {
            parts.push((DOCUMENT_RELS, rels.as_bytes()));
        }

        parts.extend_from_slice(extra);
        pack(&parts)
    }

    fn extract_package(bytes: &[u8]) -> Result<Ast, ParseError> {
        extract(bytes, &Options::default()).map(|extracted| Ast {
            ast: extracted.ast,
            media: extracted.media,
        })
    }

    /// The part of `Extracted` a test looks at, without the module's own lifetimes.
    struct Ast {
        ast: DocumentAst,
        media: Vec<Media>,
    }

    #[test]
    fn a_heading_run_and_its_formatting_are_read() {
        let body = r#"
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Report</w:t></w:r></w:p>
            <w:p>
              <w:r><w:t xml:space="preserve">Revenue grew </w:t></w:r>
              <w:r><w:rPr><w:b/></w:rPr><w:t>twelve percent</w:t></w:r>
              <w:r><w:rPr><w:i/><w:u w:val="single"/><w:color w:val="C00000"/><w:sz w:val="28"/><w:vertAlign w:val="superscript"/><w:strike/></w:rPr><w:t>flat</w:t></w:r>
            </w:p>"#;

        let bytes = package(&document(body), None, &[]);
        let parsed = extract_package(&bytes).unwrap();

        assert_eq!(parsed.ast.blocks.len(), 2);

        let Block::Paragraph { heading, runs, .. } = &parsed.ast.blocks[0] else {
            panic!("the first block is a paragraph");
        };
        assert_eq!(*heading, Some(1));
        assert!(matches!(&runs[0], Run::Text { text, .. } if text == "Report"));

        let Block::Paragraph { runs, .. } = &parsed.ast.blocks[1] else {
            panic!("the second block is a paragraph");
        };
        assert_eq!(runs.len(), 3);

        let Run::Text { text, bold, .. } = &runs[1] else {
            panic!("the second run is text");
        };
        assert_eq!(text, "twelve percent");
        assert!(*bold);

        let Run::Text {
            text,
            italic,
            underline,
            strike,
            script,
            color,
            size_pt,
            bold,
        } = &runs[2]
        else {
            panic!("the third run is text");
        };

        assert_eq!(text, "flat");
        assert!(!*bold);
        assert!(*italic);
        assert!(*strike);
        assert_eq!(underline.as_deref(), Some("single"));
        assert_eq!(script.as_deref(), Some("superscript"));
        assert_eq!(color.as_deref(), Some("C00000"));
        assert_eq!(*size_pt, Some(14.0));
    }

    #[test]
    fn a_style_supplies_the_heading_level_when_the_id_does_not() {
        let body =
            r#"<w:p><w:pPr><w:pStyle w:val="Custom"/></w:pPr><w:r><w:t>Report</w:t></w:r></w:p>"#;

        let styles = format!(
            r#"<w:styles xmlns:w="{WORD}"><w:style w:styleId="Custom"><w:name w:val="Custom"/><w:pPr><w:outlineLvl w:val="2"/></w:pPr></w:style></w:styles>"#
        );

        let rels = relationships(
            r#"<Relationship Id="rId1" Type="http://x/styles" Target="styles.xml"/>"#,
        );

        let bytes = package(
            &document(body),
            Some(&rels),
            &[("word/styles.xml", styles.as_bytes())],
        );

        let parsed = extract_package(&bytes).unwrap();

        let Block::Paragraph { heading, .. } = &parsed.ast.blocks[0] else {
            panic!("the block is a paragraph");
        };

        assert_eq!(*heading, Some(3));
    }

    #[test]
    fn a_bulleted_level_is_not_ordered() {
        let body = r#"<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>First finding</w:t></w:r></w:p>"#;

        let numbering = format!(
            r#"<w:numbering xmlns:w="{WORD}">
                 <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="bullet"/></w:lvl></w:abstractNum>
                 <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
               </w:numbering>"#
        );

        let rels = relationships(
            r#"<Relationship Id="rId1" Type="http://x/numbering" Target="numbering.xml"/>"#,
        );

        let bytes = package(
            &document(body),
            Some(&rels),
            &[("word/numbering.xml", numbering.as_bytes())],
        );

        let parsed = extract_package(&bytes).unwrap();

        let Block::Paragraph { list, .. } = &parsed.ast.blocks[0] else {
            panic!("the block is a paragraph");
        };

        let list = list.expect("the paragraph is in a list");
        assert_eq!(list.numbering_id, 1);
        assert_eq!(list.level, 0);
        assert!(!list.ordered);
    }

    #[test]
    fn a_table_keeps_its_grid_span_and_its_row_span() {
        let body = r#"
            <w:tbl>
              <w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>
              <w:tr>
                <w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>Merged</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Third</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:tcPr><w:vMerge w:val="restart"/></w:tcPr><w:p><w:r><w:t>Left</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Middle</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Right</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc>
                <w:tc><w:p><w:r><w:t>Middle below</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Right below</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>"#;

        let bytes = package(&document(body), None, &[]);
        let parsed = extract_package(&bytes).unwrap();

        let Block::Table { columns, rows } = &parsed.ast.blocks[0] else {
            panic!("the block is a table");
        };

        assert_eq!(*columns, 3);
        assert_eq!(rows.len(), 3);
        assert_eq!(rows[0].cells.len(), 2);
        assert_eq!(rows[0].cells[0].colspan, 2);
        assert_eq!(rows[1].cells[0].rowspan, 2);
        assert_eq!(rows[2].cells.len(), 2);
    }

    #[test]
    fn an_image_is_read_through_its_relationship() {
        let body = r#"
            <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://x/wordprocessingDrawing">
              <a:graphic xmlns:a="http://x/drawingml"><a:graphicData>
                <a:blip r:embed="rId3"/>
              </a:graphicData></a:graphic>
            </wp:inline></w:drawing></w:r></w:p>"#;

        let rels = relationships(
            r#"<Relationship Id="rId3" Type="http://x/image" Target="media/image1.png"/>"#,
        );

        let png = [0x89u8, 0x50, 0x4E, 0x47];

        let bytes = package(
            &document(body),
            Some(&rels),
            &[("word/media/image1.png", &png)],
        );

        let parsed = extract_package(&bytes).unwrap();

        assert_eq!(parsed.media.len(), 1);
        assert_eq!(parsed.media[0].name, "word/media/image1.png");
        assert_eq!(parsed.media[0].mime_type, "image/png");
        assert_eq!(parsed.ast.images.len(), 1);
        assert_eq!(parsed.ast.images[0].length, 4);

        let Block::Paragraph { runs, .. } = &parsed.ast.blocks[0] else {
            panic!("the block is a paragraph");
        };
        let Run::Image {
            name, bytes_handle, ..
        } = &runs[0]
        else {
            panic!("the run is an image");
        };
        assert_eq!(name, "word/media/image1.png");
        assert_eq!(*bytes_handle, 0);
    }

    #[test]
    fn an_image_relationship_that_leaves_the_media_directory_is_refused() {
        for target in [
            "../../word/document.xml",
            "/word/media/image1.png",
            "C:/word/media/image1.png",
            "../document.xml",
        ] {
            let body = r#"<w:p><w:r><w:t>text</w:t></w:r></w:p>"#;
            let rels = relationships(&format!(
                r#"<Relationship Id="rId1" Type="http://x/image" Target="{target}"/>"#
            ));

            let bytes = package(&document(body), Some(&rels), &[]);

            assert!(extract_package(&bytes).is_err(), "{target} must be refused");
        }
    }

    #[test]
    fn turning_images_off_reads_no_media_at_all() {
        let body = r#"<w:p><w:r><w:t>text</w:t></w:r></w:p>"#;
        let rels = relationships(
            r#"<Relationship Id="rId1" Type="http://x/image" Target="media/image1.png"/>"#,
        );

        let bytes = package(
            &document(body),
            Some(&rels),
            &[("word/media/image1.png", &[0x89, 0x50])],
        );

        let extracted = extract(
            &bytes,
            &Options {
                images: false,
                ..Options::default()
            },
        )
        .unwrap();

        assert!(extracted.ast.images.is_empty());
        assert!(extracted.media.is_empty());
    }

    #[test]
    fn the_page_setup_comes_from_the_body_section() {
        let body = r#"
            <w:p><w:r><w:t>text</w:t></w:r></w:p>
            <w:sectPr>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1701" w:bottom="1440" w:left="1701"/>
            </w:sectPr>"#;

        let bytes = package(&document(body), None, &[]);
        let page = extract_package(&bytes).unwrap().ast.page.expect("a page");

        assert_eq!(page.width_pt, 595.3);
        assert_eq!(page.height_pt, 841.9);
        assert_eq!(page.margin_top_pt, 72.0);
        assert_eq!(page.margin_left_pt, 85.05);
        assert!(!page.landscape);
    }

    #[test]
    fn a_section_that_states_word_defaults_is_no_page_setup() {
        let body = r#"
            <w:p><w:r><w:t>text</w:t></w:r></w:p>
            <w:sectPr>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1985" w:right="1701" w:bottom="1701" w:left="1701"/>
            </w:sectPr>"#;

        let bytes = package(&document(body), None, &[]);

        assert!(extract_package(&bytes).unwrap().ast.page.is_none());
    }

    #[test]
    fn bytes_that_are_not_a_package_are_malformed() {
        let error = match extract(b"this is not a zip package", &Options::default()) {
            Ok(_) => panic!("plain bytes must not parse"),
            Err(error) => error,
        };

        assert_eq!(error.code(), crate::ffi::TD_ERR_MALFORMED_PACKAGE);
    }

    #[test]
    fn a_package_without_a_document_is_a_missing_part() {
        let bytes = pack(&[
            (CONTENT_TYPES, b"<Types/>"),
            (PACKAGE_RELS, b"<Relationships/>"),
        ]);

        let error = match extract(&bytes, &Options::default()) {
            Ok(_) => panic!("a package with no document must not parse"),
            Err(error) => error,
        };

        assert_eq!(error.code(), crate::ffi::TD_ERR_MISSING_PART);
        assert!(error.message().contains("word/document.xml"));
    }

    #[test]
    fn a_document_over_the_limit_is_refused_before_it_is_opened() {
        let bytes = package(&document(""), None, &[]);

        let error = match extract(
            &bytes,
            &Options {
                max_bytes: 16,
                ..Options::default()
            },
        ) {
            Ok(_) => panic!("a document over the ceiling must not parse"),
            Err(error) => error,
        };

        assert_eq!(error.code(), crate::ffi::TD_ERR_LIMIT);
    }

    #[test]
    fn a_part_that_decodes_larger_than_its_cap_is_refused() {
        let bytes = pack(&[("word/document.xml", &[b'a'; 64])]);

        let mut package = Package::open(&bytes).unwrap();

        let error = match package.read_capped("word/document.xml", 8) {
            Ok(_) => panic!("a part over the cap must not be read"),
            Err(error) => error,
        };

        assert_eq!(error.code(), crate::ffi::TD_ERR_LIMIT);
    }

    #[test]
    fn a_document_nested_past_the_depth_cap_is_refused() {
        let mut body = String::new();

        for _ in 0..MAX_DEPTH + 2 {
            body.push_str("<w:tbl><w:tr><w:tc>");
        }

        body.push_str("<w:p><w:r><w:t>deep</w:t></w:r></w:p>");

        for _ in 0..MAX_DEPTH + 2 {
            body.push_str("</w:tc></w:tr></w:tbl>");
        }

        let bytes = package(&document(&body), None, &[]);

        let error = match extract(&bytes, &Options::default()) {
            Ok(_) => panic!("a nesting bomb must not parse"),
            Err(error) => error,
        };

        assert_eq!(error.code(), crate::ffi::TD_ERR_LIMIT);
    }

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
    fn a_png_is_named_by_its_extension() {
        assert_eq!(mime_type("word/media/image1.PNG"), "image/png");
        assert_eq!(
            mime_type("word/media/movie.mp4"),
            "application/octet-stream"
        );
    }
}
