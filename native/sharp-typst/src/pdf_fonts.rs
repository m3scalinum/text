//! What a PDF export embedded as fonts: the faces, their byte cost and whether they are subsets.
//!
//! `typst-pdf` 0.15.1 has no switch for font subsetting — krilla subsets every face
//! unconditionally, so the caller cannot ask for whole fonts, and the option the board item
//! imagined does not exist. What can be answered is the other half: which fonts the file carries,
//! how many bytes each one costs, and whether the face is a subset. That is what this module reads
//! back out of the bytes the exporter just produced (Spec 03 §13).
//!
//! The walker is deliberately small: it follows `obj` headers, skips stream bodies by their
//! `/Length`, and reads the font descriptors. It never builds a document model, and it cannot be
//! fooled by a `stream` keyword inside binary font data because the body is skipped before the
//! scan resumes.

use std::collections::HashMap;

use serde_json::{Value, json};

/// Bytes a keyword needs to be a keyword: not preceded by a name or a number.
fn is_delimiter(byte: u8) -> bool {
    matches!(
        byte,
        b' ' | b'\t' | b'\r' | b'\n' | b'/' | b'<' | b'>' | b'[' | b']' | b'(' | b')'
    )
}

/// Finds `word` at or after `from`, ignoring an occurrence that is the tail of a longer word
/// (`stream` inside `endstream`, `obj` inside `endobj`).
fn find_keyword(data: &[u8], from: usize, word: &[u8]) -> Option<usize> {
    let mut pos = from;

    while pos + word.len() <= data.len() {
        let found = data[pos..].windows(word.len()).position(|w| w == word)? + pos;

        let ends = found + word.len();
        // `FontFile` must not match `FontFile2`, and `Length` must not match `Length1`.
        let is_word = (found == 0 || is_delimiter(data[found - 1]))
            && (ends == data.len() || !data[ends].is_ascii_alphanumeric());

        if is_word {
            return Some(found);
        }

        pos = found + 1;
    }

    None
}

/// Reads the object number of the header that ends at `at`, or `None` when the `obj` keyword is
/// part of something else.
///
/// A header is `<number> <generation> obj`. Walking backwards keeps the scan independent of how
/// the file is laid out: `pretty` only changes the whitespace around it.
fn object_number(data: &[u8], at: usize) -> Option<u32> {
    if at == 0 {
        return None;
    }

    let mut end = at;
    while end > 0 && matches!(data[end - 1], b' ' | b'\t' | b'\r' | b'\n') {
        end -= 1;
    }

    // Generation number.
    let generation_start = digits_start(data, end)?;
    let mut cursor = generation_start;

    while cursor > 0 && matches!(data[cursor - 1], b' ' | b'\t' | b'\r' | b'\n') {
        cursor -= 1;
    }

    let number_start = digits_start(data, cursor)?;
    let text = std::str::from_utf8(&data[number_start..cursor]).ok()?;

    text.parse().ok()
}

/// Start of the run of ASCII digits that ends at `end`.
fn digits_start(data: &[u8], end: usize) -> Option<usize> {
    let mut start = end;

    while start > 0 && data[start - 1].is_ascii_digit() {
        start -= 1;
    }

    (start < end).then_some(start)
}

/// One object of the file: its dictionary text and, for a stream, the length of its body.
struct RawObject<'a> {
    number: u32,
    dict: &'a [u8],
    stream_len: Option<usize>,
}

/// Walks the objects of a PDF, skipping stream bodies by their declared length.
fn objects(data: &[u8]) -> Vec<RawObject<'_>> {
    let mut found = Vec::new();
    let mut pos = 0;

    while let Some(at) = find_keyword(data, pos, b"obj") {
        pos = at + 3;

        let Some(number) = object_number(data, at) else {
            continue;
        };

        let stream_at = find_keyword(data, pos, b"stream");
        let endobj_at = find_keyword(data, pos, b"endobj");

        let Some(stream_at) = stream_at.filter(|s| endobj_at.is_none_or(|e| *s < e)) else {
            let end = endobj_at.unwrap_or(data.len());
            found.push(RawObject {
                number,
                dict: &data[pos..end],
                stream_len: None,
            });
            pos = end + b"endobj".len();
            continue;
        };

        let dict = &data[pos..stream_at];
        let body = skip_eol(data, stream_at + b"stream".len());

        // The declared length is the fast path; a stream written with an indirect `/Length` is
        // delimited by `endstream` instead, and its span is then the best answer available.
        let (stream_len, resume) = match declared_length(dict) {
            Some(len)
                if find_keyword(data, body + len, b"endstream")
                    .is_some_and(|end| end <= body + len + 2) =>
            {
                (Some(len), body + len)
            }
            _ => match find_keyword(data, body, b"endstream") {
                Some(end) => (Some(end.saturating_sub(body)), end),
                None => (None, body),
            },
        };

        found.push(RawObject {
            number,
            dict,
            stream_len,
        });

        pos = resume;
    }

    found
}

/// The `/Length` of a stream dictionary, when it is a direct number.
fn declared_length(dict: &[u8]) -> Option<usize> {
    let at = find_keyword(dict, 0, b"Length")?;
    let mut pos = at + b"Length".len();

    while pos < dict.len() && matches!(dict[pos], b' ' | b'\t' | b'\r' | b'\n') {
        pos += 1;
    }

    let start = pos;
    while pos < dict.len() && dict[pos].is_ascii_digit() {
        pos += 1;
    }

    if start == pos {
        return None;
    }

    // `12 0 R` is a reference to another object, not the length itself.
    let rest = &dict[pos..];
    let rest = String::from_utf8_lossy(rest);
    let mut words = rest.split_whitespace();
    if words.next() == Some("0") && words.next() == Some("R") {
        return None;
    }

    std::str::from_utf8(&dict[start..pos]).ok()?.parse().ok()
}

/// Skips one end-of-line after a keyword: `stream` is followed by `\n` or `\r\n`.
fn skip_eol(data: &[u8], at: usize) -> usize {
    let mut pos = at;

    if pos < data.len() && data[pos] == b'\r' {
        pos += 1;
    }
    if pos < data.len() && data[pos] == b'\n' {
        pos += 1;
    }

    pos
}

/// The value of a `/Name` entry, without its leading slash.
fn name_value<'a>(dict: &'a [u8], key: &[u8]) -> Option<&'a str> {
    let at = find_keyword(dict, 0, key)?;
    let mut pos = at + key.len();

    while pos < dict.len() && matches!(dict[pos], b' ' | b'\t' | b'\r' | b'\n') {
        pos += 1;
    }

    if dict.get(pos) != Some(&b'/') {
        return None;
    }
    pos += 1;

    let start = pos;
    while pos < dict.len() && !is_delimiter(dict[pos]) {
        pos += 1;
    }

    std::str::from_utf8(&dict[start..pos]).ok()
}

/// The object number a `/Key 12 0 R` entry points at.
fn reference(dict: &[u8], key: &[u8]) -> Option<u32> {
    let at = find_keyword(dict, 0, key)?;
    let mut pos = at + key.len();

    while pos < dict.len() && matches!(dict[pos], b' ' | b'\t' | b'\r' | b'\n') {
        pos += 1;
    }

    let start = pos;
    while pos < dict.len() && dict[pos].is_ascii_digit() {
        pos += 1;
    }

    std::str::from_utf8(&dict[start..pos]).ok()?.parse().ok()
}

/// A name that carries a six-letter subset tag (`ABCDEF+Arial-Bold`), split in two.
fn split_subset_tag(name: &str) -> (Option<&str>, &str) {
    match name.split_once('+') {
        Some((tag, family)) if tag.len() == 6 && tag.bytes().all(|b| b.is_ascii_uppercase()) => {
            (Some(tag), family)
        }
        _ => (None, name),
    }
}

/// Reads the fonts a PDF embeds: one entry per `/FontFile*` stream, with its encoded byte cost.
///
/// The bytes are the stream's `/Length`, which is what the file pays — the same font contributes
/// more once inflated, and the caller who wants a working set is not asking that question here.
pub fn inventory(pdf: &[u8]) -> String {
    let objects = objects(pdf);
    let mut streams: HashMap<u32, &RawObject<'_>> = HashMap::new();
    let mut descriptors: Vec<&RawObject<'_>> = Vec::new();

    for object in &objects {
        streams.insert(object.number, object);

        // A font descriptor is a plain dictionary: it names the face and points at the stream that
        // carries it. The font dictionary beside it carries `/BaseFont` instead.
        if find_keyword(object.dict, 0, b"FontName").is_some()
            && (find_keyword(object.dict, 0, b"FontFile2").is_some()
                || find_keyword(object.dict, 0, b"FontFile3").is_some()
                || find_keyword(object.dict, 0, b"FontFile").is_some())
        {
            descriptors.push(object);
        }
    }

    let mut fonts: Vec<Value> = Vec::new();
    let mut font_bytes = 0usize;

    for descriptor in descriptors {
        let Some(raw_name) = name_value(descriptor.dict, b"FontName") else {
            continue;
        };
        let (tag, family) = split_subset_tag(raw_name);

        let (format, key) = if find_keyword(descriptor.dict, 0, b"FontFile3").is_some() {
            let subtype = reference(descriptor.dict, b"FontFile3")
                .and_then(|number| streams.get(&number))
                .and_then(|stream| name_value(stream.dict, b"Subtype"));

            let format = match subtype {
                Some("OpenType") => "opentype",
                _ => "cff",
            };

            (format, b"FontFile3".as_slice())
        } else if find_keyword(descriptor.dict, 0, b"FontFile2").is_some() {
            ("truetype", b"FontFile2".as_slice())
        } else {
            ("type1", b"FontFile".as_slice())
        };

        let bytes = reference(descriptor.dict, key)
            .and_then(|number| streams.get(&number))
            .and_then(|stream| stream.stream_len)
            .unwrap_or(0);

        font_bytes += bytes;

        fonts.push(json!({
            "name": raw_name,
            "family": family,
            "subset": tag.is_some(),
            "subsetTag": tag,
            "format": format,
            "bytes": bytes,
        }));
    }

    json!({
        "bytes": pdf.len(),
        "fontBytes": font_bytes,
        "fonts": fonts,
    })
    .to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A PDF shaped like the exporter's: two embedded fonts under subset tags, one bare stream
    /// that points at no object.
    fn sample() -> Vec<u8> {
        let truetype = b"true type payload";
        let cff = b"cff payload";

        format!(
            "%PDF-1.7\n\
             1 0 obj\n<< /Type /Catalog >>\nendobj\n\
             5 0 obj\n<< /Type /FontDescriptor /FontName /QKJXZB+NewCM10-Regular /FontFile2 9 0 R >>\nendobj\n\
             9 0 obj\n<< /Length {} >>\nstream\n{}\nendstream\nendobj\n\
             6 0 obj\n<< /FontName /ABCDEF+Mono /FontFile3 10 0 R >>\nendobj\n\
             10 0 obj\n<< /Length {} /Subtype /CIDFontType0C >>\nstream\n{}\nendstream\nendobj\n\
             7 0 obj\n<< /FontName /Plain /FontFile 11 0 R >>\nendobj\n\
             trailer\n<< /Root 1 0 R >>\n%%EOF\n",
            truetype.len(),
            String::from_utf8_lossy(truetype),
            cff.len(),
            String::from_utf8_lossy(cff),
        )
        .into_bytes()
    }

    #[test]
    fn finds_every_embedded_font_and_its_bytes() {
        let report: Value = serde_json::from_str(&inventory(&sample())).expect("inventory is JSON");
        let fonts = report["fonts"].as_array().expect("fonts");

        assert_eq!(3, fonts.len());
        assert_eq!(
            (b"true type payload".len() + b"cff payload".len()) as u64,
            report["fontBytes"].as_u64().expect("font bytes")
        );

        assert_eq!("QKJXZB+NewCM10-Regular", fonts[0]["name"]);
        assert_eq!("NewCM10-Regular", fonts[0]["family"]);
        assert_eq!("QKJXZB", fonts[0]["subsetTag"]);
        assert_eq!(true, fonts[0]["subset"]);
        assert_eq!("truetype", fonts[0]["format"]);
        assert_eq!(17, fonts[0]["bytes"]);

        assert_eq!("cff", fonts[1]["format"]);
        assert_eq!(11, fonts[1]["bytes"]);

        assert_eq!(false, fonts[2]["subset"]);
        assert_eq!("type1", fonts[2]["format"]);
        assert_eq!(0, fonts[2]["bytes"]);
    }

    #[test]
    fn a_stream_body_cannot_pose_as_an_object() {
        // A font stream whose bytes contain a fake font object: the walker must skip the body, or
        // it reports fonts the file does not have.
        let body = "9 0 obj\n<< /FontName /FAKEAA+Ghost /FontFile2 2 0 R >>\n";
        let pdf = format!(
            "%PDF-1.7\n\
             1 0 obj\n<< /FontName /AAAAAA+Real /FontFile2 2 0 R >>\nendobj\n\
             2 0 obj\n<< /Length {} >>\nstream\n{}\nendstream\nendobj\n",
            body.len(),
            body,
        );

        let report: Value = serde_json::from_str(&inventory(pdf.as_bytes())).expect("JSON");
        let fonts = report["fonts"].as_array().expect("fonts");

        assert_eq!(1, fonts.len());
        assert_eq!("AAAAAA+Real", fonts[0]["name"]);
    }
}
