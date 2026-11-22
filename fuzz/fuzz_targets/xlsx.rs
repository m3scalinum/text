#![no_main]

//! XLSX: arbitrary bytes handed to `calamine`, then shaped into the workbook AST.

use libfuzzer_sys::fuzz_target;

fuzz_target!(|data: &[u8]| {
    sharp_text_xlsx::fuzz_api::extract(data);
});
