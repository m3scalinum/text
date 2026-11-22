//! Page rasterisation, so a scanned PDF becomes pixels without a second package
//! and without a native PDF engine.
//!
//! hayro is a pure-Rust CPU rasteriser; it needs no fonts on the host to draw a
//! scanned page, which is the case that matters here.

use hayro::hayro_interpret::InterpreterSettings;
use hayro::hayro_syntax::Pdf;
use hayro::{PixmapSettings, RenderCache, RenderSettings, render};

use crate::ffi::{TO_ERR_PDF, ToError};

/// The largest pixmap one page may produce. A page beyond this is a hostile
/// document (an enormous MediaBox scaled to a high DPI), not a scan.
const MAX_PIXELS: u64 = 120_000_000;

/// One rasterised page, in RGBA8.
pub struct RenderedPage {
    pub index: u32,
    pub width: u32,
    pub height: u32,
    pub dpi: u32,
    pub pixels: Vec<u8>,
}

/// The number of pages a PDF holds, without rasterising any of them.
///
/// It exists so a caller can plan per-page work — which language reads which page —
/// before paying for a render. An empty document is refused here as it is in
/// [`render_pages`]: a plan for zero pages is not a plan.
pub fn page_count(pdf_bytes: &[u8]) -> Result<u32, ToError> {
    let pdf = Pdf::new(pdf_bytes.to_vec())
        .map_err(|e| ToError::new(TO_ERR_PDF, format!("read pdf: {e:?}")))?;
    let total = pdf.pages().len() as u32;

    if total == 0 {
        return Err(ToError::new(TO_ERR_PDF, "the document has no pages"));
    }

    Ok(total)
}

/// Rasterise `page_count` pages from `first_page` at `dpi`. `page_count == 0`
/// means "to the end".
pub fn render_pages(
    pdf_bytes: &[u8],
    dpi: u32,
    first_page: u32,
    page_count: u32,
) -> Result<Vec<RenderedPage>, ToError> {
    if !(72..=1200).contains(&dpi) {
        return Err(ToError::invalid_argument("dpi must be between 72 and 1200"));
    }

    let pdf = Pdf::new(pdf_bytes.to_vec())
        .map_err(|e| ToError::new(TO_ERR_PDF, format!("read pdf: {e:?}")))?;
    let pages = pdf.pages();
    let total = pages.len() as u32;

    if total == 0 {
        return Err(ToError::new(TO_ERR_PDF, "the document has no pages"));
    }
    if first_page >= total {
        return Err(ToError::invalid_argument(format!(
            "first page {first_page} is past the last page ({})",
            total - 1
        )));
    }

    let last = if page_count == 0 {
        total
    } else {
        total.min(first_page.saturating_add(page_count))
    };

    let scale = dpi as f32 / 72.0;
    let cache = RenderCache::new();
    let interpreter_settings = InterpreterSettings::default();
    let render_settings = RenderSettings::default();

    let mut rendered = Vec::with_capacity((last - first_page) as usize);

    for index in first_page..last {
        let page = &pages[index as usize];
        let pixmap_settings = PixmapSettings {
            x_scale: scale,
            y_scale: scale,
            // Default is transparent black: a page would come out black and the
            // recogniser would see a dark rectangle. A scan is text on white.
            bg_color: hayro::vello_cpu::color::palette::css::WHITE,
        };

        let pixmap = render(
            page,
            &cache,
            &interpreter_settings,
            &render_settings,
            &pixmap_settings,
        );

        let width = u32::from(pixmap.width());
        let height = u32::from(pixmap.height());
        if u64::from(width) * u64::from(height) > MAX_PIXELS {
            return Err(ToError::new(
                TO_ERR_PDF,
                format!(
                    "page {} renders to {width}x{height} at {dpi} dpi, past the {MAX_PIXELS} pixel limit",
                    index + 1
                ),
            ));
        }

        // The pixmap is premultiplied RGBA8 over an opaque white background, so
        // premultiplication is the identity here and the alpha channel is constant;
        // ocrs ignores alpha anyway.
        let mut pixels = Vec::with_capacity(pixmap.data().len() * 4);
        for pixel in pixmap.data() {
            pixels.extend_from_slice(&[pixel.r, pixel.g, pixel.b, pixel.a]);
        }

        rendered.push(RenderedPage {
            index,
            width,
            height,
            dpi,
            pixels,
        });
    }

    Ok(rendered)
}
