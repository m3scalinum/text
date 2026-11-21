//! The recognition pipeline: one ocrs engine, fed either an encoded image or a
//! rendered PDF page, producing the JSON the managed side parses.

use ocrs::{ImageSource, OcrEngine, OcrEngineParams, OcrError, TextItem};
use rten::Model;
use rten_tensor::prelude::*;
use serde_json::{Value, json};

use crate::ffi::{TO_ERR_IMAGE, TO_ERR_MODEL, TO_ERR_OCR, ToError};
use crate::pdf;

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
    pub fn recognize_image(&self, encoded: &[u8]) -> Result<Vec<u8>, ToError> {
        let decoded = image::load_from_memory(encoded)
            .map_err(|e| ToError::new(TO_ERR_IMAGE, format!("decode image: {e}")))?;
        let rgb = decoded.into_rgb8();
        let (width, height) = (rgb.width(), rgb.height());

        let page = self.recognize_pixels(rgb.as_raw(), width, height)?;
        let value = json!({
            "width": width,
            "height": height,
            "text": page.text,
            "meanConfidence": page.mean_confidence,
            "lines": page.lines,
        });

        Ok(value.to_string().into_bytes())
    }

    /// Render every requested page of a PDF at `dpi` and recognise each one.
    pub fn recognize_pdf(
        &self,
        pdf_bytes: &[u8],
        dpi: u32,
        first_page: u32,
        page_count: u32,
    ) -> Result<Vec<u8>, ToError> {
        let mut pages = Vec::new();

        for rendered in pdf::render_pages(pdf_bytes, dpi, first_page, page_count)? {
            let page = self.recognize_pixels(&rendered.pixels, rendered.width, rendered.height)?;
            pages.push(json!({
                "pageIndex": rendered.index,
                "width": rendered.width,
                "height": rendered.height,
                "dpi": rendered.dpi,
                "text": page.text,
                "meanConfidence": page.mean_confidence,
                "lines": page.lines,
            }));
        }

        Ok(json!({ "pages": pages }).to_string().into_bytes())
    }

    fn recognize_pixels(&self, pixels: &[u8], width: u32, height: u32) -> Result<Page, ToError> {
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

        let mut out_lines = Vec::with_capacity(recognized.len());
        let mut confidences = Vec::with_capacity(recognized.len());
        let mut texts = Vec::with_capacity(recognized.len());

        for line in recognized.iter().flatten() {
            let text = line.to_string();
            let bounds = to_box(line);
            let confidence = mean_probability(&mask, line);

            let words = line
                .words()
                .map(|word| {
                    json!({
                        "text": word.to_string(),
                        "bounds": to_box(&word),
                        "confidence": mean_probability(&mask, &word),
                    })
                })
                .collect::<Vec<_>>();

            confidences.push(confidence);
            texts.push(text.clone());
            out_lines.push(json!({
                "text": text,
                "bounds": bounds,
                "confidence": confidence,
                "words": words,
            }));
        }

        let mean_confidence = if confidences.is_empty() {
            0.0
        } else {
            confidences.iter().sum::<f64>() / confidences.len() as f64
        };

        Ok(Page {
            text: texts.join("\n"),
            mean_confidence: round3(mean_confidence),
            lines: Value::Array(out_lines),
        })
    }
}

struct Page {
    text: String,
    mean_confidence: f64,
    lines: Value,
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
}
