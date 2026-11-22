//! Printable output: a physical width, a resolution, and the size the symbol really prints at.
//!
//! A PNG at `scale` pixels per module says nothing about the paper: the same file is a two
//! centimetre sticker or an eight centimetre label depending on how the consumer scales it. This
//! module takes a print target instead — a resolution and either a physical width or a module size
//! — and answers with geometry the caller can put in a layout: the module in millimetres, the real
//! width and height, and the raster the printer wants.
//!
//! Two refusals matter as much as the arithmetic:
//!
//! - a module below the symbology family's normative floor is refused, because a symbol printed
//!   that small is a symbol no scanner reads (see [`Symbology::minimum_module_mm`]);
//! - a quiet zone below the symbology's own minimum is refused, and a reserved width too narrow to
//!   hold the symbol with its quiet zone is refused too, rather than cropping the zone the
//!   specification requires (see [`Symbology::minimum_quiet_zone`]).

use crate::encode::{self, EncodeOptions, Encoded, MAX_RASTER_PIXELS};
use crate::ffi::{BT_ERR_LIMIT, BtError};
use crate::style::{self, Style};
use crate::symbology::Symbology;

/// Millimetres in an inch; the only place the two units meet.
const MM_PER_INCH: f64 = 25.4;

/// The widest quiet zone `Print` accepts, in modules on each side.
///
/// A quiet zone is white space, and white space costs paper: past sixty-four modules on each side
/// the caller is sizing a blank page, not a barcode, and the mistake is almost always a unit mix-up
/// between millimetres and modules.
pub const MAX_QUIET_ZONE_MODULES: u32 = 64;

/// The lowest and highest output resolution `Print` accepts, in dots per inch.
pub const MIN_DPI: f64 = 72.0;
pub const MAX_DPI: f64 = 2400.0;

/// The largest integer scale `Print` uses, in printer pixels per module.
///
/// The raster is whole pixels: a module crossing part of a pixel would blur the bar edge, which is
/// exactly what [`encode::to_png`] avoids. Past sixty-four pixels per module the file is larger
/// than any printer resolves, so the mistake is named instead of written.
pub const MAX_MODULE_PIXELS: u32 = 64;

/// What the caller wants printed.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct PrintRequest {
    /// Output resolution, in dots per inch.
    pub dpi: f64,
    /// Width of the whole symbol, quiet zone included, in millimetres. Exclusive with
    /// [`PrintRequest::module_mm`].
    pub width_mm: Option<f64>,
    /// The X-dimension: the printed size of one module, in millimetres. Exclusive with
    /// [`PrintRequest::width_mm`].
    pub module_mm: Option<f64>,
    /// Width the caller's layout reserves, in modules. Optional: when given, the symbol and its
    /// quiet zone must fit, or the call is refused instead of cropping the zone.
    pub reserved_modules: Option<u32>,
    /// Quiet zone in modules on each side. `None` takes the symbology's normative minimum.
    pub quiet_zone: Option<u32>,
    /// Bar height in modules, one-dimensional symbols only.
    pub bar_height: Option<u32>,
    /// Error correction, as rxing spells it.
    pub error_correction: Option<String>,
    /// The module colour, the background and the reserved centre. The default is the historic
    /// black on white with nothing reserved.
    pub style: Style,
}

/// The physical size a symbol prints at, and the raster that carries it.
#[derive(Debug, Clone, PartialEq)]
pub struct PrintGeometry {
    /// Output resolution, in dots per inch.
    pub dpi: f64,
    /// The printed size of one module, in millimetres.
    pub module_mm: f64,
    /// Printer pixels per module: the integer scale the raster uses.
    pub module_pixels: u32,
    /// Quiet zone in modules on each side, as rendered.
    pub quiet_zone: u32,
    /// Width of the module matrix, quiet zone included.
    pub width_modules: u32,
    /// Height of the module matrix, quiet zone included.
    pub height_modules: u32,
    /// Width of the printed symbol, in millimetres.
    pub width_mm: f64,
    /// Height of the printed symbol, in millimetres.
    pub height_mm: f64,
    /// Width of the raster, in pixels.
    pub width_pixels: u32,
    /// Height of the raster, in pixels.
    pub height_pixels: u32,
}

impl PrintGeometry {
    /// The geometry as the JSON object the managed side reads, with keys in camelCase.
    pub fn json(&self) -> String {
        format!(
            "{{\"dpi\":{},\"moduleMm\":{},\"modulePixels\":{},\"quietZone\":{},\"widthModules\":{},\"heightModules\":{},\"widthMm\":{},\"heightMm\":{},\"widthPixels\":{},\"heightPixels\":{}}}",
            self.dpi,
            self.module_mm,
            self.module_pixels,
            self.quiet_zone,
            self.width_modules,
            self.height_modules,
            self.width_mm,
            self.height_mm,
            self.width_pixels,
            self.height_pixels
        )
    }
}

/// A rendered printable symbol: the raster plus the geometry that explains its size.
#[derive(Debug, Clone)]
pub struct PrintOutput {
    pub png: Vec<u8>,
    pub geometry: PrintGeometry,
}

/// Resolves the quiet zone a print request asks for, or explains why it cannot be printed.
///
/// `None` is the symbology's normative minimum: a print target is the one place this library
/// promises a compliant symbol, so it never falls back to the historic defaults of
/// [`Symbology::default_quiet_zone`], which some symbologies set wider than the norm and others
/// (Code 128 among them) narrower.
pub fn quiet_zone(symbology: Symbology, requested: Option<u32>) -> Result<u32, BtError> {
    let minimum = symbology.minimum_quiet_zone();
    let quiet_zone = requested.unwrap_or(minimum);

    if quiet_zone < minimum {
        return Err(BtError::invalid_argument(format!(
            "a quiet zone of {quiet_zone} modules is below the {minimum} module minimum for {} ({}); the print target refuses a symbol a reader cannot find rather than cropping the zone",
            symbology.name(),
            symbology.quiet_zone_source()
        )));
    }

    if quiet_zone > MAX_QUIET_ZONE_MODULES {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!(
                "a quiet zone of {quiet_zone} modules exceeds the {MAX_QUIET_ZONE_MODULES} module ceiling; a quiet zone is white space, not a layout"
            ),
        ));
    }

    Ok(quiet_zone)
}

/// Encodes `payload`, sizes it for print, and renders it.
pub fn render(
    symbology: Symbology,
    payload: &str,
    request: &PrintRequest,
) -> Result<PrintOutput, BtError> {
    let quiet_zone = quiet_zone(symbology, request.quiet_zone)?;

    let options = EncodeOptions {
        quiet_zone: Some(quiet_zone),
        bar_height: request.bar_height,
        error_correction: request.error_correction.clone(),
    };

    let encoded = encode::encode(symbology, payload, &options)?;
    let geometry = geometry(&encoded, quiet_zone, request)?;
    let level = style::level_name(request.error_correction.as_deref());
    let png = style::png(&encoded, geometry.module_pixels, &request.style, level)?;

    Ok(PrintOutput { png, geometry })
}

/// Computes the physical size of an already encoded symbol.
fn geometry(
    encoded: &Encoded,
    quiet_zone: u32,
    request: &PrintRequest,
) -> Result<PrintGeometry, BtError> {
    if !request.dpi.is_finite() || !(MIN_DPI..=MAX_DPI).contains(&request.dpi) {
        return Err(BtError::invalid_argument(format!(
            "the print resolution must be between {MIN_DPI} and {MAX_DPI} dots per inch, got {}",
            request.dpi
        )));
    }

    let width_modules = encoded.width();
    let height_modules = encoded.height();

    let module_pixels = match (request.width_mm, request.module_mm) {
        (Some(_), Some(_)) => {
            return Err(BtError::invalid_argument(
                "a print target takes a width in millimetres or a module size, not both",
            ));
        }
        (None, None) => {
            return Err(BtError::invalid_argument(
                "a print target needs a width in millimetres or a module size",
            ));
        }
        (Some(width_mm), None) => {
            if !width_mm.is_finite() || width_mm <= 0.0 {
                return Err(BtError::invalid_argument(format!(
                    "the print width must be a positive number of millimetres, got {width_mm}"
                )));
            }

            // Rounded down: the printed symbol must never exceed the width the caller asked for.
            (width_mm / MM_PER_INCH * request.dpi / f64::from(width_modules)).floor()
        }
        (None, Some(module_mm)) => {
            if !module_mm.is_finite() || module_mm <= 0.0 {
                return Err(BtError::invalid_argument(format!(
                    "the module size must be a positive number of millimetres, got {module_mm}"
                )));
            }

            (module_mm / MM_PER_INCH * request.dpi).round()
        }
    };

    if module_pixels < 1.0 {
        return Err(BtError::invalid_argument(format!(
            "at {} dots per inch, a symbol of {width_modules} modules cannot be printed at the requested width: it would take less than one pixel per module",
            request.dpi
        )));
    }

    if module_pixels > f64::from(MAX_MODULE_PIXELS) {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!(
                "{module_pixels} pixels per module exceed the {MAX_MODULE_PIXELS} pixel ceiling; lower the resolution or the module size"
            ),
        ));
    }

    let module_pixels = module_pixels as u32;
    let module_mm = f64::from(module_pixels) * MM_PER_INCH / request.dpi;

    let minimum_module_mm = encoded.symbology.minimum_module_mm();
    if module_mm < minimum_module_mm {
        return Err(BtError::invalid_argument(format!(
            "a module of {module_mm:.4} mm is below the {minimum_module_mm} mm minimum for {} ({}): at {} dots per inch the symbol needs {minimum_module_mm} mm per module, so widen the target",
            encoded.symbology.name(),
            encoded.symbology.module_minimum_source(),
            request.dpi
        )));
    }

    if let Some(reserved) = request.reserved_modules {
        if reserved < width_modules {
            return Err(BtError::invalid_argument(format!(
                "the layout reserves {reserved} modules of width, but {} needs {width_modules} with its {quiet_zone} module quiet zone on each side; the zone is not cropped to fit",
                encoded.symbology.name()
            )));
        }
    }

    let width_pixels = u64::from(width_modules) * u64::from(module_pixels);
    let height_pixels = u64::from(height_modules) * u64::from(module_pixels);
    let pixels = width_pixels * height_pixels;
    if pixels > MAX_RASTER_PIXELS {
        return Err(BtError::new(
            BT_ERR_LIMIT,
            format!("a raster of {pixels} pixels exceeds the {MAX_RASTER_PIXELS} pixel limit"),
        ));
    }

    Ok(PrintGeometry {
        dpi: request.dpi,
        module_mm,
        module_pixels,
        quiet_zone,
        width_modules,
        height_modules,
        width_mm: f64::from(width_modules) * module_mm,
        height_mm: f64::from(height_modules) * module_mm,
        width_pixels: width_pixels as u32,
        height_pixels: height_pixels as u32,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn target(width_mm: f64) -> PrintRequest {
        PrintRequest {
            dpi: 300.0,
            width_mm: Some(width_mm),
            ..PrintRequest::default()
        }
    }

    /// The width the renderer produces for a symbology, quiet zone excluded, in modules.
    fn symbol_modules(symbology: Symbology, payload: &str) -> u32 {
        let bare = encode::encode(
            symbology,
            payload,
            &EncodeOptions {
                quiet_zone: Some(0),
                ..EncodeOptions::default()
            },
        )
        .expect("encode without a quiet zone");
        bare.width()
    }

    #[test]
    fn a_print_target_uses_the_normative_quiet_zone_by_default() {
        let cases = [
            (Symbology::QrCode, "PRINT-QR"),
            (Symbology::DataMatrix, "PRINT-DM"),
            (Symbology::Aztec, "PRINT-AZTEC"),
            (Symbology::Pdf417, "PRINT-PDF417"),
            (Symbology::Code128, "PRINT-128"),
            (Symbology::Ean13, "5901234123457"),
            (Symbology::Ean8, "96385074"),
            (Symbology::UpcA, "123456789012"),
            (Symbology::Code39, "PRINT39"),
            (Symbology::Itf, "1234567890"),
            (Symbology::Codabar, "A123456789B"),
        ];

        for (symbology, payload) in cases {
            let output = render(
                symbology,
                payload,
                &PrintRequest {
                    dpi: 300.0,
                    module_mm: Some(0.5),
                    ..PrintRequest::default()
                },
            )
            .unwrap_or_else(|error| panic!("{} failed: {}", symbology.name(), error.json));

            assert_eq!(
                output.geometry.quiet_zone,
                symbology.minimum_quiet_zone(),
                "{}",
                symbology.name()
            );
            assert_eq!(
                output.geometry.width_modules,
                symbol_modules(symbology, payload) + 2 * symbology.minimum_quiet_zone(),
                "{}: the zone is added twice, once per side (bare {}, printed {}, zone {})",
                symbology.name(),
                symbol_modules(symbology, payload),
                output.geometry.width_modules,
                symbology.minimum_quiet_zone()
            );
        }
    }

    #[test]
    fn one_dimensional_symbols_carry_ten_modules_and_ean_thirteen_eleven() {
        assert_eq!(Symbology::Code128.minimum_quiet_zone(), 10);
        assert_eq!(Symbology::Gs1_128.minimum_quiet_zone(), 10);
        assert_eq!(Symbology::Ean13.minimum_quiet_zone(), 11);
        assert_eq!(Symbology::Ean8.minimum_quiet_zone(), 7);
        assert_eq!(Symbology::UpcA.minimum_quiet_zone(), 9);
        assert_eq!(Symbology::QrCode.minimum_quiet_zone(), 4);
        assert_eq!(Symbology::DataMatrix.minimum_quiet_zone(), 1);
        assert_eq!(Symbology::Pdf417.minimum_quiet_zone(), 2);
    }

    #[test]
    fn a_quiet_zone_below_the_symbology_minimum_is_refused() {
        let mut request = target(40.0);
        request.quiet_zone = Some(3);

        let error = render(Symbology::QrCode, "TIGHT", &request).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("ISO/IEC 18004"), "{}", error.json);

        let mut request = target(40.0);
        request.quiet_zone = Some(9);
        let error = render(Symbology::Code128, "TIGHT", &request).unwrap_err();
        assert!(error.json.contains("ISO/IEC 15417"), "{}", error.json);
    }

    #[test]
    fn a_quiet_zone_past_the_ceiling_is_a_limit_error() {
        let mut request = target(40.0);
        request.quiet_zone = Some(MAX_QUIET_ZONE_MODULES + 1);

        let error = render(Symbology::QrCode, "WIDE", &request).unwrap_err();
        assert_eq!(error.code, BT_ERR_LIMIT);
    }

    #[test]
    fn a_width_in_millimetres_rounds_the_module_down_so_the_symbol_fits() {
        let output = render(Symbology::QrCode, "HELLO", &target(30.0)).expect("print");
        let geometry = output.geometry;

        // 30 mm at 300 dpi is 354.33 pixels; 29 modules take 12 pixels each, 348 in total.
        assert_eq!(geometry.width_modules, 29);
        assert_eq!(geometry.module_pixels, 12);
        assert_eq!(geometry.width_pixels, 348);
        assert_eq!(geometry.height_pixels, 348);
        assert!(geometry.width_mm <= 30.0);
        assert!((geometry.module_mm - 1.016).abs() < 1e-9);
        assert!((geometry.width_mm - 29.464).abs() < 1e-9);
    }

    #[test]
    fn a_module_size_sets_the_width_and_the_height_in_millimetres() {
        let mut request = PrintRequest {
            dpi: 300.0,
            module_mm: Some(0.5),
            ..PrintRequest::default()
        };
        let output = render(Symbology::Code128, "ABC-123", &request).expect("print");

        // 0.5 mm at 300 dpi is 5.9 pixels, so the raster uses 6 and prints 0.508 mm.
        assert_eq!(output.geometry.module_pixels, 6);
        assert!((output.geometry.module_mm - 0.508).abs() < 1e-9);
        assert!(
            output.geometry.width_mm > 60.0 && output.geometry.width_mm < 80.0,
            "{} mm",
            output.geometry.width_mm
        );
        assert!(output.geometry.height_mm > 15.0);

        request.module_mm = Some(-1.0);
        assert!(render(Symbology::Code128, "ABC-123", &request).is_err());
    }

    #[test]
    fn a_module_below_the_family_minimum_is_refused() {
        let mut request = target(40.0);
        request.module_mm = Some(0.2);
        request.width_mm = None;

        let error = render(Symbology::Code128, "TOO-SMALL", &request).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("0.25"), "{}", error.json);

        let mut request = target(4.0);
        request.quiet_zone = Some(1);
        let error = render(Symbology::DataMatrix, "TOO-SMALL", &request).unwrap_err();
        assert!(error.json.contains("0.4"), "{}", error.json);
    }

    #[test]
    fn a_reserved_width_too_narrow_for_the_quiet_zone_is_refused() {
        let bare = symbol_modules(Symbology::QrCode, "RESERVED");
        let mut request = target(40.0);
        request.reserved_modules = Some(bare + 2 * Symbology::QrCode.minimum_quiet_zone() - 1);

        let error = render(Symbology::QrCode, "RESERVED", &request).unwrap_err();
        assert!(error.json.contains("quiet zone"), "{}", error.json);

        request.reserved_modules = Some(bare + 2 * Symbology::QrCode.minimum_quiet_zone());
        assert!(render(Symbology::QrCode, "RESERVED", &request).is_ok());
    }

    #[test]
    fn a_target_too_small_for_one_pixel_per_module_is_refused() {
        let error = render(Symbology::QrCode, "TINY", &target(1.0)).unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("pixel per module"), "{}", error.json);
    }

    #[test]
    fn a_module_size_past_the_pixel_ceiling_is_a_limit_error() {
        let request = PrintRequest {
            dpi: 300.0,
            module_mm: Some(6.0),
            ..PrintRequest::default()
        };

        let error = render(Symbology::QrCode, "HUGE", &request).unwrap_err();
        assert_eq!(error.code, BT_ERR_LIMIT);
    }

    #[test]
    fn both_a_width_and_a_module_size_are_refused() {
        let request = PrintRequest {
            dpi: 300.0,
            width_mm: Some(30.0),
            module_mm: Some(0.5),
            ..PrintRequest::default()
        };

        let error = render(Symbology::QrCode, "AMBIGUOUS", &request).unwrap_err();
        assert!(error.json.contains("not both"), "{}", error.json);
    }

    #[test]
    fn an_out_of_range_resolution_is_refused() {
        let request = PrintRequest {
            dpi: 6000.0,
            width_mm: Some(30.0),
            ..PrintRequest::default()
        };

        assert!(render(Symbology::QrCode, "DPI", &request).is_err());
    }

    #[test]
    fn the_png_carries_exactly_the_geometry_pixels() {
        let output = render(Symbology::QrCode, "PIXELS", &target(30.0)).expect("print");
        let decoded =
            image::load_from_memory_with_format(&output.png, image::ImageFormat::Png).expect("png");

        assert_eq!(decoded.width(), output.geometry.width_pixels);
        assert_eq!(decoded.height(), output.geometry.height_pixels);
    }

    #[test]
    fn a_styled_print_keeps_the_geometry_and_still_reads_back() {
        let payload = "https://m3scalinum.example/products/07?lot=42&serial=0001234567";
        let request = PrintRequest {
            dpi: 300.0,
            module_mm: Some(0.6),
            error_correction: Some("H".to_string()),
            style: Style {
                foreground: crate::style::Rgb {
                    r: 0x1f,
                    g: 0x3a,
                    b: 0x93,
                },
                background: crate::style::Rgb::WHITE,
                logo: Some(crate::style::Logo::solid(
                    0.15,
                    crate::style::Rgb {
                        r: 0x1f,
                        g: 0x3a,
                        b: 0x93,
                    },
                )),
            },
            ..PrintRequest::default()
        };

        let output = render(Symbology::QrCode, payload, &request).expect("styled print");

        let image =
            image::load_from_memory_with_format(&output.png, image::ImageFormat::Png).expect("png");
        assert_eq!(image.width(), output.geometry.width_pixels);
        assert_eq!(image.height(), output.geometry.height_pixels);

        let decoded = crate::decode::decode_png(&output.png).expect("decode");
        assert!(decoded.success, "{:?}", decoded.error);
        assert_eq!(decoded.text, payload);
    }
}
