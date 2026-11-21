//! The symbologies this library writes and reads, and the defaults each one needs.
//!
//! The names are the ABI names: they cross the boundary as UTF-8, and they are also the stable
//! spelling the managed enum parses, so a name here is a compatibility promise.

use rxing::BarcodeFormat;

use crate::ffi::BtError;

/// A symbology the engine can encode, and therefore decode.
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
}

/// Every symbology, in the order the documentation lists them.
pub const ALL: [Symbology; 10] = [
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
        }
    }

    /// Parses an ABI name, case-insensitively, and rejects anything else by name.
    pub fn parse(name: &str) -> Result<Self, BtError> {
        let wanted = name.trim();
        ALL.iter()
            .copied()
            .find(|candidate| candidate.name().eq_ignore_ascii_case(wanted))
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
            Symbology::Code128 => BarcodeFormat::CODE_128,
            Symbology::Code39 => BarcodeFormat::CODE_39,
            Symbology::Ean13 => BarcodeFormat::EAN_13,
            Symbology::Ean8 => BarcodeFormat::EAN_8,
            Symbology::Itf => BarcodeFormat::ITF,
            Symbology::Codabar => BarcodeFormat::CODABAR,
        }
    }

    /// The symbology a decoded format maps to, or `None` for a format this package does not
    /// expose (`UPC_A`, `CODE_93`, `TELEPEN`…). The decoder accepts every format rxing can read;
    /// naming the ones outside this set is honest about what the managed enum means.
    pub fn from_format(format: &BarcodeFormat) -> Option<Self> {
        ALL.iter()
            .copied()
            .find(|candidate| candidate.format() == *format)
    }

    /// Quiet zone, in modules, on each side, when the caller does not ask for one.
    ///
    /// Two-dimensional symbols take the zone their specification requires (QR 4, DataMatrix 1,
    /// Aztec 2, PDF417 2). One-dimensional symbols take 5 on each side: rxing's writers want a
    /// total `sidesMargin` of at least ten narrow bars, and five each side is that, split evenly.
    pub fn default_quiet_zone(self) -> u32 {
        match self {
            Symbology::QrCode => 4,
            Symbology::DataMatrix => 1,
            Symbology::Aztec => 2,
            Symbology::Pdf417 => 2,
            _ => 5,
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
        )
    }

    /// The narrow bar height, in modules, a one-dimensional symbol is rendered at, unless the
    /// caller asks for another. Forty modules is a 4 cm bar at 1 mm modules: tall enough for a
    /// handheld scanner, short enough to stay a strip.
    pub const fn default_bar_height(self) -> u32 {
        40
    }
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
    fn parsing_ignores_case_and_padding() {
        assert_eq!(Symbology::parse(" qrcode "), Ok(Symbology::QrCode));
        assert_eq!(Symbology::parse("EAN13"), Ok(Symbology::Ean13));
        assert_eq!(Symbology::parse("pdf417"), Ok(Symbology::Pdf417));
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
            assert_eq!(Symbology::from_format(&symbology.format()), Some(symbology));
        }
        assert_eq!(Symbology::from_format(&BarcodeFormat::UPC_A), None);
    }

    #[test]
    fn one_dimensional_set_matches_the_formats() {
        let one_dimensional = ALL.iter().filter(|s| s.is_one_dimensional()).count();
        assert_eq!(one_dimensional, 6);
    }
}
