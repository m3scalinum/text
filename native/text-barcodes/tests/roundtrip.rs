//! The round trip that matters: encode, rasterise, decode, same payload.
//!
//! These are integration tests against the real engine, not mocks: every symbol is written by
//! rxing, rendered by this crate and read back by rxing. A symbology that cannot survive its own
//! raster is a symbology this package must not claim.

use sharp_text_barcodes::decode::decode_png;
use sharp_text_barcodes::encode::{self, EncodeOptions};
use sharp_text_barcodes::symbology::Symbology;

/// The payloads each symbology takes. One-dimensional numeric symbologies are fixed-length or
/// even-length by their specification, so the payload is chosen to fit.
pub const CASES: [(Symbology, &str); 10] = [
    (Symbology::QrCode, "HELLO WORLD"),
    (Symbology::DataMatrix, "HELLO-DM"),
    (Symbology::Aztec, "HELLO-AZTEC"),
    (Symbology::Pdf417, "HELLO-PDF417"),
    (Symbology::Code128, "HELLO-128"),
    (Symbology::Code39, "HELLO-39"),
    (Symbology::Ean13, "5901234123457"),
    (Symbology::Ean8, "96385074"),
    (Symbology::Itf, "1234567890"),
    (Symbology::Codabar, "A123456789B"),
];

/// What a decoder returns for a payload.
///
/// Codabar's start and stop characters are guards, not data: rxing strips them, as every reader
/// does, so an `A123456789B` symbol reads back as `123456789`. Everything else comes back as
/// written.
fn expected_text(symbology: Symbology, payload: &str) -> &str {
    match symbology {
        Symbology::Codabar => &payload[1..payload.len() - 1],
        _ => payload,
    }
}

fn round_trip(symbology: Symbology, payload: &str, scale: u32) -> String {
    let encoded = encode::encode(symbology, payload, &EncodeOptions::default())
        .unwrap_or_else(|error| panic!("{} failed to encode: {}", symbology.name(), error.json));

    let png = encode::to_png(&encoded, scale)
        .unwrap_or_else(|error| panic!("{} failed to raster: {}", symbology.name(), error.json));

    let decoded = decode_png(&png)
        .unwrap_or_else(|error| panic!("{} failed to decode: {}", symbology.name(), error.json));

    assert!(
        decoded.success,
        "{} did not decode at scale {scale}: {:?}",
        symbology.name(),
        decoded.error
    );
    assert_eq!(
        decoded.symbology,
        Some(symbology),
        "{} decoded as {}",
        symbology.name(),
        decoded.format
    );

    decoded.text
}

#[test]
fn every_symbology_survives_its_own_raster_at_two_pixels_per_module() {
    // Two pixels per module, not one: rxing's Aztec detector needs the bullseye to be at least two
    // pixels across, so a one-pixel Aztec is not readable by the engine that wrote it. See
    // `an_aztec_symbol_needs_two_pixels_per_module` below, which pins that boundary instead of
    // hiding it.
    for (symbology, payload) in CASES {
        assert_eq!(
            round_trip(symbology, payload, 2),
            expected_text(symbology, payload),
            "{}",
            symbology.name()
        );
    }
}

#[test]
fn every_symbology_survives_its_own_raster_at_four_pixels_per_module() {
    for (symbology, payload) in CASES {
        assert_eq!(
            round_trip(symbology, payload, 4),
            expected_text(symbology, payload),
            "{}",
            symbology.name()
        );
    }
}

#[test]
fn an_aztec_symbol_needs_two_pixels_per_module() {
    // The one raster size this package does not claim: one pixel per module is below what rxing's
    // Aztec detector can find. The default scale is four, so a caller has to ask for one.
    let encoded =
        encode::encode(Symbology::Aztec, "HELLO-AZTEC", &EncodeOptions::default()).expect("encode");

    let one = decode_png(&encode::to_png(&encoded, 1).expect("png")).expect("decode");
    assert!(
        !one.success,
        "an Aztec at one pixel per module is not readable"
    );

    let two = decode_png(&encode::to_png(&encoded, 2).expect("png")).expect("decode");
    assert!(two.success, "{:?}", two.error);
    assert_eq!(two.text, "HELLO-AZTEC");
}

#[test]
fn a_quiet_zone_of_two_modules_still_decodes() {
    let options = EncodeOptions {
        quiet_zone: Some(2),
        ..EncodeOptions::default()
    };

    let encoded = encode::encode(Symbology::QrCode, "QUIET", &options).expect("encode");
    let png = encode::to_png(&encoded, 3).expect("png");
    let decoded = decode_png(&png).expect("decode");

    assert!(decoded.success, "{:?}", decoded.error);
    assert_eq!(decoded.text, "QUIET");
}

#[test]
fn the_svg_names_every_module_exactly_once() {
    // The vector rendering is the one Typst consumes: it must describe the same matrix, at one
    // unit per module, and never rescale.
    for (symbology, payload) in CASES {
        let encoded = encode::encode(symbology, payload, &EncodeOptions::default())
            .unwrap_or_else(|error| panic!("{}: {}", symbology.name(), error.json));
        let svg = encode::to_svg(&encoded);

        assert!(svg.starts_with("<svg"), "{}", symbology.name());
        assert!(svg.ends_with("</svg>"), "{}", symbology.name());
        assert_eq!(svg.matches("<path").count(), 1, "{}", symbology.name());
        assert!(
            svg.contains(&format!(
                "viewBox=\"0 0 {} {}\"",
                encoded.width(),
                encoded.height()
            )),
            "{}",
            symbology.name()
        );

        // Every coordinate in the path is inside the viewBox: a module written at x >= width would
        // be clipped when the consumer scales the image.
        for instruction in svg
            .split('d')
            .nth(1)
            .expect("the path attribute")
            .split('M')
            .skip(1)
        {
            let mut parts = instruction.split(' ');
            let x: u32 = parts.next().expect("x").parse().expect("x is a number");
            let rest = parts.next().expect("y");
            let y: u32 = rest
                .split('h')
                .next()
                .expect("y")
                .parse()
                .expect("y is a number");
            assert!(x < encoded.width(), "{}: x={x}", symbology.name());
            assert!(y < encoded.height(), "{}: y={y}", symbology.name());
        }
    }
}

#[test]
fn a_factur_x_style_header_round_trips_through_a_qr_code() {
    // An invoice header is what a QR code on an invoice carries: key=value lines, one per
    // business term, ASCII only and dense enough to need more than a version 1 symbol.
    let payload = [
        "BT-1=FA-2026-000123",
        "BT-2=380",
        "BT-3=EUR",
        "BT-5=1250.00",
        "BT-9=2026-11-21",
        "BT-10=FR12345678901",
        "BT-13=FR99123456789",
        "BT-31=FR12345678901",
        "BT-49=seller@example.fr",
    ]
    .join("\n");

    let encoded = encode::encode(Symbology::QrCode, &payload, &EncodeOptions::default())
        .expect("an invoice header fits a QR code");
    assert!(encoded.width() > 29, "the symbol grew past version 1");

    let png = encode::to_png(&encoded, 4).expect("png");
    let decoded = decode_png(&png).expect("decode");

    assert!(decoded.success, "{:?}", decoded.error);
    assert_eq!(decoded.text, payload);
    assert_eq!(decoded.symbology, Some(Symbology::QrCode));
}

#[test]
fn a_png_without_a_barcode_is_a_failed_decode() {
    let pixels = vec![255_u8; 300 * 120];
    let png = {
        use image::ImageEncoder as _;
        let mut bytes = Vec::new();
        image::codecs::png::PngEncoder::new(&mut bytes)
            .write_image(&pixels, 300, 120, image::ExtendedColorType::L8)
            .expect("png");
        bytes
    };

    let decoded = decode_png(&png).expect("a blank page is not an error");
    assert!(!decoded.success);
    assert!(decoded.text.is_empty());
    assert!(decoded.symbology.is_none());
    assert!(decoded.points.is_empty());
    assert!(decoded.error.is_some());
}

#[test]
fn a_twelve_digit_ean13_is_refused_by_name() {
    let error = encode::encode(Symbology::Ean13, "590123412345", &EncodeOptions::default())
        .expect_err("twelve digits is not an EAN-13");

    assert_eq!(error.code, sharp_text_barcodes::ffi::BT_ERR_ENCODE);
    assert!(error.json.contains("EAN13"));
    assert!(error.json.contains("13"));
}
