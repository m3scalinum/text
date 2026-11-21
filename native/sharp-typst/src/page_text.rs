//! Plain text of one page, read from the paged layout.
//!
//! This is deliberately not the same thing as [`crate::semantic::text`]: that one is a
//! reconstruction from the HTML tree, so it has structure but no page. Here the structure is
//! whatever the frame tree happens to yield, and what the caller gains is the page boundary the
//! layout already decided. The characters come from the shaped run itself: each glyph carries the
//! byte range of the characters it was shaped from, so a ligature yields both of its letters and a
//! decomposed cluster yields its combining marks. The text is therefore the layout's text, in the
//! order the frames yield it, not a semantic reconstruction.
//!
//! See `docs/specifications/02-native-abi.md` §3.10 and
//! `docs/specifications/03-compilation.md` §4.5.

use typst::layout::{Abs, Frame, FrameItem, Point};
use typst::text::TextItem;

use crate::Document;
use crate::ffi::TnError;

/// Vertical slack, in points, below which two text runs count as the same line.
///
/// Runs of one line are pushed at the same vertical origin, so the slack only absorbs the
/// rounding of a transform or of a non-integer font size.
const SAME_LINE_PT: f64 = 0.5;

/// Horizontal slack, in points, below which two runs on the same line count as abutting.
///
/// The shaping pass advances the cursor by the exact width of each run, so a gap wider than this
/// is a real gap — a table cell, a tab, an inline layout island — and not a style change.
const ABUTTING_PT: f64 = 0.5;

/// Reads the text of one page. `page` is zero-based.
pub fn page_text(document: &Document, page: usize) -> Result<String, TnError> {
    let Document::Paged(paged) = document else {
        return Err(TnError::invalid_argument(
            "page text requires a paged document; compile with document = \"paged\"",
        ));
    };

    let count = paged.pages().len();
    let page = paged.pages().get(page).ok_or_else(|| {
        TnError::invalid_argument(format!("page {page} is out of range (0..{count})"))
    })?;

    let mut collector = Collector::default();
    collector.frame(&page.frame);
    Ok(collector.finish())
}

/// Accumulates the runs of one page in frame order.
#[derive(Default)]
struct Collector {
    out: String,
    /// Vertical origin of the last run written, or `None` at the start of a line.
    line: Option<Abs>,
    /// Right edge of the last run written.
    end: Abs,
}

impl Collector {
    /// Walks `frame`, collecting text runs and recursing into groups.
    fn frame(&mut self, frame: &Frame) {
        for (pos, item) in frame.items() {
            match item {
                FrameItem::Text(text) => self.run(*pos, text),
                FrameItem::Group(group) => {
                    // A group is a layout island: what precedes it and what follows it are on
                    // different baselines, and a table cell is one group per cell.
                    self.break_line();
                    self.frame(&group.frame);
                    self.break_line();
                }
                // Shapes, images, links and tags carry no characters of their own; the text of a
                // link is a separate run, so nothing is lost by skipping them.
                FrameItem::Shape(..)
                | FrameItem::Image(..)
                | FrameItem::Link(..)
                | FrameItem::Tag(..) => {}
            }
        }
    }

    /// Appends one shaped run, separated from its predecessor by a space or a newline.
    fn run(&mut self, pos: Point, text: &TextItem) {
        if self.out.is_empty() {
            // Nothing to separate from.
        } else if let Some(line) = self.line {
            if (pos.y.to_pt() - line.to_pt()).abs() <= SAME_LINE_PT {
                if pos.x.to_pt() > self.end.to_pt() + ABUTTING_PT {
                    self.out.push(' ');
                }
            } else {
                self.out.push('\n');
            }
        } else if !self.out.ends_with('\n') {
            self.out.push('\n');
        }

        for glyph in &text.glyphs {
            // The range is a byte range into the run's own text and may cover several characters
            // when the font shaped them into a single glyph.
            if let Some(slice) = text.text.get(glyph.range()) {
                self.out.push_str(slice);
            }
        }

        self.line = Some(pos.y);
        self.end = pos.x + text.width();
    }

    /// Ends the current line, so the next run starts on a fresh one.
    fn break_line(&mut self) {
        self.line = None;
    }

    /// Trims the trailing whitespace and ends the page with exactly one newline.
    fn finish(mut self) -> String {
        while self.out.ends_with([' ', '\t', '\n']) {
            self.out.pop();
        }

        if !self.out.is_empty() {
            self.out.push('\n');
        }

        self.out
    }
}
