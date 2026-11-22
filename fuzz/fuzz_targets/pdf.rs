#![no_main]

//! PDF: a malformed document rendered the way `PdfRaster` renders it, under a small ceiling.

use libfuzzer_sys::fuzz_target;

fuzz_target!(|data: &[u8]| {
    sharp_text_pdf::fuzz_api::render(data);
});
