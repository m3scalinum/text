//! The symbologies this library writes and reads, and the defaults each one needs.
//!
//! The names are the ABI names: they cross the boundary as UTF-8, and they are also the stable
//! spelling the managed enum parses, so a name here is a compatibility promise.
//!
//! Not every name is writable: `rxing` 0.9.4 reads GS1 DataBar, MaxiCode and Micro QR Code but has
//! no writer for them. [`Symbology::can_encode`] is that fact, and the encode entry point refuses a
//! read-only symbology by name instead of failing somewhere inside the engine.

use rxing::BarcodeFormat;

use crate::ffi::BtError;

/// A symbology the engine reads — and writes, when it has a writer for it.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Symbology {
    QrCode,
    DataMatrix,
    Aztec,
    Pdf417,
    Code128,
    Code39,
    Ean13,
    Ean8,
    Itf,
    Codabar,
    /// Code 128 with FNC1 in lead position: the GS1-128 of the specification.
    Gs1_128,
    UpcA,
    UpcE,
    Code93,
    Telepen,
    /// GS1 DataBar Omnidirectional (RSS-14). Read only.
    DataBarOmnidirectional,
    /// GS1 DataBar Expanded (RSS Expanded). Read only.
    DataBarExpanded,
    /// MaxiCode. Read only.
    MaxiCode,
    /// Micro QR Code, and its rectangular variant. Read only.
    MicroQr,
}

/// Every symbology, in the order the documentation lists them.
pub const ALL: [Symbology; 19] = [
    Symbology::QrCode,
    Symbology::DataMatrix,
    Symbology::Aztec,
    Symbology::Pdf417,
    Symbology::Code128,
    Symbology::Code39,
    Symbology::Ean13,
    Symbology::Ean8,
    Symbology::Itf,
    Symbology::Codabar,
    Symbology::Gs1_128,
    Symbology::UpcA,
    Symbology::UpcE,
    Symbology::Code93,
    Symbology::Telepen,
    Symbology::DataBarOmnidirectional,
    Symbology::DataBarExpanded,
    Symbology::MaxiCode,
    Symbology::MicroQr,
];

impl Symbology {
    /// The ABI name. Stable: the managed enum and the ABI both spell it this way.
    pub fn name(self) -> &'static str {
        match self {
            Symbology::QrCode => "QRCode",
            Symbology::DataMatrix => "DataMatrix",
            Symbology::Aztec => "Aztec",
            Symbology::Pdf417 => "PDF417",
            Symbology::Code128 => "Code128",
            Symbology::Code39 => "Code39",
            Symbology::Ean13 => "EAN13",
            Symbology::Ean8 => "EAN8",
            Symbology::Itf => "ITF",
            Symbology::Codabar => "Codabar",
            Symbology::Gs1_128 => "GS1-128",
            Symbology::UpcA => "UPCA",
            Symbology::UpcE => "UPCE",
            Symbology::Code93 => "Code93",
            Symbology::Telepen => "Telepen",
            Symbology::DataBarOmnidirectional => "DataBarOmnidirectional",
            Symbology::DataBarExpanded => "DataBarExpanded",
            Symbology::MaxiCode => "MaxiCode",
            Symbology::MicroQr => "MicroQR",
        }
    }

    /// The other spellings a caller may reasonably use.
    pub fn aliases(self) -> &'static [&'static str] {
        match self {
            Symbology::Gs1_128 => &["GS1128", "GS1 Code 128"],
            Symbology::UpcA => &["UPC-A"],
            Symbology::UpcE => &["UPC-E"],
            Symbology::DataBarOmnidirectional => &["DataBar", "RSS14", "RSS-14"],
            Symbology::DataBarExpanded => &["RSSExpanded", "RSS-EXPANDED", "DataBar-Expanded"],
            Symbology::MicroQr => &["MicroQRCode", "Micro-QR"],
            _ => &[],
        }
    }

    /// Parses an ABI name, case-insensitively, and rejects anything else by name.
    ///
    /// Hyphens, underscores and spaces carry no meaning in a symbology name, so `GS1-128`,
    /// `gs1_128` and `GS1 128` are one name — the spelling a caller copies out of a specification
    /// rarely matches the one a library chose.
    pub fn parse(name: &str) -> Result<Self, BtError> {
        let wanted = normalize(name);
        ALL.iter()
            .copied()
            .find(|candidate| {
                normalize(candidate.name()) == wanted
                    || candidate
                        .aliases()
                        .iter()
                        .any(|alias| normalize(alias) == wanted)
            })
            .ok_or_else(|| {
                BtError::unsupported(format!(
                    "unknown symbology '{name}'; supported: {}",
                    ALL.iter().map(|s| s.name()).collect::<Vec<_>>().join(", ")
                ))
            })
    }

    /// The rxing format this symbology is written and read as.
    pub fn format(self) -> BarcodeFormat {
        match self {
            Symbology::QrCode => BarcodeFormat::QR_CODE,
            Symbology::DataMatrix => BarcodeFormat::DATA_MATRIX,
            Symbology::Aztec => BarcodeFormat::AZTEC,
            Symbology::Pdf417 => BarcodeFormat::PDF_417,
            Symbology::Code128 | Symbology::Gs1_128 => BarcodeFormat::CODE_128,
            Symbology::Code39 => BarcodeFormat::CODE_39,
            Symbology::Ean13 => BarcodeFormat::EAN_13,
            Symbology::Ean8 => BarcodeFormat::EAN_8,
            Symbology::Itf => BarcodeFormat::ITF,
            Symbology::Codabar => BarcodeFormat::CODABAR,
            Symbology::UpcA => BarcodeFormat::UPC_A,
            Symbology::UpcE => BarcodeFormat::UPC_E,
            Symbology::Code93 => BarcodeFormat::CODE_93,
            Symbology::Telepen => BarcodeFormat::TELEPEN,
            Symbology::DataBarOmnidirectional => BarcodeFormat::RSS_14,
            Symbology::DataBarExpanded => BarcodeFormat::RSS_EXPANDED,
            Symbology::MaxiCode => BarcodeFormat::MAXICODE,
            Symbology::MicroQr => BarcodeFormat::MICRO_QR_CODE,
        }
    }

    /// The symbology a decoded format maps to, or `None` for a format this package does not
    /// expose (`DXFilmEdge`, a UPC/EAN extension…).
    ///
    /// `CODE_128` maps to plain Code 128: a GS1-128 is recognised from the `]C1` identifier the
    /// reader puts in front of its text, which the decoder resolves before it gets here.
    pub fn from_format(format: &BarcodeFormat) -> Option<Self> {
        if *format == BarcodeFormat::RECTANGULAR_MICRO_QR_CODE {
            return Some(Symbology::MicroQr);
        }

        ALL.iter()
            .copied()
            .find(|candidate| candidate.format() == *format)
    }

    /// Whether the engine has a writer for this symbology.
    ///
    /// `false` is not a gap in this package: it is what `rxing` 0.9.4 provides. GS1 DataBar,
    /// MaxiCode and Micro QR Code are read-only there, so they are read-only here.
    pub fn can_encode(self) -> bool {
        !matches!(
            self,
            Symbology::DataBarOmnidirectional
                | Symbology::DataBarExpanded
                | Symbology::MaxiCode
                | Symbology::MicroQr
        )
    }

    /// Quiet zone, in modules, on each side, when the caller does not ask for one.
    ///
    /// Two-dimensional symbols take the zone their specification requires (QR 4, DataMatrix 1,
    /// Aztec 2, PDF417 2). One-dimensional symbols take 5 on each side: rxing's writers want a
    /// total `sidesMargin` of at least ten narrow bars, and five each side is that, split evenly.
    pub fn default_quiet_zone(self) -> u32 {
        match self {
            Symbology::QrCode | Symbology::MicroQr => 4,
            Symbology::DataMatrix | Symbology::MaxiCode => 1,
            Symbology::Aztec | Symbology::Pdf417 => 2,
            // UPC-A and UPC-E are specified with nine modules of quiet zone on each side, and the
            // UPC-E reader genuinely needs them: at five the engine writes a symbol it cannot find
            // again.
            Symbology::UpcA | Symbology::UpcE => 9,
            _ => 5,
        }
    }

    /// The smallest quiet zone, in modules on each side, the symbology's own specification allows.
    ///
    /// This is a floor, not a default: [`Symbology::default_quiet_zone`] is what `Encode` uses when
    /// the caller stays silent, and it keeps the historic values so an existing document never
    /// changes size. A print target renders at this floor or wider — [`crate::print`] refuses
    /// anything narrower rather than printing a symbol a reader cannot find.
    ///
    /// EAN-13 and UPC are specified asymmetrically (EAN-13 has eleven modules on the left and seven
    /// on the right); the value here is the larger side, because the renderer pads symmetrically
    /// and a symbol wider than the norm is always readable, while one narrower is not.
    pub fn minimum_quiet_zone(self) -> u32 {
        match self {
            Symbology::QrCode => 4,
            Symbology::MicroQr => 2,
            Symbology::DataMatrix | Symbology::MaxiCode => 1,
            Symbology::Aztec => 1,
            Symbology::Pdf417 => 2,
            Symbology::Ean13 => 11,
            Symbology::Ean8 => 7,
            Symbology::UpcA | Symbology::UpcE => 9,
            Symbology::DataBarOmnidirectional | Symbology::DataBarExpanded => 1,
            Symbology::Code128
            | Symbology::Gs1_128
            | Symbology::Code39
            | Symbology::Code93
            | Symbology::Itf
            | Symbology::Codabar
            | Symbology::Telepen => 10,
        }
    }

    /// The specification the quiet-zone floor in [`Symbology::minimum_quiet_zone`] comes from.
    pub fn quiet_zone_source(self) -> &'static str {
        match self {
            Symbology::QrCode | Symbology::MicroQr => "ISO/IEC 18004:2015, quiet zone of 4 modules",
            Symbology::DataMatrix => {
                "ISO/IEC 16022:2006 and the GS1 DataMatrix Guideline, one module on each side"
            }
            Symbology::Aztec => {
                "ISO/IEC 24778:2024 requires no quiet zone; one module is kept as a print margin"
            }
            Symbology::Pdf417 => "ISO/IEC 15438:2015, 2X on each side",
            Symbology::Ean13 => {
                "ISO/IEC 15420:2009, 11 modules left and 7 right; the larger side is padded both ways"
            }
            Symbology::Ean8 => "ISO/IEC 15420:2009, 7 modules on each side",
            Symbology::UpcA | Symbology::UpcE => {
                "GS1 General Specifications, 9X on each side of a UPC symbol"
            }
            Symbology::Code128 | Symbology::Gs1_128 => "ISO/IEC 15417:2007, 10X on each side",
            Symbology::Code39 => "ISO/IEC 16388:2007, 10X on each side",
            Symbology::Code93 => "AIM USS-93, 10X on each side",
            Symbology::Itf => "ISO/IEC 16390:2007, 10X on each side",
            Symbology::Codabar => "AIM USS-Codabar, 10X on each side",
            Symbology::Telepen => "the Telepen symbology specification, 10X on each side",
            Symbology::DataBarOmnidirectional | Symbology::DataBarExpanded => {
                "GS1 General Specifications, 1X on each side"
            }
            Symbology::MaxiCode => "ISO/IEC 16023, one module on each side",
        }
    }

    /// The smallest module (X-dimension) this library prints, in millimetres.
    ///
    /// Two families, because the standards set two floors:
    ///
    /// - a one-dimensional symbol: 0.25 mm. ISO 15394:2009 §6.1.3 states that the narrow element
    ///   shall not be less than 0.25 mm for Code 39 and Code 128, and the GS1 General
    ///   Specifications set the same absolute floor for the GS1 members (their nominal 0.495 mm
    ///   applies to general distribution — `Print` accepts that as a *width*, the floor is what it
    ///   refuses to go under);
    /// - a two-dimensional symbol: 0.40 mm. The GS1 General Specifications symbol specification
    ///   table 1 gives 0.396 mm as the minimum X for a GS1 DataMatrix at retail point of sale;
    ///   0.40 mm is that value rounded up, and it is the stricter of the two families.
    ///
    /// A symbol printed below its family floor is a symbol a scanner will fail to read, so
    /// [`crate::print`] refuses the size instead of producing it.
    pub fn minimum_module_mm(self) -> f64 {
        if self.is_one_dimensional() { 0.25 } else { 0.4 }
    }

    /// The specification the module floor in [`Symbology::minimum_module_mm`] comes from.
    pub fn module_minimum_source(self) -> &'static str {
        if self.is_one_dimensional() {
            "ISO 15394:2009 §6.1.3 and the GS1 General Specifications, X >= 0.25 mm"
        } else {
            "GS1 General Specifications symbol specification table 1 (2D at POS, X >= 0.396 mm), rounded up to 0.40 mm"
        }
    }

    /// Whether the symbol is one-dimensional, which is what decides the bar height.
    pub fn is_one_dimensional(self) -> bool {
        matches!(
            self,
            Symbology::Code128
                | Symbology::Code39
                | Symbology::Ean13
                | Symbology::Ean8
                | Symbology::Itf
                | Symbology::Codabar
                | Symbology::Gs1_128
                | Symbology::UpcA
                | Symbology::UpcE
                | Symbology::Code93
                | Symbology::Telepen
                | Symbology::DataBarOmnidirectional
                | Symbology::DataBarExpanded
        )
    }

    /// The narrow bar height, in modules, a one-dimensional symbol is rendered at, unless the
    /// caller asks for another. Forty modules is a 4 cm bar at 1 mm modules: tall enough for a
    /// handheld scanner, short enough to stay a strip.
    pub const fn default_bar_height(self) -> u32 {
        40
    }
}

/// Case folding plus the separators that carry no meaning in a symbology name.
fn normalize(name: &str) -> String {
    name.chars()
        .filter(|c| !matches!(c, '-' | '_' | ' '))
        .collect::<String>()
        .to_ascii_uppercase()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_name_round_trips() {
        for symbology in ALL {
            assert_eq!(Symbology::parse(symbology.name()), Ok(symbology));
        }
    }

    #[test]
    fn every_alias_round_trips() {
        for symbology in ALL {
            for alias in symbology.aliases() {
                assert_eq!(Symbology::parse(alias), Ok(symbology), "{alias}");
            }
        }
    }

    #[test]
    fn parsing_ignores_case_and_padding() {
        assert_eq!(Symbology::parse(" qrcode "), Ok(Symbology::QrCode));
        assert_eq!(Symbology::parse("EAN13"), Ok(Symbology::Ean13));
        assert_eq!(Symbology::parse("pdf417"), Ok(Symbology::Pdf417));
        assert_eq!(Symbology::parse("ean-13"), Ok(Symbology::Ean13));
        assert_eq!(Symbology::parse("gs1_128"), Ok(Symbology::Gs1_128));
        assert_eq!(
            Symbology::parse("DataBar"),
            Ok(Symbology::DataBarOmnidirectional)
        );
    }

    #[test]
    fn an_unknown_name_is_unsupported_and_lists_the_rest() {
        let error = Symbology::parse("Codabar92").unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_UNSUPPORTED);
        assert!(error.json.contains("Codabar92"));
        assert!(error.json.contains("Code128"));
    }

    #[test]
    fn formats_round_trip_through_the_decoder_mapping() {
        for symbology in ALL {
            // GS1-128 shares Code 128's format: the reader names it from the data, not from the
            // format, so the mapping cannot tell the two apart and is not expected to.
            if symbology == Symbology::Gs1_128 {
                continue;
            }

            assert_eq!(
                Symbology::from_format(&symbology.format()),
                Some(symbology),
                "{}",
                symbology.name()
            );
        }

        assert_eq!(
            Symbology::from_format(&BarcodeFormat::RECTANGULAR_MICRO_QR_CODE),
            Some(Symbology::MicroQr)
        );
        assert_eq!(Symbology::from_format(&BarcodeFormat::DXFilmEdge), None);
    }

    #[test]
    fn one_dimensional_set_matches_the_formats() {
        let one_dimensional = ALL.iter().filter(|s| s.is_one_dimensional()).count();
        assert_eq!(one_dimensional, 13);
    }

    #[test]
    fn aztec_rune_is_refused_because_the_engine_has_no_such_format() {
        // Aztec Rune — the seventy-seven-module symbol that carries one byte — has no
        // `BarcodeFormat` variant in rxing 0.9.4, so it has no writer and no reader there: the
        // crate's `barcode_format.rs` lists AZTEC and nothing rune-shaped, and `aztec/mod.rs`
        // exposes `AztecWriter` only. The name is therefore not a symbology this library knows,
        // and the refusal says so instead of writing a full Aztec symbol the caller did not ask
        // for (Spec 20 §12.1).
        for name in ["AztecRune", "Aztec Rune", "Rune"] {
            let error = Symbology::parse(name).unwrap_err();
            assert_eq!(error.code, crate::ffi::BT_ERR_UNSUPPORTED, "{name}");
            assert!(error.json.contains(name), "{}", error.json);
        }
    }

    #[test]
    fn the_read_only_symbologies_are_the_ones_rxing_cannot_write() {
        let read_only: Vec<&str> = ALL
            .iter()
            .filter(|s| !s.can_encode())
            .map(|s| s.name())
            .collect();

        assert_eq!(
            read_only,
            [
                "DataBarOmnidirectional",
                "DataBarExpanded",
                "MaxiCode",
                "MicroQR"
            ]
        );
    }
}
