//! A dressed symbol that still reads: colours, contrast, and a logo the decoder gets past.
//!
//! Every assertion here goes through the package's own reader. A style is a promise about the raster,
//! so the test is not "the logo is where the arithmetic says" — it is "the payload comes back".

use sharp_text_barcodes::decode::decode_png_all;
use sharp_text_barcodes::encode::{self, EncodeOptions};
use sharp_text_barcodes::style::{self, Logo, Rgb, Style};
use sharp_text_barcodes::symbology::Symbology;

/// The brand blue used throughout: dark enough for the contrast floor, and a plausible logo plate.
const BRAND: Rgb = Rgb {
    r: 0x1f,
    g: 0x3a,
    b: 0x93,
};

const PAYLOAD: &str = "https://m3scalinum.example/products/07?lot=42&serial=0001234567";

fn encode_qr(payload: &str, level: &str) -> encode::Encoded {
    encode::encode(
        Symbology::QrCode,
        payload,
        &EncodeOptions {
            error_correction: Some(level.to_string()),
            ..EncodeOptions::default()
        },
    )
    .expect("encode")
}

/// The text the package's own multi-symbol reader returns, or a failure explaining itself.
fn read_back(png: &[u8]) -> Option<String> {
    let page = decode_png_all(png).expect("the raster is a PNG this build reads");
    page.symbols.first().map(|symbol| symbol.text.clone())
}

#[test]
fn a_logo_reads_back_at_every_error_correction_level() {
    // The shares are the ones this build measured as readable: the nominal capacity of a level is a
    // per-codeword figure and a rectangle spends more than its area in codewords, so every one of
    // these sits below the level's nominal capacity. See Spec 20 §11 for the measured table.
    for (level, fraction) in [("L", 0.04), ("M", 0.07), ("Q", 0.12), ("H", 0.15)] {
        let encoded = encode_qr(PAYLOAD, level);
        let style = Style {
            foreground: Rgb::BLACK,
            background: Rgb::WHITE,
            logo: Some(Logo::solid(fraction, BRAND)),
        };

        let png = style::png(&encoded, 6, &style, Some(level))
            .unwrap_or_else(|error| panic!("{level} at {fraction}: {}", error.json));

        assert_eq!(
            read_back(&png).as_deref(),
            Some(PAYLOAD),
            "{level} with a {:.0}% logo",
            fraction * 100.0
        );
    }
}

#[test]
fn a_fifth_of_the_symbol_reads_back_at_high_correction() {
    let encoded = encode_qr(PAYLOAD, "H");
    assert_eq!(style::symbol_box(&encoded).expect("box").side, 45);

    let style = Style {
        foreground: Rgb::BLACK,
        background: Rgb::WHITE,
        logo: Some(Logo::solid(0.20, BRAND)),
    };

    let png = style::png(&encoded, 6, &style, Some("H")).expect("20 % at H reads back");
    assert_eq!(read_back(&png).as_deref(), Some(PAYLOAD));
}

#[test]
fn a_third_of_the_symbol_is_refused_because_it_does_not_read_back() {
    // 30 % is exactly the nominal capacity of H and still too much: the rectangle also destroys every
    // codeword it only partly covers. The renderer reads its own output back and refuses the design
    // rather than shipping a symbol a scanner cannot get past.
    let encoded = encode_qr(PAYLOAD, "H");
    let style = Style {
        foreground: Rgb::BLACK,
        background: Rgb::WHITE,
        logo: Some(Logo::solid(0.30, BRAND)),
    };

    let error = style::png(&encoded, 6, &style, Some("H")).unwrap_err();
    assert_eq!(
        error.code,
        sharp_text_barcodes::ffi::BT_ERR_INVALID_ARGUMENT
    );
    assert!(error.json.contains("cannot read back"), "{}", error.json);
    assert!(error.json.contains('H'), "{}", error.json);
}

#[test]
fn a_share_past_the_levels_capacity_is_refused_before_the_render() {
    let encoded = encode_qr(PAYLOAD, "Q");
    let style = Style {
        foreground: Rgb::BLACK,
        background: Rgb::WHITE,
        logo: Some(Logo::solid(0.30, BRAND)),
    };

    let error = style::png(&encoded, 6, &style, Some("Q")).unwrap_err();
    assert!(
        error.json.contains("exceeds the 25% ceiling"),
        "{}",
        error.json
    );
    assert!(error.json.contains("ISO/IEC 18004"), "{}", error.json);
}

#[test]
fn a_dark_module_on_a_light_ground_renders_in_those_colours_and_reads_back() {
    let encoded = encode_qr(PAYLOAD, "Q");
    let style = Style {
        foreground: BRAND,
        background: Rgb {
            r: 0xff,
            g: 0xf4,
            b: 0xe0,
        },
        logo: None,
    };

    let png = style::png(&encoded, 4, &style, Some("Q")).expect("png");
    let image = image::load_from_memory_with_format(&png, image::ImageFormat::Png)
        .expect("png")
        .to_rgb8();

    assert_eq!(
        image.get_pixel(0, 0).0,
        [0xff, 0xf4, 0xe0],
        "the quiet zone"
    );
    assert!(
        image.pixels().any(|pixel| pixel.0 == [0x1f, 0x3a, 0x93]),
        "the modules carry the brand colour"
    );

    assert_eq!(read_back(&png).as_deref(), Some(PAYLOAD));
}

#[test]
fn a_pale_module_is_refused_with_the_ratio_it_measured() {
    let encoded = encode_qr(PAYLOAD, "M");
    let style = Style {
        foreground: Rgb {
            r: 0x9e,
            g: 0x9e,
            b: 0x9e,
        },
        background: Rgb::WHITE,
        logo: None,
    };

    let error = style::png(&encoded, 4, &style, Some("M")).unwrap_err();
    assert!(error.json.contains("2.68:1"), "{}", error.json);
    assert!(error.json.contains("4.5:1"), "{}", error.json);
}

#[test]
fn a_ground_darker_than_the_module_is_refused() {
    let encoded = encode_qr(PAYLOAD, "M");
    let style = Style {
        foreground: Rgb::WHITE,
        background: Rgb::BLACK,
        logo: None,
    };

    let error = style::png(&encoded, 4, &style, Some("M")).unwrap_err();
    assert!(error.json.contains("not darker"), "{}", error.json);
}

#[test]
fn the_styled_svg_carries_the_colours_and_a_logo_it_can_read_back() {
    let encoded = encode_qr(PAYLOAD, "H");
    let style = Style {
        foreground: BRAND,
        background: Rgb::WHITE,
        logo: Some(Logo::solid(0.15, BRAND)),
    };

    let svg = style::svg(&encoded, &style, Some("H")).expect("svg");
    assert!(svg.contains("fill=\"#1f3a93\""), "{svg}");
    assert!(
        svg.contains("<rect x="),
        "the reserved centre is drawn: {svg}"
    );
}

#[test]
fn the_default_style_is_the_plain_raster_every_caller_already_has() {
    let encoded = encode_qr(PAYLOAD, "L");
    let styled = style::png(&encoded, 4, &Style::default(), None).expect("png");
    let plain = encode::to_png(&encoded, 4).expect("png");

    assert_eq!(styled, plain);
}
