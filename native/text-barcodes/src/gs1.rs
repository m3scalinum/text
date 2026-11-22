//! GS1: Application Identifiers, the GS1-128 payload form and the Digital Link URL.
//!
//! GS1 data is a *string of elements*, each one an Application Identifier followed by its value:
//! `01` and a fourteen-digit GTIN, `10` and a batch number, `17` and an expiry date. A symbology
//! carries that string; it does not carry the structure. This module is the structure — one table
//! of the AIs this build understands, a parser that turns a raw string into named elements, and the
//! two serialisations GS1 defines on top of the elements: the GS1-128 payload (FNC1 and field
//! separators) and the Digital Link URL.
//!
//! An AI outside the table is an error, never a guess: silently treating `99` as free text is how a
//! label ends up on the wrong pallet.

use crate::ffi::{BtError, escape_json};

/// One Application Identifier this build understands.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AiSpec {
    /// The identifier, as it appears in the data: two to four digits.
    pub ai: &'static str,
    /// The GS1 short name, so a caller can render a label without its own dictionary.
    pub title: &'static str,
    /// Shortest value, in characters.
    pub min: usize,
    /// Longest value, in characters. Equal to `min` for a fixed-length AI.
    pub max: usize,
    /// Whether the value is digits only.
    pub numeric: bool,
    /// Whether the last character is a GS1 check digit this parser verifies.
    pub check_digit: bool,
    /// Whether the AI is a Digital Link primary key, which goes in the URL path.
    pub primary_key: bool,
}

const fn fixed(
    ai: &'static str,
    title: &'static str,
    length: usize,
    numeric: bool,
    check_digit: bool,
) -> AiSpec {
    AiSpec {
        ai,
        title,
        min: length,
        max: length,
        numeric,
        check_digit,
        primary_key: false,
    }
}

const fn variable(ai: &'static str, title: &'static str, min: usize, max: usize) -> AiSpec {
    AiSpec {
        ai,
        title,
        min,
        max,
        numeric: false,
        check_digit: false,
        primary_key: false,
    }
}

const fn primary(mut spec: AiSpec) -> AiSpec {
    spec.primary_key = true;
    spec
}

/// The Application Identifiers this build parses, in numeric order.
///
/// Sourced from the GS1 General Specifications, section 7.8 (the AI tables). A table entry is a
/// promise: the parser accepts that AI, enforces its length, and checks its check digit when the
/// standard gives it one. Everything else is refused by name. The list is deliberately a working
/// subset — the identifiers a document actually carries — and Spec 20 §8 lists it in full.
pub const AI_TABLE: &[AiSpec] = &[
    primary(fixed("00", "SSCC", 18, true, true)),
    primary(fixed("01", "GTIN", 14, true, true)),
    fixed("02", "CONTENT", 14, true, true),
    variable("10", "BATCH/LOT", 1, 20),
    fixed("11", "PROD DATE", 6, true, false),
    fixed("12", "DUE DATE", 6, true, false),
    fixed("13", "PACK DATE", 6, true, false),
    fixed("15", "BEST BEFORE", 6, true, false),
    fixed("16", "SELL BY", 6, true, false),
    fixed("17", "USE BY/EXPIRY", 6, true, false),
    fixed("20", "VARIANT", 2, true, false),
    variable("21", "SERIAL", 1, 20),
    variable("22", "CPV", 1, 20),
    variable("30", "VAR. COUNT", 1, 8),
    variable("37", "COUNT", 1, 8),
    variable("235", "TPI", 1, 28),
    variable("240", "ADDITIONAL ID", 1, 30),
    variable("241", "CUST. PART NO.", 1, 30),
    fixed("242", "MTO VARIANT", 6, true, false),
    variable("243", "PACK COMPONENT", 1, 20),
    variable("250", "SECONDARY SERIAL", 1, 30),
    variable("251", "SOURCE ENTITY", 1, 30),
    variable("254", "GLN EXTENSION", 1, 20),
    variable("400", "ORDER NUMBER", 1, 30),
    variable("401", "GINC", 1, 30),
    fixed("402", "GSIN", 17, true, true),
    variable("403", "ROUTE", 1, 30),
    fixed("410", "SHIP TO GLN", 13, true, true),
    fixed("411", "BILL TO GLN", 13, true, true),
    fixed("412", "PURCHASE FROM GLN", 13, true, true),
    fixed("413", "SHIP FOR GLN", 13, true, true),
    fixed("414", "LOCATION GLN", 13, true, true),
    fixed("415", "INVOICE FROM GLN", 13, true, true),
    fixed("416", "PROD/SERV LOCATION GLN", 13, true, true),
    fixed("417", "PARTY GLN", 13, true, true),
    variable("420", "POSTAL CODE", 1, 20),
    variable("421", "CC WITH ISO COUNTRY", 3, 12),
    fixed("422", "ORIGIN COUNTRY", 3, true, false),
    variable("423", "INITIAL PROCESS COUNTRY", 3, 15),
    fixed("424", "PROCESS COUNTRY", 3, true, false),
    fixed("426", "FULL PROCESS COUNTRY", 3, true, false),
    fixed("7001", "NSN", 13, true, true),
    variable("7002", "MEAT CUT", 1, 30),
    fixed("7003", "EXPIRY DATE/TIME", 10, true, false),
    variable("7004", "ACTIVE POTENCY", 1, 4),
    variable("7005", "CATCH AREA", 1, 12),
    fixed("7006", "FIRST FREEZE DATE", 6, true, false),
    variable("7007", "HARVEST DATE", 6, 12),
    variable("7008", "AQUATIC SPECIES", 1, 3),
    variable("7009", "FISHING GEAR", 1, 10),
    variable("7010", "PRODUCTION METHOD", 1, 2),
    primary(variable("7020", "REF PROCESSOR", 1, 20)),
    fixed("7040", "UIC EXTENSION", 4, true, false),
    variable("710", "NHRN PZN", 1, 20),
    variable("711", "NHRN CIP", 1, 20),
    variable("712", "NHRN CN", 1, 20),
    variable("713", "NHRN DRN", 1, 20),
    variable("714", "NHRN AIM", 1, 20),
    variable("715", "NHRN NDC", 1, 20),
    fixed("8001", "DIMENSIONS", 14, true, false),
    variable("8002", "CIV", 1, 20),
    variable("8003", "GRAI", 14, 30),
    variable("8004", "GIAI", 1, 30),
    fixed("8005", "PRICE PER UNIT", 6, true, false),
    primary(fixed("8006", "ITIP", 18, true, true)),
    variable("8007", "IBAN", 1, 34),
    variable("8008", "PROD TIME", 8, 12),
    primary(variable("8010", "CPID", 1, 30)),
    variable("8011", "CPID SERIAL", 1, 12),
    variable("8012", "VERSION", 1, 20),
    variable("8013", "GMN", 1, 30),
    primary(fixed("8017", "GSRN PROVIDER", 18, true, true)),
    primary(fixed("8018", "GSRN RECIPIENT", 18, true, true)),
    variable("8019", "SRIN", 1, 10),
    fixed("8020", "REFUND RECEIPT", 25, true, false),
    fixed("8026", "ITIP CONTENT", 18, true, false),
];

/// The AI table, for a caller that wants to render or validate on its own side.
pub fn table() -> &'static [AiSpec] {
    AI_TABLE
}

/// The specification of `ai`, or `None` when this build does not understand it.
pub fn spec(ai: &str) -> Option<&'static AiSpec> {
    AI_TABLE.iter().find(|candidate| candidate.ai == ai)
}

/// The field separator GS1 uses in the decoded form: ASCII 29, "group separator".
pub const GS: char = '\u{1d}';

/// The FNC1 character rxing's Code 128 writer understands, which *is* the GS1-128 marker.
pub const FNC1: char = '\u{f1}';

/// One parsed element: an AI and the value that follows it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Element {
    pub ai: String,
    pub value: String,
}

impl Element {
    /// The GS1 title of the AI, or the AI itself when the table has no entry for it.
    pub fn title(&self) -> &'static str {
        spec(&self.ai).map(|s| s.title).unwrap_or("UNKNOWN")
    }

    /// `(01)09501101530003`, the human-readable form a label prints.
    pub fn human_readable(&self) -> String {
        format!("({}){}", self.ai, self.value)
    }
}

/// A parsed GS1 element string.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Gs1Data {
    pub elements: Vec<Element>,
}

impl Gs1Data {
    /// Parses a GS1 element string.
    ///
    /// Three spellings are accepted, because the same data reaches a caller in all three: the raw
    /// form with `\x1D` separators as a reader returns it, the raw form a symbol carried (any
    /// leading symbology identifier such as `]C1` is dropped), and the human-readable form,
    /// `(01)…(10)…`, which is what a label prints and what a person types.
    pub fn parse(raw: &str) -> Result<Self, BtError> {
        let text = strip_identifier(raw);

        if text.is_empty() {
            return Err(gs1_error("the GS1 data is empty"));
        }

        let elements = if text.starts_with('(') {
            parse_human_readable(text)?
        } else {
            parse_raw(text)?
        };

        let data = Self { elements };
        data.validate()?;
        Ok(data)
    }

    /// The canonical raw form: the identifiers and values, concatenated, with a field separator
    /// after every value whose length is not fixed. This is the string GS1-128 carries, without
    /// the leading FNC1.
    pub fn raw(&self) -> String {
        let mut out = String::new();
        for (index, element) in self.elements.iter().enumerate() {
            out.push_str(&element.ai);
            out.push_str(&element.value);

            let last = index + 1 == self.elements.len();
            if !last && !is_fixed(&element.ai) {
                out.push(GS);
            }
        }
        out
    }

    /// The printed form: `(01)09501101530003(10)LOT-42`.
    pub fn human_readable(&self) -> String {
        self.elements
            .iter()
            .map(Element::human_readable)
            .collect::<String>()
    }

    /// The Code 128 contents that make this a GS1-128 symbol: FNC1 first, then every element, and
    /// FNC1 in place of the separator after each variable-length value.
    pub fn to_gs1_128(&self) -> String {
        let mut out = String::with_capacity(self.raw().len() + 2);
        out.push(FNC1);
        for (index, element) in self.elements.iter().enumerate() {
            out.push_str(&element.ai);
            out.push_str(&element.value);

            let last = index + 1 == self.elements.len();
            if !last && !is_fixed(&element.ai) {
                out.push(FNC1);
            }
        }
        out
    }

    /// Builds the Digital Link URL for these elements: primary keys in the path, every other
    /// element as a query parameter.
    ///
    /// `base` is the resolver the URL hangs off — `https://example.com/01` style prefixes work as
    /// well as a bare origin, because the Digital Link grammar only cares about what follows.
    pub fn to_digital_link(&self, base: &str) -> Result<String, BtError> {
        let base = base.trim_end_matches('/');

        if !(base.starts_with("http://") || base.starts_with("https://")) {
            return Err(gs1_error(format!(
                "a Digital Link base must be an absolute http or https URL, got '{base}'"
            )));
        }

        if base.contains('?') || base.contains('#') {
            return Err(gs1_error(format!(
                "a Digital Link base must not carry a query or a fragment, got '{base}'"
            )));
        }

        let mut path = String::new();
        let mut query: Vec<String> = Vec::new();

        for element in &self.elements {
            let ai_spec = spec(&element.ai).ok_or_else(|| unknown_ai(&element.ai))?;

            if ai_spec.primary_key {
                path.push('/');
                path.push_str(&element.ai);
                path.push('/');
                path.push_str(&percent_encode(&element.value));
            } else {
                query.push(format!("{}={}", element.ai, percent_encode(&element.value)));
            }
        }

        if path.is_empty() {
            return Err(gs1_error(
                "a Digital Link needs a primary key in the path: 00, 01, 8006, 8010, 8017 or 8018",
            ));
        }

        let mut url = format!("{base}{path}");
        if !query.is_empty() {
            url.push('?');
            url.push_str(&query.join("&"));
        }

        Ok(url)
    }

    /// Parses a Digital Link URL back into its elements.
    pub fn from_digital_link(url: &str) -> Result<Self, BtError> {
        let rest = url
            .strip_prefix("https://")
            .or_else(|| url.strip_prefix("http://"))
            .ok_or_else(|| {
                gs1_error(format!(
                    "a Digital Link must be an absolute http or https URL, got '{url}'"
                ))
            })?;

        let path_start = rest
            .find('/')
            .ok_or_else(|| gs1_error(format!("the Digital Link '{url}' carries no AI path")))?;

        let path_and_query = &rest[path_start..];
        let (path, query) = match path_and_query.split_once('?') {
            Some((path, query)) => (path, query),
            None => (path_and_query, ""),
        };

        // A fragment is not part of the identification, so it is dropped rather than refused.
        let query = query.split('#').next().unwrap_or_default();

        let segments: Vec<&str> = path
            .trim_matches('/')
            .split('/')
            .filter(|segment| !segment.is_empty())
            .collect();

        if segments.is_empty() {
            return Err(gs1_error(format!(
                "the Digital Link '{url}' carries no AI path"
            )));
        }

        // A Digital Link may hang off a resolver prefix — `https://example.com/gs1/01/…` — so the
        // pairs start at the first segment that names an identifier this build knows.
        let start = segments
            .iter()
            .position(|segment| spec(segment).is_some())
            .ok_or_else(|| {
                gs1_error(format!(
                    "the Digital Link '{url}' names no Application Identifier this build knows"
                ))
            })?;

        let pairs = &segments[start..];
        if pairs.len() % 2 != 0 {
            return Err(gs1_error(format!(
                "the Digital Link path '{}' is not a sequence of identifier and value pairs",
                pairs.join("/")
            )));
        }

        let mut elements = Vec::with_capacity(pairs.len() / 2);
        for pair in pairs.chunks(2) {
            elements.push(Element {
                ai: pair[0].to_string(),
                value: percent_decode(pair[1])?,
            });
        }

        for parameter in query.split('&').filter(|part| !part.is_empty()) {
            let (ai, value) = parameter.split_once('=').ok_or_else(|| {
                gs1_error(format!(
                    "the Digital Link query parameter '{parameter}' is not an identifier and a value"
                ))
            })?;
            elements.push(Element {
                ai: ai.to_string(),
                value: percent_decode(value)?,
            });
        }

        let data = Self { elements };
        data.validate()?;
        Ok(data)
    }

    /// The data as JSON, the shape the ABI carries: the two rendered strings and the elements.
    pub fn json(&self) -> String {
        let elements: Vec<String> = self
            .elements
            .iter()
            .map(|element| {
                format!(
                    "{{\"ai\":\"{}\",\"title\":\"{}\",\"value\":\"{}\",\"primaryKey\":{}}}",
                    element.ai,
                    element.title(),
                    escape_json(&element.value),
                    spec(&element.ai).is_some_and(|s| s.primary_key)
                )
            })
            .collect();

        format!(
            "{{\"raw\":\"{}\",\"humanReadable\":\"{}\",\"elements\":[{}]}}",
            escape_json(&self.raw()),
            escape_json(&self.human_readable()),
            elements.join(",")
        )
    }

    /// Checks every element against the table: known AI, length inside the range the standard
    /// gives it, digits where the standard says digits, check digit where it has one, and no AI
    /// twice.
    fn validate(&self) -> Result<(), BtError> {
        if self.elements.is_empty() {
            return Err(gs1_error("the GS1 data holds no element"));
        }

        for (index, element) in self.elements.iter().enumerate() {
            if !element.ai.chars().all(|c| c.is_ascii_digit()) {
                return Err(gs1_error(format!(
                    "'{}' is not an Application Identifier: an AI is two to four digits",
                    element.ai
                )));
            }

            let spec = spec(&element.ai).ok_or_else(|| unknown_ai(&element.ai))?;
            let length = element.value.chars().count();

            if length < spec.min || length > spec.max {
                let expected = if spec.min == spec.max {
                    format!("exactly {}", spec.min)
                } else {
                    format!("{} to {}", spec.min, spec.max)
                };
                return Err(gs1_error(format!(
                    "AI {} ({}) expects {expected} characters, the value has {length}",
                    spec.ai, spec.title
                )));
            }

            if spec.numeric && !element.value.chars().all(|c| c.is_ascii_digit()) {
                return Err(gs1_error(format!(
                    "AI {} ({}) takes digits only, got '{}'",
                    spec.ai, spec.title, element.value
                )));
            }

            if spec.check_digit && !check_digit_is_valid(&element.value) {
                return Err(gs1_error(format!(
                    "AI {} ({}) has a check digit that does not match: '{}'",
                    spec.ai, spec.title, element.value
                )));
            }

            if self.elements[index + 1..]
                .iter()
                .any(|other| other.ai == element.ai)
            {
                return Err(gs1_error(format!(
                    "AI {} ({}) appears twice; a GS1 element string names each AI once",
                    spec.ai, spec.title
                )));
            }
        }

        Ok(())
    }
}

/// Whether the last character of `value` is the GS1 mod-10 check digit of the rest.
///
/// The GS1 algorithm weights the digits from the right with 3, 1, 3, 1… and takes the difference
/// to the next multiple of ten. It is the same for every GS1 key, which is why one function serves
/// them all.
pub fn check_digit_is_valid(value: &str) -> bool {
    let digits: Vec<u32> = match value
        .chars()
        .map(|c| c.to_digit(10))
        .collect::<Option<Vec<u32>>>()
    {
        Some(digits) => digits,
        None => return false,
    };

    let Some((&check, body)) = digits.split_last() else {
        return false;
    };

    if body.is_empty() {
        return false;
    }

    let mut sum = 0_u32;
    for (index, digit) in body.iter().rev().enumerate() {
        sum += if index % 2 == 0 { digit * 3 } else { *digit };
    }

    (10 - (sum % 10)) % 10 == check
}

/// The check digit `body` needs, for a caller building a GTIN.
pub fn check_digit(body: &str) -> Option<u32> {
    let mut sum = 0_u32;
    for (index, c) in body.chars().rev().enumerate() {
        let digit = c.to_digit(10)?;
        sum += if index % 2 == 0 { digit * 3 } else { digit };
    }
    Some((10 - (sum % 10)) % 10)
}

fn is_fixed(ai: &str) -> bool {
    spec(ai).is_some_and(|s| s.min == s.max)
}

fn unknown_ai(ai: &str) -> BtError {
    let known: Vec<&str> = AI_TABLE.iter().map(|s| s.ai).collect();
    gs1_error(format!(
        "AI '{ai}' is not one this build understands; known: {}",
        known.join(", ")
    ))
}

fn gs1_error(message: impl Into<String>) -> BtError {
    BtError::gs1(message)
}

/// Drops a leading symbology identifier (`]C1`, `]Q3`, `]d2`…), which a reader puts in front of
/// the data to name the symbology it read.
fn strip_identifier(raw: &str) -> &str {
    if let Some(rest) = raw.strip_prefix(']') {
        let mut chars = rest.chars();
        if let (Some(kind), Some(class)) = (chars.next(), chars.next()) {
            if kind.is_ascii_alphabetic() && class.is_ascii_alphanumeric() {
                return chars.as_str();
            }
        }
    }
    raw
}

fn parse_raw(text: &str) -> Result<Vec<Element>, BtError> {
    let mut elements = Vec::new();

    for segment in text.split(['\u{1d}', '\u{f1}']).filter(|s| !s.is_empty()) {
        let mut rest = segment;

        while !rest.is_empty() {
            let (ai, value) = split_element(rest)?;
            elements.push(Element {
                ai: ai.to_string(),
                value: value.to_string(),
            });

            rest = &rest[ai.len() + value.len()..];
        }
    }

    Ok(elements)
}

/// Splits the longest known AI off `text`, and the value that follows it.
fn split_element(text: &str) -> Result<(&str, &str), BtError> {
    let digits = text.chars().take_while(char::is_ascii_digit).count();
    if digits < 2 {
        return Err(gs1_error(format!(
            "expected an Application Identifier at the start of '{text}'"
        )));
    }

    // Longest first: `8006` is one identifier, not `80` and `06`.
    for length in (2..=digits.min(4)).rev() {
        let ai = &text[..length];
        let Some(spec) = spec(ai) else {
            continue;
        };

        let rest = &text[length..];
        let value = if spec.min == spec.max {
            let available = rest.chars().count();
            if available < spec.min {
                return Err(gs1_error(format!(
                    "AI {} ({}) expects exactly {} characters, only {available} follow it",
                    spec.ai, spec.title, spec.min
                )));
            }
            &rest[..char_boundary(rest, spec.min)]
        } else {
            rest
        };

        return Ok((ai, value));
    }

    let candidate = &text[..digits.min(4)];
    Err(unknown_ai(candidate))
}

/// Parses the printed form, `(01)09501101530003(10)LOT-42`.
fn parse_human_readable(text: &str) -> Result<Vec<Element>, BtError> {
    let mut elements = Vec::new();
    let mut rest = text;

    while !rest.is_empty() {
        let rest_after_open = rest.strip_prefix('(').ok_or_else(|| {
            gs1_error(format!(
                "expected '(' before an Application Identifier in '{rest}'"
            ))
        })?;

        let close = rest_after_open.find(')').ok_or_else(|| {
            gs1_error(format!(
                "expected ')' after the Application Identifier in '({rest_after_open}'"
            ))
        })?;

        let ai = &rest_after_open[..close];
        let after = &rest_after_open[close + 1..];

        // The value runs to the next '(' — the printed form has no other delimiter.
        let end = after.find('(').unwrap_or(after.len());

        elements.push(Element {
            ai: ai.to_string(),
            value: after[..end].to_string(),
        });

        rest = &after[end..];
    }

    Ok(elements)
}

/// The byte index of the `characters`-th character of `text`, saturating at its length.
fn char_boundary(text: &str, characters: usize) -> usize {
    text.char_indices()
        .nth(characters)
        .map(|(index, _)| index)
        .unwrap_or(text.len())
}

/// Percent-encodes a value for a Digital Link: everything outside the unreserved set of RFC 3986
/// becomes `%XX`, over the UTF-8 bytes.
pub fn percent_encode(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for byte in value.bytes() {
        let keep = byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~');
        if keep {
            out.push(byte as char);
        } else {
            out.push_str(&format!("%{byte:02X}"));
        }
    }
    out
}

/// Reverses [`percent_encode`], refusing a `%` that is not followed by two hex digits.
pub fn percent_decode(value: &str) -> Result<String, BtError> {
    let bytes = value.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut index = 0;

    while index < bytes.len() {
        if bytes[index] == b'%' {
            let hex = value.get(index + 1..index + 3).ok_or_else(|| {
                gs1_error(format!("'{value}' ends in an incomplete percent escape"))
            })?;
            let byte = u8::from_str_radix(hex, 16).map_err(|_| {
                gs1_error(format!(
                    "'{value}' holds '%{hex}', which is not a hex escape"
                ))
            })?;
            out.push(byte);
            index += 3;
        } else {
            out.push(bytes[index]);
            index += 1;
        }
    }

    String::from_utf8(out).map_err(|_| gs1_error(format!("'{value}' does not decode to UTF-8")))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn gtin() -> &'static str {
        "09501101530003"
    }

    #[test]
    fn a_two_element_string_parses_into_named_fields() {
        let data = Gs1Data::parse(&format!("(01){}(10)LOT-42", gtin())).expect("parse");

        assert_eq!(data.elements.len(), 2);
        assert_eq!(data.elements[0].ai, "01");
        assert_eq!(data.elements[0].value, gtin());
        assert_eq!(data.elements[0].title(), "GTIN");
        assert_eq!(data.elements[1].ai, "10");
        assert_eq!(data.elements[1].value, "LOT-42");
        assert_eq!(data.elements[1].title(), "BATCH/LOT");
    }

    #[test]
    fn three_elements_with_a_variable_one_need_a_separator() {
        let raw = format!("01{}{GS}10LOT-42{GS}17271231", gtin());
        let data = Gs1Data::parse(&raw).expect("parse");

        assert_eq!(data.elements.len(), 3);
        assert_eq!(data.elements[1].ai, "10");
        assert_eq!(data.elements[1].value, "LOT-42");
        assert_eq!(data.elements[2].ai, "17");
        assert_eq!(data.elements[2].value, "271231");
        assert_eq!(
            data.human_readable(),
            format!("(01){}(10)LOT-42(17)271231", gtin())
        );
    }

    #[test]
    fn fixed_length_identifiers_concatenate_without_a_separator() {
        let raw = format!("01{}{}", gtin(), "17271231");
        let data = Gs1Data::parse(&raw).expect("parse");

        assert_eq!(data.elements.len(), 2);
        assert_eq!(data.elements[1].ai, "17");
        assert_eq!(data.elements[1].value, "271231");
    }

    #[test]
    fn a_reader_symbol_identifier_is_dropped() {
        let raw = format!("]C101{}{GS}10LOT", gtin());
        let data = Gs1Data::parse(&raw).expect("parse");

        assert_eq!(data.elements[0].ai, "01");
        assert_eq!(data.elements[1].value, "LOT");
    }

    #[test]
    fn the_printed_form_parses_too() {
        let data = Gs1Data::parse(&format!("(01){}(10)BATCH 7(17)271231", gtin())).expect("parse");

        assert_eq!(data.elements.len(), 3);
        assert_eq!(data.elements[1].value, "BATCH 7");
        assert_eq!(data.elements[2].value, "271231");
    }

    #[test]
    fn an_unknown_identifier_is_refused_by_code() {
        let error = Gs1Data::parse(&format!("9999{}", "ABC")).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("9999"));
    }

    #[test]
    fn a_wrong_length_for_a_fixed_identifier_is_refused() {
        let error = Gs1Data::parse("01123").unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("AI 01"));
        assert!(error.json.contains("exactly 14"));
    }

    #[test]
    fn a_variable_identifier_that_runs_into_the_next_is_refused_by_name() {
        // Twenty-one characters of batch number is past the AI 10 maximum of twenty.
        let raw = format!("01{}{GS}10{}", gtin(), "A".repeat(21));
        let error = Gs1Data::parse(&raw).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("1 to 20"));
    }

    #[test]
    fn a_gtin_with_a_bad_check_digit_is_refused() {
        // The same GTIN with its last digit moved.
        let error = Gs1Data::parse("0109501101530004").unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("check digit"));
    }

    #[test]
    fn a_duplicate_identifier_is_refused() {
        let raw = format!("01{}{}10A{}10B", gtin(), GS, GS);
        let error = Gs1Data::parse(&raw).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("appears twice"));
    }

    #[test]
    fn the_check_digit_algorithm_matches_the_gs1_example() {
        assert_eq!(check_digit("0950110153000"), Some(3));
        assert!(check_digit_is_valid("09501101530003"));
        assert!(!check_digit_is_valid("09501101530004"));
    }

    #[test]
    fn the_gs1_128_payload_starts_with_fnc1_and_separates_the_variable_fields() {
        let raw = format!("01{}{GS}10LOT{GS}17271231", gtin());
        let data = Gs1Data::parse(&raw).expect("parse");
        let payload = data.to_gs1_128();

        assert_eq!(payload.chars().next(), Some(FNC1));
        assert_eq!(payload, format!("{FNC1}01{}10LOT{FNC1}17271231", gtin()));
        assert!(!payload.contains(GS));
    }

    #[test]
    fn a_digital_link_puts_the_primary_key_in_the_path_and_the_rest_in_the_query() {
        let raw = format!("01{}{GS}10LOT 42{GS}17271231", gtin());
        let data = Gs1Data::parse(&raw).expect("parse");

        assert_eq!(
            data.to_digital_link("https://example.com").unwrap(),
            format!("https://example.com/01/{}?10=LOT%2042&17=271231", gtin())
        );
    }

    #[test]
    fn a_digital_link_round_trips_through_its_own_url() {
        let raw = format!("01{}{}10AB-12{}17271231", gtin(), GS, GS);
        let data = Gs1Data::parse(&raw).expect("parse");
        let url = data
            .to_digital_link("https://example.com/gs1")
            .expect("url");

        let parsed = Gs1Data::from_digital_link(&url).expect("parse back");

        assert_eq!(parsed.elements, data.elements);
        assert_eq!(parsed.raw(), data.raw());
    }

    #[test]
    fn a_digital_link_without_a_primary_key_is_refused() {
        let data = Gs1Data::parse("10LOT-42").expect("parse");
        let error = data.to_digital_link("https://example.com").unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("primary key"));
    }

    #[test]
    fn a_digital_link_with_an_odd_path_is_refused() {
        let error = Gs1Data::from_digital_link("https://example.com/01").unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("pairs"));
    }

    #[test]
    fn a_digital_link_with_an_unknown_query_identifier_is_refused() {
        let url = format!("https://example.com/01/{}?99=X", gtin());
        let error = Gs1Data::from_digital_link(&url).unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("99"));
    }

    #[test]
    fn a_gtin_in_a_digital_link_is_check_digit_verified() {
        let error =
            Gs1Data::from_digital_link("https://example.com/01/09501101530004").unwrap_err();

        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
        assert!(error.json.contains("check digit"));
    }

    #[test]
    fn a_relative_url_is_not_a_digital_link() {
        let error = Gs1Data::from_digital_link("/01/09501101530003").unwrap_err();
        assert_eq!(error.code, crate::ffi::BT_ERR_GS1);
    }

    #[test]
    fn percent_escapes_round_trip() {
        let value = "A b/1&2%3";
        let encoded = percent_encode(value);

        assert_eq!(encoded, "A%20b%2F1%262%253");
        assert_eq!(percent_decode(&encoded).unwrap(), value);
        assert!(percent_decode("%ZZ").is_err());
        assert!(percent_decode("%2").is_err());
    }

    #[test]
    fn the_json_carries_the_raw_form_the_printed_form_and_the_elements() {
        let data = Gs1Data::parse(&format!("01{}{GS}10LOT{GS}17271231", gtin())).expect("parse");
        let json = data.json();

        assert!(json.contains("\"ai\":\"01\""));
        assert!(json.contains("\"title\":\"GTIN\""));
        assert!(json.contains("\"humanReadable\":\"(01)"));
        assert!(json.contains("\"primaryKey\":true"));
        // The separator is a control character, so it is escaped rather than emitted raw.
        assert!(json.contains("\\u001d"));
    }

    #[test]
    fn the_table_holds_the_identifiers_the_documentation_lists() {
        for ai in [
            "00", "01", "10", "17", "21", "30", "37", "400", "410", "7001", "8017",
        ] {
            assert!(spec(ai).is_some(), "AI {ai} is missing from the table");
        }

        assert_eq!(spec("01").unwrap().min, 14);
        assert!(spec("01").unwrap().check_digit);
        assert_eq!(spec("10").unwrap().max, 20);
        assert!(spec("10").unwrap().min != spec("10").unwrap().max);
    }
}
