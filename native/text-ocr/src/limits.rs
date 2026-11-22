//! The ceilings a received image may cost, read from its header before it is decoded.
//!
//! An image arrives from whoever sent the document, and the size it declares is that
//! sender's claim. The two numbers below therefore bound the decode before the decoder
//! allocates anything: the encoded byte length, and the width times height the format
//! header states. [`admit`] refuses an image past either ceiling with
//! [`TO_ERR_IMAGE_LIMIT`], naming the size observed and the ceiling that refused it, and
//! never allocates the pixels first.
//!
//! The dimensions come from the format header, not from the pixels: PNG's `IHDR`, JPEG's
//! `SOF`, BMP's info header, GIF's logical screen descriptor, TIFF's first `IFD` and
//! WebP's `VP8`/`VP8L`/`VP8X` block. [`image::ImageReader::into_dimensions`] builds the
//! decoder and asks it for its size, which reads those headers and no image data.

use std::io::Cursor;

use image::{ImageFormat, ImageReader, Limits};

use crate::ffi::{TO_ERR_IMAGE, TO_ERR_IMAGE_LIMIT, ToError};

/// The pixel ceiling: 64 megapixels, an A4 page at 600 DPI with room to spare.
///
/// A4 at 600 DPI is 4961 x 7016 = 34.8 megapixels, so this is nearly twice the largest
/// page a scanner produces. It is the same ceiling `native/text-barcodes` applies to an
/// image it decodes, and the same order as the 120 megapixel ceiling the PDF rasteriser
/// applies to a page it renders: a rendered page is measured from a document's own
/// MediaBox at a DPI the caller chose, while this number bounds a claim made by whoever
/// produced the file, so the tighter of the two is the honest one.
pub const HARD_MAXIMUM_PIXELS: u64 = 64_000_000;

/// The encoded-byte ceiling: 256 MiB, the same ceiling `text-barcodes` puts on an image.
pub const HARD_MAXIMUM_BYTES: usize = 256 * 1024 * 1024;

/// The pixel ceiling an image gets when the caller names none.
pub const DEFAULT_MAXIMUM_PIXELS: u64 = HARD_MAXIMUM_PIXELS;

/// The byte ceiling an image gets when the caller names none.
pub const DEFAULT_MAXIMUM_BYTES: usize = HARD_MAXIMUM_BYTES;

/// The most bytes one decoded pixel can occupy: eight-bit RGBA, the widest layout the
/// accepted formats produce.
const DECODED_BYTES_PER_PIXEL: u64 = 4;

/// What one decode is allowed to cost.
///
/// Both numbers are ceilings, and a caller may only lower them: a value above
/// [`HARD_MAXIMUM_PIXELS`] or [`HARD_MAXIMUM_BYTES`] is refused by name rather than
/// clamped, because a clamp would report a ceiling the caller did not ask for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ImageBounds {
    /// The largest number of pixels the image may hold, width times height.
    pub maximum_pixels: u64,
    /// The largest number of bytes the encoded image may occupy.
    pub maximum_bytes: usize,
}

impl Default for ImageBounds {
    fn default() -> Self {
        Self {
            maximum_pixels: DEFAULT_MAXIMUM_PIXELS,
            maximum_bytes: DEFAULT_MAXIMUM_BYTES,
        }
    }
}

impl ImageBounds {
    /// Reads the two ceilings a caller passed, where zero means "the default".
    ///
    /// A ceiling above the hard maximum is refused rather than clamped: silently
    /// lowering it would make the refusal name a number the caller never wrote, and
    /// silently honouring it would lift the bound this module exists to keep.
    pub fn from_options(maximum_pixels: u64, maximum_bytes: usize) -> Result<Self, ToError> {
        let defaults = Self::default();

        let maximum_pixels = if maximum_pixels == 0 {
            defaults.maximum_pixels
        } else {
            maximum_pixels
        };

        let maximum_bytes = if maximum_bytes == 0 {
            defaults.maximum_bytes
        } else {
            maximum_bytes
        };

        if maximum_pixels > HARD_MAXIMUM_PIXELS {
            return Err(ToError::invalid_argument(format!(
                "the image pixel ceiling may only be lowered: {maximum_pixels} is past the \
                 {HARD_MAXIMUM_PIXELS} pixel hard maximum"
            )));
        }

        if maximum_bytes > HARD_MAXIMUM_BYTES {
            return Err(ToError::invalid_argument(format!(
                "the image byte ceiling may only be lowered: {maximum_bytes} is past the \
                 {HARD_MAXIMUM_BYTES} byte hard maximum"
            )));
        }

        Ok(Self {
            maximum_pixels,
            maximum_bytes,
        })
    }

    /// The decoder's own limits, derived from these ceilings.
    ///
    /// They are the second line: a header that understates its size is still bounded by
    /// the decoder, which refuses a width, a height or an allocation past what these
    /// ceilings allow. `max_alloc` is the pixel ceiling at four bytes per pixel, the
    /// widest eight-bit layout, so a sixteen-bit image past half the pixel ceiling is
    /// refused by the allocation rather than decoded.
    fn decoder_limits(self) -> Limits {
        let mut limits = Limits::default();
        limits.max_image_width = Some(u32::try_from(self.maximum_pixels).unwrap_or(u32::MAX));
        limits.max_image_height = Some(u32::try_from(self.maximum_pixels).unwrap_or(u32::MAX));
        limits.max_alloc = Some(self.maximum_pixels.saturating_mul(DECODED_BYTES_PER_PIXEL));
        limits
    }
}

/// The size an image declares in its format header.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ImageHeader {
    /// The width the header states, in pixels.
    pub width: u32,
    /// The height the header states, in pixels.
    pub height: u32,
    /// The format the bytes were recognised as, when the decoder recognised one.
    pub format: Option<ImageFormat>,
}

impl ImageHeader {
    /// The number of pixels the header claims, as width times height.
    pub fn pixels(&self) -> u64 {
        u64::from(self.width) * u64::from(self.height)
    }

    /// The format's name as the ABI spells it: `png`, `jpeg`, `bmp`, `gif`, `tiff`, `webp`.
    pub fn format_name(&self) -> &'static str {
        match self.format {
            Some(ImageFormat::Png) => "png",
            Some(ImageFormat::Jpeg) => "jpeg",
            Some(ImageFormat::Bmp) => "bmp",
            Some(ImageFormat::Gif) => "gif",
            Some(ImageFormat::Tiff) => "tiff",
            Some(ImageFormat::WebP) => "webp",
            _ => "unknown",
        }
    }
}

/// Admits an encoded image, or refuses it before any pixel is allocated.
///
/// The byte ceiling is checked first, on the length alone, so an input past it is refused
/// without a single header byte being read. The pixel ceiling is then checked on the
/// dimensions the format header states, which is read without decoding the image data.
///
/// # Errors
/// `invalid_argument` for empty bytes, `image_limit` for an image past either ceiling —
/// naming the size observed and the ceiling — and `image` for bytes no decoder of this
/// build recognises.
pub fn admit(encoded: &[u8], bounds: ImageBounds) -> Result<ImageHeader, ToError> {
    check_encoded_bytes(encoded.len(), bounds)?;

    let header = read_header(encoded)?;
    check_pixels(header.width, header.height, bounds)?;

    Ok(header)
}

/// Refuses an input of `length` bytes past the byte ceiling, without reading any of it.
///
/// Split out so the arithmetic is provable on its own: an input that announces two
/// gigabytes is refused here, and no such buffer is ever materialised to prove it.
pub fn check_encoded_bytes(length: usize, bounds: ImageBounds) -> Result<(), ToError> {
    if length == 0 {
        return Err(ToError::invalid_argument("image is empty"));
    }

    if length > bounds.maximum_bytes {
        return Err(limit_error(
            "bytes",
            length as u64,
            bounds.maximum_bytes as u64,
            format!(
                "the image is {length} bytes, past the {maximum} byte ceiling",
                maximum = bounds.maximum_bytes
            ),
            None,
        ));
    }

    Ok(())
}

/// Refuses dimensions past the pixel ceiling, naming both the size and the ceiling.
pub fn check_pixels(width: u32, height: u32, bounds: ImageBounds) -> Result<(), ToError> {
    let pixels = u64::from(width) * u64::from(height);

    if pixels > bounds.maximum_pixels {
        return Err(limit_error(
            "pixels",
            pixels,
            bounds.maximum_pixels,
            format!(
                "the image is {width} x {height} = {pixels} pixels, past the {maximum} pixel ceiling",
                maximum = bounds.maximum_pixels
            ),
            Some((width, height)),
        ));
    }

    Ok(())
}

/// The dimensions the format header states, read without decoding the image data.
fn read_header(encoded: &[u8]) -> Result<ImageHeader, ToError> {
    let reader = ImageReader::new(Cursor::new(encoded))
        .with_guessed_format()
        .map_err(|error| decode_error(&error))?;

    let format = reader.format();

    let (width, height) = reader
        .into_dimensions()
        .map_err(|error| decode_error(&error))?;

    Ok(ImageHeader {
        width,
        height,
        format,
    })
}

/// Decodes the pixels under the caller's ceilings, re-checking what the decoder produced.
///
/// The header check already ran in [`admit`]; this closes the loop on the pixels actually
/// decoded, because a format can declare one size and a decoder can still be handed
/// something else. The decoder's own limits are set from the same ceilings, so the
/// allocation cannot pass them even if the header lied.
pub fn decode(encoded: &[u8], bounds: ImageBounds) -> Result<image::DynamicImage, ToError> {
    let mut reader = ImageReader::new(Cursor::new(encoded))
        .with_guessed_format()
        .map_err(|error| decode_error(&error))?;

    reader.limits(bounds.decoder_limits());

    let decoded = reader.decode().map_err(|error| decode_error(&error))?;

    check_pixels(decoded.width(), decoded.height(), bounds)?;

    Ok(decoded)
}

/// One refusal, as the error object a caller switches on.
///
/// `limit` names which ceiling fired, `observed` and `maximum` carry the two numbers so a
/// host does not have to parse the sentence, and a pixel refusal also names the width and
/// height it measured.
fn limit_error(
    limit: &str,
    observed: u64,
    maximum: u64,
    message: String,
    dimensions: Option<(u32, u32)>,
) -> ToError {
    let mut value = serde_json::json!({
        "code": crate::ffi::code_name(TO_ERR_IMAGE_LIMIT),
        "message": message,
        "limit": limit,
        "observed": observed,
        "maximum": maximum,
    });

    if let Some((width, height)) = dimensions {
        value["width"] = serde_json::json!(width);
        value["height"] = serde_json::json!(height);
    }

    ToError {
        code: TO_ERR_IMAGE_LIMIT,
        json: value.to_string(),
        traceback: String::new(),
    }
}

/// Bytes no decoder of this build recognises: an `image` error, not a limit refusal.
fn decode_error(error: &dyn std::fmt::Display) -> ToError {
    ToError::new(
        TO_ERR_IMAGE,
        format!("decode image: the bytes are not an image this build can read: {error}"),
    )
}

/// A PNG whose `IHDR` announces `width` x `height`, holding no image data at all.
///
/// The dimensions are the claim under test, so nothing else is needed: a decoder reads
/// this header, reports the size, and would then fail on the empty `IDAT`. Refusing
/// before that point is exactly what a ceiling test asserts, and it is why such a test
/// costs a few kilobytes rather than gigabytes.
///
/// The chunks are written by hand rather than by encoding a real image: a 40 000 x 40 000
/// PNG would be gigabytes, and the whole point of the ceiling is that the decoder never
/// gets that far.
#[cfg(test)]
pub(crate) fn png_declaring(width: u32, height: u32) -> Vec<u8> {
    let mut bytes = vec![0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];

    let mut header = Vec::with_capacity(13);
    header.extend_from_slice(&width.to_be_bytes());
    header.extend_from_slice(&height.to_be_bytes());
    header.extend_from_slice(&[8, 2, 0, 0, 0]); // bit depth, truecolour, no interlace

    push_chunk(&mut bytes, b"IHDR", &header);
    // A decoder reads chunks up to the first `IDAT` to learn the size, so one empty
    // `IDAT` is what makes the header readable; its contents are never inspected.
    push_chunk(&mut bytes, b"IDAT", &[]);
    push_chunk(&mut bytes, b"IEND", &[]);

    bytes
}

/// Appends one PNG chunk, with the CRC the format requires.
#[cfg(test)]
fn push_chunk(bytes: &mut Vec<u8>, kind: &[u8; 4], data: &[u8]) {
    bytes.extend_from_slice(&(data.len() as u32).to_be_bytes());
    bytes.extend_from_slice(kind);
    bytes.extend_from_slice(data);
    bytes.extend_from_slice(&crc32(&[kind.as_slice(), data].concat()).to_be_bytes());
}

/// A WebP whose `VP8L` lossless block announces `width` x `height`, with no picture data.
///
/// A lossless WebP states its size in the first four bytes of the bitstream, after the
/// `0x2F` signature, so the file is a RIFF container around a five-byte header. `image`
/// encodes no WebP, which is why this one is written by hand rather than saved.
#[cfg(test)]
fn webp_declaring(width: u32, height: u32) -> Vec<u8> {
    let packed = (width - 1) & 0x3FFF | (((height - 1) & 0x3FFF) << 14);

    let mut bitstream = vec![0x2F];
    bitstream.extend_from_slice(&packed.to_le_bytes());

    let mut body = Vec::new();
    body.extend_from_slice(b"WEBP");
    body.extend_from_slice(b"VP8L");
    body.extend_from_slice(&(bitstream.len() as u32).to_le_bytes());
    body.extend_from_slice(&bitstream);
    if body.len() % 2 == 1 {
        body.push(0); // RIFF chunks are padded to an even length.
    }

    let mut bytes = Vec::new();
    bytes.extend_from_slice(b"RIFF");
    bytes.extend_from_slice(&(body.len() as u32).to_le_bytes());
    bytes.extend_from_slice(&body);

    bytes
}

/// CRC-32/ISO-HDLC, which is what a PNG chunk carries.
#[cfg(test)]
fn crc32(bytes: &[u8]) -> u32 {
    let mut crc = 0xFFFF_FFFFu32;

    for byte in bytes {
        crc ^= u32::from(*byte);
        for _ in 0..8 {
            let mask = (crc & 1).wrapping_neg();
            crc = (crc >> 1) ^ (0xEDB8_8320 & mask);
        }
    }

    !crc
}

#[cfg(test)]
mod tests {
    use super::*;

    use crate::allocation_probe;

    fn png(width: u32, height: u32) -> Vec<u8> {
        let mut bytes = Vec::new();
        image::RgbImage::from_pixel(width, height, image::Rgb([255, 255, 255]))
            .write_to(&mut Cursor::new(&mut bytes), ImageFormat::Png)
            .expect("a size this test can afford encodes");
        bytes
    }

    #[test]
    fn a_png_that_announces_forty_thousand_square_is_refused_before_a_pixel_is_allocated() {
        let encoded = png_declaring(40_000, 40_000);
        let (result, largest) =
            allocation_probe::measure(|| admit(&encoded, ImageBounds::default()));

        let error = result.expect_err("1.6 gigapixels is past the ceiling");

        assert_eq!(error.code, TO_ERR_IMAGE_LIMIT);

        let document: serde_json::Value =
            serde_json::from_str(&error.json).expect("the error object is JSON");

        assert_eq!(document["limit"], "pixels");
        assert_eq!(document["observed"], 1_600_000_000u64);
        assert_eq!(document["maximum"], 64_000_000u64);
        assert_eq!(document["width"], 40_000);
        assert_eq!(document["height"], 40_000);

        let message = document["message"].as_str().expect("a message");
        assert!(message.contains("40000 x 40000"), "{message}");
        assert!(message.contains("1600000000 pixels"), "{message}");
        assert!(message.contains("64000000 pixel ceiling"), "{message}");

        // The proof that nothing was allocated: decoding 40 000 x 40 000 would need
        // 4.8 GiB as RGB8. What the refusal costs instead is one fixed decoder read
        // buffer, and the next assertion shows it does not grow with the claim.
        assert!(
            largest < 192 * 1024,
            "the refusal allocated {largest} bytes; it must not allocate pixels"
        );

        let (small, small_largest) =
            allocation_probe::measure(|| admit(&png_declaring(320, 200), ImageBounds::default()));

        assert!(small.is_ok());
        assert!(
            largest <= small_largest + 8192,
            "a 40 000 x 40 000 claim allocated {largest} bytes where a 320 x 200 image \
             allocated {small_largest}: the refusal is proportional to the claim"
        );
    }

    #[test]
    fn an_input_of_two_gibibytes_is_refused_on_bytes_without_being_read() {
        let declared = 2 * 1024 * 1024 * 1024;
        let (result, largest) =
            allocation_probe::measure(|| check_encoded_bytes(declared, ImageBounds::default()));

        let error = result.expect_err("two gigabytes is past the byte ceiling");
        assert_eq!(error.code, TO_ERR_IMAGE_LIMIT);

        let document: serde_json::Value =
            serde_json::from_str(&error.json).expect("the error object is JSON");

        assert_eq!(document["limit"], "bytes");
        assert_eq!(document["observed"], 2_147_483_648u64);
        assert_eq!(document["maximum"], 268_435_456u64);

        let message = document["message"].as_str().expect("a message");
        assert!(message.contains("2147483648 bytes"), "{message}");
        assert!(message.contains("268435456 byte ceiling"), "{message}");
        assert!(largest < 1024, "the byte check allocated {largest} bytes");
    }

    #[test]
    fn an_image_past_the_byte_ceiling_is_refused_before_its_header_is_read() {
        let encoded = png(32, 32);
        let bounds = ImageBounds {
            maximum_bytes: encoded.len() - 1,
            ..ImageBounds::default()
        };

        let error = admit(&encoded, bounds).expect_err("one byte past the ceiling");

        assert_eq!(error.code, TO_ERR_IMAGE_LIMIT);
        assert!(error.json.contains("\"limit\":\"bytes\""), "{}", error.json);
    }

    #[test]
    fn a_normal_image_is_admitted_and_decoded() {
        let encoded = png(40, 20);
        let header = admit(&encoded, ImageBounds::default()).expect("a 800-pixel image passes");

        assert_eq!(header.width, 40);
        assert_eq!(header.height, 20);
        assert_eq!(header.format, Some(ImageFormat::Png));
        assert_eq!(header.pixels(), 800);

        let decoded = decode(&encoded, ImageBounds::default()).expect("the pixels decode");
        assert_eq!((decoded.width(), decoded.height()), (40, 20));
    }

    #[test]
    fn a_lowered_ceiling_refuses_an_image_that_passed_the_default() {
        let encoded = png(40, 20);
        assert!(admit(&encoded, ImageBounds::default()).is_ok());

        let bounds = ImageBounds {
            maximum_pixels: 100,
            ..ImageBounds::default()
        };

        let error = admit(&encoded, bounds).expect_err("800 pixels is past a 100-pixel ceiling");

        let document: serde_json::Value =
            serde_json::from_str(&error.json).expect("the error object is JSON");

        assert_eq!(document["observed"], 800u64);
        assert_eq!(document["maximum"], 100u64);

        let message = document["message"].as_str().expect("a message");
        assert!(message.contains("800"), "{message}");
        assert!(message.contains("100"), "{message}");
    }

    #[test]
    fn every_format_this_build_accepts_is_measured_from_its_header() {
        let cases = [
            (image::ImageFormat::Png, "png"),
            (image::ImageFormat::Jpeg, "jpeg"),
            (image::ImageFormat::Bmp, "bmp"),
            (image::ImageFormat::Gif, "gif"),
            (image::ImageFormat::Tiff, "tiff"),
        ];

        for (format, name) in cases {
            let mut bytes = Vec::new();
            image::RgbImage::from_pixel(48, 24, image::Rgb([12, 34, 56]))
                .write_to(&mut Cursor::new(&mut bytes), format)
                .unwrap_or_else(|error| panic!("{name} encodes: {error}"));

            let header = admit(&bytes, ImageBounds::default())
                .unwrap_or_else(|error| panic!("{name} is admitted: {}", error.json));

            assert_eq!(header.width, 48, "{name} width");
            assert_eq!(header.height, 24, "{name} height");
            assert_eq!(header.format, Some(format), "{name} format");
        }

        // WebP has no encoder in `image`, so its fixture is a hand-written header. It is
        // the last of the six formats the ABI accepts, and it is measured the same way.
        let header = admit(&webp_declaring(48, 24), ImageBounds::default())
            .expect("a lossless WebP header is admitted");

        assert_eq!(header.width, 48);
        assert_eq!(header.height, 24);
        assert_eq!(header.format, Some(image::ImageFormat::WebP));
    }

    #[test]
    fn a_webp_past_the_ceiling_is_refused_from_its_header() {
        let error = admit(&webp_declaring(16_000, 16_000), ImageBounds::default())
            .expect_err("256 megapixels is past the ceiling");

        assert_eq!(error.code, TO_ERR_IMAGE_LIMIT);

        let document: serde_json::Value =
            serde_json::from_str(&error.json).expect("the error object is JSON");
        assert_eq!(document["observed"], 256_000_000u64);
    }

    #[test]
    fn the_header_of_every_format_is_read_without_allocating_pixels() {
        for format in [
            image::ImageFormat::Png,
            image::ImageFormat::Jpeg,
            image::ImageFormat::Bmp,
            image::ImageFormat::Gif,
            image::ImageFormat::Tiff,
        ] {
            let mut bytes = Vec::new();
            image::RgbImage::from_pixel(48, 24, image::Rgb([0, 0, 0]))
                .write_to(&mut Cursor::new(&mut bytes), format)
                .expect("the fixture encodes");

            let (header, largest) = allocation_probe::measure(|| read_header(&bytes));

            assert!(header.is_ok(), "{format:?} header is read");
            // 48 x 24 decoded would be 3.5 KiB at worst; the header read must not even
            // produce the pixel buffer, so a small constant is the whole budget.
            assert!(
                largest < 64 * 1024,
                "{format:?} header read allocated {largest} bytes"
            );
        }
    }

    #[test]
    fn bytes_no_decoder_recognises_are_an_image_error_not_a_limit_error() {
        let error = admit(b"this is not an image", ImageBounds::default())
            .expect_err("junk is not an image");

        assert_eq!(error.code, TO_ERR_IMAGE);
    }

    #[test]
    fn an_empty_image_is_an_argument_error() {
        let error = admit(&[], ImageBounds::default()).expect_err("nothing is not an image");
        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn zero_takes_the_default_ceiling() {
        let bounds = ImageBounds::from_options(0, 0).expect("zero means the default");
        assert_eq!(bounds, ImageBounds::default());
    }

    #[test]
    fn a_ceiling_above_the_hard_maximum_is_refused_rather_than_clamped() {
        let error = ImageBounds::from_options(HARD_MAXIMUM_PIXELS + 1, 0)
            .expect_err("a ceiling above the hard maximum is refused");
        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
        assert!(
            error.json.contains("64000000 pixel hard maximum"),
            "{}",
            error.json
        );

        let error = ImageBounds::from_options(0, HARD_MAXIMUM_BYTES + 1)
            .expect_err("a byte ceiling above the hard maximum is refused");
        assert!(error.json.contains("byte hard maximum"), "{}", error.json);
    }

    #[test]
    fn a_ceiling_below_the_hard_maximum_is_honoured() {
        let bounds = ImageBounds::from_options(1_000, 4_096).expect("a lower ceiling is allowed");
        assert_eq!(bounds.maximum_pixels, 1_000);
        assert_eq!(bounds.maximum_bytes, 4_096);
    }

    #[test]
    fn the_decoder_limits_are_derived_from_the_ceilings() {
        let bounds = ImageBounds {
            maximum_pixels: 1_000,
            maximum_bytes: 4_096,
        };
        let limits = bounds.decoder_limits();

        assert_eq!(limits.max_image_width, Some(1_000));
        assert_eq!(limits.max_image_height, Some(1_000));
        assert_eq!(limits.max_alloc, Some(4_000));
    }
}
