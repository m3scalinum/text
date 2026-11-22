//! Rasterising one page of a PDF.
//!
//! `hayro` is a pure-Rust CPU renderer: no native PDF engine, no system poppler, no font on the
//! host. A page is rendered at a resolution the caller picks, encoded as PNG, and returned
//! alongside the metadata a preview needs (pixel size, the resolution actually achieved).
//!
//! The pixel and byte ceilings are the security property of this module. A page's `MediaBox` comes
//! from the sender, so a 200×200 inch page at 1200 DPI would ask for 2400×2400×... of memory before
//! anything else could refuse it; the ceiling is checked against the page geometry *before* the
//! renderer allocates a pixmap.

use hayro::hayro_interpret::InterpreterSettings;
use hayro::hayro_syntax::{LoadPdfError, Pdf};
use hayro::{PixmapSettings, RenderCache, RenderSettings, render};

use crate::ffi::{
    TP_ERR_ENCRYPTED, TP_ERR_LIMIT, TP_ERR_NOT_A_PDF, TP_ERR_PAGE_OUT_OF_RANGE, TpError,
};

/// The resolution a caller gets when it names none: A4 at 150 DPI is 1240×1754, which is a preview
/// that stays sharp at 100 % on a HiDPI screen and is still the floor the OCR documentation calls
/// readable. A caller that wants an OCR pass asks for 300.
pub const DEFAULT_DPI: u32 = 150;

/// The lowest accepted resolution. Below 72 DPI a page is a thumbnail, which is a downscale of a
/// page image rather than a render: the text antialiasing of the renderer has nothing left to
/// average at one pixel per point.
pub const MIN_DPI: u32 = 72;

/// The highest accepted resolution. Past 1200 DPI the renderer costs minutes for a gain no reader
/// and no recogniser can use.
pub const MAX_DPI: u32 = 1200;

/// Default ceiling on the pixels one page may produce, 50 megapixels: roughly A2 at 300 DPI.
pub const DEFAULT_MAX_PIXELS: u64 = 50_000_000;

/// Default ceiling on the bytes one page may produce, 256 MiB: 67 megapixels of RGBA8.
pub const DEFAULT_MAX_BYTES: u64 = 256 * 1024 * 1024;

/// The largest pixel ceiling the crate will honour. A caller may lower the cap as far as it likes;
/// raising it past this is refused rather than clamped, because a silently lowered cap is a
/// promise the caller thinks it has.
pub const HARD_MAX_PIXELS: u64 = 200_000_000;

/// The largest byte ceiling the crate will honour.
pub const HARD_MAX_BYTES: u64 = 1024 * 1024 * 1024;

/// One rendered page.
#[derive(Debug)]
pub struct RenderedPage {
    /// The 1-based page number.
    pub page: u32,
    /// How many pages the document holds.
    pub page_count: u32,
    /// The PNG encoder's output.
    pub png: Vec<u8>,
    pub width: u32,
    pub height: u32,
    /// The resolution asked for.
    pub dpi: u32,
    /// The resolution the pixels actually carry: `width` divided by the page width in inches. The
    /// renderer truncates, so a 595.28 pt page at 150 DPI is 1240 px wide, not 1240.16.
    pub effective_dpi: f64,
}

/// What a render is allowed to produce. `Default` is the documented default; a caller overrides the
/// three fields from the options JSON.
#[derive(Clone, Copy, Debug)]
pub struct Options {
    pub dpi: u32,
    pub max_pixels: u64,
    pub max_bytes: u64,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            dpi: DEFAULT_DPI,
            max_pixels: DEFAULT_MAX_PIXELS,
            max_bytes: DEFAULT_MAX_BYTES,
        }
    }
}

impl Options {
    /// Refuses a combination the crate will not honour, before any document is read.
    pub fn validate(&self) -> Result<(), TpError> {
        if !(MIN_DPI..=MAX_DPI).contains(&self.dpi) {
            return Err(TpError::invalid_argument(format!(
                "dpi must be between {MIN_DPI} and {MAX_DPI}"
            )));
        }

        if self.max_pixels == 0 || self.max_pixels > HARD_MAX_PIXELS {
            return Err(TpError::invalid_argument(format!(
                "maxPixels must be between 1 and {HARD_MAX_PIXELS}"
            )));
        }

        if self.max_bytes == 0 || self.max_bytes > HARD_MAX_BYTES {
            return Err(TpError::invalid_argument(format!(
                "maxBytes must be between 1 and {HARD_MAX_BYTES}"
            )));
        }

        Ok(())
    }
}

/// Reads the document and returns how many pages it holds.
pub fn page_count(pdf_bytes: &[u8]) -> Result<u32, TpError> {
    Ok(load(pdf_bytes)?.pages().len() as u32)
}

/// Renders `page` (1-based) at `options.dpi` into PNG.
pub fn render_page(
    pdf_bytes: &[u8],
    page: u32,
    options: &Options,
) -> Result<RenderedPage, TpError> {
    options.validate()?;

    let pdf = load(pdf_bytes)?;
    let pages = pdf.pages();
    let page_count = pages.len() as u32;

    if page_count == 0 {
        return Err(TpError::new(TP_ERR_NOT_A_PDF, "the document has no page"));
    }

    if page == 0 || page > page_count {
        return Err(TpError::new(
            TP_ERR_PAGE_OUT_OF_RANGE,
            format!(
                "page {page} does not exist: the document has {page_count} page(s), numbered from 1"
            ),
        ));
    }

    let index = (page - 1) as usize;
    let page_ref = &pages[index];

    // The renderer truncates each dimension to a whole pixel and then to a `u16`, so the ceiling is
    // checked against that same arithmetic: refusing after the render has allocated the pixmap
    // would be refusing too late.
    let (width_pt, height_pt) = page_ref.render_dimensions();
    let scale = options.dpi as f32 / 72.0;
    let width = (width_pt * scale) as u32;
    let height = (height_pt * scale) as u32;

    if width == 0 || height == 0 {
        return Err(TpError::new(
            TP_ERR_NOT_A_PDF,
            format!("page {page} has no area at {} dpi", options.dpi),
        ));
    }

    if width > u32::from(u16::MAX) || height > u32::from(u16::MAX) {
        return Err(TpError::new(
            TP_ERR_LIMIT,
            format!(
                "page {page} renders to {width}x{height} at {} dpi, past the 65535-pixel dimension limit",
                options.dpi
            ),
        ));
    }

    let pixels = u64::from(width) * u64::from(height);
    check_ceiling(page, width, height, pixels, options)?;

    let cache = RenderCache::new();
    let interpreter_settings = InterpreterSettings::default();
    let render_settings = RenderSettings::default();
    let pixmap_settings = PixmapSettings {
        x_scale: scale,
        y_scale: scale,
        // The default is transparent black: a page with no background paint would come out black
        // with an alpha channel nobody asked for. A page is text on paper, and paper is white.
        bg_color: hayro::vello_cpu::color::palette::css::WHITE,
    };

    let pixmap = render(
        page_ref,
        &cache,
        &interpreter_settings,
        &render_settings,
        &pixmap_settings,
    );

    let rendered_width = u32::from(pixmap.width());
    let rendered_height = u32::from(pixmap.height());

    // The repository's own promise, checked against what the renderer actually produced rather
    // than against the arithmetic above: a disagreement is a bug in this module, and a caller that
    // set a ceiling must never receive a pixmap past it.
    if rendered_width != width || rendered_height != height {
        return Err(TpError::new(
            TP_ERR_LIMIT,
            format!(
                "page {page} was rendered at {rendered_width}x{rendered_height}, not the {width}x{height} the ceiling was checked against"
            ),
        ));
    }

    let png = pixmap.into_png().map_err(|error| {
        TpError::new(
            TP_ERR_NOT_A_PDF,
            format!("page {page} could not be encoded as PNG: {error}"),
        )
    })?;

    Ok(RenderedPage {
        page,
        page_count,
        png,
        width,
        height,
        dpi: options.dpi,
        effective_dpi: f64::from(width) * 72.0 / f64::from(width_pt),
    })
}

/// Checks the pixmap the render would allocate against both ceilings.
fn check_ceiling(
    page: u32,
    width: u32,
    height: u32,
    pixels: u64,
    options: &Options,
) -> Result<(), TpError> {
    if pixels > options.max_pixels {
        return Err(TpError::new(
            TP_ERR_LIMIT,
            format!(
                "page {page} renders to {width}x{height} at {} dpi, {pixels} pixels, past the {} pixel limit",
                options.dpi, options.max_pixels
            ),
        ));
    }

    // The renderer draws in premultiplied RGBA8: four bytes per pixel, which is the figure the cap
    // has to cover. The PNG that comes out is smaller, but the peak is the pixmap.
    let bytes = pixels.saturating_mul(4);
    if bytes > options.max_bytes {
        return Err(TpError::new(
            TP_ERR_LIMIT,
            format!(
                "page {page} renders to {width}x{height} at {} dpi, {bytes} bytes of pixels, past the {} byte limit",
                options.dpi, options.max_bytes
            ),
        ));
    }

    Ok(())
}

/// Parses the document, naming an encrypted one the way the rest of the package names it.
fn load(pdf_bytes: &[u8]) -> Result<Pdf, TpError> {
    Pdf::new(pdf_bytes.to_vec()).map_err(|error| match error {
        LoadPdfError::Decryption(_) => TpError::new(
            TP_ERR_ENCRYPTED,
            "the document is encrypted, so it cannot be rendered until it is decrypted: open it with PdfEncryption.Open and use the bytes it returns",
        ),
        LoadPdfError::Invalid => TpError::new(
            TP_ERR_NOT_A_PDF,
            "the bytes are not a PDF, or the document is too damaged to read",
        ),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A one-page PDF, `width` by `height` points, with an empty content stream. The offsets in the
    /// cross-reference table are computed, not written by hand: a table that points one byte off is
    /// a document some readers accept and others refuse, which is the worst kind of test fixture.
    fn minimal_pdf(width: u32, height: u32) -> Vec<u8> {
        let mut out = Vec::new();
        let mut offsets = Vec::new();

        out.extend_from_slice(b"%PDF-1.7\n");

        offsets.push(out.len());
        out.extend_from_slice(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(b"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets.push(out.len());
        let page = format!(
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents 4 0 R /Resources << >> >>\nendobj\n"
        );
        out.extend_from_slice(page.as_bytes());

        offsets.push(out.len());
        out.extend_from_slice(b"4 0 obj\n<< /Length 0 >>\nstream\n\nendstream\nendobj\n");

        let xref = out.len();
        out.extend_from_slice(b"xref\n0 5\n0000000000 65535 f \n");
        for offset in &offsets {
            out.extend_from_slice(format!("{offset:010} 00000 n \n").as_bytes());
        }
        out.extend_from_slice(b"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        out.extend_from_slice(format!("{xref}\n%%EOF\n").as_bytes());

        out
    }

    #[test]
    fn a_page_renders_to_the_requested_pixel_size() {
        let pdf = minimal_pdf(200, 100);

        let rendered = render_page(&pdf, 1, &Options::default()).unwrap();

        // 150 DPI is 150/72 of a point, and the renderer truncates.
        assert_eq!(rendered.width, 416);
        assert_eq!(rendered.height, 208);
        assert_eq!(rendered.page, 1);
        assert_eq!(rendered.page_count, 1);
    }

    #[test]
    fn the_output_is_a_png_of_the_declared_size() {
        let pdf = minimal_pdf(200, 100);

        let rendered = render_page(&pdf, 1, &Options::default()).unwrap();

        assert_eq!(&rendered.png[..8], b"\x89PNG\r\n\x1a\n");
        // The IHDR payload starts after the 8-byte signature, the 4-byte length and the 4-byte type.
        let ihdr_width = u32::from_be_bytes(rendered.png[16..20].try_into().unwrap());
        let ihdr_height = u32::from_be_bytes(rendered.png[20..24].try_into().unwrap());
        assert_eq!(ihdr_width, rendered.width);
        assert_eq!(ihdr_height, rendered.height);
    }

    #[test]
    fn the_effective_resolution_is_derived_from_the_render_box() {
        let pdf = minimal_pdf(72, 72);

        let rendered = render_page(&pdf, 1, &Options::default()).unwrap();

        assert_eq!((rendered.width, rendered.height), (150, 150));
        assert!((rendered.effective_dpi - 150.0).abs() < f64::EPSILON);
    }

    #[test]
    fn the_default_resolution_is_one_hundred_and_fifty() {
        assert_eq!(Options::default().dpi, DEFAULT_DPI);
    }

    #[test]
    fn a_page_past_the_last_one_is_refused_by_number() {
        let pdf = minimal_pdf(100, 100);

        let error = match render_page(&pdf, 2, &Options::default()) {
            Ok(_) => panic!("page 2 must not render in a one-page document"),
            Err(error) => error,
        };

        assert_eq!(error.code, TP_ERR_PAGE_OUT_OF_RANGE);
    }

    #[test]
    fn page_zero_is_not_a_page() {
        let pdf = minimal_pdf(100, 100);

        let error = render_page(&pdf, 0, &Options::default()).unwrap_err();

        assert_eq!(error.code, TP_ERR_PAGE_OUT_OF_RANGE);
    }

    #[test]
    fn a_resolution_outside_the_range_is_refused() {
        let pdf = minimal_pdf(100, 100);

        let low = Options {
            dpi: MIN_DPI - 1,
            ..Options::default()
        };
        let high = Options {
            dpi: MAX_DPI + 1,
            ..Options::default()
        };

        assert_eq!(
            render_page(&pdf, 1, &low).unwrap_err().code,
            crate::ffi::TP_ERR_INVALID_ARGUMENT
        );
        assert_eq!(
            render_page(&pdf, 1, &high).unwrap_err().code,
            crate::ffi::TP_ERR_INVALID_ARGUMENT
        );
    }

    #[test]
    fn a_ceiling_is_enforced_before_the_render_allocates() {
        let pdf = minimal_pdf(1000, 1000);

        let error = render_page(
            &pdf,
            1,
            &Options {
                dpi: 1200,
                max_pixels: 1_000_000,
                ..Options::default()
            },
        )
        .unwrap_err();

        assert_eq!(error.code, TP_ERR_LIMIT);
        assert!(error.json.contains("pixel limit"));
    }

    #[test]
    fn the_byte_ceiling_catches_what_the_pixel_ceiling_let_through() {
        let pdf = minimal_pdf(500, 500);

        let error = render_page(
            &pdf,
            1,
            &Options {
                dpi: 300,
                max_pixels: DEFAULT_MAX_PIXELS,
                max_bytes: 1024,
            },
        )
        .unwrap_err();

        assert_eq!(error.code, TP_ERR_LIMIT);
        assert!(error.json.contains("byte limit"));
    }

    #[test]
    fn a_ceiling_above_the_hard_cap_is_refused_not_clamped() {
        let error = Options {
            max_pixels: HARD_MAX_PIXELS + 1,
            ..Options::default()
        }
        .validate()
        .unwrap_err();

        assert_eq!(error.code, crate::ffi::TP_ERR_INVALID_ARGUMENT);

        let zero = Options {
            max_bytes: 0,
            ..Options::default()
        }
        .validate()
        .unwrap_err();

        assert_eq!(zero.code, crate::ffi::TP_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn bytes_that_are_not_a_pdf_are_refused() {
        let error = render_page(b"not a pdf at all", 1, &Options::default()).unwrap_err();

        assert_eq!(error.code, crate::ffi::TP_ERR_NOT_A_PDF);
    }

    #[test]
    fn the_page_count_is_read_without_rendering() {
        assert_eq!(page_count(&minimal_pdf(10, 10)).unwrap(), 1);
    }
}
