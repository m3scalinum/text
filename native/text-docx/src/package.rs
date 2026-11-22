//! The container: a `.docx` is a zip, and this is the only module that touches it.
//!
//! Two rules live here, and both exist because the bytes come from whoever sent the document.
//!
//! **A part name is resolved through the package, never trusted as a path.** An OPC relationship
//! target is a relative URI; resolving it is arithmetic over segments, and a segment that is `..`,
//! empty or `.` means the target leaves the directory the relationship lives in. Nothing in this
//! module ever hands a name to the file system — but the name it returns is used as an asset key by
//! every consumer downstream (Typst asset paths, EPUB-style writers), so a name that could be read
//! as a traversal is refused here, at the one place that decides what a part is called.
//!
//! **A size is measured, not believed.** The zip central directory states an uncompressed size for
//! every entry, and that statement is what the "Zip Cloaking" class of bug abuses: a reader that
//! allocates from the header, or that stops reading when it has what the header promised, sees a
//! different part from the one a second reader would. Every part is therefore bounded on both
//! sides — refused when the header declares more than the cap, and refused again when the decoded
//! bytes exceed it — and the local header is checked against the central directory before a single
//! byte is decoded.

use std::io::{Cursor, Read};

use roxmltree::Node;

use crate::parse::ParseError;

/// The largest a single part may be, declared or decoded.
///
/// A `word/document.xml` that is larger than this is not a document a person wrote; 32 MiB is many
/// times the largest realistic body, and the whole package is already capped by `Options::max_bytes`.
pub const MAX_PART_BYTES: u64 = 32 * 1024 * 1024;

/// A zip archive opened over the caller's bytes.
pub struct Package<'a> {
    archive: zip::ZipArchive<Cursor<&'a [u8]>>,
}

impl<'a> Package<'a> {
    /// Opens the bytes as a zip archive, or says they are not one.
    pub fn open(bytes: &'a [u8]) -> Result<Self, ParseError> {
        let archive = zip::ZipArchive::new(Cursor::new(bytes)).map_err(|error| {
            ParseError::MalformedPackage(format!("the bytes are not a zip package: {error}"))
        })?;

        Ok(Self { archive })
    }

    /// Whether the package holds a part with exactly this name.
    pub fn has(&self, name: &str) -> bool {
        self.archive.index_for_name(name).is_some()
    }

    /// Reads a part whole, refusing it when it is larger than [`MAX_PART_BYTES`].
    pub fn read(&mut self, name: &str) -> Result<Vec<u8>, ParseError> {
        self.read_capped(name, MAX_PART_BYTES)
    }

    /// Reads a part whole, refusing it when it is larger than `cap` bytes.
    ///
    /// The declared size is a hint the header supplies, so it is checked first — that is the cheap
    /// refusal — and then the decoded length is checked again, because a header that understates the
    /// payload is exactly the attack this bound exists for.
    pub fn read_capped(&mut self, name: &str, cap: u64) -> Result<Vec<u8>, ParseError> {
        // `enforce_header_consistency` compares every local file header to its central directory
        // entry and refuses a package that carries an entry one reader would see and another would
        // not. It is cheap and it is the parser differential, not the decoder, that is dangerous.
        let options = zip::ZipReadOptions::new().enforce_header_consistency(true);

        let file = self
            .archive
            .by_name_with_options(name, options)
            .map_err(|error| {
                ParseError::MalformedPackage(format!("the part {name} could not be read: {error}"))
            })?;

        let declared = file.size();

        if declared > cap {
            return Err(ParseError::Limit(format!(
                "the part {name} declares {declared} bytes, over the {cap}-byte limit"
            )));
        }

        let mut bytes = Vec::with_capacity(declared.min(64 * 1024) as usize);

        file.take(cap.saturating_add(1))
            .read_to_end(&mut bytes)
            .map_err(|error| {
                ParseError::MalformedPackage(format!(
                    "the part {name} could not be decompressed: {error}"
                ))
            })?;

        if bytes.len() as u64 > cap {
            return Err(ParseError::Limit(format!(
                "the part {name} decoded to more than the {cap}-byte limit"
            )));
        }

        Ok(bytes)
    }
}

/// Resolves a relationship target against the directory of the part that owns the relationship.
///
/// `base` is a directory inside the package — `word` for `word/document.xml` — and `allowed` is the
/// prefix every resolved part must sit under, which is how "an image relationship may only name a
/// part under `word/media/`" is enforced. A target that is absolute, that carries a drive letter or
/// a URI scheme, that is empty, or that holds a `..`, `.` or empty segment is refused before the
/// prefix is even considered.
pub fn resolve_part(base: &str, target: &str, allowed: &[&str]) -> Result<String, ParseError> {
    let target = target.replace('\\', "/");
    let target = target.trim();

    if target.is_empty() {
        return Err(refuse(target, "it is empty"));
    }

    if target.starts_with('/') {
        return Err(refuse(target, "it is absolute"));
    }

    // `C:` and `file:` and `http:` all reduce to "a colon before the first separator". A relative
    // OPC target never has one; a name that does is either a drive letter or a scheme.
    if let Some(colon) = target.find(':')
        && !target[..colon].contains('/')
    {
        return Err(refuse(target, "it names a drive or a URI scheme"));
    }

    let mut segments: Vec<&str> = base.split('/').filter(|part| !part.is_empty()).collect();

    for segment in target.split('/') {
        match segment {
            "" | "." => return Err(refuse(target, "it has an empty or `.` segment")),
            ".." => return Err(refuse(target, "it has a `..` segment")),
            other => segments.push(other),
        }
    }

    if segments.len() <= allowed.len() || &segments[..allowed.len()] != allowed {
        return Err(refuse(
            target,
            &format!("it does not resolve under {}/", allowed.join("/")),
        ));
    }

    Ok(segments.join("/"))
}

fn refuse(target: &str, why: &str) -> ParseError {
    ParseError::MalformedPackage(format!("the part name {target:?} is refused: {why}"))
}

// ---------------------------------------------------------------------------
// Relationships
// ---------------------------------------------------------------------------

/// One `<Relationship>` of a `.rels` part.
#[derive(Debug, Clone)]
pub struct Relationship {
    /// The `Id` a run or a picture refers to.
    pub id: String,
    /// The `Type` URI; the last path segment is the relationship kind.
    pub kind: String,
    /// The `Target`, still relative — [`resolve_part`] turns it into a part name.
    pub target: String,
    /// Whether `TargetMode="External"`: a hyperlink, not a part.
    pub external: bool,
}

impl Relationship {
    /// Whether this relationship is of the kind whose type URI ends with `suffix`.
    pub fn is(&self, suffix: &str) -> bool {
        self.kind.ends_with(suffix)
    }
}

/// The ceiling on relationships in one part: a package with more is a generator, not a document.
pub const MAX_RELATIONSHIPS: usize = 4096;

/// Reads a `.rels` part.
pub fn read_relationships(xml: &[u8]) -> Result<Vec<Relationship>, ParseError> {
    let text = xml_text(xml)?;

    let document = crate::xml::parse(text, "a relationships part")?;

    let mut relationships = Vec::new();

    for node in document.descendants().filter(roxmltree::Node::is_element) {
        if !is_local(&node, "Relationship") {
            continue;
        }

        if relationships.len() >= MAX_RELATIONSHIPS {
            return Err(ParseError::Limit(format!(
                "a relationships part holds more than {MAX_RELATIONSHIPS} relationships"
            )));
        }

        let (Some(id), Some(target)) = (attribute(node, "Id"), attribute(node, "Target")) else {
            continue;
        };

        relationships.push(Relationship {
            id: id.to_owned(),
            kind: attribute(node, "Type").unwrap_or_default().to_owned(),
            target: target.to_owned(),
            external: attribute(node, "TargetMode") == Some("External"),
        });
    }

    Ok(relationships)
}

// ---------------------------------------------------------------------------
// XML helpers shared with the document reader
// ---------------------------------------------------------------------------

/// The transitional WordprocessingML namespace.
pub const WORD: &str = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
/// The strict WordprocessingML namespace, which Word can also emit.
pub const WORD_STRICT: &str = "http://purl.oclc.org/ooxml/wordprocessingml/main";

/// Decodes a part as the UTF-8 text every OOXML part is written in.
///
/// A part that is not UTF-8 is not a document Word writes; refusing it names the problem instead of
/// letting a lossy conversion invent characters.
pub fn xml_text(xml: &[u8]) -> Result<&str, ParseError> {
    let xml = xml.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(xml);

    std::str::from_utf8(xml)
        .map_err(|_| ParseError::MalformedPackage("a part is not UTF-8 text".to_owned()))
}

/// Whether the element's local name is `name` and it sits in WordprocessingML.
///
/// A part that declares no namespace at all is accepted: it is not valid OOXML, but treating an
/// unprefixed element as Word's own is the reading every other consumer gives it, and the AST shape
/// is decided by the local name either way.
pub fn is_w(node: &roxmltree::Node<'_, '_>, name: &str) -> bool {
    if !node.is_element() || node.tag_name().name() != name {
        return false;
    }

    match node.tag_name().namespace() {
        Some(namespace) => namespace == WORD || namespace == WORD_STRICT,
        None => true,
    }
}

/// Whether the element's local name is `name`, whatever its namespace.
pub fn is_local(node: &roxmltree::Node<'_, '_>, name: &str) -> bool {
    node.is_element() && node.tag_name().name() == name
}

/// An attribute by local name, in WordprocessingML or — for the parts that carry no namespace —
/// plain. This is how `w:val` is read without caring which of the two namespaces a producer chose.
pub fn w_attr<'a>(node: Node<'a, '_>, name: &str) -> Option<&'a str> {
    node.attribute((WORD, name))
        .or_else(|| node.attribute((WORD_STRICT, name)))
        .or_else(|| node.attribute(name))
}

/// An unqualified attribute by name — `Id`, `Target`, `Type` of a relationship.
pub fn attribute<'a>(node: Node<'a, '_>, name: &str) -> Option<&'a str> {
    node.attribute(name)
}

/// The direct children of `node` whose local name is `name` in WordprocessingML.
pub fn children<'a, 'input>(
    node: roxmltree::Node<'a, 'input>,
    name: &str,
) -> impl Iterator<Item = roxmltree::Node<'a, 'input>> + 'a {
    let name = name.to_owned();
    node.children().filter(move |child| is_w(child, &name))
}

/// The first direct child with this local name in WordprocessingML.
pub fn child<'a, 'input>(
    node: roxmltree::Node<'a, 'input>,
    name: &str,
) -> Option<roxmltree::Node<'a, 'input>> {
    node.children().find(|child| is_w(child, name))
}

/// Walks down a chain of direct children, for the fixed shapes `tcPr/gridSpan`, `pPr/numPr/numId`.
pub fn path<'a, 'input>(
    node: roxmltree::Node<'a, 'input>,
    names: &[&str],
) -> Option<roxmltree::Node<'a, 'input>> {
    let mut current = node;

    for name in names {
        current = child(current, name)?;
    }

    Some(current)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_relative_target_resolves_under_the_part_directory() {
        assert_eq!(
            resolve_part("word", "media/image1.png", &["word", "media"]).unwrap(),
            "word/media/image1.png"
        );
        assert!(
            resolve_part("word", "./media/image1.png", &["word"]).is_err(),
            "a `.` segment is refused, not collapsed"
        );
    }

    #[test]
    fn a_traversal_is_refused() {
        for target in [
            "../media/image1.png",
            "media/../../word/document.xml",
            "/word/media/image1.png",
            "C:/word/media/image1.png",
            "C:\\word\\media\\image1.png",
            "file:///word/media/image1.png",
            "",
            "media//image1.png",
        ] {
            assert!(
                resolve_part("word", target, &["word", "media"]).is_err(),
                "{target} must be refused"
            );
        }
    }

    #[test]
    fn a_part_outside_the_expected_directory_is_refused() {
        assert!(resolve_part("word", "document.xml", &["word", "media"]).is_err());
        assert!(resolve_part("word", "media", &["word", "media"]).is_err());
        assert!(resolve_part("word", "media/image1.png", &["word", "media"]).is_ok());
    }

    #[test]
    fn a_backslash_is_read_as_a_separator_then_judged() {
        assert_eq!(
            resolve_part("word", "media\\image1.png", &["word", "media"]).unwrap(),
            "word/media/image1.png"
        );
    }

    #[test]
    fn relationships_are_read_with_their_mode() {
        let xml = br#"<?xml version="1.0"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://x/image" Target="media/image1.png"/>
              <Relationship Id="rId2" Type="http://x/hyperlink" Target="https://example.test" TargetMode="External"/>
            </Relationships>"#;

        let relationships = read_relationships(xml).unwrap();

        assert_eq!(relationships.len(), 2);
        assert!(relationships[0].is("/image"));
        assert!(!relationships[0].external);
        assert!(relationships[1].external);
    }
}
