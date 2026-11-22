//! A dressed symbol: the module colour, the background, and the reserved centre a logo goes in.
//!
//! A QR Code that leaves the logistics world is a communication object: it carries a brand colour and
//! a mark in the middle. Both are appearance, not data, so nothing here changes the module matrix the
//! decoder reads — the matrix stays the encoder's truth and this module paints it. That separation is
//! what makes the two rules below possible to state and to test:
//!
//! - the modules must stay **darker** than the background, by a contrast ratio this library computes
//!   and refuses below a floor (WCAG 2.1 relative luminance, see [`MINIMUM_CONTRAST_RATIO`]);
//! - a logo may only destroy a share of the symbol that the error correction level can repair
//!   (see [`maximum_logo_fraction`]), and it is only offered where the symbol is two-dimensional.
//!
//! The reserved centre is a flat rectangle — the background colour, or a colour the caller names.
//! This library does not blend a photograph into the symbol: the caller composites its own artwork in
//! its layout, over the region this module reserves and clears.

use std::fmt::Write as _;

use image::ImageEncoder as _;

use crate::encode::{self, Encoded};
use crate::ffi::BtError;
use crate::symbology::Symbology;

/// The lowest contrast ratio, module against background, this library renders.
///
/// The ratio is the WCAG 2.1 relative-luminance one, `(L1 + 0.05) / (L2 + 0.05)` with the lighter of
/// the two luminances on top. The floor is 4.5:1, the AA requirement of WCAG 2.1
/// success criterion 1.4.5 *Contrast (Minimum)* — the one written for text, not the 3:1 of 1.4.11
/// *Non-text Contrast*: a module is a few tenths of a millimetre wide when printed and is read
/// through a camera lens, so the weaker graphic floor is not enough. A symbol below the text floor is
/// refused with the ratio it would have had, rather than rendered into something a scanner misses.
pub const MINIMUM_CONTRAST_RATIO: f64 = 4.5;

/// A colour, 8 bits per channel, sRGB.
///
/// sRGB is the space WCAG defines its coefficients in, so no conversion happens anywhere: the bytes
/// the caller passes are the bytes the raster holds, and the luminance the contrast check uses is
/// computed from them by the definition, not by an approximation of it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Rgb {
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

impl Rgb {
    /// Black, the default module colour.
    pub const BLACK: Self = Self { r: 0, g: 0, b: 0 };

    /// White, the default background colour.
    pub const WHITE: Self = Self {
        r: 255,
        g: 255,
        b: 255,
    };

    /// Reads a colour from the `0xRRGGBB` packing the ABI uses, or `None` past 24 bits.
    pub fn from_packed(value: u32) -> Option<Self> {
        (value <= 0x00FF_FFFF).then_some(Self {
            r: (value >> 16) as u8,
            g: (value >> 8) as u8,
            b: value as u8,
        })
    }

    /// The colour as `#rrggbb`, lowercase, as CSS and SVG spell it.
    pub fn to_hex(self) -> String {
        format!("#{:02x}{:02x}{:02x}", self.r, self.g, self.b)
    }

    /// The WCAG 2.1 relative luminance, in `[0, 1]`.
    ///
    /// WCAG 2.1 defines this over linearised sRGB: every channel above 0.03928 is raised to the
    /// 2.4 power after the offset, and the three are weighted 0.2126, 0.7152 and 0.0722 — the
    /// coefficients of ITU-R BT.709. The thresholds are the definition's own, not tuned here.
    pub fn relative_luminance(self) -> f64 {
        fn linear(channel: u8) -> f64 {
            let value = f64::from(channel) / 255.0;
            if value <= 0.039_28 {
                value / 12.92
            } else {
                ((value + 0.055) / 1.055).powf(2.4)
            }
        }

        0.2126 * linear(self.r) + 0.7152 * linear(self.g) + 0.0722 * linear(self.b)
    }
}

/// The contrast ratio between two colours, from 1:1 to 21:1.
///
/// Symmetric, as WCAG defines it: black on white and white on black both measure 21:1. Which one is
/// the module matters for readability but not for the ratio, so [`Style::check`] tests the order
/// separately.
pub fn contrast_ratio(a: Rgb, b: Rgb) -> f64 {
    let first = a.relative_luminance();
    let second = b.relative_luminance();
    let (lighter, darker) = if first >= second {
        (first, second)
    } else {
        (second, first)
    };

    (lighter + 0.05) / (darker + 0.05)
}

/// What fills the reserved centre.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LogoFill {
    /// The background colour: the region is cleared, so the caller's own mark lands on plain paper.
    Background,
    /// A flat colour. A brand plate, or a placeholder a layout draws over.
    Solid(Rgb),
}

/// A reserved centre: a square share of the symbol, cleared for the caller's mark.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Logo {
    /// The share of the symbol's **area** the square covers, in `(0, 1]`. 0.2 is a fifth of the
    /// symbol; the side of the square is the square root of that share.
    pub fraction: f64,
    /// What fills the region.
    pub fill: LogoFill,
}

impl Logo {
    /// A cleared square covering `fraction` of the symbol's area.
    pub fn cleared(fraction: f64) -> Self {
        Self {
            fraction,
            fill: LogoFill::Background,
        }
    }

    /// A square of one flat colour covering `fraction` of the symbol's area.
    pub fn solid(fraction: f64, colour: Rgb) -> Self {
        Self {
            fraction,
            fill: LogoFill::Solid(colour),
        }
    }
}

/// The largest share of the symbol's area a logo may *ask* for at an error correction level.
///
/// The four shares are the level's own codeword capacity — about 7 % at L, 15 % at M, 25 % at Q and
/// 30 % at H, the figures of ISO/IEC 18004 Table 9 and of every QR table since — used as a hard
/// ceiling on the request. They are a ceiling and not a budget: the capacity counts codewords, and a
/// rectangle also destroys every codeword it only partly covers, so the share that really reads back
/// is lower — see [`maximum_area_fraction`] and the read-back in [`png`]. Spec 20 §11 carries the
/// measured table.
///
/// `None` is the level a symbol is written with when the caller names none: L, the engine's default
/// for a QR Code.
pub fn maximum_logo_fraction(error_correction: Option<&str>) -> f64 {
    match error_correction
        .unwrap_or("L")
        .to_ascii_uppercase()
        .as_str()
    {
        "H" => 0.30,
        "Q" => 0.25,
        "M" => 0.15,
        _ => 0.07,
    }
}

/// The colours and the reserved centre a symbol is rendered with.
///
/// The default is the historic rendering, black on white with no logo, so a caller that names no
/// style gets exactly the bytes it got before this type existed — including the 8-bit greyscale
/// raster, which [`Style::is_default`] is what selects.
#[derive(Debug, Clone, PartialEq)]
pub struct Style {
    /// Module colour. Must be darker than [`Style::background`].
    pub foreground: Rgb,
    /// Background colour, quiet zone included.
    pub background: Rgb,
    /// The reserved centre, when the symbol carries one.
    pub logo: Option<Logo>,
}

impl Default for Style {
    fn default() -> Self {
        Self {
            foreground: Rgb::BLACK,
            background: Rgb::WHITE,
            logo: None,
        }
    }
}

impl Style {
    /// Whether this renders as the plain symbol: black, white, nothing reserved.
    pub fn is_default(&self) -> bool {
        self.foreground == Rgb::BLACK && self.background == Rgb::WHITE && self.logo.is_none()
    }

    /// Checks the colours, and the logo against the symbology and its error correction level.
    ///
    /// Every refusal names what was measured and what the rule is, because the caller's next move
    /// depends on the number: "contrast 2.1:1, the floor is 4.5:1" says darken the module or lighten
    /// the background, while "a logo covering 34 % exceeds the 25 % ceiling for Q" says enlarge the
    /// symbol or raise the level.
    pub fn check(
        &self,
        symbology: Symbology,
        error_correction: Option<&str>,
    ) -> Result<(), BtError> {
        self.check_contrast()?;

        let Some(logo) = self.logo else {
            return Ok(());
        };

        if symbology != Symbology::QrCode {
            return Err(BtError::unsupported(format!(
                "a reserved centre is offered on a QR Code only; {} is drawn as it is, so nothing can be cleared from it without destroying data no error correction level repairs",
                symbology.name()
            )));
        }

        if !logo.fraction.is_finite() || logo.fraction <= 0.0 || logo.fraction > 1.0 {
            return Err(BtError::invalid_argument(format!(
                "a logo covers a share of the symbol between 0 and 1, got {}",
                logo.fraction
            )));
        }

        let maximum = maximum_logo_fraction(error_correction);
        if logo.fraction > maximum {
            return Err(BtError::invalid_argument(format!(
                "a logo covering {:.1}% of the symbol exceeds the {:.0}% ceiling at error correction {}: the level repairs about that share and nothing more, so enlarge the symbol or raise the level; ISO/IEC 18004 Table 9 carries the capacity of each level",
                logo.fraction * 100.0,
                maximum * 100.0,
                error_correction.unwrap_or("L")
            )));
        }

        Ok(())
    }

    /// Checks that the module is darker than the background and that the pair clears the floor.
    pub fn check_contrast(&self) -> Result<(), BtError> {
        let ratio = contrast_ratio(self.foreground, self.background);

        if self.foreground.relative_luminance() >= self.background.relative_luminance() {
            return Err(BtError::invalid_argument(format!(
                "the module colour {} is not darker than the background {}: a QR Code is dark modules on a light ground (ISO/IEC 18004), and the pair measures {ratio:.2}:1 which says nothing about which is which",
                self.foreground.to_hex(),
                self.background.to_hex()
            )));
        }

        if ratio < MINIMUM_CONTRAST_RATIO {
            return Err(BtError::invalid_argument(format!(
                "the module colour {} on the background {} measures a contrast of {ratio:.2}:1, below the {MINIMUM_CONTRAST_RATIO:.1}:1 floor (WCAG 2.1 SC 1.4.5, Contrast (Minimum)): darken the module or lighten the background",
                self.foreground.to_hex(),
                self.background.to_hex()
            )));
        }

        Ok(())
    }
}

/// A reserved centre in module coordinates: where it starts and how wide it is.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LogoRect {
    pub x: u32,
    pub y: u32,
    pub side: u32,
}

/// The box the symbol itself occupies in the module matrix, quiet zone excluded.
///
/// Found from the dark modules rather than from a quiet zone the renderer was told about, because
/// the quiet zone is not stored in [`Encoded`]. For a QR Code the two are the same box: the finder
/// patterns sit in three corners of the symbol and their outer rings are dark, so the extremes of the
/// dark modules are the extremes of the symbol. `the_symbol_box_is_the_quiet_zone_free_square`
/// asserts exactly that, so the assumption is a test rather than a comment.
pub fn symbol_box(encoded: &Encoded) -> Option<LogoRect> {
    let width = encoded.width();
    let height = encoded.height();

    let (mut min_x, mut min_y, mut max_x, mut max_y) = (width, height, 0, 0);
    let mut found = false;

    for y in 0..height {
        for x in 0..width {
            if encoded.modules.get(x, y) {
                found = true;
                min_x = min_x.min(x);
                min_y = min_y.min(y);
                max_x = max_x.max(x);
                max_y = max_y.max(y);
            }
        }
    }

    if !found {
        return None;
    }

    // A square share of a non-square box would not be a square, so the shorter side sets it.
    let width_modules = max_x - min_x + 1;
    let height_modules = max_y - min_y + 1;
    let side = width_modules.min(height_modules);

    Some(LogoRect {
        x: min_x + (width_modules - side) / 2,
        y: min_y + (height_modules - side) / 2,
        side,
    })
}

/// The columns and rows a QR Code spends on its timing patterns, counted once.
///
/// Both timing patterns sit on line 6 of the symbol, one running along row 6 and one along column 6,
/// from the seventh module to the seventh from the end. Keeping them means the reserved centre can
/// start no earlier than module 7 and end no later than module `side - 8`, so it holds at most
/// `side - 14` modules. This is not decoration: a square big enough to swallow a timing line is a
/// symbol the detector cannot sample, which `a_logo_that_covers_a_timing_pattern_would_not_decode`
/// measures.
pub const TIMING_GUARD_MODULES: u32 = 14;

/// The largest share of the symbol's area a logo of this size can cover, geometry included.
///
/// Two ceilings apply and the lower wins: the level's recovery capacity (see
/// [`maximum_logo_fraction`]) and the room the symbol has between its timing patterns, which is
/// `(side - 14)^2 / side^2`. A 21-module symbol — version 1 — therefore holds about 11 %, and a
/// 30 % logo needs 31 modules of symbol, which is version 4.
pub fn maximum_area_fraction(side: u32, error_correction: Option<&str>) -> f64 {
    let level = maximum_logo_fraction(error_correction);
    let free = f64::from(side.saturating_sub(TIMING_GUARD_MODULES));
    let geometry = (free / f64::from(side.max(1))).powi(2);

    level.min(geometry)
}

/// Where the logo lands: centred on the symbol, inside both ceilings.
///
/// The side is rounded to whole modules so the region aligns with the module grid, and then floored
/// to the module count the ceiling allows, so rounding can never push the rendered logo past either
/// ceiling. A share the symbol cannot hold is refused rather than quietly shrunk: the caller asked
/// for a logo of a certain prominence, and a symbol that renders it at half that is a different
/// design, not the same one.
pub fn logo_rect(
    encoded: &Encoded,
    logo: Option<&Logo>,
    level: Option<&str>,
) -> Result<Option<LogoRect>, BtError> {
    let (Some(symbol), Some(logo)) = (symbol_box(encoded), logo) else {
        return Ok(None);
    };

    let level_cap = maximum_logo_fraction(level);
    let maximum = maximum_area_fraction(symbol.side, level);

    if logo.fraction > maximum {
        // Which of the two ceilings binds decides the advice: a bigger symbol, or a higher level.
        if logo.fraction > level_cap {
            return Err(BtError::invalid_argument(format!(
                "a logo covering {:.1}% of the symbol exceeds the {:.0}% ceiling at error correction {}: the level repairs about that share and nothing more, so enlarge the symbol or raise the level; ISO/IEC 18004 Table 9 carries the capacity of each level",
                logo.fraction * 100.0,
                level_cap * 100.0,
                level.unwrap_or("L")
            )));
        }

        return Err(BtError::invalid_argument(format!(
            "a logo covering {:.1}% of a {}-module symbol needs a {:.1}-module side, but the timing patterns leave room for {} modules, {:.1}%: the reserved centre never covers a timing line, so use a longer payload or a higher error correction level to grow the symbol",
            logo.fraction * 100.0,
            symbol.side,
            logo.fraction.sqrt() * f64::from(symbol.side),
            symbol.side - TIMING_GUARD_MODULES,
            maximum * 100.0
        )));
    }

    let side = (logo.fraction.sqrt() * f64::from(symbol.side)).round() as u32;
    let ceiling = (maximum.sqrt() * f64::from(symbol.side)).floor() as u32;
    let side = side.min(ceiling).clamp(1, symbol.side);

    Ok(Some(LogoRect {
        x: symbol.x + (symbol.side - side) / 2,
        y: symbol.y + (symbol.side - side) / 2,
        side,
    }))
}

/// The colour the reserved region is painted with.
fn logo_colour(style: &Style, logo: &Logo) -> Rgb {
    match logo.fill {
        LogoFill::Background => style.background,
        LogoFill::Solid(colour) => colour,
    }
}

/// A level spelled the way rxing spells it, as the ABI carries it.
///
/// The caps are read from the level, so a caller that passes `H` and a caller that passes `h` must
/// reach the same ceiling: the ABI accepts either case and refuses anything else by name rather than
/// silently falling back to the default.
pub fn level_name(level: Option<&str>) -> Option<&str> {
    match level {
        Some(value) if value.eq_ignore_ascii_case("h") => Some("H"),
        Some(value) if value.eq_ignore_ascii_case("q") => Some("Q"),
        Some(value) if value.eq_ignore_ascii_case("m") => Some("M"),
        Some(value) if value.eq_ignore_ascii_case("l") => Some("L"),
        Some(value) => Some(value),
        None => None,
    }
}

/// Reads the four scalars the ABI passes for a style into a [`Style`].
///
/// `0xRRGGBB` packs the colours; a negative value takes the default — black for the module, white
/// for the background. A `logo_fraction` of zero or less is no logo; a negative `logo_fill` fills the
/// reserved centre with the background colour.
pub fn from_abi(
    foreground: i32,
    background: i32,
    logo_fraction: f64,
    logo_fill: i32,
) -> Result<Style, BtError> {
    let colour = |value: i32, default: Rgb, what: &str| -> Result<Rgb, BtError> {
        if value < 0 {
            return Ok(default);
        }
        Rgb::from_packed(value as u32).ok_or_else(|| {
            BtError::invalid_argument(format!("{what} must be a 0xRRGGBB value, got {value:#x}"))
        })
    };

    let foreground = colour(foreground, Rgb::BLACK, "the module colour")?;
    let background = colour(background, Rgb::WHITE, "the background colour")?;

    let logo = if logo_fraction > 0.0 {
        let fill = if logo_fill < 0 {
            LogoFill::Background
        } else {
            LogoFill::Solid(colour(logo_fill, background, "the logo fill")?)
        };
        Some(Logo {
            fraction: logo_fraction,
            fill,
        })
    } else {
        None
    };

    Ok(Style {
        foreground,
        background,
        logo,
    })
}

/// The raster scale the verification pass reads back, in pixels per module.
///
/// Four is enough for the engine's own reader — the plain raster round trip is tested at one — and
/// small enough that verifying a symbol costs less than writing it.
pub const VERIFICATION_SCALE: u32 = 4;

/// Paints the module matrix in the style's colours, with the reserved centre over it.
fn paint(
    encoded: &Encoded,
    scale: u32,
    style: &Style,
    logo: Option<LogoRect>,
) -> (Vec<u8>, u32, u32) {
    let width = encoded.width() * scale;
    let height = encoded.height() * scale;

    let background = style.background;
    let mut pixels = vec![0_u8; (width as usize) * (height as usize) * 3];
    for pixel in pixels.chunks_exact_mut(3) {
        pixel.copy_from_slice(&[background.r, background.g, background.b]);
    }

    let mut fill = |rect: LogoRect, colour: Rgb| {
        let start_x = (rect.x * scale) as usize;
        let end_x = (start_x + rect.side as usize * scale as usize).min(width as usize);
        for y in (rect.y * scale) as usize..((rect.y + rect.side) * scale) as usize {
            let row = y * width as usize * 3;
            for x in start_x..end_x {
                let offset = row + x * 3;
                pixels[offset..offset + 3].copy_from_slice(&[colour.r, colour.g, colour.b]);
            }
        }
    };

    for y in 0..encoded.height() {
        for x in 0..encoded.width() {
            if encoded.modules.get(x, y) {
                fill(LogoRect { x, y, side: 1 }, style.foreground);
            }
        }
    }

    if let (Some(rect), Some(logo)) = (logo, &style.logo) {
        fill(rect, logo_colour(style, logo));
    }

    (pixels, width, height)
}

/// Writes 8-bit RGB pixels as a PNG.
fn write_png(pixels: &[u8], width: u32, height: u32) -> Result<Vec<u8>, BtError> {
    let mut png = Vec::new();
    image::codecs::png::PngEncoder::new(&mut png)
        .write_image(pixels, width, height, image::ExtendedColorType::Rgb8)
        .map_err(|error| BtError::encode(format!("could not write the PNG: {error}")))?;
    Ok(png)
}

/// Reads the rendered symbol back, and refuses the style when the payload does not come out.
///
/// This is the promise of the feature, made where it cannot be forgotten: a logo costs error
/// correction, the level's nominal capacity is a per-codeword figure, and a rectangle also destroys
/// every codeword it only partly covers. The measured share a symbol really tolerates is therefore
/// below the nominal one, and it depends on the symbol's version and on what sits under the
/// rectangle — see the table in Spec 20 §11. Rather than publish an approximation, the renderer
/// applies the exact test on every starred symbol, with the package's own decoder, and refuses the
/// ones a reader would not get back.
fn verify(
    png: &[u8],
    encoded: &Encoded,
    logo: LogoRect,
    level: Option<&str>,
) -> Result<(), BtError> {
    let page = crate::decode::decode_png_all(png)?;
    if page
        .symbols
        .iter()
        .any(|symbol| symbol.text == encoded.payload)
    {
        return Ok(());
    }

    Err(BtError::invalid_argument(format!(
        "a {}-module reserved centre leaves a symbol this package's own decoder cannot read back at error correction {}: the level's nominal capacity is a per-codeword figure, and a rectangle also destroys every codeword it only partly covers, so the share it really tolerates is lower. Use a smaller logo, a longer payload or a higher level",
        logo.side,
        level.unwrap_or("L")
    )))
}

/// Renders the styled symbol as an 8-bit RGB PNG, `scale` pixels per module.
///
/// Black on white with no logo is not re-implemented here: it is routed back to
/// [`crate::encode::to_png`], so the default raster stays the 8-bit greyscale one every existing
/// caller already has, and only a symbol that really is coloured becomes a colour PNG.
pub fn png(
    encoded: &Encoded,
    scale: u32,
    style: &Style,
    level: Option<&str>,
) -> Result<Vec<u8>, BtError> {
    if style.is_default() {
        return encode::to_png(encoded, scale);
    }

    style.check(encoded.symbology, level)?;

    let scale = encode::checked_scale(encoded, scale)?;
    let logo = logo_rect(encoded, style.logo.as_ref(), level)?;
    let (pixels, width, height) = paint(encoded, scale, style, logo);
    let png = write_png(&pixels, width, height)?;

    if let Some(rect) = logo {
        verify(&png, encoded, rect, level)?;
    }

    Ok(png)
}

/// Renders the styled symbol as SVG, one unit per module.
///
/// The same one `<path>` the plain rendering uses, with the module colour as its fill, the
/// background as a `<rect>` behind it, and — when the symbol reserves a centre — a `<rect>` of that
/// colour over both. A vector keeps its edges at any size, which is what a document wants.
pub fn svg(encoded: &Encoded, style: &Style, level: Option<&str>) -> Result<String, BtError> {
    if style.is_default() {
        return Ok(encode::to_svg(encoded));
    }

    style.check(encoded.symbology, level)?;

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

    let rect = logo_rect(encoded, style.logo.as_ref(), level)?;

    let mut logo = String::new();
    if let (Some(rect), Some(shape)) = (rect, &style.logo) {
        let _ = write!(
            logo,
            "<rect x=\"{}\" y=\"{}\" width=\"{}\" height=\"{}\" fill=\"{}\"/>",
            rect.x,
            rect.y,
            rect.side,
            rect.side,
            logo_colour(style, shape).to_hex()
        );

        // The vector and the raster are the same module matrix and the same rectangle, so the
        // raster proves the vector: a reader that reads the one reads the other.
        let (pixels, width, height) = paint(encoded, VERIFICATION_SCALE, style, Some(rect));
        verify(&write_png(&pixels, width, height)?, encoded, rect, level)?;
    }

    Ok(format!(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" \
         viewBox=\"0 0 {width} {height}\" shape-rendering=\"crispEdges\">\
         <rect width=\"{width}\" height=\"{height}\" fill=\"{}\"/>\
         <path fill=\"{}\" d=\"{path}\"/>{logo}</svg>",
        style.background.to_hex(),
        style.foreground.to_hex()
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn encode_qr(payload: &str, level: &str) -> Encoded {
        encode::encode(
            Symbology::QrCode,
            payload,
            &encode::EncodeOptions {
                error_correction: Some(level.to_string()),
                ..encode::EncodeOptions::default()
            },
        )
        .expect("encode")
    }

    #[test]
    fn black_on_white_is_the_wcag_maximum() {
        assert!((contrast_ratio(Rgb::BLACK, Rgb::WHITE) - 21.0).abs() < 1e-9);
        assert!((contrast_ratio(Rgb::WHITE, Rgb::BLACK) - 21.0).abs() < 1e-9);
        assert!((contrast_ratio(Rgb::BLACK, Rgb::BLACK) - 1.0).abs() < 1e-9);
    }

    #[test]
    fn a_known_pair_measures_the_published_ratio() {
        // #767676 on white is the textbook "smallest grey that clears 4.5:1".
        let grey = Rgb {
            r: 0x76,
            g: 0x76,
            b: 0x76,
        };
        let ratio = contrast_ratio(grey, Rgb::WHITE);
        assert!((ratio - 4.54).abs() < 0.02, "{ratio}");
        assert!(ratio >= MINIMUM_CONTRAST_RATIO);

        // #777777 is one step lighter and drops below the floor, which is why the floor is 4.5 and
        // not "grey is fine": the check is arithmetic, not a colour name.
        let lighter = Rgb {
            r: 0x77,
            g: 0x77,
            b: 0x77,
        };
        assert!(contrast_ratio(lighter, Rgb::WHITE) < MINIMUM_CONTRAST_RATIO);
    }

    #[test]
    fn a_dark_brand_blue_on_white_is_accepted() {
        let style = Style {
            foreground: Rgb {
                r: 0x1f,
                g: 0x3a,
                b: 0x93,
            },
            ..Style::default()
        };

        assert!(style.check_contrast().is_ok());
        assert!(contrast_ratio(style.foreground, style.background) > 6.0);
    }

    #[test]
    fn a_pale_module_on_white_is_refused_with_the_ratio_it_measured() {
        // #9e9e9e on white is 2.7:1: the ratio is in the message, so the caller can act on it.
        let style = Style {
            foreground: Rgb {
                r: 0x9e,
                g: 0x9e,
                b: 0x9e,
            },
            ..Style::default()
        };

        let error = style.check_contrast().unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("2.68"), "{}", error.json);
        assert!(error.json.contains("4.5"), "{}", error.json);
        assert!(error.json.contains("WCAG"), "{}", error.json);
    }

    #[test]
    fn a_background_darker_than_the_module_is_refused_even_at_high_contrast() {
        // White on black is 21:1 and still wrong: ISO/IEC 18004 makes the modules the dark half.
        let style = Style {
            foreground: Rgb::WHITE,
            background: Rgb::BLACK,
            logo: None,
        };

        let error = style.check_contrast().unwrap_err();
        assert!(error.json.contains("not darker"), "{}", error.json);
    }

    #[test]
    fn the_logo_ceiling_follows_the_error_correction_level() {
        assert!((maximum_logo_fraction(None) - 0.07).abs() < f64::EPSILON);
        assert!((maximum_logo_fraction(Some("L")) - 0.07).abs() < f64::EPSILON);
        assert!((maximum_logo_fraction(Some("M")) - 0.15).abs() < f64::EPSILON);
        assert!((maximum_logo_fraction(Some("Q")) - 0.25).abs() < f64::EPSILON);
        assert!((maximum_logo_fraction(Some("H")) - 0.30).abs() < f64::EPSILON);
    }

    #[test]
    fn a_logo_past_the_level_ceiling_is_refused_with_both_numbers() {
        let style = Style {
            logo: Some(Logo::cleared(0.34)),
            ..Style::default()
        };

        let error = style
            .check(Symbology::QrCode, Some("Q"))
            .expect_err("34 % is past Q");
        assert!(error.json.contains("34.0%"), "{}", error.json);
        assert!(error.json.contains("25%"), "{}", error.json);
        assert!(error.json.contains("Q"), "{}", error.json);
        assert!(error.json.contains("ISO/IEC 18004"), "{}", error.json);
    }

    #[test]
    fn a_logo_on_a_one_dimensional_symbol_is_refused_by_name() {
        let style = Style {
            logo: Some(Logo::cleared(0.05)),
            ..Style::default()
        };

        let error = style
            .check(Symbology::Code128, None)
            .expect_err("Code 128 has no centre to spare");
        assert_eq!(error.code, crate::ffi::BT_ERR_UNSUPPORTED);
        assert!(error.json.contains("Code128"), "{}", error.json);
    }

    #[test]
    fn the_symbol_box_is_the_quiet_zone_free_square() {
        let encoded = encode_qr("BOX", "H");

        // Four modules of quiet zone on each side, so the symbol is the matrix less eight.
        let symbol = symbol_box(&encoded).expect("box");
        assert_eq!(symbol.x, 4);
        assert_eq!(symbol.y, 4);
        assert_eq!(symbol.side, encoded.width() - 8);
        assert_eq!(symbol.side, encoded.height() - 8);
    }

    #[test]
    fn a_centred_logo_is_centred_and_never_past_the_ceiling() {
        let encoded = encode_qr("https://m3scalinum.example/products/07?lot=42", "H");
        let symbol = symbol_box(&encoded).expect("box");

        let rect = logo_rect(&encoded, Some(&Logo::cleared(0.30)), Some("H"))
            .expect("30 % fits")
            .expect("rect");
        // The side the share asks for, floored to the module count the ceiling allows.
        let asked = (0.30_f64.sqrt() * f64::from(symbol.side)).round() as u32;
        let allowed = (0.30_f64.sqrt() * f64::from(symbol.side)).floor() as u32;
        assert_eq!(rect.side, asked.min(allowed), "{rect:?} in {symbol:?}");

        // The region is centred to within the odd module a whole number of modules forces.
        let left = rect.x - symbol.x;
        let right = symbol.x + symbol.side - (rect.x + rect.side);
        assert!(left.abs_diff(right) <= 1, "{rect:?} in {symbol:?}");

        // Rounding up to whole modules can never push the area past the ceiling.
        let area = f64::from(rect.side).powi(2) / f64::from(symbol.side).powi(2);
        assert!(area <= 0.30 + 1e-9, "{area}");

        // And the region clears both timing lines: module 6 of the symbol on each axis.
        assert!(rect.x > symbol.x + 6, "{rect:?}");
        assert!(rect.y > symbol.y + 6, "{rect:?}");
        assert!(rect.x + rect.side < symbol.x + symbol.side - 6, "{rect:?}");
        assert!(rect.y + rect.side < symbol.y + symbol.side - 6, "{rect:?}");
    }

    #[test]
    fn a_reserved_centre_that_covers_a_timing_line_would_not_read_back() {
        let encoded = encode_qr(
            "https://m3scalinum.example/products/07?lot=42&serial=0001234567",
            "H",
        );
        let symbol = symbol_box(&encoded).expect("box");
        assert_eq!(symbol.side, 45);

        // The same 19-module square twice: once starting on the timing line, once one module later.
        let reads_back = |start: u32| {
            let mut patched = encoded.clone();
            for y in symbol.y + start..symbol.y + start + 19 {
                for x in symbol.x + start..symbol.x + start + 19 {
                    patched.modules.unset(x, y);
                }
            }
            let png = encode::to_png(&patched, 6).expect("png");
            !crate::decode::decode_png_all(&png)
                .expect("page")
                .symbols
                .is_empty()
        };

        assert!(
            reads_back(7),
            "a square clear of the timing lines reads back, which is why the guard sits at 7"
        );
        assert!(
            !reads_back(6),
            "a square that swallows a timing line does not, which is why the guard exists"
        );
    }

    #[test]
    fn a_share_the_symbol_is_too_small_for_is_refused_with_the_room_it_has() {
        // Version 1: 21 modules, so the timing lines leave seven and about 11 % of the area.
        let encoded = encode_qr("SMALL", "H");
        assert_eq!(symbol_box(&encoded).expect("box").side, 21);

        let error = logo_rect(&encoded, Some(&Logo::cleared(0.20)), Some("H"))
            .expect_err("a fifth of a version 1 code is not a logo, it is the whole code");
        assert_eq!(error.code, crate::ffi::BT_ERR_INVALID_ARGUMENT);
        assert!(error.json.contains("7 modules"), "{}", error.json);
        assert!(error.json.contains("11.1%"), "{}", error.json);
        assert!(error.json.contains("timing"), "{}", error.json);
    }

    #[test]
    fn the_default_style_is_the_plain_greyscale_raster() {
        let encoded = encode_qr("DEFAULT", "L");
        let styled = png(&encoded, 2, &Style::default(), None).expect("png");
        let plain = encode::to_png(&encoded, 2).expect("png");

        assert_eq!(styled, plain);
    }

    #[test]
    fn a_coloured_raster_carries_the_two_colours_and_the_reserved_centre() {
        let encoded = encode_qr("https://m3scalinum.example/coloured/07", "H");
        let style = Style {
            foreground: Rgb {
                r: 0x1f,
                g: 0x3a,
                b: 0x93,
            },
            background: Rgb {
                r: 0xff,
                g: 0xf4,
                b: 0xe0,
            },
            logo: Some(Logo::solid(0.20, Rgb::WHITE)),
        };

        let png = png(&encoded, 4, &style, Some("H")).expect("png");
        let image = image::load_from_memory_with_format(&png, image::ImageFormat::Png)
            .expect("png")
            .to_rgb8();

        assert_eq!(image.width(), encoded.width() * 4);

        // A corner of the quiet zone is the background, exactly as asked for.
        assert_eq!(image.get_pixel(0, 0).0, [0xff, 0xf4, 0xe0]);

        // Every pixel is one of the three colours: no resampling, no dithering.
        let palette = [[0x1f, 0x3a, 0x93], [0xff, 0xf4, 0xe0], [0xff, 0xff, 0xff]];
        assert!(image.pixels().all(|pixel| palette.contains(&pixel.0)));

        // The centre of the symbol is the reserved region, so it is not the module colour.
        let centre = image.width() / 2;
        assert_eq!(image.get_pixel(centre, centre).0, [0xff, 0xff, 0xff]);
    }

    #[test]
    fn the_coloured_svg_carries_the_colours_and_the_reserved_rect() {
        let encoded = encode_qr("https://m3scalinum.example/vector/07", "H");
        let style = Style {
            foreground: Rgb {
                r: 0x0b,
                g: 0x0b,
                b: 0x0b,
            },
            background: Rgb::WHITE,
            logo: Some(Logo::solid(0.20, Rgb::WHITE)),
        };

        let svg = svg(&encoded, &style, Some("H")).expect("svg");
        assert!(svg.contains("fill=\"#0b0b0b\""), "{svg}");
        assert!(svg.contains("<rect x="), "a reserved rect is drawn: {svg}");
        assert_eq!(svg.matches("<rect ").count(), 2, "{svg}");
    }
}
