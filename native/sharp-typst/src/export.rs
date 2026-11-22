//! Export paths: PDF, SVG, PNG and HTML.
//!
//! Markdown and plain text are derived from the HTML document and arrive in v0.3.

use std::num::NonZeroUsize;

use serde::Deserialize;
use typst::foundations::{Datetime, Smart};
use typst::layout::{Abs, PageRanges};
use typst::utils::Scalar;
use typst_html::{HtmlDocument, HtmlOptions};
use typst_layout::{Page, PagedDocument};
use typst_pdf::{PdfOptions, PdfStandard, PdfStandards, Timestamp};
use typst_render::RenderOptions;
use typst_svg::SvgOptions;

use crate::Document;
use crate::diag;
use crate::ffi::{TN_ERR_EXPORT, TnError};
use crate::semantic;

/// Gap between pages in a merged SVG or PNG, in points.
const MERGED_GAP_PT: f64 = 10.0;

/// Default raster resolution, in pixels per inch.
const DEFAULT_PPI: f64 = 144.0;

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct ExportOptions {
    page: Option<usize>,
    ppi: Option<f64>,
    pretty: Option<bool>,
    pdf: Option<PdfOptionsDto>,
}

/// PDF-specific settings. Every field is optional; the defaults match `PdfOptions::default()`.
#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct PdfOptionsDto {
    /// Standards to enforce, e.g. `["a-2b"]` or `["ua-1"]`.
    standards: Option<Vec<String>>,
    /// One-indexed page ranges, e.g. `"1-3,5"` or `"2-"`.
    page_ranges: Option<String>,
    tagged: Option<bool>,
    ident: Option<String>,
    creator: Option<String>,
    pretty: Option<bool>,
    /// Creation timestamp, required by PDF/A when the document does not set its own date.
    timestamp: Option<TimestampDto>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct TimestampDto {
    year: i32,
    month: u8,
    day: u8,
    hour: u8,
    minute: u8,
    second: u8,
}

pub fn export(document: &Document, format: &str, options_json: &str) -> Result<Vec<u8>, TnError> {
    let options = parse_options(options_json)?;

    match format {
        "pdf" => paged(document, |doc| pdf(doc, &options)),
        // The fonts the PDF carries, read back from the bytes this format encodes. It exists
        // because `typst-pdf` 0.15.1 exposes no subsetting switch: the measurement is what a
        // caller can actually ask for.
        "pdf-fonts" => paged(document, |doc| pdf_fonts(doc, &options)),
        "svg" => paged(document, |doc| svg_page(doc, &options)),
        "svg-merged" => paged(document, |doc| Ok(svg_merged(doc, &options).into_bytes())),
        "png" => paged(document, |doc| png_page(doc, &options)),
        "png-merged" => paged(document, |doc| png_merged(doc, &options)),
        "html" => html(document, &options),
        "markdown" => semantic(document, semantic::markdown),
        "text" => semantic(document, semantic::text),
        "bundle" => bundle_manifest(document),
        other => Err(TnError::invalid_argument(format!(
            "unknown export format `{other}`"
        ))),
    }
}

/// The bundle manifest: every file path and size, in bundle order. The bytes are fetched one by
/// one through [`bundle_asset`], so a large file never crosses the ABI twice.
fn bundle_manifest(document: &Document) -> Result<Vec<u8>, TnError> {
    let Document::Bundle { files } = document else {
        return Err(TnError::invalid_argument(
            "the bundle manifest requires a bundle document; compile with document = \"bundle\"",
        ));
    };

    Ok(crate::bundle::manifest(files))
}

/// The bytes of one bundle file, by index into the manifest.
pub fn bundle_asset(document: &Document, index: usize) -> Result<Vec<u8>, TnError> {
    let Document::Bundle { files } = document else {
        return Err(TnError::invalid_argument(
            "asset data requires a bundle document; compile with document = \"bundle\"",
        ));
    };

    crate::bundle::file(files, index)
        .map(<[u8]>::to_vec)
        .ok_or_else(|| {
            TnError::invalid_argument(format!(
                "bundle file {index} is out of range (0..{})",
                files.len()
            ))
        })
}

fn parse_options(json: &str) -> Result<ExportOptions, TnError> {
    if json.trim().is_empty() {
        return Ok(ExportOptions::default());
    }
    serde_json::from_str(json)
        .map_err(|e| TnError::invalid_argument(format!("invalid export options: {e}")))
}

fn paged(
    document: &Document,
    export: impl FnOnce(&PagedDocument) -> Result<Vec<u8>, TnError>,
) -> Result<Vec<u8>, TnError> {
    match document {
        Document::Paged(doc) => export(doc),
        Document::Html(_) | Document::Bundle { .. } => Err(TnError::invalid_argument(
            "this format requires a paged document; compile with document = \"paged\"",
        )),
    }
}

fn page_of<'a>(document: &'a PagedDocument, options: &ExportOptions) -> Result<&'a Page, TnError> {
    let index = options.page.unwrap_or(0);
    document.pages().get(index).ok_or_else(|| {
        TnError::invalid_argument(format!(
            "page {index} is out of range (0..{})",
            document.pages().len()
        ))
    })
}

fn pdf(document: &PagedDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    let pdf_options = build_pdf_options(options)?;

    typst_pdf::pdf(document, &pdf_options).map_err(|diagnostics| {
        TnError::with_json(TN_ERR_EXPORT, diag::to_json_empty(&diagnostics, "export"))
    })
}

/// The fonts the PDF would embed, as JSON. The document is encoded and read back, so this costs
/// one more export than asking for the bytes alone.
fn pdf_fonts(document: &PagedDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    let bytes = pdf(document, options)?;
    Ok(crate::pdf_fonts::inventory(&bytes).into_bytes())
}

fn build_pdf_options(options: &ExportOptions) -> Result<PdfOptions, TnError> {
    let Some(dto) = &options.pdf else {
        return Ok(PdfOptions::default());
    };

    let requested_names = dto.standards.as_deref().unwrap_or_default();
    let mut requested = Vec::with_capacity(requested_names.len());
    for name in requested_names {
        requested.push(parse_standard(name)?);
    }

    let standards = PdfStandards::new(&requested).map_err(|e| {
        TnError::invalid_argument(format!("invalid PDF standards: {}", e.message()))
    })?;

    let page_ranges = match &dto.page_ranges {
        Some(text) => Some(parse_page_ranges(text)?),
        None => None,
    };

    let timestamp = match &dto.timestamp {
        Some(t) => {
            let datetime =
                Datetime::from_ymd_hms(t.year, t.month, t.day, t.hour, t.minute, t.second)
                    .ok_or_else(|| {
                        TnError::invalid_argument(format!(
                            "invalid PDF timestamp {}-{:02}-{:02}T{:02}:{:02}:{:02}Z",
                            t.year, t.month, t.day, t.hour, t.minute, t.second
                        ))
                    })?;

            Some(Timestamp::new_utc(datetime))
        }
        None => None,
    };

    Ok(PdfOptions {
        ident: match &dto.ident {
            Some(ident) => Smart::Custom(ident.clone()),
            None => Smart::Auto,
        },
        creator: match &dto.creator {
            Some(creator) => Smart::Custom(Some(creator.clone())),
            None => Smart::Auto,
        },
        timestamp,
        page_ranges,
        standards,
        tagged: dto.tagged.unwrap_or(true),
        pretty: dto.pretty.unwrap_or(false),
    })
}

fn parse_standard(name: &str) -> Result<PdfStandard, TnError> {
    Ok(match name {
        "1.4" => PdfStandard::V_1_4,
        "1.5" => PdfStandard::V_1_5,
        "1.6" => PdfStandard::V_1_6,
        "1.7" => PdfStandard::V_1_7,
        "2.0" => PdfStandard::V_2_0,
        "a-1b" => PdfStandard::A_1b,
        "a-1a" => PdfStandard::A_1a,
        "a-2b" => PdfStandard::A_2b,
        "a-2u" => PdfStandard::A_2u,
        "a-2a" => PdfStandard::A_2a,
        "a-3b" => PdfStandard::A_3b,
        "a-3u" => PdfStandard::A_3u,
        "a-3a" => PdfStandard::A_3a,
        "a-4" => PdfStandard::A_4,
        "a-4f" => PdfStandard::A_4f,
        "a-4e" => PdfStandard::A_4e,
        "ua-1" => PdfStandard::Ua_1,
        other => {
            return Err(TnError::invalid_argument(format!(
                "unknown PDF standard `{other}`; expected one of 1.4, 1.5, 1.6, 1.7, 2.0, \
                 a-1b, a-1a, a-2b, a-2u, a-2a, a-3b, a-3u, a-3a, a-4, a-4f, a-4e, ua-1"
            )));
        }
    })
}

/// Parses `"1-3,5"` into one-indexed ranges. An open bound means "from the start" or "to the end".
fn parse_page_ranges(text: &str) -> Result<PageRanges, TnError> {
    let mut ranges = Vec::new();

    for part in text.split(',') {
        let part = part.trim();
        if part.is_empty() {
            continue;
        }

        let (start, end) = match part.split_once('-') {
            Some((start, end)) => (start.trim(), end.trim()),
            None => (part, part),
        };

        ranges.push(parse_page_bound(start)?..=parse_page_bound(end)?);
    }

    if ranges.is_empty() {
        return Err(TnError::invalid_argument(
            "page ranges must name at least one page",
        ));
    }

    Ok(PageRanges::new(ranges))
}

fn parse_page_bound(text: &str) -> Result<Option<NonZeroUsize>, TnError> {
    if text.is_empty() {
        return Ok(None);
    }

    text.parse::<NonZeroUsize>().map(Some).map_err(|_| {
        TnError::invalid_argument(format!("invalid page number `{text}`; pages start at 1"))
    })
}

fn svg_page(document: &PagedDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    let page = page_of(document, options)?;
    let svg = typst_svg::svg(page, &svg_options(options));
    Ok(svg.into_bytes())
}

fn svg_merged(document: &PagedDocument, options: &ExportOptions) -> String {
    typst_svg::svg_merged(document, &svg_options(options), Abs::pt(MERGED_GAP_PT))
}

fn svg_options(options: &ExportOptions) -> SvgOptions {
    SvgOptions {
        render_bleed: false,
        pretty: options.pretty.unwrap_or(false),
    }
}

fn png_page(document: &PagedDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    let page = page_of(document, options)?;
    typst_render::render(page, &render_options(options))
        .encode_png()
        .map_err(|e| TnError::new(TN_ERR_EXPORT, format!("PNG encoding failed: {e}")))
}

fn png_merged(document: &PagedDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    typst_render::render_merged(
        document,
        &render_options(options),
        Abs::pt(MERGED_GAP_PT),
        None,
    )
    .encode_png()
    .map_err(|e| TnError::new(TN_ERR_EXPORT, format!("PNG encoding failed: {e}")))
}

fn render_options(options: &ExportOptions) -> RenderOptions {
    let ppi = options.ppi.unwrap_or(DEFAULT_PPI);
    RenderOptions {
        pixel_per_pt: Scalar::new(ppi / 72.0),
        render_bleed: false,
    }
}

fn html(document: &Document, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    match document {
        Document::Html(doc) => encode_html(doc, options),
        // A bundle already carries its exported HTML.
        Document::Bundle { files } => {
            crate::bundle::html(files)
                .map(<[u8]>::to_vec)
                .ok_or_else(|| {
                    TnError::new(
                        TN_ERR_EXPORT,
                        "the bundle contains no HTML document at its root",
                    )
                })
        }
        Document::Paged(_) => Err(TnError::invalid_argument(
            "HTML export requires an HTML document; compile with document = \"html\"",
        )),
    }
}

/// Markdown and text are derived from the HTML tree, never from the paged layout.
fn semantic(
    document: &Document,
    render: impl FnOnce(&HtmlDocument) -> String,
) -> Result<Vec<u8>, TnError> {
    match document {
        Document::Html(doc) => Ok(render(doc).into_bytes()),
        Document::Bundle { .. } => Err(TnError::invalid_argument(
            "markdown and text require an HTML document; compile with document = \"html\"",
        )),
        Document::Paged(_) => Err(TnError::invalid_argument(
            "this format requires an HTML document; compile with document = \"html\"",
        )),
    }
}

fn encode_html(document: &HtmlDocument, options: &ExportOptions) -> Result<Vec<u8>, TnError> {
    let html_options = HtmlOptions {
        pretty: options.pretty.unwrap_or(true),
    };
    typst_html::html(document, &html_options)
        .map(String::into_bytes)
        .map_err(|diagnostics| {
            TnError::with_json(TN_ERR_EXPORT, diag::to_json_empty(&diagnostics, "export"))
        })
}
