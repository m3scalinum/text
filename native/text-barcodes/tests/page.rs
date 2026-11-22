//! The page path: every symbology this crate writes is read back by the multi-symbol decoder.

use sharp_text_barcodes::decode;
use sharp_text_barcodes::encode::{self, EncodeOptions};
use sharp_text_barcodes::symbology::Symbology;

/// The same payloads the round trip uses.
const CASES: &[(Symbology, &str)] = &[
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
    (Symbology::UpcA, "123456789012"),
    (Symbology::UpcE, "01234565"),
    (Symbology::Code93, "HELLO-93"),
    (Symbology::Telepen, "HELLO-TELEPEN"),
    (Symbology::Gs1_128, "0109501101530003\u{1d}10LOT-42"),
];

#[test]
fn every_symbology_survives_the_multi_symbol_decoder() {
    for (symbology, payload) in CASES {
        let encoded = encode::encode(*symbology, payload, &EncodeOptions::default())
            .unwrap_or_else(|error| {
                panic!("{} failed to encode: {}", symbology.name(), error.json)
            });
        let png = encode::to_png(&encoded, 4).expect("png");

        let page = decode::decode_png_all(&png).unwrap_or_else(|error| {
            panic!("{} failed to decode: {}", symbology.name(), error.json)
        });

        assert!(
            !page.symbols.is_empty(),
            "{} was not found on its own raster: {:?}",
            symbology.name(),
            page.error
        );
    }
}
