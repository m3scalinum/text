//! Page fingerprints: a digest of what each page draws.
//!
//! A preview server that compiles the same document again needs to know which pages it has to
//! re-render. Typst exposes no "this page changed" flag, so the answer is a comparison: hash each
//! page of a compilation, hash the same page of the previous one, and report the indices where the
//! two differ.
//!
//! The hash cannot be `Page`'s own [`Hash`] implementation, which includes the [`Span`] of every
//! drawn element and of the page's supplement. A span is a byte midpoint in the source
//! (`typst_syntax::Span`), so editing a sentence on page one shifts every later span and would
//! report every later page as changed — the opposite of what the comparison is for. This module
//! therefore walks the frame and hashes what is drawn: the geometry, the glyph runs, the shapes,
//! the images and the link destinations. Element tags and spans are excluded, and that exclusion
//! is the definition of "changed" a caller gets back.
//!
//! [`Span`]: typst::syntax::Span

use std::hash::{Hash, Hasher};

use typst::layout::{Frame, FrameItem};
use typst_layout::PagedDocument;

/// A 64-bit FNV-1a hasher.
///
/// The algorithm is fixed, so two fingerprints of the same content are equal across processes and
/// across runs of the same build. The numbers are hashed in the host's byte order, so a digest is
/// comparable between processes on one machine, not between architectures: a fingerprint is a
/// cache key, not a content address.
#[derive(Debug, Clone)]
pub(crate) struct Fnv1a(u64);

impl Default for Fnv1a {
    fn default() -> Self {
        // The FNV-1a 64-bit offset basis.
        Self(0xcbf2_9ce4_8422_2325)
    }
}

impl Hasher for Fnv1a {
    fn finish(&self) -> u64 {
        self.0
    }

    fn write(&mut self, bytes: &[u8]) {
        for byte in bytes {
            self.0 ^= u64::from(*byte);
            self.0 = self.0.wrapping_mul(0x0000_0100_0000_01b3);
        }
    }
}

/// One fingerprint per page, in page order.
pub(crate) fn page_fingerprints(document: &PagedDocument) -> Vec<u64> {
    document
        .pages()
        .iter()
        .map(|page| {
            let mut hasher = Fnv1a::default();

            // The page's own properties change what is drawn: a background colour, the physical
            // size the frame was laid out for, the number printed in a header.
            page.number.hash(&mut hasher);
            page.fill.hash(&mut hasher);
            page.numbering.hash(&mut hasher);
            page.bleed.hash(&mut hasher);

            digest(&page.frame, &mut hasher);
            hasher.finish()
        })
        .collect()
}

/// Hashes a frame's geometry and everything it draws, recursively.
fn digest(frame: &Frame, hasher: &mut Fnv1a) {
    frame.size().hash(hasher);
    frame.baseline().hash(hasher);
    frame.kind().hash(hasher);

    for (position, item) in frame.items() {
        position.hash(hasher);

        match item {
            FrameItem::Group(group) => {
                group.transform.hash(hasher);
                group.clip.hash(hasher);
                group.label.hash(hasher);
                digest(&group.frame, hasher);
            }
            // A run of shaped text carries its font, size, paint, language, plain text and
            // glyphs: hashing it is span-free and complete.
            FrameItem::Text(text) => text.hash(hasher),
            // The span is the only part of these two that is not drawn.
            FrameItem::Shape(shape, _) => shape.hash(hasher),
            FrameItem::Image(image, size, _) => {
                image.hash(hasher);
                size.hash(hasher);
            }
            FrameItem::Link(destination, size) => {
                destination.hash(hasher);
                size.hash(hasher);
            }
            // An introspection marker: it carries a location and a span and draws nothing.
            FrameItem::Tag(_) => {}
        }
    }
}

#[cfg(test)]
mod tests {
    use std::hash::Hasher;

    use super::*;

    #[test]
    fn the_hasher_is_fnv1a_64() {
        // The canonical check value of FNV-1a 64 over the empty string and over "a".
        let empty = Fnv1a::default().finish();
        assert_eq!(empty, 0xcbf2_9ce4_8422_2325);

        let mut hasher = Fnv1a::default();
        hasher.write(b"a");
        assert_eq!(hasher.finish(), 0xaf63_dc4c_8601_ec8c);
    }

    #[test]
    fn a_different_byte_changes_the_digest() {
        let mut one = Fnv1a::default();
        one.write(b"page");
        let mut two = Fnv1a::default();
        two.write(b"pagf");

        assert_ne!(one.finish(), two.finish());
    }
}
