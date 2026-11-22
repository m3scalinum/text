//! The recognition pipeline: one ocrs engine, fed either an encoded image or a
//! rendered PDF page, producing the JSON the managed side parses.
//!
//! The readings and the verdict are two steps: [`Engine::recognize_pixels`] turns
//! pixels into [`LineReading`]s, and [`Page::assess`] applies the confidence
//! thresholds to them. That split is what makes the refusal rule testable without
//! two models, which this repository does not ship.

use ocrs::{ImageSource, OcrEngine, OcrEngineParams, OcrError, TextItem};
use rten::Model;
use rten_tensor::prelude::*;
use serde_json::{Value, json};

use crate::ffi::{TO_ERR_IMAGE, TO_ERR_MODEL, TO_ERR_OCR, ToError};
use crate::pdf;

/// The confidence thresholds that decide whether a page is readable.
///
/// The numbers live on the detection-probability scale `ocrs` already produces,
/// from 0 to 1: see `docs/specifications/21-ocr.md` §4. There is no second scale,
/// and no conversion.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Thresholds {
    /// A page whose mean line confidence is below this is refused.
    pub page: f64,
    /// A word whose confidence is below this is flagged, and is not reliable text.
    pub word: f64,
    /// A page whose flagged words exceed this proportion of its words is refused.
    pub max_rejected_ratio: f64,
}

impl Default for Thresholds {
    /// Thresholds that leave a correct 300 DPI scan untouched.
    ///
    /// A crisp printed page scores above 0.75 on every line and word, so a page
    /// threshold of 0.6, a word threshold of 0.5 and a tolerated rejection ratio of
    /// one half refuse only what a caller could not use. A page holding no line at
    /// all is always refused: an empty string is not a reading.
    fn default() -> Self {
        Self {
            page: 0.6,
            word: 0.5,
            max_rejected_ratio: 0.5,
        }
    }
}

impl Thresholds {
    /// Rejects a threshold that is not a number in [0, 1], naming the field at fault.
    ///
    /// The range test also refuses NaN, which is the point: a NaN threshold would
    /// make every comparison false and silently disable the refusal.
    pub fn validate(self) -> Result<Self, ToError> {
        for (name, value) in [
            ("page confidence threshold", self.page),
            ("word confidence threshold", self.word),
            ("maximum rejected word ratio", self.max_rejected_ratio),
        ] {
            if !(0.0..=1.0).contains(&value) {
                return Err(ToError::invalid_argument(format!(
                    "{name} must be a number between 0 and 1, got {value}"
                )));
            }
        }

        Ok(self)
    }
}

/// One word, as the detector and the recogniser produced it.
#[derive(Clone, Debug)]
pub struct WordReading {
    /// The recognised characters.
    pub text: String,
    /// The word's box in image pixels.
    pub bounds: Value,
    /// The mean detection probability inside the box, in [0, 1].
    pub confidence: f64,
}

/// One line, with the words inside it.
#[derive(Clone, Debug)]
pub struct LineReading {
    /// The line as one string, words separated by spaces.
    pub text: String,
    /// The line's box in image pixels.
    pub bounds: Value,
    /// The mean detection probability inside the box, in [0, 1].
    pub confidence: f64,
    /// The words of the line, in reading order.
    pub words: Vec<WordReading>,
}

/// Why a page was refused.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RefusalReason {
    /// No line was found at all: a blank page or an unreadable scan.
    BlankPage,
    /// The page's mean line confidence is below the page threshold.
    MeanConfidence,
    /// More than the tolerated proportion of the page's words are flagged.
    RejectedWords,
}

impl RefusalReason {
    /// The name the result document and the message carry.
    fn name(self) -> &'static str {
        match self {
            Self::BlankPage => "blank_page",
            Self::MeanConfidence => "mean_confidence",
            Self::RejectedWords => "rejected_words",
        }
    }
}

/// A refusal, with everything a caller needs to act on it.
#[derive(Clone, Debug)]
pub struct Refusal {
    /// Which rule fired.
    pub reason: RefusalReason,
    /// The threshold that rule compares against.
    pub threshold: f64,
    /// The value the page actually produced.
    pub observed: f64,
    /// How many words the refusal concerns.
    pub word_count: usize,
}

impl Refusal {
    /// One sentence naming the rule, the threshold, the value seen and the word count.
    fn message(&self) -> String {
        format!(
            "unreadable page: {}; threshold {:.3}, observed {:.3}, {} word(s) concerned",
            self.reason.name(),
            self.threshold,
            self.observed,
            self.word_count
        )
    }

    fn to_json(&self) -> Value {
        json!({
            "reason": self.reason.name(),
            "threshold": round3(self.threshold),
            "observed": round3(self.observed),
            "wordCount": self.word_count,
            "message": self.message(),
        })
    }
}

/// One recognised image or page and the verdict the thresholds returned.
#[derive(Clone, Debug)]
pub struct Page {
    thresholds: Thresholds,
    text: String,
    mean_confidence: f64,
    lines: Vec<LineReading>,
    word_count: usize,
    rejected_word_count: usize,
    refusal: Option<Refusal>,
}

impl Page {
    /// Applies the thresholds to what was read, and decides the verdict.
    ///
    /// The rules are checked in this order, and the first one that fires is the
    /// reason reported: no line at all, then the page's mean confidence, then the
    /// proportion of flagged words.
    pub fn assess(lines: Vec<LineReading>, thresholds: Thresholds) -> Self {
        let word_count: usize = lines.iter().map(|line| line.words.len()).sum();
        let rejected_word_count = lines
            .iter()
            .flat_map(|line| &line.words)
            .filter(|word| word.confidence < thresholds.word)
            .count();

        let mean_confidence = if lines.is_empty() {
            0.0
        } else {
            lines.iter().map(|line| line.confidence).sum::<f64>() / lines.len() as f64
        };
        let rejected_ratio = if word_count == 0 {
            0.0
        } else {
            rejected_word_count as f64 / word_count as f64
        };

        let refusal = if lines.is_empty() {
            Some(Refusal {
                reason: RefusalReason::BlankPage,
                threshold: thresholds.page,
                observed: 0.0,
                word_count: 0,
            })
        } else if mean_confidence < thresholds.page {
            Some(Refusal {
                reason: RefusalReason::MeanConfidence,
                threshold: thresholds.page,
                observed: mean_confidence,
                word_count,
            })
        } else if word_count > 0 && rejected_ratio > thresholds.max_rejected_ratio {
            Some(Refusal {
                reason: RefusalReason::RejectedWords,
                threshold: thresholds.max_rejected_ratio,
                observed: rejected_ratio,
                word_count: rejected_word_count,
            })
        } else {
            None
        };

        let text = lines
            .iter()
            .map(|line| line.text.as_str())
            .collect::<Vec<_>>()
            .join("\n");

        Self {
            thresholds,
            text,
            mean_confidence: round3(mean_confidence),
            lines,
            word_count,
            rejected_word_count,
            refusal,
        }
    }

    /// Whether the thresholds refused this page.
    pub fn is_unreadable(&self) -> bool {
        self.refusal.is_some()
    }

    /// The number of words below the word threshold.
    pub fn rejected_word_count(&self) -> usize {
        self.rejected_word_count
    }

    /// The document one image or page contributes.
    ///
    /// A refused page carries no text and no lines: the whole point of the refusal
    /// is that there is nothing here a caller should mistake for a reading.
    pub fn to_json(&self) -> Value {
        let mut value = json!({
            "status": if self.is_unreadable() { "unreadable" } else { "recognized" },
            "text": if self.is_unreadable() { "" } else { self.text.as_str() },
            "meanConfidence": self.mean_confidence,
            "wordCount": self.word_count,
            "rejectedWordCount": self.rejected_word_count(),
            "lines": self.lines_json(),
        });

        if let Some(refusal) = &self.refusal {
            value["refusal"] = refusal.to_json();
        }

        value
    }

    fn lines_json(&self) -> Value {
        if self.is_unreadable() {
            return Value::Array(Vec::new());
        }

        Value::Array(
            self.lines
                .iter()
                .map(|line| {
                    let words = line
                        .words
                        .iter()
                        .map(|word| {
                            json!({
                                "text": word.text,
                                "bounds": word.bounds,
                                "confidence": word.confidence,
                                "rejected": word.confidence < self.thresholds.word,
                            })
                        })
                        .collect::<Vec<_>>();

                    json!({
                        "text": line.text,
                        "bounds": line.bounds,
                        "confidence": line.confidence,
                        "words": words,
                    })
                })
                .collect(),
        )
    }
}

/// A loaded engine. It owns both models: dropping it drops them.
pub struct Engine {
    inner: OcrEngine,
}

impl std::fmt::Debug for Engine {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Engine").finish_non_exhaustive()
    }
}

fn ocr_error(error: OcrError) -> ToError {
    ToError::new(TO_ERR_OCR, error.to_string())
}

impl Engine {
    /// Build an engine from caller-supplied model bytes.
    ///
    /// Nothing here reads a file or a network: a caller that has no models gets an
    /// error, not a download. `dictionary` is the character alphabet the
    /// recognition model was trained on, and only matters for a model that is not
    /// the default Latin one.
    pub fn new(
        detection_model: &[u8],
        recognition_model: &[u8],
        dictionary: Option<&str>,
    ) -> Result<Self, ToError> {
        if detection_model.is_empty() {
            return Err(ToError::invalid_argument("detection model is empty"));
        }
        if recognition_model.is_empty() {
            return Err(ToError::invalid_argument("recognition model is empty"));
        }

        let detection = Model::load(detection_model.to_vec())
            .map_err(|e| ToError::new(TO_ERR_MODEL, format!("detection model: {e}")))?;
        let recognition = Model::load(recognition_model.to_vec())
            .map_err(|e| ToError::new(TO_ERR_MODEL, format!("recognition model: {e}")))?;

        let params = OcrEngineParams {
            detection_model: Some(detection),
            recognition_model: Some(recognition),
            alphabet: dictionary
                .filter(|d| !d.is_empty())
                .map(std::string::ToString::to_string),
            ..Default::default()
        };

        let inner = OcrEngine::new(params)
            .map_err(|e| ToError::new(TO_ERR_MODEL, format!("engine: {e}")))?;

        Ok(Self { inner })
    }

    /// Recognise the text of an encoded image, returning the result as JSON.
    pub fn recognize_image(
        &self,
        encoded: &[u8],
        thresholds: Thresholds,
    ) -> Result<Vec<u8>, ToError> {
        let thresholds = thresholds.validate()?;

        let decoded = image::load_from_memory(encoded)
            .map_err(|e| ToError::new(TO_ERR_IMAGE, format!("decode image: {e}")))?;
        let rgb = decoded.into_rgb8();
        let (width, height) = (rgb.width(), rgb.height());

        let page = self.recognize_pixels(rgb.as_raw(), width, height, thresholds)?;
        let mut value = page.to_json();
        value["width"] = json!(width);
        value["height"] = json!(height);

        Ok(value.to_string().into_bytes())
    }

    /// Render every requested page of a PDF at `dpi` and recognise each one.
    pub fn recognize_pdf(
        &self,
        pdf_bytes: &[u8],
        dpi: u32,
        first_page: u32,
        page_count: u32,
        thresholds: Thresholds,
    ) -> Result<Vec<u8>, ToError> {
        let thresholds = thresholds.validate()?;
        let mut pages = Vec::new();

        for rendered in pdf::render_pages(pdf_bytes, dpi, first_page, page_count)? {
            let page = self.recognize_pixels(
                &rendered.pixels,
                rendered.width,
                rendered.height,
                thresholds,
            )?;

            let mut value = page.to_json();
            value["pageIndex"] = json!(rendered.index);
            value["width"] = json!(rendered.width);
            value["height"] = json!(rendered.height);
            value["dpi"] = json!(rendered.dpi);
            pages.push(value);
        }

        Ok(json!({ "pages": pages }).to_string().into_bytes())
    }

    fn recognize_pixels(
        &self,
        pixels: &[u8],
        width: u32,
        height: u32,
        thresholds: Thresholds,
    ) -> Result<Page, ToError> {
        let source = ImageSource::from_bytes(pixels, (width, height))
            .map_err(|e| ToError::new(TO_ERR_IMAGE, e.to_string()))?;
        let input = self.inner.prepare_input(source).map_err(ocr_error)?;

        let word_rects = self.inner.detect_words(&input).map_err(ocr_error)?;
        let lines = self.inner.find_text_lines(&input, &word_rects);
        let recognized = self
            .inner
            .recognize_text(&input, &lines)
            .map_err(ocr_error)?;

        // ocrs 0.14 exposes no decoder score, so "confidence" is the mean text
        // probability the *detection* model assigned over the box. It measures how
        // strongly the detector saw text there, not how sure the recogniser was of
        // the characters; the spec says so in as many words.
        let mask = self.inner.detect_text_pixels(&input).map_err(ocr_error)?;

        let mut readings = Vec::with_capacity(recognized.len());

        for line in recognized.iter().flatten() {
            let words = line
                .words()
                .map(|word| WordReading {
                    text: word.to_string(),
                    bounds: to_box(&word),
                    confidence: mean_probability(&mask, &word),
                })
                .collect();

            readings.push(LineReading {
                text: line.to_string(),
                bounds: to_box(line),
                confidence: mean_probability(&mask, line),
                words,
            });
        }

        Ok(Page::assess(readings, thresholds))
    }
}

fn round3(value: f64) -> f64 {
    (value * 1000.0).round() / 1000.0
}

/// The axis-aligned bounding box, clamped to the image and rounded to whole pixels.
///
/// The rectangles ocrs reports are already integral, so this only clamps the two
/// edges that a box touching the border can carry below zero.
fn to_box<T: TextItem + ?Sized>(item: &T) -> Value {
    let rect = item.bounding_rect();
    let left = rect.left().max(0) as i64;
    let top = rect.top().max(0) as i64;
    let right = rect.right().max(0) as i64;
    let bottom = rect.bottom().max(0) as i64;

    json!({
        "x": left,
        "y": top,
        "width": (right - left).max(0),
        "height": (bottom - top).max(0),
    })
}

/// The mean detection probability inside the item's bounding box.
fn mean_probability<T: TextItem + ?Sized>(mask: &rten_tensor::NdTensor<f32, 2>, item: &T) -> f64 {
    let rect = item.bounding_rect();
    let [rows, cols] = mask.shape();

    let top = rect.top().max(0) as usize;
    let left = rect.left().max(0) as usize;
    let bottom = (rect.bottom().max(0) as usize).min(rows);
    let right = (rect.right().max(0) as usize).min(cols);

    if bottom <= top || right <= left {
        return 0.0;
    }

    let view = mask.view();
    let region = view.slice((top..bottom, left..right));

    let mut sum = 0.0_f32;
    let mut count = 0_u32;
    for value in region.iter() {
        sum += *value;
        count += 1;
    }

    if count == 0 {
        return 0.0;
    }

    round3(f64::from(sum / count as f32))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn word(text: &str, x: i64, confidence: f64) -> WordReading {
        WordReading {
            text: text.to_string(),
            bounds: json!({ "x": x, "y": 10, "width": 40, "height": 12 }),
            confidence,
        }
    }

    fn line(text: &str, confidence: f64, words: Vec<WordReading>) -> LineReading {
        LineReading {
            text: text.to_string(),
            bounds: json!({ "x": 0, "y": 10, "width": 200, "height": 12 }),
            confidence,
            words,
        }
    }

    fn flags(page: &Page) -> Vec<bool> {
        page.lines_json()
            .as_array()
            .expect("lines is an array")
            .iter()
            .flat_map(|line| line["words"].as_array().expect("words is an array").clone())
            .map(|word| word["rejected"].as_bool().expect("rejected is a bool"))
            .collect()
    }

    #[test]
    fn empty_model_is_rejected_before_any_decoding() {
        let error = Engine::new(&[], &[], None).expect_err("must fail");
        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
    }

    #[test]
    fn garbage_model_is_rejected_as_a_model_error() {
        let error = Engine::new(b"not a model", b"not a model", None).expect_err("must fail");
        assert_eq!(error.code, TO_ERR_MODEL);
    }

    #[test]
    fn the_default_thresholds_leave_a_correct_page_alone() {
        let page = Page::assess(
            vec![
                line(
                    "INVOICE 2026",
                    0.94,
                    vec![word("INVOICE", 0, 0.95), word("2026", 45, 0.93)],
                ),
                line(
                    "Total 42",
                    0.9,
                    vec![word("Total", 0, 0.91), word("42", 90, 0.89)],
                ),
            ],
            Thresholds::default(),
        );

        assert!(!page.is_unreadable());
        assert_eq!(page.rejected_word_count(), 0);
        assert_eq!(page.text, "INVOICE 2026\nTotal 42");

        let document = page.to_json();
        assert_eq!(document["status"], "recognized");
        assert_eq!(document["text"], "INVOICE 2026\nTotal 42");
        assert_eq!(document["meanConfidence"], 0.92);
        assert_eq!(document["wordCount"], 4);
        assert_eq!(document["rejectedWordCount"], 0);
        assert!(document.get("refusal").is_none());
        assert_eq!(flags(&page), [false, false, false, false]);
    }

    #[test]
    fn an_empty_page_is_refused_as_a_blank_page_rather_than_read_as_empty_text() {
        let page = Page::assess(Vec::new(), Thresholds::default());

        assert!(page.is_unreadable());

        let document = page.to_json();
        assert_eq!(document["status"], "unreadable");
        assert_eq!(document["text"], "");
        assert!(document["lines"].as_array().expect("an array").is_empty());
        assert_eq!(document["refusal"]["reason"], "blank_page");
        assert_eq!(document["refusal"]["threshold"], 0.6);
        assert_eq!(document["refusal"]["observed"], 0.0);
        assert_eq!(document["refusal"]["wordCount"], 0);

        let message = document["refusal"]["message"].as_str().expect("a message");
        assert!(message.contains("blank_page"), "{message}");
        assert!(message.contains("threshold 0.600"), "{message}");
        assert!(message.contains("observed 0.000"), "{message}");
        assert!(message.contains("0 word(s)"), "{message}");
    }

    #[test]
    fn a_page_below_the_page_threshold_names_the_threshold_and_the_value_seen() {
        let page = Page::assess(
            vec![line("faint", 0.42, vec![word("faint", 0, 0.42)])],
            Thresholds {
                page: 0.8,
                ..Thresholds::default()
            },
        );

        let document = page.to_json();
        assert_eq!(document["status"], "unreadable");
        assert_eq!(document["refusal"]["reason"], "mean_confidence");
        assert_eq!(document["refusal"]["threshold"], 0.8);
        assert_eq!(document["refusal"]["observed"], 0.42);
        assert_eq!(document["refusal"]["wordCount"], 1);
        assert_eq!(document["text"], "");
    }

    #[test]
    fn a_page_whose_words_are_all_below_the_word_threshold_is_refused_as_rejected_words() {
        // The page threshold is 0 so the word rule, not the mean, is what fires:
        // this test is about the word threshold on its own.
        let page = Page::assess(
            vec![line(
                "noise",
                0.35,
                vec![word("noise", 0, 0.35), word("more", 60, 0.2)],
            )],
            Thresholds {
                page: 0.0,
                word: 0.9,
                max_rejected_ratio: 0.5,
            },
        );

        let document = page.to_json();
        assert_eq!(document["status"], "unreadable");
        assert_eq!(document["refusal"]["reason"], "rejected_words");
        assert_eq!(document["refusal"]["threshold"], 0.5);
        assert_eq!(document["refusal"]["observed"], 1.0);
        assert_eq!(document["refusal"]["wordCount"], 2);
        // A refusal carries no text, so the caller cannot use the noise by accident.
        assert_eq!(document["text"], "");
        assert!(document["lines"].as_array().expect("an array").is_empty());
    }

    #[test]
    fn a_minority_of_doubtful_words_is_returned_and_flagged_with_its_confidence_and_box() {
        let page = Page::assess(
            vec![line(
                "INVOICE 2026 Total",
                0.88,
                vec![
                    word("INVOICE", 0, 0.95),
                    word("2026", 45, 0.92),
                    word("Total", 90, 0.18),
                ],
            )],
            Thresholds::default(),
        );

        assert!(!page.is_unreadable());
        assert_eq!(page.rejected_word_count(), 1);
        assert_eq!(flags(&page), [false, false, true]);

        let document = page.to_json();
        assert_eq!(document["status"], "recognized");
        assert_eq!(document["text"], "INVOICE 2026 Total");
        assert_eq!(document["rejectedWordCount"], 1);

        let rejected = &document["lines"][0]["words"][2];
        assert_eq!(rejected["text"], "Total");
        assert_eq!(rejected["confidence"], 0.18);
        assert_eq!(rejected["bounds"]["x"], 90);
        assert_eq!(rejected["bounds"]["width"], 40);
        assert_eq!(rejected["rejected"], true);
    }

    #[test]
    fn a_word_threshold_of_zero_flags_nothing() {
        let page = Page::assess(
            vec![line("ab", 0.4, vec![word("a", 0, 0.0), word("b", 45, 0.3)])],
            Thresholds {
                page: 0.0,
                word: 0.0,
                max_rejected_ratio: 0.0,
            },
        );

        assert!(!page.is_unreadable());
        assert_eq!(page.rejected_word_count(), 0);
        assert_eq!(flags(&page), [false, false]);
    }

    #[test]
    fn a_word_threshold_of_one_flags_every_word_short_of_certainty() {
        let page = Page::assess(
            vec![line(
                "ab",
                1.0,
                vec![word("a", 0, 1.0), word("b", 45, 0.999)],
            )],
            Thresholds {
                page: 0.0,
                word: 1.0,
                max_rejected_ratio: 1.0,
            },
        );

        // The tolerated ratio is 1, so one word out of two stays a reading.
        assert!(!page.is_unreadable());
        assert_eq!(page.rejected_word_count(), 1);
        assert_eq!(flags(&page), [false, true]);
    }

    #[test]
    fn a_page_threshold_of_zero_refuses_only_a_page_with_no_line() {
        // Only the page rule is under test: the word rule is off (threshold 0,
        // every doubt tolerated), so the 0.01 page survives and the empty one does not.
        let thresholds = Thresholds {
            page: 0.0,
            word: 0.0,
            max_rejected_ratio: 1.0,
        };

        assert!(
            !Page::assess(
                vec![line("faint", 0.01, vec![word("faint", 0, 0.01)])],
                thresholds
            )
            .is_unreadable()
        );
        assert!(Page::assess(Vec::new(), thresholds).is_unreadable());
    }

    #[test]
    fn a_page_threshold_of_one_refuses_any_page_short_of_certainty() {
        let page = Page::assess(
            vec![line("near", 0.999, vec![word("near", 0, 0.999)])],
            Thresholds {
                page: 1.0,
                ..Thresholds::default()
            },
        );

        assert!(page.is_unreadable());
        assert_eq!(page.to_json()["refusal"]["reason"], "mean_confidence");
        assert_eq!(page.to_json()["refusal"]["threshold"], 1.0);
        assert_eq!(page.to_json()["refusal"]["observed"], 0.999);
    }

    #[test]
    fn a_mean_confidence_at_the_threshold_is_a_reading() {
        // The comparison is strict: a page exactly at its threshold is kept.
        let page = Page::assess(
            vec![line("edge", 0.6, vec![word("edge", 0, 0.6)])],
            Thresholds::default(),
        );

        assert!(!page.is_unreadable());
    }

    #[test]
    fn thresholds_outside_zero_to_one_are_refused_by_name() {
        let error = Thresholds {
            page: 1.5,
            ..Thresholds::default()
        }
        .validate()
        .expect_err("1.5 is out of range");
        assert_eq!(error.code, crate::ffi::TO_ERR_INVALID_ARGUMENT);
        assert!(format!("{error:?}").contains("page confidence threshold"));

        let error = Thresholds {
            word: f64::NAN,
            ..Thresholds::default()
        }
        .validate()
        .expect_err("NaN is not a threshold");
        assert!(format!("{error:?}").contains("word confidence threshold"));

        let error = Thresholds {
            max_rejected_ratio: -0.1,
            ..Thresholds::default()
        }
        .validate()
        .expect_err("a negative ratio is out of range");
        assert!(format!("{error:?}").contains("maximum rejected word ratio"));
    }
}
