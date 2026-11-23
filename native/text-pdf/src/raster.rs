//! Rasterising one page of a PDF.
//!
//! `hayro` is a pure-Rust CPU renderer: no native PDF engine, no system poppler, no font on the
//! host. A page is rendered at a resolution the caller picks, encoded as PNG or JPEG, and returned
//! alongside the metadata a preview needs (pixel size, the resolution actually achieved).
//!
//! The pixel and byte ceilings are the security property of this module. A page's `MediaBox` comes
//! from the sender, so a 200×200 inch page at 1200 DPI would ask for 2400×2400×... of memory before
//! anything else could refuse it; the ceiling is checked against the page geometry *before* the
//! renderer allocates a pixmap.

use hayro::hayro_interpret::InterpreterSettings;
use hayro::hayro_syntax::{LoadPdfError, Pdf};
use hayro::vello_cpu::Pixmap;
use hayro::{PixmapSettings, RenderCache, RenderSettings, render};
use image::ImageEncoder;
use image::codecs::jpeg::JpegEncoder;

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

/// The JPEG quality a render gets when it names none: 85 is the value libjpeg's own documentation
/// calls the point where the artefacts stop being visible on a page of text, and it is what every
/// other encoder in the suite defaults to.
pub const DEFAULT_JPEG_QUALITY: u8 = 85;

/// The lowest accepted JPEG quality. Below 1 there is no quantisation table to scale.
pub const MIN_JPEG_QUALITY: u8 = 1;

/// The highest accepted JPEG quality: 100 disables quantisation, so the file is the largest a JPEG
/// can be, not a lossless one.
pub const MAX_JPEG_QUALITY: u8 = 100;

/// The encoding of a rendered page.
///
/// The renderer draws premultiplied RGBA8 and encodes PNG itself; JPEG comes from the `image` crate,
/// which is already in this crate's graph through `hayro` and only needs its `jpeg` feature
/// turned on. Nothing is converted silently: PNG keeps the pixmap's own RGB, JPEG drops the alpha
/// channel — which is opaque white for every page this module produces — and keeps the RGB values
/// the renderer wrote.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Format {
    /// A lossless PNG, 8 bit, RGB where the page is opaque.
    #[default]
    Png,
    /// A baseline JPEG, always RGB (no alpha), lossy and irreversible.
    Jpeg,
}

impl Format {
    /// The name the metadata and the options JSON use.
    pub const fn name(self) -> &'static str {
        match self {
            Self::Png => "png",
            Self::Jpeg => "jpeg",
        }
    }

    /// Reads a format the caller named in the options JSON, refusing anything else by name.
    pub fn parse(value: &str) -> Result<Self, TpError> {
        match value {
            "png" => Ok(Self::Png),
            "jpeg" => Ok(Self::Jpeg),
            other => Err(TpError::invalid_argument(format!(
                "format must be \"png\" or \"jpeg\", not \"{other}\""
            ))),
        }
    }
}

/// One rendered page.
#[derive(Debug)]
pub struct RenderedPage {
    /// The 1-based page number.
    pub page: u32,
    /// How many pages the document holds.
    pub page_count: u32,
    /// The encoder's output, in `format`.
    pub bytes: Vec<u8>,
    /// The encoding `bytes` is in: the one the caller asked for, never a renamed other.
    pub format: Format,
    /// The width in pixels.
    pub width: u32,
    /// The height in pixels.
    pub height: u32,
    /// The resolution asked for.
    pub dpi: u32,
    /// The resolution the pixels actually carry: `width` divided by the page width in inches. The
    /// renderer truncates, so a 595.28 pt page at 150 DPI is 1240 px wide, not 1240.16.
    pub effective_dpi: f64,
}

/// What a render is allowed to produce. `Default` is the documented default; a caller overrides the
/// fields from the options JSON.
#[derive(Clone, Copy, Debug)]
pub struct Options {
    pub dpi: u32,
    pub max_pixels: u64,
    pub max_bytes: u64,
    /// The encoding of the result.
    pub format: Format,
    /// The JPEG quality, 1 to 100. Carried whatever the format is, and validated whatever the
    /// format is: a value outside the range is a caller's mistake worth naming, not a setting to
    /// ignore because the format happens to be PNG.
    pub quality: u8,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            dpi: DEFAULT_DPI,
            max_pixels: DEFAULT_MAX_PIXELS,
            max_bytes: DEFAULT_MAX_BYTES,
            format: Format::Png,
            quality: DEFAULT_JPEG_QUALITY,
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

        if !(MIN_JPEG_QUALITY..=MAX_JPEG_QUALITY).contains(&self.quality) {
            return Err(TpError::invalid_argument(format!(
                "quality must be between {MIN_JPEG_QUALITY} and {MAX_JPEG_QUALITY}"
            )));
        }

        Ok(())
    }
}

/// Reads the document and returns how many pages it holds.
pub fn page_count(pdf_bytes: &[u8]) -> Result<u32, TpError> {
    Ok(load(pdf_bytes)?.pages().len() as u32)
}

/// Renders `page` (1-based) at `options.dpi`, encoded in `options.format`.
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

    let bytes = match options.format {
        Format::Png => pixmap.into_png().map_err(|error| {
            TpError::new(
                TP_ERR_NOT_A_PDF,
                format!("page {page} could not be encoded as PNG: {error}"),
            )
        })?,
        Format::Jpeg => encode_jpeg(&pixmap, options.quality).map_err(|error| {
            TpError::new(
                TP_ERR_NOT_A_PDF,
                format!("page {page} could not be encoded as JPEG: {error}"),
            )
        })?,
    };

    Ok(RenderedPage {
        page,
        page_count,
        bytes,
        format: options.format,
        width,
        height,
        dpi: options.dpi,
        effective_dpi: f64::from(width) * 72.0 / f64::from(width_pt),
    })
}

/// Encodes the pixmap as a baseline JPEG at `quality`, in RGB.
///
/// The pixmap is premultiplied RGBA8 and the page background was set to white, so the alpha channel
/// is opaque and the RGB bytes are already the page's colours: dropping it is not a conversion. The
/// composite over white is written out anyway rather than assumed, so a pixel the renderer left
/// translucent comes out as the white page a reader would see instead of as a darkened one. JPEG has
/// no alpha channel and no 16-bit sample, so those are the two properties a caller loses here.
///
/// The intermediate RGB buffer is three bytes per pixel on top of the pixmap's four. Both are
/// proportional to the pixel count the byte ceiling already bounds, so the peak of a JPEG render is
/// at most 7/4 of the ceiling rather than the ceiling itself; the spec says so.
fn encode_jpeg(pixmap: &Pixmap, quality: u8) -> Result<Vec<u8>, image::ImageError> {
    let width = u32::from(pixmap.width());
    let height = u32::from(pixmap.height());
    let pixels = pixmap.data_as_u8_slice();

    let mut rgb = Vec::with_capacity(pixels.len() / 4 * 3);

    for pixel in pixels.chunks_exact(4) {
        // Premultiplied over opaque white: out = c + (1 - a) * 255, per channel.
        let alpha = u16::from(pixel[3]);
        let opaque = 255 - alpha;

        for channel in &pixel[..3] {
            rgb.push((u16::from(*channel) + opaque).min(255) as u8);
        }
    }

    let mut out = Vec::new();
    JpegEncoder::new_with_quality(&mut out, quality).write_image(
        &rgb,
        width,
        height,
        image::ExtendedColorType::Rgb8,
    )?;

    Ok(out)
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

    /// A one-page PDF whose content stream is `content`, with Helvetica registered as `/F1`.
    ///
    /// The rectangle and text tests need a page that is not blank: a blank page is the one case
    /// where PNG beats JPEG on every count, because there is nothing to encode but a white run.
    fn content_pdf(width: u32, height: u32, content: &str) -> Vec<u8> {
        let mut out = Vec::new();
        let mut offsets = Vec::new();

        out.extend_from_slice(b"%PDF-1.7\n");

        offsets.push(out.len());
        out.extend_from_slice(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(b"2 0 obj\n<< /Type /Pages /Kids [4 0 R] /Count 1 >>\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(
            b"3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n",
        );

        offsets.push(out.len());
        let page = format!(
            "4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>\nendobj\n"
        );
        out.extend_from_slice(page.as_bytes());

        offsets.push(out.len());
        out.extend_from_slice(
            format!(
                "5 0 obj\n<< /Length {} >>\nstream\n{content}\nendstream\nendobj\n",
                content.len()
            )
            .as_bytes(),
        );

        let xref = out.len();
        out.extend_from_slice(b"xref\n0 6\n0000000000 65535 f \n");
        for offset in &offsets {
            out.extend_from_slice(format!("{offset:010} 00000 n \n").as_bytes());
        }
        out.extend_from_slice(b"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        out.extend_from_slice(format!("{xref}\n%%EOF\n").as_bytes());

        out
    }

    /// An A4 page of dense body text, which is what a page image of a document actually looks like:
    /// thousands of antialiased glyph edges.
    fn dense_text_content() -> String {
        let mut content = String::from("0.2 g 40 792 515 3 re f\n");
        content.push_str(&text_ops());
        content
    }

    /// The text block of the fixtures: 62 justified lines at 9 pt.
    fn text_ops() -> String {
        const WORDS: [&str; 12] = [
            "the",
            "rasteriser",
            "renders",
            "a",
            "page",
            "at",
            "the",
            "resolution",
            "the",
            "caller",
            "asks",
            "for",
        ];

        let mut content = String::from("BT /F1 9 Tf 11 TL 40 770 Td\n");

        for line in 0..62 {
            let mut text = String::new();
            let mut word = line * 5;
            while text.len() < 108 {
                text.push_str(WORDS[word % WORDS.len()]);
                text.push(' ');
                word += 3;
            }

            // A PDF literal string: no parenthesis, no backslash, and nothing past 7-bit ASCII.
            content.push('(');
            content.push_str(text.trim_end());
            content.push_str(") Tj T*\n");
        }

        content.push_str("ET\n");
        content
    }

    /// A one-page A4 PDF whose page is a full-page image at the page's own resolution, with grain:
    /// a scanned page, which is the content a preview is actually asked to carry and the one where
    /// a lossless encoder pays for every speck of the scan.
    ///
    /// The image is a 1240×1754 RGB `DeviceRGB` XObject of interfering sine waves plus deterministic
    /// grain, uncompressed — legal PDF, and deterministic, so the two encoders are measured on
    /// exactly the same pixels.
    fn photo_page_pdf() -> Vec<u8> {
        const IMAGE_WIDTH: u32 = 1240;
        const IMAGE_HEIGHT: u32 = 1754;

        let mut samples = Vec::with_capacity((IMAGE_WIDTH * IMAGE_HEIGHT * 3) as usize);

        for y in 0..IMAGE_HEIGHT {
            for x in 0..IMAGE_WIDTH {
                let fx = f64::from(x);
                let fy = f64::from(y);

                // A scan is never clean: film and sensor grain is fine, low-amplitude noise that a
                // lossless encoder has to store pixel by pixel and a DCT attenuates to nothing.
                let grain =
                    f64::from((x.wrapping_mul(2_654_435_761) ^ y.wrapping_mul(40_503)) % 13) - 6.0;

                for wave in [
                    128.0 + 90.0 * (fx / 41.0).sin() * (fy / 67.0).cos(),
                    128.0 + 90.0 * ((fx + fy) / 53.0).sin(),
                    128.0 + 90.0 * (fy / 37.0).sin() * (fx / 71.0).cos(),
                ] {
                    samples.push((wave + grain).round().clamp(0.0, 255.0) as u8);
                }
            }
        }

        // A scanned page is the page's content and nothing else: no vector text over the image.
        let content = String::from("q 595 0 0 842 0 0 cm /Im0 Do Q\n");

        let mut out = Vec::new();
        let mut offsets = Vec::new();

        out.extend_from_slice(b"%PDF-1.7\n");

        offsets.push(out.len());
        out.extend_from_slice(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(b"2 0 obj\n<< /Type /Pages /Kids [4 0 R] /Count 1 >>\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(
            b"3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n",
        );

        offsets.push(out.len());
        out.extend_from_slice(
            b"4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 6 0 R /Resources << /Font << /F1 3 0 R >> /XObject << /Im0 5 0 R >> >> >>\nendobj\n",
        );

        offsets.push(out.len());
        out.extend_from_slice(
            format!(
                "5 0 obj\n<< /Type /XObject /Subtype /Image /Width {IMAGE_WIDTH} /Height {IMAGE_HEIGHT} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {} >>\nstream\n",
                samples.len()
            )
            .as_bytes(),
        );
        out.extend_from_slice(&samples);
        out.extend_from_slice(b"\nendstream\nendobj\n");

        offsets.push(out.len());
        out.extend_from_slice(
            format!(
                "6 0 obj\n<< /Length {} >>\nstream\n{content}\nendstream\nendobj\n",
                content.len()
            )
            .as_bytes(),
        );

        let xref = out.len();
        out.extend_from_slice(b"xref\n0 7\n0000000000 65535 f \n");
        for offset in &offsets {
            out.extend_from_slice(format!("{offset:010} 00000 n \n").as_bytes());
        }
        out.extend_from_slice(b"trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        out.extend_from_slice(format!("{xref}\n%%EOF\n").as_bytes());

        out
    }

    /// Encodes one page of the dense text fixture in `format` and at `quality`.
    fn dense_render(format: Format, quality: u8) -> RenderedPage {
        render_page(
            &content_pdf(595, 842, &dense_text_content()),
            1,
            &Options {
                format,
                quality,
                ..Options::default()
            },
        )
        .unwrap()
    }

    /// Encodes one page of the photographic fixture in `format` and at `quality`.
    fn photo_render(format: Format, quality: u8) -> RenderedPage {
        render_page(
            &photo_page_pdf(),
            1,
            &Options {
                format,
                quality,
                ..Options::default()
            },
        )
        .unwrap()
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

        assert_eq!(rendered.format, Format::Png);
        assert_eq!(&rendered.bytes[..8], b"\x89PNG\r\n\x1a\n");
        // The IHDR payload starts after the 8-byte signature, the 4-byte length and the 4-byte type.
        let ihdr_width = u32::from_be_bytes(rendered.bytes[16..20].try_into().unwrap());
        let ihdr_height = u32::from_be_bytes(rendered.bytes[20..24].try_into().unwrap());
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
                ..Options::default()
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

    #[test]
    fn a_jpeg_is_much_smaller_than_the_png_of_the_same_page() {
        let png = photo_render(Format::Png, DEFAULT_JPEG_QUALITY);
        let jpeg = photo_render(Format::Jpeg, DEFAULT_JPEG_QUALITY);

        // The whole point of the format: a preview is bytes on the wire. The measured figures are
        // in docs/specifications/35-pdf-raster.md; the assertion states the direction with room to
        // spare, so a change in `image`'s encoder makes this test less impressive, not flaky.
        println!("png={} jpeg85={}", png.bytes.len(), jpeg.bytes.len());
        assert_eq!((png.width, png.height), (jpeg.width, jpeg.height));
        assert!(
            jpeg.bytes.len() * 4 < png.bytes.len(),
            "a JPEG of {} bytes is not a quarter of the {} byte PNG",
            jpeg.bytes.len(),
            png.bytes.len()
        );
    }

    #[test]
    fn a_higher_quality_is_a_larger_file() {
        let low = photo_render(Format::Jpeg, MIN_JPEG_QUALITY);
        let high = photo_render(Format::Jpeg, MAX_JPEG_QUALITY);

        println!(
            "jpeg1={} jpeg50={} jpeg85={} jpeg95={} jpeg100={}",
            low.bytes.len(),
            photo_render(Format::Jpeg, 50).bytes.len(),
            photo_render(Format::Jpeg, DEFAULT_JPEG_QUALITY).bytes.len(),
            photo_render(Format::Jpeg, 95).bytes.len(),
            high.bytes.len()
        );
        assert!(
            high.bytes.len() > low.bytes.len(),
            "quality {} produced {} bytes, quality {} produced {}",
            MAX_JPEG_QUALITY,
            high.bytes.len(),
            MIN_JPEG_QUALITY,
            low.bytes.len()
        );
    }

    #[test]
    fn the_requested_format_is_the_one_in_the_bytes() {
        let jpeg = photo_render(Format::Jpeg, DEFAULT_JPEG_QUALITY);

        assert_eq!(jpeg.format, Format::Jpeg);
        // SOI, then an APP0 segment: a JPEG, not a PNG with a different name.
        assert_eq!(&jpeg.bytes[..2], b"\xff\xd8");
        assert_eq!(&jpeg.bytes[2..4], b"\xff\xe0");
        assert!(jpeg.bytes.ends_with(b"\xff\xd9"), "no EOI marker");
        assert_ne!(&jpeg.bytes[..4], b"\x89PNG");
    }

    #[test]
    fn the_jpeg_decodes_to_the_declared_pixel_size() {
        let rendered = photo_render(Format::Jpeg, DEFAULT_JPEG_QUALITY);

        let decoded = image::load_from_memory(&rendered.bytes).unwrap();

        assert_eq!(decoded.width(), rendered.width);
        assert_eq!(decoded.height(), rendered.height);
        // RGB, 8 bit: what the encoder was handed, and what JPEG can carry.
        assert_eq!(
            decoded.color(),
            image::ColorType::Rgb8,
            "the JPEG is not an 8-bit RGB image"
        );
    }

    #[test]
    fn the_two_pages_are_measured_against_each_other() {
        // The figures the specification quotes, measured rather than remembered. Two pages, because
        // the honest answer depends on the content: a photographic page is where a JPEG earns its
        // place, and a page of flat text at 150 DPI is where a PNG already wins.
        let text_png = dense_render(Format::Png, DEFAULT_JPEG_QUALITY);
        let text_jpeg = dense_render(Format::Jpeg, DEFAULT_JPEG_QUALITY);
        let photo_png = photo_render(Format::Png, DEFAULT_JPEG_QUALITY);
        let photo_jpeg = photo_render(Format::Jpeg, DEFAULT_JPEG_QUALITY);

        println!(
            "text png={} jpeg85={} | photo png={} jpeg85={} | pixels={}x{}",
            text_png.bytes.len(),
            text_jpeg.bytes.len(),
            photo_png.bytes.len(),
            photo_jpeg.bytes.len(),
            photo_png.width,
            photo_png.height
        );

        assert!(photo_jpeg.bytes.len() < photo_png.bytes.len());
    }

    #[test]
    fn a_quality_outside_the_range_is_refused() {
        for quality in [0, MAX_JPEG_QUALITY + 1] {
            let error = Options {
                format: Format::Jpeg,
                quality,
                ..Options::default()
            }
            .validate()
            .unwrap_err();

            assert_eq!(error.code, crate::ffi::TP_ERR_INVALID_ARGUMENT);
            assert!(error.json.contains("quality"));
        }
    }

    #[test]
    fn an_unknown_format_is_refused_by_name() {
        assert_eq!(Format::parse("png").unwrap(), Format::Png);
        assert_eq!(Format::parse("jpeg").unwrap(), Format::Jpeg);

        let error = Format::parse("gif").unwrap_err();

        assert_eq!(error.code, crate::ffi::TP_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("gif"));
    }

    #[test]
    fn the_default_quality_is_eighty_five_and_the_default_format_is_png() {
        let options = Options::default();

        assert_eq!(options.quality, 85);
        assert_eq!(options.format, Format::Png);
    }
}
