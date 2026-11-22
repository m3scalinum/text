#![no_main]

//! The XML policy: one part of a package, parsed exactly as `xml::parse` parses it.

use libfuzzer_sys::fuzz_target;

fuzz_target!(|data: &[u8]| {
    sharp_text_docx::fuzz_api::parse_xml(data);
});
