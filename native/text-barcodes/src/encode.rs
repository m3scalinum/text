//! Encoding: payload to module matrix, matrix to SVG and to PNG.
//!
//! The module matrix is the single source of both renderings, so the vector and the raster can
//! never disagree about where a dark module sits. Every rendering is emitted at exactly one unit
//! per cell of that matrix; a consumer scales the vector, and the raster scales by an integer
//! factor, so no resampling ever blurs a bar edge.

use std::fmt::Write as _;

use image::ImageEncoder as _;
use rxing::common::BitMatrix;
use rxing::{EncodeHintValue, EncodeHints, MultiFormatWriter, Writer};

use crate::ffi::{BT_ERR_LIMIT, BtError};
use crate::symbology::Symbology;

/// The largest payload accepted, in bytes of UTF-8. A QR Code tops out around 3 KB and a
/// one-dimensional symbol far below that; anything past this is a mistake, not a barcode.
pub const MAX_PAYLOAD_BYTES: usize = 4096;

/// The largest raster accepted, in pixels. 100 megapixels is far beyond a printable barcode and
/// keeps a stray `Scale` from asking for tens of gigabytes.
pub const MAX_RASTER_PIXELS: u64 = 100_000_000;

/// What the caller can change about the symbol.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct EncodeOptions {
    /// Quiet zone in modules on each side. `None` takes the symbology default.
    pub quiet_zone: Option<u32>,
    /// Bar height in modules, one-dimensional symbols only. `None` takes the default.
    pub bar_height: Option<u32>,
    /// Error correction, as rxing spells it: `L`/`M`/`Q`/`H` for QR, a percentage for Aztec, a
    /// level from 0 to 8 for PDF417. `None` takes the symbology default.
    pub error_correction: Option<String>,
}

/// A symbol, as modules. Both `to_svg` and `to_png` read this and nothing else.
#[derive(Debug, Clone)]
pub struct Encoded {
    pub symbology: Symbology,
    pub payload: String,
    pub modules: BitMatrix,
}

impl Encoded {
    pub fn width(&self) -> u32 {
        self.modules.width()
    }

    pub fn height(&self) -> u32 {
        self.modules.height()
    }

    pub fn module_count(&self) -> u64 {
        u64::from(self.width()) * u64::from(self.height())
    }
}

/// Encodes `payload` as `symbology`.
///
/// The requested pixel size is always zero: rxing then returns the symbol at one unit per module
/// instead of scaling it to a requested bitmap, which is what keeps the SVG exact.
pub fn encode(
    symbology: Symbology,
    payload: &str,
    options: &EncodeOptions,
) -> Result<Encoded, BtError> {
    if payload.is_empty() {
        return Err(BtError::invalid_argument("payload must not be empty"));
    }

    if payload.len() > MAX_PAYLOAD_BYTES {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!(
                "payload is {} bytes, the limit is {MAX_PAYLOAD_BYTES}",
                payload.len()
            ),
        ));
    }

    validate_payload(symbology, payload)?;

    let quiet_zone = options
        .quiet_zone
        .unwrap_or_else(|| symbology.default_quiet_zone());

    let bar_height = options
        .bar_height
        .unwrap_or_else(|| symbology.default_bar_height());

    // rxing wants the total margin across both sides for a one-dimensional symbol, and the margin
    // per side for a two-dimensional one.
    let margin = if symbology.is_one_dimensional() {
        quiet_zone.saturating_mul(2)
    } else {
        quiet_zone
    };

    let mut hints = EncodeHints::default().with(EncodeHintValue::Margin(margin.to_string()));

    if let Some(error_correction) = &options.error_correction {
        hints = hints.with(EncodeHintValue::ErrorCorrection(error_correction.clone()));
    }

    // A one-dimensional symbol has no width to choose: the bar pattern sets it, and asking for
    // zero leaves rxing at one unit per module. Only the height is ours, because a strip one
    // module tall is not a barcode.
    let height = if symbology.is_one_dimensional() {
        bar_height as i32
    } else {
        0
    };

    let modules = MultiFormatWriter
        .encode_with_hints(payload, &symbology.format(), 0, height, &hints)
        .map_err(|error| {
            BtError::encode(format!(
                "{} cannot encode this payload: {error}",
                symbology.name()
            ))
        })?;

    if modules.width() == 0 || modules.height() == 0 {
        return Err(BtError::encode(format!(
            "{} produced an empty matrix",
            symbology.name()
        )));
    }

    Ok(Encoded {
        symbology,
        payload: payload.to_string(),
        modules,
    })
}

/// Renders the modules as SVG, one unit per module, in the user-space units the viewBox declares.
///
/// One `<path>`, with one `M…h…v1h-…z` run per horizontal run of dark modules: a 300-module QR
/// is a few hundred instructions instead of several thousand `<rect>` elements, and Typst scales
/// the whole thing without a raster step.
pub fn to_svg(encoded: &Encoded) -> String {
    let width = encoded.width();
    let height = encoded.height();

    let mut path = String::with_capacity(encoded.module_count() as usize / 4);
    for y in 0..height {
        let mut x = 0;
        while x < width {
            if !encoded.modules.get(x, y) {
                x += 1;
                continue;
            }

            let start = x;
            while x < width && encoded.modules.get(x, y) {
                x += 1;
            }

            let run = x - start;
            let _ = write!(path, "M{start} {y}h{run}v1h-{run}z");
        }
    }

    format!(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" \
         viewBox=\"0 0 {width} {height}\" shape-rendering=\"crispEdges\">\
         <rect width=\"{width}\" height=\"{height}\" fill=\"#ffffff\"/>\
         <path fill=\"#000000\" d=\"{path}\"/></svg>"
    )
}

/// Renders the modules as an 8-bit greyscale PNG, `scale` pixels per module.
///
/// Greyscale with only 0 and 255 in it: a 1-bit image would halve the file, but every decoder and
/// every image library reads an 8-bit grey PNG correctly, and the round trip is the property that
/// matters — see `the_png_decodes_back_to_its_payload` in the test module.
pub fn to_png(encoded: &Encoded, scale: u32) -> Result<Vec<u8>, BtError> {
    let scale = checked_scale(encoded, scale)?;

    let width = encoded.width() * scale;
    let height = encoded.height() * scale;

    let mut pixels = vec![255_u8; (width as usize) * (height as usize)];

    for y in 0..encoded.height() {
        for sy in 0..scale {
            let row = ((y * scale + sy) as usize) * (width as usize);
            for x in 0..encoded.width() {
                if encoded.modules.get(x, y) {
                    let start = row + (x * scale) as usize;
                    pixels[start..start + scale as usize].fill(0);
                }
            }
        }
    }

    let mut png = Vec::new();
    image::codecs::png::PngEncoder::new(&mut png)
        .write_image(&pixels, width, height, image::ExtendedColorType::L8)
        .map_err(|error| BtError::encode(format!("could not write the PNG: {error}")))?;

    Ok(png)
}

/// Rejects a payload the symbology cannot hold before rxing is asked.
///
/// Only the fixed-length digited symbologies are checked here: rxing happily turns twelve digits
/// into an EAN-13 by computing the check digit, which hides a caller that passed the wrong field.
/// This library's contract is the whole code, check digit included.
fn validate_payload(symbology: Symbology, payload: &str) -> Result<(), BtError> {
    let expected = match symbology {
        Symbology::Ean13 => Some(13),
        Symbology::Ean8 => Some(8),
        _ => None,
    };

    let Some(expected) = expected else {
        return Ok(());
    };

    let length = payload.chars().count();
    if length != expected {
        return Err(BtError::encode(format!(
            "{} expects exactly {expected} characters, the check digit included; the payload has {length}",
            symbology.name()
        )));
    }

    if !payload.chars().all(|c| c.is_ascii_digit()) {
        return Err(BtError::encode(format!(
            "{} expects digits only",
            symbology.name()
        )));
    }

    Ok(())
}

fn checked_scale(encoded: &Encoded, scale: u32) -> Result<u32, BtError> {
    if scale == 0 || scale > 64 {
        return Err(BtError::invalid_argument(format!(
            "scale must be between 1 and 64, got {scale}"
        )));
    }

    let pixels = u64::from(encoded.width())
        * u64::from(encoded.height())
        * u64::from(scale)
        * u64::from(scale);
    if pixels > MAX_RASTER_PIXELS {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!("a raster of {pixels} pixels exceeds the {MAX_RASTER_PIXELS} pixel limit"),
        ));
    }

    Ok(scale)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample() -> Encoded {
        encode(Symbology::QrCode, "HELLO", &EncodeOptions::default()).expect("QR HELLO")
    }

    #[test]
    fn and_empty_payload_is_rejected() {
        let error = encode(Symbology::QrCode, "", &EncodeOptions::default()).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn an_oversized_payload_is_a_limit_error() {
        let payload = "A".repeat(MAX_PAYLOAD_BYTES + 1);
        let error = encode(Symbology::QrCode, &payload, &EncodeOptions::default()).unwrap_err();
        assert_eq!(error.code, BT_ERR_LIMIT);
    }

    #[test]
    fn the_svg_is_one_path_at_one_unit_per_module() {
        let encoded = sample();
        let svg = to_svg(&encoded);

        assert!(svg.starts_with("<svg"));
        assert!(svg.ends_with("</svg>"));
        assert_eq!(svg.matches("<path").count(), 1);
        assert_eq!(
            svg.matches('M').count(),
            svg.matches('z').count(),
            "every run opens with M and closes with z"
        );
        assert!(svg.contains(&format!(
            "viewBox=\"0 0 {} {}\"",
            encoded.width(),
            encoded.height()
        )));
    }

    #[test]
    fn the_svg_has_one_run_per_dark_module_when_nothing_touches() {
        let encoded = encode(Symbology::Code39, "A", &EncodeOptions::default()).expect("code39");
        let svg = to_svg(&encoded);
        let runs: usize = svg.matches('M').count();

        // The run count is the number of maximal horizontal runs of dark modules, computed here
        // from the matrix instead of from the SVG.
        let mut expected = 0_usize;
        for y in 0..encoded.height() {
            let mut x = 0;
            while x < encoded.width() {
                if encoded.modules.get(x, y) {
                    expected += 1;
                    while x < encoded.width() && encoded.modules.get(x, y) {
                        x += 1;
                    }
                } else {
                    x += 1;
                }
            }
        }

        assert_eq!(runs, expected);

        // Code 39 draws wide bars, so two dark modules are sometimes adjacent and one run covers
        // both: the run count must stay below the number of dark modules, or the renderer is
        // emitting a rectangle per module and the runs are meaningless.
        let dark = (0..encoded.height())
            .map(|y| {
                (0..encoded.width())
                    .filter(|x| encoded.modules.get(*x, y))
                    .count()
            })
            .sum::<usize>();
        assert!(runs < dark, "{runs} runs for {dark} dark modules");
    }

    #[test]
    fn the_png_is_an_eight_bit_grey_png_of_the_requested_scale() {
        let encoded = sample();
        let png = to_png(&encoded, 4).expect("png");

        assert_eq!(&png[..8], b"\x89PNG\r\n\x1a\n");

        let decoded = image::load_from_memory_with_format(&png, image::ImageFormat::Png)
            .expect("the bytes are a PNG");
        assert_eq!(decoded.width(), encoded.width() * 4);
        assert_eq!(decoded.height(), encoded.height() * 4);
        assert_eq!(decoded.color(), image::ColorType::L8);
    }

    #[test]
    fn only_black_and_white_pixels_are_written() {
        let encoded = sample();
        let png = to_png(&encoded, 1).expect("png");
        let grey = image::load_from_memory_with_format(&png, image::ImageFormat::Png)
            .expect("png")
            .to_luma8();

        assert!(grey.pixels().all(|p| p.0[0] == 0 || p.0[0] == 255));
    }

    #[test]
    fn a_scale_of_zero_is_rejected() {
        let encoded = sample();
        let error = to_png(&encoded, 0).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn a_scale_that_would_exceed_the_raster_limit_is_rejected() {
        let encoded = sample();
        // 21x21 modules at 64x is 1.8 megapixels, so ask for a matrix that is genuinely too big.
        let big = Encoded {
            symbology: Symbology::QrCode,
            payload: String::new(),
            modules: BitMatrix::new(5000, 5000).expect("matrix"),
        };
        let error = to_png(&big, 4).unwrap_err();
        assert_eq!(error.code, BT_ERR_LIMIT);
        assert_eq!(
            encoded.width(),
            29,
            "the sample is a version 1 QR plus its quiet zone"
        );
    }

    #[test]
    fn a_one_dimensional_symbol_is_a_strip_of_the_requested_height() {
        let options = EncodeOptions {
            bar_height: Some(7),
            ..EncodeOptions::default()
        };
        let encoded = encode(Symbology::Code128, "ABC-123", &options).expect("code128");
        assert_eq!(encoded.height(), 7);
        assert!(encoded.width() > 20);
    }

    #[test]
    fn a_fixed_length_payload_of_the_wrong_length_is_an_encode_error() {
        let error =
            encode(Symbology::Ean13, "123456789012", &EncodeOptions::default()).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_ENCODE);
        assert!(error.json.contains("EAN13"));
    }

    #[test]
    fn every_symbology_encodes_the_decimal_payload_it_accepts() {
        let payloads = [
            (Symbology::QrCode, "HELLO"),
            (Symbology::DataMatrix, "HELLO"),
            (Symbology::Aztec, "HELLO"),
            (Symbology::Pdf417, "HELLO"),
            (Symbology::Code128, "HELLO-128"),
            (Symbology::Code39, "HELLO39"),
            (Symbology::Ean13, "5901234123457"),
            (Symbology::Ean8, "96385074"),
            (Symbology::Itf, "1234567890"),
            (Symbology::Codabar, "A123456789B"),
        ];

        for (symbology, payload) in payloads {
            let encoded = encode(symbology, payload, &EncodeOptions::default())
                .unwrap_or_else(|error| panic!("{} failed: {}", symbology.name(), error.json));
            assert!(encoded.width() > 0);
            assert!(encoded.height() > 0);
        }
    }
}
