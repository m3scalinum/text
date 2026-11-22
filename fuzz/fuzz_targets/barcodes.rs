#![no_main]

//! Barcodes: a PNG decoded into luma, then every symbology the crate ships searched for it.

use libfuzzer_sys::fuzz_target;
use sharp_text_barcodes::decode::{DecodeBounds, decode_png_all_within};

/// Small enough that one iteration stays in the milliseconds: a fuzz input is a few kilobytes, and
/// a 4-megapixel ceiling leaves the detector's per-pixel work bounded without changing its code.
const BOUNDS: DecodeBounds = DecodeBounds {
    maximum_pixels: 4_000_000,
    maximum_bytes: 4 * 1024 * 1024,
};

fuzz_target!(|data: &[u8]| {
    let _ = decode_png_all_within(data, BOUNDS);
});
