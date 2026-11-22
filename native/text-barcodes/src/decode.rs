//! Decoding: PNG bytes to text, symbology and corner points.
//!
//! A PNG that holds no barcode is not an error: it is a decode that failed, reported as
//! [`Decoded::success`] false with the reason. Only bytes that are not a readable PNG at all are
//! an error, because that is a caller mistake rather than a document without a symbol.

use rxing::BarcodeFormat;
use rxing::{DecodeHints, helpers};

use crate::ffi::BtError;
use crate::symbology::Symbology;

/// One point of the detected symbol, in pixels of the input image.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DecodePoint {
    pub x: f32,
    pub y: f32,
}

/// The outcome of one decode attempt. Never `Err` for "no barcode here".
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
            error: Some(reason.into()),
        }
    }
}

/// Decodes the first barcode found in a PNG.
///
/// `Ok(Decoded)` covers both outcomes a caller can expect from a real document; `Err` means the
/// bytes are not a PNG this build can read, which is a bug in the call rather than in the image.
pub fn decode_png(png: &[u8]) -> Result<Decoded, BtError> {
    if png.is_empty() {
        return Err(BtError::invalid_argument("the image is empty"));
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

    match helpers::detect_in_luma_slice_with_hints(
        grey.as_raw(),
        width,
        height,
        None,
        &mut decode_hints(),
    ) {
        Ok(result) => {
            let format = result.getBarcodeFormat();
            let (text, symbology) = resolve_text(result.getText(), format);
            Ok(Decoded {
                success: true,
                text,
                symbology,
                format: format_spelling(format),
                points: result
                    .getPoints()
                    .iter()
                    .map(|point| DecodePoint {
                        x: point.x,
                        y: point.y,
                    })
                    .collect(),
                error: None,
            })
        }
        Err(error) => Ok(Decoded::failure(format!(
            "no barcode was found in the image: {error}"
        ))),
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
    use image::ImageEncoder as _;

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
        let pixels = vec![255_u8; 200 * 200];
        let mut png = Vec::new();
        image::codecs::png::PngEncoder::new(&mut png)
            .write_image(&pixels, 200, 200, image::ExtendedColorType::L8)
            .expect("png");

        let decoded = decode_png(&png).expect("a blank image is a decode, not an error");
        assert!(!decoded.success);
        assert!(decoded.text.is_empty());
        assert!(decoded.error.is_some());
    }

    #[test]
    fn a_qr_rendered_at_one_pixel_per_module_decodes() {
        let encoded = encode::encode(
            crate::symbology::Symbology::QrCode,
            "ROUND TRIP",
            &EncodeOptions::default(),
        )
        .expect("encode");
        let png = encode::to_png(&encoded, 1).expect("png");

        let decoded = decode_png(&png).expect("decode");
        assert!(decoded.success, "{:?}", decoded.error);
        assert_eq!(decoded.text, "ROUND TRIP");
        assert_eq!(decoded.symbology, Some(crate::symbology::Symbology::QrCode));
        assert_eq!(decoded.format, "QR_CODE");
        assert!(!decoded.points.is_empty());
    }
}
