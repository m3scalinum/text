//! The image work that happens before the recogniser looks at a page:
//! quarter-turn detection, a fine deskew and a denoise.
//!
//! Everything here works on the already-decoded image, in greyscale. No model is
//! involved and no crate beyond `image`'s decoding is added: the three algorithms
//! are a projection profile, a coarse-to-fine angle search over that profile, and a
//! 3×3 median filter. That is deliberate — a page turned by a scanner is a
//! geometry problem, and geometry does not need 40 MB of weights.

/// The widest fine rotation the deskew searches for, in degrees, either way.
pub const MAX_SKEW_DEGREES: f64 = 10.0;

/// The angle step of the deskew search, in degrees.
///
/// A quarter of a degree is a quarter of a pixel of displacement over a 60 px line,
/// which is below what the detector resolves; finer steps cost time and buy nothing.
const SKEW_STEP_DEGREES: f64 = 0.25;

/// Below this angle the deskew does nothing, in degrees.
///
/// A page that is level to within a third of a degree is level: rotating it would
/// resample every pixel for a correction nobody asked for.
pub const MINIMUM_SKEW_TO_CORRECT: f64 = 0.3;

/// How much heavier the ink in a line's bottom quarter must be than in its top
/// quarter before the page is called upside down.
///
/// The dead zone is the honest part of the test: text whose bands are vertically
/// symmetric — all capitals, digits, a table rule — gives no signal, and guessing
/// 180° there would be worse than leaving the page alone.
const UPSIDE_DOWN_RATIO: f64 = 1.25;

/// How much longer the ink runs one way than the other before the text is called
/// vertical.
const VERTICAL_TEXT_RATIO: f64 = 1.15;

/// The largest side, in pixels, the skew search works on. Larger pages are sampled
/// down to this, because the search is O(pixels × angles) and the angle does not
/// need every pixel to be seen.
const SKEW_SEARCH_SIZE: f64 = 600.0;

/// White, the colour a rotated page's new corners are filled with.
const WHITE: u8 = 0xFF;

/// The fewest sampled ink pixels a skew estimate is trusted on.
const MINIMUM_SKEW_SAMPLES: usize = 100;

/// What preprocessing a caller asked for. All three are off by default.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct PreprocessOptions {
    /// Detect a page turned by 90°, 180° or 270° and turn it back.
    pub detect_rotation: bool,
    /// Estimate and remove a fine skew of at most [`MAX_SKEW_DEGREES`].
    pub deskew: bool,
    /// Apply a 3×3 median filter to the luma channel.
    pub denoise: bool,
}

impl PreprocessOptions {
    /// Whether any stage has work to do. Nothing is touched when this is false.
    pub fn any(self) -> bool {
        self.detect_rotation || self.deskew || self.denoise
    }

    /// The options as three ABI flags: 0 is off, anything else is on.
    pub fn from_flags(detect_rotation: i32, deskew: i32, denoise: i32) -> Self {
        Self {
            detect_rotation: detect_rotation != 0,
            deskew: deskew != 0,
            denoise: denoise != 0,
        }
    }
}

/// A preprocessed page, in RGB8, and what was done to get it.
#[derive(Clone, Debug)]
pub struct Preprocessed {
    /// The pixels the recogniser must be given, `width * height * 3` bytes.
    pub pixels: Vec<u8>,
    /// The width after rotation, in pixels.
    pub width: u32,
    /// The height after rotation, in pixels.
    pub height: u32,
    /// The clockwise quarter turns applied to make the page upright: 0, 90, 180 or 270.
    pub rotation_degrees: i32,
    /// The fine rotation applied by the deskew, in degrees, positive clockwise.
    pub skew_degrees: f64,
    /// Whether the median filter ran.
    pub denoised: bool,
}

impl Preprocessed {
    /// Whether anything was actually changed, so a report is worth writing.
    pub fn changed(&self) -> bool {
        self.rotation_degrees != 0 || self.skew_degrees != 0.0 || self.denoised
    }
}

/// Preprocesses one decoded page.
///
/// The stages run in the order a scanner's mistakes occur: the denoise filters the
/// noise out of the luma, the rotation detection decides which way is up, and the
/// deskew levels the lines on the result. With every option off the input is
/// returned byte for byte, which is what makes "off" a real default rather than a
/// nearly-invisible filter.
pub fn preprocess(
    pixels: &[u8],
    width: u32,
    height: u32,
    options: PreprocessOptions,
) -> Preprocessed {
    let (w, h) = (width as usize, height as usize);

    if !options.any() || w == 0 || h == 0 || pixels.len() < w * h * 3 {
        return Preprocessed {
            pixels: pixels.to_vec(),
            width,
            height,
            rotation_degrees: 0,
            skew_degrees: 0.0,
            denoised: false,
        };
    }

    let mut grey = luma(pixels, w, h);
    let denoised = options.denoise;
    if denoised {
        grey = median3(&grey, w, h);
    }

    let mut width = w;
    let mut height = h;
    let mut rotation_degrees = 0;

    if options.detect_rotation {
        let (upright, new_width, new_height, degrees) = upright(&grey, w, h);
        grey = upright;
        width = new_width;
        height = new_height;
        rotation_degrees = degrees;
    }

    let mut skew_degrees = 0.0;
    if options.deskew {
        let (straight, new_width, new_height, angle) = deskew(&grey, width, height);
        grey = straight;
        width = new_width;
        height = new_height;
        skew_degrees = (angle * 1000.0).round() / 1000.0;
    }

    let mut rgb = Vec::with_capacity(grey.len() * 3);
    for value in grey {
        rgb.extend_from_slice(&[value, value, value]);
    }

    Preprocessed {
        pixels: rgb,
        width: width as u32,
        height: height as u32,
        rotation_degrees,
        skew_degrees,
        denoised,
    }
}

/// The luma of every pixel, in the 0–255 range. Integer weights, so the result is
/// identical on every target.
fn luma(pixels: &[u8], w: usize, h: usize) -> Vec<u8> {
    pixels
        .chunks_exact(3)
        .take(w * h)
        .map(|pixel| {
            ((77 * u32::from(pixel[0]) + 150 * u32::from(pixel[1]) + 29 * u32::from(pixel[2])) >> 8)
                as u8
        })
        .collect()
}

/// A 3×3 median filter, with the border clamped to the last row or column.
///
/// A median, not a mean: it removes isolated specks without softening the edge of a
/// stroke, which a mean filter would blur into a thinner, greyer one. The cost is
/// that a stroke one pixel wide is a minority in its own 3×3 window and disappears;
/// a 300 DPI scan's strokes are 2–4 px, so that is the documented limit.
fn median3(source: &[u8], w: usize, h: usize) -> Vec<u8> {
    let mut out = vec![0_u8; source.len()];

    for y in 0..h {
        for x in 0..w {
            let mut window = [0_u8; 9];
            let mut index = 0;
            for dy in 0..3 {
                for dx in 0..3 {
                    let sx = (x + dx).saturating_sub(1).min(w - 1);
                    let sy = (y + dy).saturating_sub(1).min(h - 1);
                    window[index] = source[sy * w + sx];
                    index += 1;
                }
            }
            window.sort_unstable();
            out[y * w + x] = window[4];
        }
    }

    out
}

/// Otsu's threshold: the split that maximises the variance between the two classes.
///
/// A fixed threshold would be a constant guessed from one scanner; Otsu adapts to
/// the page in hand. A page with no ink at all yields 0, and the caller raises it to
/// 1 so that no pixel is called ink.
fn otsu_threshold(grey: &[u8]) -> u8 {
    if grey.is_empty() {
        return 0;
    }

    let mut histogram = [0_u32; 256];
    for &value in grey {
        histogram[value as usize] += 1;
    }

    let total: u32 = grey.len() as u32;
    let sum: u64 = (0..256).map(|i| i as u64 * u64::from(histogram[i])).sum();

    let mut background_weight = 0_u32;
    let mut background_sum = 0_u64;
    let mut best = 0_usize;
    let mut best_variance = -1.0_f64;

    for (threshold, &count) in histogram.iter().enumerate() {
        background_weight += count;
        if background_weight == 0 {
            continue;
        }

        let foreground_weight = total - background_weight;
        if foreground_weight == 0 {
            break;
        }

        background_sum += threshold as u64 * u64::from(count);

        let background_mean = background_sum as f64 / f64::from(background_weight);
        let foreground_mean = (sum - background_sum) as f64 / f64::from(foreground_weight);
        let variance = f64::from(background_weight)
            * f64::from(foreground_weight)
            * (background_mean - foreground_mean).powi(2);

        if variance > best_variance {
            best_variance = variance;
            best = threshold;
        }
    }

    best as u8
}

/// One byte per pixel: 1 where the pixel is darker than the Otsu threshold, 0 elsewhere.
fn ink_map(grey: &[u8]) -> Vec<u8> {
    let threshold = otsu_threshold(grey).max(1);
    grey.iter()
        .map(|&value| u8::from(value <= threshold))
        .collect()
}

/// The sum of ink per row (`rows` true) or per column.
fn profile(ink: &[u8], w: usize, h: usize) -> (Vec<u32>, Vec<u32>) {
    let mut rows = vec![0_u32; h];
    let mut columns = vec![0_u32; w];

    for y in 0..h {
        for x in 0..w {
            if ink[y * w + x] == 1 {
                rows[y] += 1;
                columns[x] += 1;
            }
        }
    }

    (rows, columns)
}

/// The mean length of the ink runs along one axis, seen by an ink pixel.
///
/// Only a run of ink seen from inside counts, and a long run counts proportionally
/// more: `Σ length² / Σ length` is the length an ink pixel actually sits in. Letters
/// are separated by two or three pixels and are far wider than a stroke is tall, so
/// horizontal text has long horizontal runs and short vertical ones, and text turned
/// a quarter turn swaps the two. That makes the axis test independent of the page's
/// margins and of how dense the text is.
fn mean_run(ink: &[u8], w: usize, h: usize, horizontal: bool) -> f64 {
    let mut mass = 0_u64;
    let mut total = 0_u64;

    let mut push = |run: u64| {
        if run > 0 {
            mass += run;
            total += run * run;
        }
    };

    if horizontal {
        for y in 0..h {
            let mut run = 0_u64;
            for x in 0..w {
                if ink[y * w + x] == 1 {
                    run += 1;
                } else {
                    push(run);
                    run = 0;
                }
            }
            push(run);
        }
    } else {
        for x in 0..w {
            let mut run = 0_u64;
            for y in 0..h {
                if ink[y * w + x] == 1 {
                    run += 1;
                } else {
                    push(run);
                    run = 0;
                }
            }
            push(run);
        }
    }

    if mass == 0 {
        0.0
    } else {
        total as f64 / mass as f64
    }
}

/// Whether the text on a page reads bottom-up: 180° from upright.
///
/// Each band of ink is one line of text. In printed Latin text the ascenders above
/// the x-height are common and the descenders below the baseline are rare, so a
/// line's top quarter carries more ink than its bottom quarter — and half a page of
/// text makes that a strong, repeatable imbalance. Turned upside down it reverses.
/// The ratio, not a difference, is what is compared, so a faint page and a dark one
/// agree. Bands too small to measure, and a page with no band at all, give no signal
/// and answer `false`.
fn upside_down(grey: &[u8], w: usize, h: usize) -> bool {
    if w == 0 || h == 0 {
        return false;
    }

    let ink = ink_map(grey);
    let (rows, _) = profile(&ink, w, h);

    // A band is a run of rows holding any ink at all, kept only when it also holds a
    // row with a fiftieth of the page's width in ink. The sparse ascender and
    // descender rows belong to the line they touch; a dust speck on its own does not
    // clear the floor, and is not a line.
    let floor = (w / 50).max(1) as u32;

    let mut bands: Vec<(usize, usize)> = Vec::new();
    let mut band: Option<usize> = None;

    for (y, &row) in rows.iter().enumerate() {
        if row > 0 {
            band.get_or_insert(y);
        } else if let Some(top) = band.take() {
            bands.push((top, y));
        }
    }
    if let Some(top) = band {
        bands.push((top, h));
    }

    let mut upper_mass = 0_u64;
    let mut lower_mass = 0_u64;

    for (top, bottom) in bands {
        let height = bottom - top;
        if height < 6 || rows[top..bottom].iter().all(|&row| row < floor) {
            continue;
        }

        let quarter = height / 4;
        upper_mass += rows[top..top + quarter]
            .iter()
            .map(|&row| u64::from(row))
            .sum::<u64>();
        lower_mass += rows[bottom - quarter..bottom]
            .iter()
            .map(|&row| u64::from(row))
            .sum::<u64>();
    }

    if upper_mass == 0 && lower_mass == 0 {
        return false;
    }

    lower_mass as f64 > upper_mass as f64 * UPSIDE_DOWN_RATIO
}

/// Rotates a single-channel image 90° clockwise.
fn rotate90cw(source: &[u8], w: usize, h: usize) -> (Vec<u8>, usize, usize) {
    // The destination is h wide and w tall: destination (h - 1 - y, x) is source (x, y).
    let mut out = vec![0_u8; source.len()];

    for y in 0..h {
        for x in 0..w {
            out[x * h + (h - 1 - y)] = source[y * w + x];
        }
    }

    (out, h, w)
}

/// Rotates a single-channel image 180°.
fn rotate180(source: &[u8], w: usize, h: usize) -> (Vec<u8>, usize, usize) {
    let mut out = source.to_vec();
    out.reverse();

    (out, w, h)
}

/// Turns a page upright and reports the clockwise quarter turns it took: 0, 90, 180
/// or 270.
fn upright(grey: &[u8], w: usize, h: usize) -> (Vec<u8>, usize, usize, i32) {
    if w < 2 || h < 2 {
        return (grey.to_vec(), w, h, 0);
    }

    let ink = ink_map(grey);
    let horizontal = mean_run(&ink, w, h, true);
    let vertical = mean_run(&ink, w, h, false);

    if vertical > horizontal * VERTICAL_TEXT_RATIO {
        // Lines run down the page, so the text is a quarter turn from upright. Turn
        // it a quarter turn first, then ask the same question about 180°.
        let (turned, turned_w, turned_h) = rotate90cw(grey, w, h);

        if upside_down(&turned, turned_w, turned_h) {
            let (fixed, fixed_w, fixed_h) = rotate180(&turned, turned_w, turned_h);
            return (fixed, fixed_w, fixed_h, 270);
        }

        return (turned, turned_w, turned_h, 90);
    }

    if upside_down(grey, w, h) {
        let (fixed, fixed_w, fixed_h) = rotate180(grey, w, h);
        return (fixed, fixed_w, fixed_h, 180);
    }

    (grey.to_vec(), w, h, 0)
}

/// Estimates the page's skew and removes it, reporting the fine rotation applied.
///
/// The estimate is the angle whose projection of the ink onto the vertical axis is
/// the sharpest — text lines become one thin histogram spike each when the lines are
/// horizontal, and smear when they are not. The search is a grid over
/// ±[`MAX_SKEW_DEGREES`] at [`SKEW_STEP_DEGREES`], on a page sampled down to
/// [`SKEW_SEARCH_SIZE`], which is what keeps it cheap enough to run on every page.
fn deskew(grey: &[u8], w: usize, h: usize) -> (Vec<u8>, usize, usize, f64) {
    if w < 2 || h < 2 {
        return (grey.to_vec(), w, h, 0.0);
    }

    let ink = ink_map(grey);
    let angle = estimate_skew(&ink, w, h);

    if angle.abs() < MINIMUM_SKEW_TO_CORRECT {
        return (grey.to_vec(), w, h, 0.0);
    }

    let (straight, new_width, new_height) = rotate_bilinear(grey, w, h, angle);

    (straight, new_width, new_height, angle)
}

/// The clockwise angle, in degrees, that levels the ink of a binarised page.
fn estimate_skew(ink: &[u8], w: usize, h: usize) -> f64 {
    let stride = ((w.max(h) as f64 / SKEW_SEARCH_SIZE).ceil() as usize).max(1);

    let mut points: Vec<(f64, f64)> = Vec::new();
    for y in (0..h).step_by(stride) {
        for x in (0..w).step_by(stride) {
            if ink[y * w + x] == 1 {
                points.push((x as f64, y as f64));
            }
        }
    }

    if points.len() < MINIMUM_SKEW_SAMPLES {
        return 0.0;
    }

    // A rotation by at most 10° moves a row by at most 0.18 × the width, so this
    // window holds every projection the search produces.
    let offset = (0.2 * w as f64) as i32 + 2;
    let bins = h + (0.4 * w as f64) as usize + 4;

    let steps = (MAX_SKEW_DEGREES / SKEW_STEP_DEGREES) as i32;
    let mut best_angle = 0.0;
    let mut best_score = -1.0_f64;

    for step in -steps..=steps {
        let angle = f64::from(step) * SKEW_STEP_DEGREES;
        let (sin, cos) = angle.to_radians().sin_cos();

        let mut histogram = vec![0_u32; bins];
        for &(x, y) in &points {
            let projected = y * cos + x * sin;
            let index = (projected.round() as i32 + offset).max(0) as usize;
            if index < bins {
                histogram[index] += 1;
            }
        }

        // The same number of ink points at every angle, so the mass is constant and
        // the sum of squares alone measures how tightly the projection is packed.
        let score: f64 = histogram
            .iter()
            .map(|&count| f64::from(count) * f64::from(count))
            .sum();

        if score > best_score {
            best_score = score;
            best_angle = angle;
        }
    }

    best_angle
}

/// Rotates a single-channel image about its centre by `degrees` clockwise, expanding
/// the canvas to hold the whole page and filling the new corners with white.
fn rotate_bilinear(source: &[u8], w: usize, h: usize, degrees: f64) -> (Vec<u8>, usize, usize) {
    let (sin, cos) = degrees.to_radians().sin_cos();
    let new_w = (w as f64 * cos.abs() + h as f64 * sin.abs()).ceil() as usize;
    let new_h = (w as f64 * sin.abs() + h as f64 * cos.abs()).ceil() as usize;

    let mut out = vec![WHITE; new_w * new_h];

    let centre_x = (w as f64 - 1.0) / 2.0;
    let centre_y = (h as f64 - 1.0) / 2.0;
    let new_centre_x = (new_w as f64 - 1.0) / 2.0;
    let new_centre_y = (new_h as f64 - 1.0) / 2.0;

    for y in 0..new_h {
        for x in 0..new_w {
            // The inverse of a clockwise rotation: where in the source this pixel came from.
            let dx = x as f64 - new_centre_x;
            let dy = y as f64 - new_centre_y;
            let sx = centre_x + cos * dx + sin * dy;
            let sy = centre_y - sin * dx + cos * dy;

            out[y * new_w + x] = sample(source, w, h, sx, sy);
        }
    }

    (out, new_w, new_h)
}

/// A bilinear sample, white outside the image.
fn sample(source: &[u8], w: usize, h: usize, x: f64, y: f64) -> u8 {
    if x < -1.0 || y < -1.0 || x > w as f64 || y > h as f64 {
        return WHITE;
    }

    let x = x.clamp(0.0, (w - 1) as f64);
    let y = y.clamp(0.0, (h - 1) as f64);

    let x0 = x.floor() as usize;
    let y0 = y.floor() as usize;
    let x1 = (x0 + 1).min(w - 1);
    let y1 = (y0 + 1).min(h - 1);

    let fx = x - x0 as f64;
    let fy = y - y0 as f64;

    let top = f64::from(source[y0 * w + x0]) * (1.0 - fx) + f64::from(source[y0 * w + x1]) * fx;
    let bottom = f64::from(source[y1 * w + x0]) * (1.0 - fx) + f64::from(source[y1 * w + x1]) * fx;

    (top * (1.0 - fy) + bottom * fy).round().clamp(0.0, 255.0) as u8
}

#[cfg(test)]
mod tests {
    use image::{Rgb, RgbImage, imageops};

    use super::*;

    const PAPER: u8 = 245;
    const INK: u8 = 30;

    /// A page of five lines of glyph-like marks: a wide block for the body of each
    /// glyph, a thin ascender above it on most glyphs and a thinner descender below
    /// it on a few — the proportions of printed Latin text, where ascenders are
    /// common and descenders are not.
    fn text_page() -> RgbImage {
        let mut image = RgbImage::from_pixel(320, 220, Rgb([PAPER, PAPER, PAPER]));

        for line in 0..5_u32 {
            let top = 18 + line * 38;

            for glyph in 0..8_u32 {
                let left = 20 + glyph * 28;

                // The x-height body: 22 × 14.
                for y in top + 6..top + 20 {
                    for x in left..left + 22 {
                        image.put_pixel(x, y, Rgb([INK, INK, INK]));
                    }
                }

                // The ascender, three pixels wide above the body, on six glyphs of eight.
                if glyph % 4 != 3 {
                    for y in top..top + 6 {
                        for x in left + 2..left + 5 {
                            image.put_pixel(x, y, Rgb([INK, INK, INK]));
                        }
                    }
                }

                // A descender two pixels wide below the baseline, on two glyphs of eight.
                if glyph % 4 == 0 {
                    for y in top + 20..top + 26 {
                        for x in left + 2..left + 4 {
                            image.put_pixel(x, y, Rgb([INK, INK, INK]));
                        }
                    }
                }
            }
        }

        image
    }

    /// The luma of an image, for the tests that measure ink.
    fn grey_of(image: &RgbImage) -> Vec<u8> {
        luma(
            image.as_raw(),
            image.width() as usize,
            image.height() as usize,
        )
    }

    /// 1 where a pixel is ink, at a plain midpoint threshold: the tests' measure,
    /// independent of the Otsu split the implementation uses.
    fn binary(grey: &[u8]) -> Vec<u8> {
        grey.iter().map(|&value| u8::from(value < 128)).collect()
    }

    /// The coefficient of variation of the row profile: how sharply the lines of a
    /// page separate. Deskewing a page must raise it.
    fn row_sharpness(grey: &[u8], w: usize, h: usize) -> f64 {
        let (rows, _) = profile(&binary(grey), w, h);
        let mean = rows.iter().map(|&v| f64::from(v)).sum::<f64>() / rows.len() as f64;

        if mean <= 0.0 {
            return 0.0;
        }

        let variance = rows
            .iter()
            .map(|&v| (f64::from(v) - mean).powi(2))
            .sum::<f64>()
            / rows.len() as f64;

        variance.sqrt() / mean
    }

    /// How many isolated blobs of ink a page holds, counting only blobs of at most
    /// `max_area` pixels: the speck measure the denoise test asks about.
    fn small_blobs(grey: &[u8], w: usize, h: usize, max_area: usize) -> usize {
        let ink = binary(grey);
        let mut seen = vec![false; ink.len()];
        let mut count = 0;

        for start in 0..ink.len() {
            if ink[start] == 0 || seen[start] {
                continue;
            }

            let mut stack = vec![start];
            seen[start] = true;
            let mut area = 0;

            while let Some(index) = stack.pop() {
                area += 1;
                let (x, y) = (index % w, index / w);

                for (dx, dy) in [(-1_isize, 0_isize), (1, 0), (0, -1), (0, 1)] {
                    let nx = x as isize + dx;
                    let ny = y as isize + dy;
                    if nx < 0 || ny < 0 || nx >= w as isize || ny >= h as isize {
                        continue;
                    }

                    let neighbour = ny as usize * w + nx as usize;
                    if ink[neighbour] == 1 && !seen[neighbour] {
                        seen[neighbour] = true;
                        stack.push(neighbour);
                    }
                }
            }

            if area <= max_area {
                count += 1;
            }
        }

        count
    }

    /// A page with a deterministic sprinkle of isolated black and white specks.
    fn speckled(image: &RgbImage) -> RgbImage {
        let mut out = image.clone();
        let (w, h) = (out.width(), out.height());

        for y in 0..h {
            for x in 0..w {
                if (x * 31 + y * 17) % 101 == 0 {
                    let value = if (x + y) % 2 == 0 { 0 } else { 255 };
                    out.put_pixel(x, y, Rgb([value, value, value]));
                }
            }
        }

        out
    }

    #[test]
    fn every_option_off_returns_the_input_byte_for_byte() {
        let page = text_page();
        let result = preprocess(
            page.as_raw(),
            page.width(),
            page.height(),
            PreprocessOptions::default(),
        );

        assert_eq!(result.pixels.as_slice(), page.as_raw().as_slice());
        assert_eq!((result.width, result.height), (320, 220));
        assert_eq!(result.rotation_degrees, 0);
        assert_eq!(result.skew_degrees, 0.0);
        assert!(!result.denoised);
        assert!(!result.changed());
    }

    #[test]
    fn a_straight_clean_page_is_neither_turned_nor_resampled() {
        let page = text_page();
        let result = preprocess(
            page.as_raw(),
            page.width(),
            page.height(),
            PreprocessOptions {
                detect_rotation: true,
                deskew: true,
                denoise: false,
            },
        );

        assert_eq!(result.rotation_degrees, 0);
        assert_eq!(result.skew_degrees, 0.0);
        // Same bytes: a level page is not resampled for a correction it does not need.
        assert_eq!(result.pixels.as_slice(), page.as_raw().as_slice());
    }

    #[test]
    fn a_page_turned_a_quarter_turn_clockwise_is_turned_back() {
        let upright = text_page();
        let turned = imageops::rotate90(&upright);

        let result = preprocess(
            turned.as_raw(),
            turned.width(),
            turned.height(),
            PreprocessOptions {
                detect_rotation: true,
                ..PreprocessOptions::default()
            },
        );

        assert_eq!(result.rotation_degrees, 270);
        assert_eq!((result.width, result.height), (320, 220));
        assert_eq!(result.pixels.as_slice(), upright.as_raw().as_slice());
    }

    #[test]
    fn a_page_turned_a_quarter_turn_counter_clockwise_is_turned_back() {
        let upright = text_page();
        let turned = imageops::rotate270(&upright);

        let result = preprocess(
            turned.as_raw(),
            turned.width(),
            turned.height(),
            PreprocessOptions {
                detect_rotation: true,
                ..PreprocessOptions::default()
            },
        );

        assert_eq!(result.rotation_degrees, 90);
        assert_eq!((result.width, result.height), (320, 220));
        assert_eq!(result.pixels.as_slice(), upright.as_raw().as_slice());
    }

    #[test]
    fn a_page_turned_upside_down_is_turned_back() {
        let upright = text_page();
        let turned = imageops::rotate180(&upright);

        let result = preprocess(
            turned.as_raw(),
            turned.width(),
            turned.height(),
            PreprocessOptions {
                detect_rotation: true,
                ..PreprocessOptions::default()
            },
        );

        assert_eq!(result.rotation_degrees, 180);
        assert_eq!(result.pixels.as_slice(), upright.as_raw().as_slice());
    }

    #[test]
    fn an_upright_page_is_not_turned_upside_down() {
        let upright = text_page();
        let grey = grey_of(&upright);

        assert!(!upside_down(&grey, 320, 220));
    }

    #[test]
    fn a_page_skewed_by_three_degrees_is_straightened_and_reports_the_angle() {
        let page = text_page();
        let grey = grey_of(&page);
        // A scan whose lines run downhill to the right: a three-degree clockwise turn.
        let (skewed, w, h) = rotate_bilinear(&grey, 320, 220, 3.0);

        let before = row_sharpness(&skewed, w, h);

        let (straight, new_w, new_h, angle) = deskew(&skewed, w, h);

        assert!(
            (angle + 3.0).abs() < 0.5,
            "the applied angle should undo the +3° skew, got {angle}"
        );
        assert!(
            row_sharpness(&straight, new_w, new_h) > before * 1.2,
            "the lines should be sharper after the deskew: {before} then {}",
            row_sharpness(&straight, new_w, new_h)
        );
    }

    #[test]
    fn a_level_page_is_not_rotated_by_the_deskew() {
        let page = text_page();
        let grey = grey_of(&page);

        let (straight, w, h, angle) = deskew(&grey, 320, 220);

        assert_eq!(angle, 0.0);
        assert_eq!((w, h), (320, 220));
        assert_eq!(straight, grey);
    }

    #[test]
    fn a_skew_beyond_the_search_window_is_not_guessed_at_more_than_ten_degrees() {
        let page = text_page();
        let grey = grey_of(&page);
        let (skewed, w, h) = rotate_bilinear(&grey, 320, 220, 25.0);

        let (_, _, _, angle) = deskew(&skewed, w, h);

        assert!(
            angle.abs() <= MAX_SKEW_DEGREES,
            "the deskew must stay inside its window, got {angle}"
        );
    }

    #[test]
    fn denoising_removes_the_specks_of_a_noisy_page() {
        let page = text_page();
        let noisy = speckled(&page);
        let grey = grey_of(&noisy);

        let before = small_blobs(&grey, 320, 220, 4);
        assert!(
            before > 100,
            "the fixture should be visibly noisy, got {before}"
        );

        let result = preprocess(
            noisy.as_raw(),
            noisy.width(),
            noisy.height(),
            PreprocessOptions {
                denoise: true,
                ..PreprocessOptions::default()
            },
        );

        let cleaned = luma(&result.pixels, 320, 220);
        let after = small_blobs(&cleaned, 320, 220, 4);

        assert!(result.denoised);
        assert!(
            after * 5 < before,
            "the median filter should remove most specks: {before} then {after}"
        );
        // The lines survive: the denoise is not a blur that erases the text.
        assert!(row_sharpness(&cleaned, 320, 220) > row_sharpness(&grey, 320, 220));
    }

    #[test]
    fn a_blank_page_is_left_alone_whatever_is_asked() {
        let blank = RgbImage::from_pixel(64, 64, Rgb([PAPER, PAPER, PAPER]));

        let result = preprocess(
            blank.as_raw(),
            64,
            64,
            PreprocessOptions {
                detect_rotation: true,
                deskew: true,
                denoise: false,
            },
        );

        assert_eq!(result.rotation_degrees, 0);
        assert_eq!(result.skew_degrees, 0.0);
        assert_eq!(result.pixels.as_slice(), blank.as_raw().as_slice());
    }

    #[test]
    fn the_flags_of_the_abi_map_to_the_three_stages() {
        let options = PreprocessOptions::from_flags(1, 0, -1);

        assert!(options.detect_rotation);
        assert!(!options.deskew);
        assert!(options.denoise);
        assert!(options.any());
        assert!(!PreprocessOptions::from_flags(0, 0, 0).any());
    }
}
