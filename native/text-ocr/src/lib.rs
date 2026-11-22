//! C ABI over ocrs, for `M3scalinum.Text.Ocr`.
//!
//! The conventions are `native/sharp-typst`'s: a status code plus an out-parameter
//! error object, owned buffers the caller frees, one opaque handle type, a live
//! handle counter, and `catch_unwind` on every entry point so a panic cannot
//! unwind into C.
//!
//! Nothing here downloads a model or reads one from a default path: the bytes the
//! caller passes to [`to_engine_create`] are the only models this library ever
//! sees, and [`to_engine_create_from_directory`] reads exactly `detection.rten`
//! and `recognition.rten` from the directory the caller names.
//!
//! Every model is validated before it is parsed, identified by SHA-256, and the
//! result of a recognition carries the identity of the weights that produced it.

mod engine;
mod ffi;
mod language;
mod limits;
mod models;
mod pdf;
mod preprocess;
mod sha256;

#[cfg(test)]
mod allocation_probe;

use std::ffi::{CString, c_char};
use std::path::PathBuf;
use std::sync::OnceLock;
use std::sync::atomic::{AtomicUsize, Ordering};

use engine::{Engine, EngineSet, LanguageEngines, Thresholds};
use ffi::{ToBuffer, ToError, guard_buffer, guard_ptr, read_bytes};
use language::{LanguageCatalogue, PageLanguages, normalize_tag};
use limits::{ImageBounds, ImageHeader};
use models::{ModelFile, ModelKind, ModelSet};
use preprocess::PreprocessOptions;

/// The opaque engine handle. It owns both models, or one pair per language.
pub struct ToEngine {
    engine: EngineSet,
}

// ---------------------------------------------------------------------------
// Live handles
// ---------------------------------------------------------------------------

/// Handles alive right now, so a host can prove that every one it created was
/// released. Process memory cannot witness that portably; the count is exact.
static LIVE_ENGINES: AtomicUsize = AtomicUsize::new(0);

impl Drop for ToEngine {
    fn drop(&mut self) {
        LIVE_ENGINES.fetch_sub(1, Ordering::Relaxed);
    }
}

/// The only way a handle is created: counting it here is what keeps the count exact.
fn engine_handle(engine: EngineSet) -> Box<ToEngine> {
    LIVE_ENGINES.fetch_add(1, Ordering::Relaxed);
    Box::new(ToEngine { engine })
}

/// Number of engine handles alive right now.
#[unsafe(no_mangle)]
pub extern "C" fn to_engine_live_count() -> u32 {
    LIVE_ENGINES.load(Ordering::Relaxed) as u32
}

// ---------------------------------------------------------------------------
// Version handshake and build info
// ---------------------------------------------------------------------------

#[unsafe(no_mangle)]
pub extern "C" fn to_abi_version() -> u32 {
    2
}

#[unsafe(no_mangle)]
pub extern "C" fn to_abi_version_string() -> *const c_char {
    static VERSION: OnceLock<CString> = OnceLock::new();
    VERSION
        .get_or_init(|| CString::new("2.0.0").expect("literal has no interior NUL"))
        .as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn to_build_info() -> *const c_char {
    static INFO: OnceLock<CString> = OnceLock::new();
    INFO.get_or_init(|| {
        let json = serde_json::json!({
            "target": std::env::consts::ARCH.to_string() + "-" + std::env::consts::OS,
            "profile": if cfg!(debug_assertions) { "debug" } else { "release" },
            "ocrs": "0.14",
            "rten": "0.28",
            "hayro": "0.9",
            "preprocess": "quarter-turn, deskew and median denoise",
            "models": "validated: rten v2 header or onnx signature, sha256 identity",
        })
        .to_string();
        CString::new(json).expect("build info has no interior NUL")
    })
    .as_ptr()
}

// ---------------------------------------------------------------------------
// Errors and buffers
// ---------------------------------------------------------------------------

/// # Safety
/// `error` is null or a valid error pointer from this library, not yet freed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_free(error: *mut ToError) {
    if error.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(error) });
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_code(error: *const ToError) -> i32 {
    if error.is_null() {
        return 0;
    }
    unsafe { (*error).code }
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_json(error: *const ToError) -> ToBuffer {
    if error.is_null() {
        return ToBuffer::empty();
    }
    ToBuffer::from_vec(unsafe { (*error).json.clone() }.into_bytes())
}

/// # Safety
/// `error` is null or a valid error pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_error_traceback(error: *const ToError) -> ToBuffer {
    if error.is_null() {
        return ToBuffer::empty();
    }
    ToBuffer::from_vec(unsafe { (*error).traceback.clone() }.into_bytes())
}

/// # Safety
/// `buffer` comes from this library and is not freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_buffer_free(buffer: ToBuffer) {
    unsafe { buffer.free() };
}

// ---------------------------------------------------------------------------
// Engine
// ---------------------------------------------------------------------------

/// Builds an engine from caller-supplied model bytes.
///
/// `dictionary` is the alphabet the recognition model was trained on: pass a null
/// pointer (or `dictionary_len` 0) to use the default Latin alphabet of the
/// published models.
///
/// # Safety
/// Each pointer must be valid for its length, or null with a length of 0.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_create(
    detection_model: *const u8,
    detection_len: usize,
    recognition_model: *const u8,
    recognition_len: usize,
    dictionary: *const u8,
    dictionary_len: usize,
    out_error: *mut *mut ToError,
) -> *mut ToEngine {
    unsafe {
        guard_ptr(out_error, || {
            let detection = read_bytes(detection_model, detection_len, "detection model")?;
            let recognition = read_bytes(recognition_model, recognition_len, "recognition model")?;
            let dictionary_bytes = read_bytes(dictionary, dictionary_len, "character dictionary")?;

            let dictionary = if dictionary_bytes.is_empty() {
                None
            } else {
                Some(std::str::from_utf8(&dictionary_bytes).map_err(|_| {
                    ToError::invalid_argument("character dictionary is not valid UTF-8")
                })?)
            };

            let engine = Engine::new(&detection, &recognition, dictionary)?;
            Ok(engine_handle(EngineSet::Single(Box::new(engine))))
        })
    }
}

/// Inspects model bytes without building an engine.
///
/// On success `out_json` holds
/// `{"valid":true,"role":"…","format":"rten"|"onnx","length":N,"sha256":"…"}`; on
/// refusal the return value is the error code (always `model`) and the error names
/// the defect in a field. Nothing is loaded, so a caller can preflight a file
/// before paying for the weights.
///
/// `kind` is 0 for the detection model and 1 for the recognition model.
///
/// # Safety
/// `model` is valid for `length`; `out_json` and `out_error` are null or
/// caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_model_inspect(
    model: *const u8,
    length: usize,
    kind: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        guard_buffer(out_error, out_json, || {
            let kind = ModelKind::from_code(kind).ok_or_else(|| {
                ToError::invalid_argument(format!(
                    "model kind must be 0 (detection) or 1 (recognition), got {kind}"
                ))
            })?;

            let bytes = read_bytes(model, length, "model")?;
            let file = ModelFile::inspect(bytes, kind)
                .map_err(|defect| models::model_error(Some(kind), None, defect))?;

            let mut value = file.fingerprint.to_json();
            value["valid"] = serde_json::json!(true);

            Ok(value.to_string().into_bytes())
        })
    }
}

/// Validates and identifies the two models of a directory, without loading them.
///
/// `directory` is UTF-8 bytes, not NUL-terminated. The directory must hold
/// `detection.rten` and `recognition.rten`; a missing one is a `model` error whose
/// `file` field names the file that was expected. No path is defaulted and
/// nothing is downloaded.
///
/// # Safety
/// `directory` is valid for `length`; `out_json` and `out_error` are null or
/// caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_model_set_inspect(
    directory: *const u8,
    length: usize,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        guard_buffer(out_error, out_json, || {
            let directory = read_path(directory, length, "model directory")?;
            let set = ModelSet::from_directory(&directory)?;
            Ok(set.to_json().to_string().into_bytes())
        })
    }
}

/// Builds an engine from the two models of a directory.
///
/// The counterpart of [`to_engine_create`] for a caller that keeps its weights on
/// disk: the directory must hold `detection.rten` and `recognition.rten`, nothing
/// else is looked at, and the same validation and identity apply.
///
/// # Safety
/// `directory` and `dictionary` are valid for their lengths, or null with a length
/// of 0.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_create_from_directory(
    directory: *const u8,
    length: usize,
    dictionary: *const u8,
    dictionary_len: usize,
    out_error: *mut *mut ToError,
) -> *mut ToEngine {
    unsafe {
        guard_ptr(out_error, || {
            let directory = read_path(directory, length, "model directory")?;
            let dictionary_bytes = read_bytes(dictionary, dictionary_len, "character dictionary")?;

            let dictionary = if dictionary_bytes.is_empty() {
                None
            } else {
                Some(std::str::from_utf8(&dictionary_bytes).map_err(|_| {
                    ToError::invalid_argument("character dictionary is not valid UTF-8")
                })?)
            };

            let engine = Engine::from_directory(&directory, dictionary)?;
            Ok(engine_handle(EngineSet::Single(Box::new(engine))))
        })
    }
}

/// Builds an engine holding one model set per language.
///
/// `plan` is a UTF-8 JSON document, not NUL-terminated:
///
/// ```json
/// {"default":"fr","dictionary":null,
///  "sets":[{"language":"fr","directory":"/srv/models/fr"},
///          {"language":"en","directory":"/srv/models/en"}]}
/// ```
///
/// `default` names the set that reads a page asking for nothing, or for a language
/// the plan does not hold; it must itself appear in `sets`. `dictionary` is the
/// alphabet every recognition model was trained on, or null for the default Latin
/// one; one dictionary applies to all sets. Each directory is read exactly as
/// [`to_engine_create_from_directory`] reads it — `detection.rten` and
/// `recognition.rten`, validated and identified, nothing downloaded. A malformed
/// plan is `invalid_argument`; a bad directory is the `model` error that call
/// returns.
///
/// The engine this returns holds one copy of the weights per language. The entry
/// points of revisions 1 and 2 still work on it and read with the default set.
///
/// # Safety
/// `plan` is valid for `length`; `out_error` is null or a caller-owned slot.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_create_languages(
    plan: *const u8,
    length: usize,
    out_error: *mut *mut ToError,
) -> *mut ToEngine {
    unsafe {
        guard_ptr(out_error, || {
            let bytes = read_bytes(plan, length, "language plan")?;

            if bytes.is_empty() {
                return Err(ToError::invalid_argument("the language plan is empty"));
            }

            let value: serde_json::Value = serde_json::from_slice(&bytes).map_err(|error| {
                ToError::invalid_argument(format!("the language plan is not valid JSON: {error}"))
            })?;

            let default = value
                .get("default")
                .and_then(|tag| tag.as_str())
                .ok_or_else(|| {
                    ToError::invalid_argument("the language plan has no 'default' language")
                })?;

            let dictionary = match value.get("dictionary") {
                None | Some(serde_json::Value::Null) => None,
                Some(serde_json::Value::String(text)) if text.is_empty() => None,
                Some(serde_json::Value::String(text)) => Some(text.clone()),
                Some(_) => {
                    return Err(ToError::invalid_argument(
                        "the language plan's 'dictionary' must be a string or null",
                    ));
                }
            };

            let sets = value
                .get("sets")
                .and_then(|sets| sets.as_array())
                .ok_or_else(|| {
                    ToError::invalid_argument("the language plan has no 'sets' array")
                })?;

            if sets.is_empty() {
                return Err(ToError::invalid_argument(
                    "the language plan holds no model set",
                ));
            }

            let mut catalogue_sets = Vec::with_capacity(sets.len());

            for entry in sets {
                let language = entry
                    .get("language")
                    .and_then(|tag| tag.as_str())
                    .ok_or_else(|| ToError::invalid_argument("a model set has no 'language'"))?;

                let directory = entry
                    .get("directory")
                    .and_then(|path| path.as_str())
                    .ok_or_else(|| {
                        ToError::invalid_argument(format!(
                            "the model set for language '{language}' has no 'directory'"
                        ))
                    })?;

                let set = ModelSet::from_directory(std::path::Path::new(directory))?;
                catalogue_sets.push((language.to_string(), set));
            }

            let catalogue = LanguageCatalogue::new(default, catalogue_sets)?;
            let engines = LanguageEngines::build(catalogue, dictionary.as_deref())?;

            Ok(engine_handle(EngineSet::Languages(engines)))
        })
    }
}

/// The identity of the models an engine runs.
///
/// `{"detection":{"role","format","length","sha256"},"recognition":{…},"set":"…"}`,
/// where `set` is `SHA-256(detection_digest || recognition_digest)`. The same
/// object is embedded in every result document, so a report can name the weights
/// that produced a reading without asking the engine.
///
/// # Safety
/// `engine` is null or a valid handle; `out_json` and `out_error` are null or
/// caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_models(
    engine: *const ToEngine,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        guard_buffer(out_error, out_json, || {
            if engine.is_null() {
                return Err(ToError::invalid_argument("engine must not be null"));
            }

            Ok((*engine)
                .engine
                .default_engine()
                .models()
                .to_json()
                .to_string()
                .into_bytes())
        })
    }
}

/// Reads a UTF-8 path the caller owns. This ABI does not accept NUL-terminated
/// strings and does not accept a path that is not valid UTF-8: a Windows path that
/// is not is a path this API refuses by name rather than mangles.
///
/// # Safety
/// `pointer` is valid for `length` bytes, or null with a length of 0.
unsafe fn read_path(pointer: *const u8, length: usize, what: &str) -> Result<PathBuf, ToError> {
    let bytes = unsafe { read_bytes(pointer, length, what)? };

    if bytes.is_empty() {
        return Err(ToError::invalid_argument(format!("{what} is empty")));
    }

    let text = std::str::from_utf8(&bytes)
        .map_err(|_| ToError::invalid_argument(format!("{what} is not valid UTF-8")))?;

    Ok(PathBuf::from(text))
}

/// # Safety
/// `engine` must come from `to_engine_create` and must not be freed twice.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_free(engine: *mut ToEngine) {
    if engine.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(engine) });
}

/// Recognises an encoded image (PNG, JPEG, BMP, GIF, TIFF or WebP).
///
/// The three thresholds are the ones `M3scalinum.Text.Ocr`'s `OcrOptions` carries:
/// a page confidence minimum, a word confidence minimum and the tolerated
/// proportion of flagged words, each in [0, 1]. A threshold outside that range is
/// refused as `invalid_argument`; it is never silently ignored, because ignoring it
/// would turn a refusal rule off.
///
/// No preprocessing is done: this is the entry point of ABI revision 1 and 2. A
/// caller that wants the page turned upright or levelled calls
/// [`to_engine_recognize_preprocessed`], which is the same call with three flags.
///
/// On success `out_json` holds the result document and the return value is `TO_OK`;
/// on failure the return value is the error code and `out_error` holds the error.
///
/// # Safety
/// `engine` is a valid handle; `image` is valid for `length`; `out_json` and
/// `out_error` are null or caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        recognize_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::default(),
            ImageBounds::default(),
            out_json,
            out_error,
        )
    }
}

/// Recognises an encoded image after the requested preprocessing.
///
/// `detect_rotation`, `deskew` and `denoise` are the three options `OcrOptions`
/// carries; 0 leaves that stage off and any other value turns it on. The page is
/// turned upright, levelled and denoised before the recogniser sees it, and the
/// result document carries a `preprocess` object naming what was done.
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize_preprocessed(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        recognize_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
            ImageBounds::default(),
            out_json,
            out_error,
        )
    }
}

/// The body of both image entry points, so the two signatures share one path.
///
/// # Safety
/// `engine` is null or a valid handle; `image` is valid for `length`.
#[allow(clippy::too_many_arguments)]
unsafe fn recognize_image(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    thresholds: Thresholds,
    preprocess: PreprocessOptions,
    bounds: ImageBounds,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let bytes = read_bytes(image, length, "image")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("image is empty"));
            }
            handle
                .engine
                .default_engine()
                .recognize_image(&bytes, thresholds, preprocess, bounds)
        })
    }
}

/// Reports the size an image declares, and refuses it before it is decoded.
///
/// This is the preflight behind every image entry point: the byte ceiling is applied to
/// `length` alone, then the pixel ceiling to the dimensions the format header states, so
/// no pixels are allocated. On success `out_json` holds
/// `{"width":W,"height":H,"pixels":N,"format":"png","maximumPixels":…,"maximumBytes":…}`;
/// on refusal the return value is `TO_ERR_IMAGE_LIMIT` and the error names the size
/// observed and the ceiling.
///
/// `maximum_pixels` and `maximum_bytes` are ceilings that may only be lowered: zero takes
/// the library default (64 000 000 pixels and 256 MiB), and a value above the hard maximum
/// is refused rather than clamped.
///
/// # Safety
/// `image` is valid for `length`; `out_json` and `out_error` are null or caller-owned slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_image_admit(
    image: *const u8,
    length: usize,
    maximum_pixels: u64,
    maximum_bytes: usize,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        guard_buffer(out_error, out_json, || {
            let bounds = ImageBounds::from_options(maximum_pixels, maximum_bytes)?;
            let bytes = read_bytes(image, length, "image")?;
            let header: ImageHeader = limits::admit(&bytes, bounds)?;

            Ok(serde_json::json!({
                "width": header.width,
                "height": header.height,
                "pixels": header.pixels(),
                "format": header.format_name(),
                "maximumPixels": bounds.maximum_pixels,
                "maximumBytes": bounds.maximum_bytes,
            })
            .to_string()
            .into_bytes())
        })
    }
}

/// Recognises an encoded image with explicit decode ceilings.
///
/// The counterpart of [`to_engine_recognize`] for a caller that wants to bound what the
/// decode may cost. `maximum_pixels` and `maximum_bytes` may only be lowered: zero takes
/// the library default (64 000 000 pixels and 256 MiB), and a value above the hard maximum
/// is `invalid_argument` rather than a silent clamp. An image past either ceiling is
/// `TO_ERR_IMAGE_LIMIT`, refused before its pixels are allocated.
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn to_engine_recognize_limited(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    maximum_pixels: u64,
    maximum_bytes: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        let bounds = match ImageBounds::from_options(maximum_pixels, maximum_bytes) {
            Ok(bounds) => bounds,
            Err(error) => return guard_buffer(out_error, out_json, || Err(error)),
        };

        recognize_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::default(),
            bounds,
            out_json,
            out_error,
        )
    }
}

/// Recognises an encoded image after the requested preprocessing, under explicit ceilings.
///
/// [`to_engine_recognize_preprocessed`] with the two decode ceilings of
/// [`to_engine_recognize_limited`].
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn to_engine_recognize_preprocessed_limited(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    maximum_pixels: u64,
    maximum_bytes: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        let bounds = match ImageBounds::from_options(maximum_pixels, maximum_bytes) {
            Ok(bounds) => bounds,
            Err(error) => return guard_buffer(out_error, out_json, || Err(error)),
        };

        recognize_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
            bounds,
            out_json,
            out_error,
        )
    }
}

/// Renders and recognises a PDF.
///
/// `first_page` is zero-based; `page_count` 0 means "to the end". Each page is
/// assessed on its own with the same thresholds as [`to_engine_recognize`]. No
/// preprocessing is done; see [`to_engine_recognize_pdf_preprocessed`].
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize_pdf(
    engine: *mut ToEngine,
    pdf: *const u8,
    length: usize,
    dpi: u32,
    first_page: u32,
    page_count: u32,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        recognize_pdf(
            engine,
            pdf,
            length,
            dpi,
            first_page,
            page_count,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::default(),
            out_json,
            out_error,
        )
    }
}

/// Renders pages of a PDF and recognises each one after the requested preprocessing.
///
/// Each page is preprocessed and assessed on its own, so one page turned by a scanner
/// does not turn the others.
///
/// # Safety
/// Same contract as [`to_engine_recognize`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_engine_recognize_pdf_preprocessed(
    engine: *mut ToEngine,
    pdf: *const u8,
    length: usize,
    dpi: u32,
    first_page: u32,
    page_count: u32,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        recognize_pdf(
            engine,
            pdf,
            length,
            dpi,
            first_page,
            page_count,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
            out_json,
            out_error,
        )
    }
}

/// The body of both PDF entry points, so the two signatures share one path.
///
/// # Safety
/// `engine` is null or a valid handle; `pdf` is valid for `length`.
#[allow(clippy::too_many_arguments)]
unsafe fn recognize_pdf(
    engine: *mut ToEngine,
    pdf: *const u8,
    length: usize,
    dpi: u32,
    first_page: u32,
    page_count: u32,
    thresholds: Thresholds,
    preprocess: PreprocessOptions,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let bytes = read_bytes(pdf, length, "pdf")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("pdf is empty"));
            }
            handle
                .engine
                .default_engine()
                .recognize_pdf(&bytes, dpi, first_page, page_count, thresholds, preprocess)
        })
    }
}

/// Recognises an encoded image with the model set of a named language.
///
/// `language` is UTF-8 bytes, not NUL-terminated; an empty tag means "no request",
/// which is the default set. A tag the engine has no set for is **not** an error:
/// the image is read with the default set and the result carries a `language`
/// object saying which set was asked for, which one ran, and why. It is never
/// silent.
///
/// The three preprocessing flags are as in [`to_engine_recognize_preprocessed`],
/// and the engine must have been built by [`to_engine_create_languages`]; one built
/// from two models is refused as `invalid_argument` rather than guessed at.
///
/// # Safety
/// `engine` is a valid handle; `image` and `language` are valid for their lengths;
/// `out_json` and `out_error` are null or caller-owned slots.
#[unsafe(no_mangle)]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn to_engine_recognize_language(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    language: *const u8,
    language_len: usize,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        recognize_language_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
            ImageBounds::default(),
            language,
            language_len,
            out_json,
            out_error,
        )
    }
}

/// Recognises an encoded image with the model set of a named language, under explicit
/// decode ceilings.
///
/// [`to_engine_recognize_language`] with the two decode ceilings of
/// [`to_engine_recognize_limited`]: they may only be lowered, and an image past either is
/// `TO_ERR_IMAGE_LIMIT` before its pixels are allocated.
///
/// # Safety
/// Same contract as [`to_engine_recognize_language`].
#[unsafe(no_mangle)]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn to_engine_recognize_language_limited(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    maximum_pixels: u64,
    maximum_bytes: usize,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    language: *const u8,
    language_len: usize,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        let bounds = match ImageBounds::from_options(maximum_pixels, maximum_bytes) {
            Ok(bounds) => bounds,
            Err(error) => return guard_buffer(out_error, out_json, || Err(error)),
        };

        recognize_language_image(
            engine,
            image,
            length,
            Thresholds {
                page: page_confidence,
                word: word_confidence,
                max_rejected_ratio: max_rejected_word_ratio,
            },
            PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
            bounds,
            language,
            language_len,
            out_json,
            out_error,
        )
    }
}

/// The body of both language image entry points, so the two signatures share one path.
///
/// # Safety
/// `engine` is null or a valid handle; `image` and `language` are valid for their lengths.
#[allow(clippy::too_many_arguments)]
unsafe fn recognize_language_image(
    engine: *mut ToEngine,
    image: *const u8,
    length: usize,
    thresholds: Thresholds,
    preprocess: PreprocessOptions,
    bounds: ImageBounds,
    language: *const u8,
    language_len: usize,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let languages = handle.engine.languages().ok_or_else(|| {
                ToError::invalid_argument(
                    "this engine holds one model set; build it with to_engine_create_languages \
                     to name a language",
                )
            })?;

            let bytes = read_bytes(image, length, "image")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("image is empty"));
            }

            let requested = read_language(language, language_len)?;

            languages.recognize_image(&bytes, thresholds, preprocess, requested.as_deref(), bounds)
        })
    }
}

/// Renders and recognises a PDF, reading each page with the set of its language.
///
/// `page_languages` is a UTF-8 JSON document, not NUL-terminated: an array with one
/// entry per page of the requested range, in page order, each entry a language tag
/// or null for "no request":
///
/// ```json
/// ["fr", "en"]
/// ```
///
/// The count must match the number of pages rendered — a misaligned plan is refused
/// as `invalid_argument` rather than shifted, because a shift would read a page with
/// another page's language and say nothing. An empty document means every page on
/// the default set.
///
/// Each page of the result carries its own `language` object and its own `models`
/// identity, because two pages can have been read by two pairs of weights; the root
/// carries `languages: {default, available}`.
///
/// # Safety
/// `engine` is a valid handle; `pdf` and `page_languages` are valid for their
/// lengths; `out_json` and `out_error` are null or caller-owned slots.
#[unsafe(no_mangle)]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn to_engine_recognize_pdf_languages(
    engine: *mut ToEngine,
    pdf: *const u8,
    length: usize,
    dpi: u32,
    first_page: u32,
    page_count: u32,
    page_confidence: f64,
    word_confidence: f64,
    max_rejected_word_ratio: f64,
    page_languages: *const u8,
    page_languages_len: usize,
    detect_rotation: i32,
    deskew: i32,
    denoise: i32,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        if engine.is_null() {
            return guard_buffer(out_error, out_json, || {
                Err(ToError::invalid_argument("engine must not be null"))
            });
        }

        let handle = &*engine;

        guard_buffer(out_error, out_json, || {
            let languages = handle.engine.languages().ok_or_else(|| {
                ToError::invalid_argument(
                    "this engine holds one model set; build it with to_engine_create_languages \
                     to choose a language per page",
                )
            })?;

            let bytes = read_bytes(pdf, length, "pdf")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("pdf is empty"));
            }

            let plan = read_bytes(page_languages, page_languages_len, "page languages")?;
            let plan = PageLanguages::parse(&plan)?;

            languages.recognize_pdf(
                &bytes,
                dpi,
                first_page,
                page_count,
                Thresholds {
                    page: page_confidence,
                    word: word_confidence,
                    max_rejected_ratio: max_rejected_word_ratio,
                },
                PreprocessOptions::from_flags(detect_rotation, deskew, denoise),
                &plan,
            )
        })
    }
}

/// The number of pages a PDF holds, as `{"pages":N}`.
///
/// A caller needs it to name a language per page when it reads to the end of a
/// document whose length it does not know. Only the page tree is read: nothing is
/// rasterised and no model is involved. An empty document is refused, as it is by
/// every recognition call.
///
/// # Safety
/// `pdf` is valid for `length`; `out_json` and `out_error` are null or caller-owned
/// slots.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_pdf_page_count(
    pdf: *const u8,
    length: usize,
    out_json: *mut ToBuffer,
    out_error: *mut *mut ToError,
) -> i32 {
    unsafe {
        guard_buffer(out_error, out_json, || {
            let bytes = read_bytes(pdf, length, "pdf")?;
            if bytes.is_empty() {
                return Err(ToError::invalid_argument("pdf is empty"));
            }

            let pages = pdf::page_count(&bytes)?;
            Ok(serde_json::json!({ "pages": pages })
                .to_string()
                .into_bytes())
        })
    }
}

/// Reads a language tag the caller owns: UTF-8 bytes and a length.
///
/// An empty range is "no request", which is not an error: a caller that resolves a
/// page to no language says so by passing nothing.
///
/// # Safety
/// `pointer` is valid for `length` bytes, or null with a length of 0.
unsafe fn read_language(pointer: *const u8, length: usize) -> Result<Option<String>, ToError> {
    let bytes = unsafe { read_bytes(pointer, length, "language") }?;

    if bytes.is_empty() {
        return Ok(None);
    }

    let text = std::str::from_utf8(&bytes)
        .map_err(|_| ToError::invalid_argument("language is not valid UTF-8"))?;

    Ok(normalize_tag(text))
}

/// Reads a NUL-terminated UTF-8 string the native side owns (a version or build
/// info constant). Exposed for completeness with the compiler crate's ABI.
///
/// # Safety
/// `pointer` is null or a valid NUL-terminated string owned by this library.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn to_string_free(pointer: *const c_char) {
    // The strings this library returns are static; this exists so a host can
    // mirror the `tn_` lifecycle without special-casing, and does nothing.
    let _ = pointer;
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ffi::{TO_ERR_INVALID_ARGUMENT, TO_ERR_MODEL, TO_OK};

    #[test]
    fn abi_version_is_two() {
        assert_eq!(to_abi_version(), 2);
    }

    #[test]
    fn live_count_is_zero_with_no_engine() {
        assert_eq!(to_engine_live_count(), 0);
    }

    /// A file whose header is a well-formed RTEN v2 header for a 1 KB graph.
    fn rten_bytes(total: usize) -> Vec<u8> {
        let mut bytes = vec![0u8; total];
        let model_offset = 32u64;
        bytes[..4].copy_from_slice(b"RTEN");
        bytes[4..8].copy_from_slice(&2u32.to_le_bytes());
        bytes[8..16].copy_from_slice(&model_offset.to_le_bytes());
        bytes[16..24].copy_from_slice(&1024u64.to_le_bytes());
        bytes[24..32].copy_from_slice(&(model_offset + 1024).to_le_bytes());
        bytes
    }

    #[test]
    fn model_inspect_reports_the_digest_and_the_role_without_loading() {
        let bytes = rten_bytes(4096);
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status =
            unsafe { to_model_inspect(bytes.as_ptr(), bytes.len(), 0, &mut json, &mut error) };

        assert_eq!(status, TO_OK);
        assert!(error.is_null());

        let document: serde_json::Value =
            serde_json::from_slice(unsafe { std::slice::from_raw_parts(json.ptr, json.len) })
                .expect("the document is JSON");

        assert_eq!(document["valid"], true);
        assert_eq!(document["role"], "detection");
        assert_eq!(document["format"], "rten");
        assert_eq!(document["length"], 4096);
        assert_eq!(document["sha256"].as_str().expect("a string").len(), 64);
        assert_eq!(to_engine_live_count(), 0);

        unsafe { json.free() };
    }

    #[test]
    fn model_inspect_refuses_a_kind_it_does_not_know() {
        let bytes = rten_bytes(4096);
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status =
            unsafe { to_model_inspect(bytes.as_ptr(), bytes.len(), 7, &mut json, &mut error) };

        assert_eq!(status, TO_ERR_INVALID_ARGUMENT);
        assert!(!error.is_null());

        let message = unsafe { (*error).json.clone() };
        assert!(message.contains("model kind must be 0"), "{message}");

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn model_inspect_refuses_a_file_that_is_not_a_model() {
        let junk = b"this is a text file, not a model";
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status =
            unsafe { to_model_inspect(junk.as_ptr(), junk.len(), 1, &mut json, &mut error) };

        assert_eq!(status, TO_ERR_MODEL);
        assert!(!error.is_null());

        let document = unsafe { (*error).json.clone() };
        assert!(
            document.contains("\"defect\":\"unrecognized_format\""),
            "{document}"
        );
        assert!(document.contains("\"role\":\"recognition\""), "{document}");

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn watching_the_models_of_a_null_engine_is_an_argument_error_not_a_crash() {
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status = unsafe { to_engine_models(std::ptr::null(), &mut json, &mut error) };

        assert_eq!(status, TO_ERR_INVALID_ARGUMENT);
        assert!(!error.is_null());

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn a_missing_model_directory_is_refused_without_creating_a_handle() {
        let directory = std::env::temp_dir().join("sharp-text-ocr-abi-no-such-directory");
        let path = directory.display().to_string();
        let mut error: *mut ToError = std::ptr::null_mut();
        let before = to_engine_live_count();

        let engine = unsafe {
            to_engine_create_from_directory(
                path.as_ptr(),
                path.len(),
                std::ptr::null(),
                0,
                &mut error,
            )
        };

        assert!(engine.is_null());
        assert!(!error.is_null());
        assert_eq!(to_engine_live_count(), before);

        let document = unsafe { (*error).json.clone() };
        assert!(
            document.contains("\"defect\":\"missing_directory\""),
            "{document}"
        );

        unsafe { to_error_free(error) };
    }

    #[test]
    fn a_language_plan_that_is_empty_or_malformed_is_refused_without_a_handle() {
        let before = to_engine_live_count();

        for plan in [
            b"".as_slice(),
            b"not json".as_slice(),
            b"{}".as_slice(),
            br#"{"default":"fr"}"#.as_slice(),
        ] {
            let mut error: *mut ToError = std::ptr::null_mut();

            let engine =
                unsafe { to_engine_create_languages(plan.as_ptr(), plan.len(), &mut error) };

            assert!(engine.is_null(), "{:?}", String::from_utf8_lossy(plan));
            assert!(!error.is_null());
            assert_eq!(unsafe { (*error).code }, TO_ERR_INVALID_ARGUMENT);
            unsafe { to_error_free(error) };
        }

        assert_eq!(to_engine_live_count(), before);
    }

    #[test]
    fn a_language_plan_naming_a_missing_directory_is_refused_by_name() {
        let plan = br#"{"default":"fr","sets":[{"language":"fr","directory":"/nope/ocr-models"}]}"#;
        let mut error: *mut ToError = std::ptr::null_mut();

        let engine = unsafe { to_engine_create_languages(plan.as_ptr(), plan.len(), &mut error) };

        assert!(engine.is_null());

        let document = unsafe { (*error).json.clone() };
        assert!(
            document.contains("\"defect\":\"missing_directory\""),
            "{document}"
        );

        unsafe { to_error_free(error) };
    }

    #[test]
    fn the_page_count_of_a_document_that_is_not_a_pdf_is_a_pdf_error() {
        let junk = b"this is not a pdf";
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status = unsafe { to_pdf_page_count(junk.as_ptr(), junk.len(), &mut json, &mut error) };

        assert_eq!(status, crate::ffi::TO_ERR_PDF);
        assert!(!error.is_null());

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn the_page_count_of_an_empty_document_is_an_argument_error() {
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status = unsafe { to_pdf_page_count(std::ptr::null(), 0, &mut json, &mut error) };

        assert_eq!(status, TO_ERR_INVALID_ARGUMENT);

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn admitting_a_forty_thousand_square_png_is_a_typed_refusal_through_the_abi() {
        let encoded = crate::limits::png_declaring(40_000, 40_000);
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status =
            unsafe { to_image_admit(encoded.as_ptr(), encoded.len(), 0, 0, &mut json, &mut error) };

        assert_eq!(status, crate::ffi::TO_ERR_IMAGE_LIMIT);
        assert!(json.ptr.is_null());

        let document = unsafe { (*error).json.clone() };
        assert!(document.contains("\"code\":\"image_limit\""), "{document}");
        assert!(document.contains("\"observed\":1600000000"), "{document}");
        assert!(document.contains("\"maximum\":64000000"), "{document}");

        unsafe {
            to_error_free(error);
            json.free();
        }
    }

    #[test]
    fn admitting_a_normal_image_reports_the_size_its_header_states() {
        let encoded = crate::limits::png_declaring(320, 200);
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status =
            unsafe { to_image_admit(encoded.as_ptr(), encoded.len(), 0, 0, &mut json, &mut error) };

        assert_eq!(status, TO_OK);
        assert!(error.is_null());

        let document: serde_json::Value =
            serde_json::from_slice(unsafe { std::slice::from_raw_parts(json.ptr, json.len) })
                .expect("the document is JSON");

        assert_eq!(document["width"], 320);
        assert_eq!(document["height"], 200);
        assert_eq!(document["pixels"], 64_000);
        assert_eq!(document["format"], "png");
        assert_eq!(document["maximumPixels"], 64_000_000u64);
        assert_eq!(document["maximumBytes"], 268_435_456u64);

        unsafe { json.free() };
    }

    #[test]
    fn a_ceiling_above_the_hard_maximum_is_refused_before_the_image_is_looked_at() {
        let encoded = crate::limits::png_declaring(320, 200);
        let mut json = ToBuffer::empty();
        let mut error: *mut ToError = std::ptr::null_mut();

        let status = unsafe {
            to_image_admit(
                encoded.as_ptr(),
                encoded.len(),
                1_000_000_000,
                0,
                &mut json,
                &mut error,
            )
        };

        assert_eq!(status, TO_ERR_INVALID_ARGUMENT);

        let document = unsafe { (*error).json.clone() };
        assert!(document.contains("may only be lowered"), "{document}");

        unsafe {
            to_error_free(error);
            json.free();
        }
    }
}
