#![no_main]

//! The DOCX container: a zip a stranger wrote, opened and read part by part.

use libfuzzer_sys::fuzz_target;

fuzz_target!(|data: &[u8]| {
    sharp_text_docx::fuzz_api::extract(data);
});
