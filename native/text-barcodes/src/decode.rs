//! Decoding: PNG bytes to every barcode the image holds, with their text, symbology and position.
//!
//! A PNG that holds no barcode is not an error: it is a decode that found nothing, reported as an
//! empty [`DecodePage::symbols`] with the reason in [`DecodePage::error`]. Only bytes that are not a
//! readable PNG at all, or an image past the caller's ceilings, are an error, because those are
//! caller mistakes rather than a document without a symbol.

use rxing::BarcodeFormat;
use rxing::{DecodeHints, helpers};

use crate::ffi::{BT_ERR_LIMIT, BtError};
use crate::symbology::Symbology;

/// The pixel ceiling a decode gets when the caller names none: 64 megapixels, an A4 page at 600 DPI
/// with room to spare.
pub const DEFAULT_MAXIMUM_PIXELS: u64 = 64_000_000;

/// The byte ceiling a decode gets when the caller names none: 256 MiB, the same order as the
/// rasteriser's ceiling, so a page cannot be rendered small enough to pass there and too large here.
pub const DEFAULT_MAXIMUM_BYTES: usize = 256 * 1024 * 1024;

/// What a decode is allowed to cost.
///
/// The dimensions of an image come from whoever produced it, and that is not this library: a scan
/// arrives from a sender. Both ceilings are checked before the decoder allocates the pixels, so an
/// image that claims to be a gigapixel is refused rather than decoded into memory.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DecodeBounds {
    /// The largest number of pixels the image may hold.
    pub maximum_pixels: u64,
    /// The largest number of bytes the encoded image may occupy.
    pub maximum_bytes: usize,
}

impl Default for DecodeBounds {
    fn default() -> Self {
        Self {
            maximum_pixels: DEFAULT_MAXIMUM_PIXELS,
            maximum_bytes: DEFAULT_MAXIMUM_BYTES,
        }
    }
}

/// One point of the detected symbol, in pixels of the input image.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DecodePoint {
    pub x: f32,
    pub y: f32,
}

/// The axis-aligned box enclosing every point the detector reported, in pixels of the input image.
///
/// It is the extent of [`Decoded::points`] and nothing more: a quiet zone around the symbol is not
/// included, because whether one exists is the image's business rather than the detector's.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DecodeBox {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

impl DecodeBox {
    /// The box around `points`, or `None` when there are none.
    fn around(points: &[DecodePoint]) -> Option<Self> {
        let first = points.first()?;
        let (mut min_x, mut min_y, mut max_x, mut max_y) = (first.x, first.y, first.x, first.y);

        for point in points {
            min_x = min_x.min(point.x);
            min_y = min_y.min(point.y);
            max_x = max_x.max(point.x);
            max_y = max_y.max(point.y);
        }

        Some(Self {
            x: min_x,
            y: min_y,
            width: max_x - min_x,
            height: max_y - min_y,
        })
    }
}

/// One symbol read from the image.
#[derive(Debug, Clone)]
pub struct Decoded {
    pub success: bool,
    pub text: String,
    /// The symbology, when the format is one this package exposes.
    pub symbology: Option<Symbology>,
    /// The format rxing detected, spelled as rxing spells it, even when it is outside
    /// [`Symbology`]. Empty when nothing was found.
    pub format: String,
    pub points: Vec<DecodePoint>,
    /// The box around [`Self::points`], when the detector reported any.
    pub bounds: Option<DecodeBox>,
    /// Why nothing was decoded, when nothing was.
    pub error: Option<String>,
}

impl Decoded {
    fn failure(reason: impl Into<String>) -> Self {
        Self {
            success: false,
            text: String::new(),
            symbology: None,
            format: String::new(),
            points: Vec::new(),
            bounds: None,
            error: Some(reason.into()),
        }
    }
}

/// Every symbol one image holds, in the order the engine found them.
///
/// A page of a delivery note carries one symbol per line item, so "the barcode" is not a property of
/// the document: it is a list, possibly empty. `error` explains the empty list; it is never a reason
/// to throw, because scanning a stack of documents should not throw once per blank page.
#[derive(Debug, Clone)]
pub struct DecodePage {
    pub symbols: Vec<Decoded>,
    /// Width of the decoded image, in pixels.
    pub width: u32,
    /// Height of the decoded image, in pixels.
    pub height: u32,
    pub error: Option<String>,
}

/// Decodes every barcode in a PNG under the default ceilings.
///
/// # Errors
/// `Err` means the bytes are not a PNG this build can read, or the image is past
/// [`DecodeBounds::default`]; both are bugs in the call rather than in the image.
pub fn decode_png_all(png: &[u8]) -> Result<DecodePage, BtError> {
    decode_png_all_within(png, DecodeBounds::default())
}

/// Decodes the first barcode found in a PNG, under the default ceilings.
///
/// This is the fast path, and the one [`crate::decode::decode_png`] has always been: a single
/// scanner pass, no `TryHarder`, no search for further symbols. [`decode_png_all`] is the thorough
/// one, and a caller that wants every symbol on a page pays for exactly that.
///
/// `Ok(Decoded)` covers both outcomes a caller can expect from a real document; `Err` means the
/// bytes are not a PNG this build can read, which is a bug in the call rather than in the image.
pub fn decode_png(png: &[u8]) -> Result<Decoded, BtError> {
    decode_png_within(png, DecodeBounds::default())
}

/// [`decode_png`] with explicit ceilings.
///
/// # Errors
/// `BT_ERR_INVALID_ARGUMENT` for empty bytes, `BT_ERR_LIMIT` for an image past either ceiling, and
/// `BT_ERR_DECODE` for bytes that are not a PNG this build can read.
pub fn decode_png_within(png: &[u8], bounds: DecodeBounds) -> Result<Decoded, BtError> {
    let (luma, width, height) = load_luma(png, bounds)?;

    match helpers::detect_in_luma_slice_with_hints(&luma, width, height, None, &mut decode_hints())
    {
        Ok(result) => Ok(symbol(&result)),
        Err(error) => Ok(Decoded::failure(format!(
            "no barcode was found in the image: {error}"
        ))),
    }
}

/// Decodes every barcode in a PNG, refusing an image past `bounds`.
///
/// # Errors
/// `BT_ERR_INVALID_ARGUMENT` for empty bytes, `BT_ERR_LIMIT` for an image past either ceiling, and
/// `BT_ERR_DECODE` for bytes that are not a PNG this build can read.
pub fn decode_png_all_within(png: &[u8], bounds: DecodeBounds) -> Result<DecodePage, BtError> {
    let (luma, width, height) = load_luma(png, bounds)?;

    let results =
        helpers::detect_multiple_in_luma_with_hints(luma, width, height, &mut decode_hints());

    let (symbols, error) = match results {
        Ok(results) if !results.is_empty() => (results.iter().map(symbol).collect(), None),
        Ok(_) => (Vec::new(), Some(NO_SYMBOL.to_string())),
        Err(error) => (
            Vec::new(),
            Some(format!("no barcode was found in the image: {error}")),
        ),
    };

    Ok(DecodePage {
        symbols,
        width,
        height,
        error,
    })
}

/// The grey pixels of a PNG, after both ceilings have been applied.
///
/// The header check runs before the decoder touches the pixels: a PNG states its size in the IHDR
/// chunk, and refusing there means a gigapixel claim never becomes an allocation. The same bound is
/// applied again to what came out of the decoder, because the header is a claim.
fn load_luma(png: &[u8], bounds: DecodeBounds) -> Result<(Vec<u8>, u32, u32), BtError> {
    if png.is_empty() {
        return Err(BtError::invalid_argument("the image is empty"));
    }

    if png.len() > bounds.maximum_bytes {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!(
                "the image is {} bytes, the limit is {}",
                png.len(),
                bounds.maximum_bytes
            ),
        ));
    }

    if let Some((width, height)) = png_dimensions(png) {
        if width as u64 * height as u64 > bounds.maximum_pixels {
            return Err(past_pixel_ceiling(width, height, bounds.maximum_pixels));
        }
    }

    let image =
        image::load_from_memory_with_format(png, image::ImageFormat::Png).map_err(|error| {
            BtError::decode(format!(
                "the bytes are not a PNG this build can read: {error}"
            ))
        })?;

    let grey = image.to_luma8();
    let width = grey.width();
    let height = grey.height();

    if width == 0 || height == 0 {
        return Err(BtError::decode("the PNG has no pixels"));
    }

    if width as u64 * height as u64 > bounds.maximum_pixels {
        return Err(past_pixel_ceiling(width, height, bounds.maximum_pixels));
    }

    Ok((grey.into_raw(), width, height))
}

/// What an image with no symbol reports. The engine's own message names its scanner and changes
/// between versions, so this package spells the empty case itself.
const NO_SYMBOL: &str = "no barcode was found in the image";

fn past_pixel_ceiling(width: u32, height: u32, maximum_pixels: u64) -> BtError {
    BtError::new(
        BT_ERR_LIMIT,
        format!(
            "the image is {width} x {height} = {} pixels, the limit is {maximum_pixels}",
            width as u64 * height as u64
        ),
    )
}

/// The width and height a PNG declares in its IHDR chunk, when the bytes are a PNG at all.
fn png_dimensions(png: &[u8]) -> Option<(u32, u32)> {
    const SIGNATURE: [u8; 8] = [0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];

    if png.len() < 24 || png[..8] != SIGNATURE || &png[12..16] != b"IHDR" {
        return None;
    }

    let width = u32::from_be_bytes(png[16..20].try_into().ok()?);
    let height = u32::from_be_bytes(png[20..24].try_into().ok()?);

    Some((width, height))
}

/// One rxing result, with the GS1 identifier split off and the detector's points turned into a box.
fn symbol(result: &rxing::RXingResult) -> Decoded {
    let format = result.getBarcodeFormat();
    let (text, symbology) = resolve_text(result.getText(), format);
    let points: Vec<DecodePoint> = result
        .getPoints()
        .iter()
        .map(|point| DecodePoint {
            x: point.x,
            y: point.y,
        })
        .collect();

    Decoded {
        success: true,
        text,
        symbology,
        format: format_spelling(format),
        bounds: DecodeBox::around(&points),
        points,
        error: None,
    }
}

/// The hints every decode runs under.
///
/// `AssumeGs1` is the only one, and it is on unconditionally: without it rxing drops FNC1 from a
/// Code 128 entirely, so a GS1-128 would read back as its AIs run together with no field
/// separators and no way to tell where one value ends. With it, the first FNC1 becomes the `]C1`
/// identifier and every later one becomes ASCII 29 (GS), which is the form
/// [`crate::gs1::Gs1Data::parse`] reads. A Code 128 that carries no FNC1 is unaffected.
fn decode_hints() -> DecodeHints {
    DecodeHints {
        AssumeGs1: Some(true),
        ..DecodeHints::default()
    }
}

/// Splits the GS1-128 symbology identifier from a decoded Code 128, so the caller sees the data
/// and the symbology it actually is.
fn resolve_text(text: &str, format: &BarcodeFormat) -> (String, Option<Symbology>) {
    let gs1_128 = text
        .strip_prefix("]C1")
        .filter(|_| *format == BarcodeFormat::CODE_128);

    if let Some(data) = gs1_128 {
        return (data.to_string(), Some(Symbology::Gs1_128));
    }

    (text.to_string(), Symbology::from_format(format))
}

/// How a format is spelled across the ABI, so a consumer that receives a symbol outside
/// [`Symbology`] can still name it.
fn format_spelling(format: &BarcodeFormat) -> String {
    format!("{format:?}")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::encode::{self, EncodeOptions};
    use crate::symbology::Symbology;
    use image::ImageEncoder as _;

    /// A greyscale PNG of `width` x `height`, every pixel `grey`.
    fn blank_png(width: u32, height: u32, grey: u8) -> Vec<u8> {
        let pixels = vec![grey; (width * height) as usize];
        let mut png = Vec::new();
        image::codecs::png::PngEncoder::new(&mut png)
            .write_image(&pixels, width, height, image::ExtendedColorType::L8)
            .expect("png");
        png
    }

    /// The grey pixels of a PNG, so two symbols can be pasted onto one page.
    fn luma(png: &[u8]) -> (u32, u32, Vec<u8>) {
        let image = image::load_from_memory_with_format(png, image::ImageFormat::Png)
            .expect("png")
            .to_luma8();
        (image.width(), image.height(), image.into_raw())
    }

    #[test]
    fn bytes_that_are_not_a_png_are_an_error() {
        let error = decode_png(b"this is not a png").unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_DECODE);
    }

    #[test]
    fn an_empty_image_is_rejected() {
        let error = decode_png(&[]).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn a_grey_image_without_a_symbol_is_a_failed_decode_not_an_error() {
        let page = decode_png_all(&blank_png(200, 200, 255)).expect("a blank image is a decode");

        assert!(page.symbols.is_empty());
        assert!(page.error.is_some());
        assert_eq!((page.width, page.height), (200, 200));
    }

    #[test]
    fn a_qr_rendered_at_one_pixel_per_module_decodes() {
        let encoded = encode::encode(Symbology::QrCode, "ROUND TRIP", &EncodeOptions::default())
            .expect("encode");
        let png = encode::to_png(&encoded, 1).expect("png");

        let decoded = decode_png(&png).expect("decode");
        assert!(decoded.success, "{:?}", decoded.error);
        assert_eq!(decoded.text, "ROUND TRIP");
        assert_eq!(decoded.symbology, Some(Symbology::QrCode));
        assert_eq!(decoded.format, "QR_CODE");
        assert!(!decoded.points.is_empty());
        assert!(decoded.bounds.is_some());
    }

    #[test]
    fn two_symbols_on_one_page_are_both_returned_with_distinct_boxes() {
        let qr = encode::encode(Symbology::QrCode, "ITEM-1", &EncodeOptions::default())
            .expect("encode qr");
        let qr = encode::to_png(&qr, 6).expect("png");

        let linear = encode::encode(Symbology::Code128, "ITEM-2", &EncodeOptions::default())
            .expect("encode code 128");
        let linear = encode::to_png(&linear, 3).expect("png");

        let (qr_width, qr_height, qr_pixels) = luma(&qr);
        let (bar_width, bar_height, bar_pixels) = luma(&linear);

        // A white page wide enough for both: the QR on the left, the Code 128 on the right.
        const MARGIN: u32 = 40;
        let width = MARGIN * 3 + qr_width + bar_width;
        let height = MARGIN * 2 + qr_height.max(bar_height);
        let mut page = vec![255_u8; (width * height) as usize];

        let paste = |page: &mut Vec<u8>, pixels: &[u8], source_width: u32, x: u32, y: u32| {
            let rows = pixels.len() as u32 / source_width;
            for row in 0..rows {
                let start = ((y + row) * width + x) as usize;
                let source = (row * source_width) as usize;
                page[start..start + source_width as usize]
                    .copy_from_slice(&pixels[source..source + source_width as usize]);
            }
        };

        paste(&mut page, &qr_pixels, qr_width, MARGIN, MARGIN);
        paste(
            &mut page,
            &bar_pixels,
            bar_width,
            MARGIN * 2 + qr_width,
            MARGIN,
        );

        let rgb: Vec<u8> = page.iter().flat_map(|grey| [*grey, *grey, *grey]).collect();
        let mut png = Vec::new();
        image::codecs::png::PngEncoder::new(&mut png)
            .write_image(&rgb, width, height, image::ExtendedColorType::Rgb8)
            .expect("png");

        let decoded = decode_png_all(&png).expect("decode");

        let mut texts: Vec<&str> = decoded.symbols.iter().map(|s| s.text.as_str()).collect();
        texts.sort_unstable();
        assert_eq!(texts, ["ITEM-1", "ITEM-2"], "{:?}", decoded.error);

        let qr = decoded
            .symbols
            .iter()
            .find(|symbol| symbol.text == "ITEM-1")
            .expect("qr");
        let linear = decoded
            .symbols
            .iter()
            .find(|symbol| symbol.text == "ITEM-2")
            .expect("code 128");

        assert_eq!(qr.symbology, Some(Symbology::QrCode));
        assert_eq!(linear.symbology, Some(Symbology::Code128));

        // The QR is on the left, the linear symbol on the right: their boxes cannot overlap, which
        // is what a caller positioning an annotation relies on.
        let qr_box = qr.bounds.expect("qr box");
        let linear_box = linear.bounds.expect("linear box");
        assert!(
            qr_box.x + qr_box.width <= linear_box.x,
            "qr {qr_box:?} must end before the linear symbol starts at {linear_box:?}"
        );
    }

    #[test]
    fn an_image_one_pixel_past_the_ceiling_is_a_limit_error() {
        // 100 x 100 is exactly 10 000 pixels, and the ceiling is inclusive: this one decodes.
        let at_the_ceiling = DecodeBounds {
            maximum_pixels: 10_000,
            maximum_bytes: DEFAULT_MAXIMUM_BYTES,
        };
        let page = decode_png_all_within(&blank_png(100, 100, 255), at_the_ceiling)
            .expect("an image at the ceiling decodes");
        assert!(page.symbols.is_empty());

        let past_the_ceiling = DecodeBounds {
            maximum_pixels: 9_999,
            ..at_the_ceiling
        };
        let error = decode_png_all_within(&blank_png(100, 100, 255), past_the_ceiling).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_LIMIT);
        assert!(error.json.contains("10000 pixels"), "{}", error.json);
    }

    #[test]
    fn an_image_past_the_byte_ceiling_is_a_limit_error() {
        let bounds = DecodeBounds {
            maximum_pixels: DEFAULT_MAXIMUM_PIXELS,
            maximum_bytes: 64,
        };
        let error = decode_png_all_within(&blank_png(100, 100, 255), bounds).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_LIMIT);
        assert!(error.json.contains("64"), "{}", error.json);
    }
}
