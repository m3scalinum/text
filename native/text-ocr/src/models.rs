//! The model files: what this library accepts, what it refuses, and the
//! fingerprint that names the weights a reading was produced with.
//!
//! Three layers, in this order, each refusing with a typed error that says what is
//! wrong rather than letting the failure surface as a `panic` at inference time:
//!
//! 1. **The container**, before anything is parsed: an empty file, a file below
//!    the plausible size of a model, a file that starts with neither the `RTEN`
//!    magic nor an ONNX model signature, an `RTEN` file whose declared sections
//!    point past its end (a truncated download), or a version this crate cannot
//!    read.
//! 2. **The graph**, once `rten` has parsed it but before the engine is built: the
//!    detection model must take a rank-4 NCHW input with one channel and fixed
//!    spatial dimensions, and the recognition model a rank-4 NCHW input with one
//!    channel. `ocrs` dereferences those dimensions at recognition time, so a
//!    model of the wrong generation is refused here instead of panicking there.
//! 3. **The identity**: a SHA-256 per file, and one SHA-256 for the pair.
//!
//! Nothing here downloads a model, looks in a default directory, or picks a
//! fallback: the bytes and the directory a caller names are the only ones read.

use std::path::{Path, PathBuf};

use rten::{Dimension, Model};
use serde_json::{Value, json};

use crate::ffi::{TO_ERR_MODEL, ToError};
use crate::sha256;

/// The detection model's file name inside a model directory.
pub const DETECTION_FILE_NAME: &str = "detection.rten";

/// The recognition model's file name inside a model directory.
pub const RECOGNITION_FILE_NAME: &str = "recognition.rten";

/// The smallest file this library will even hand to the loader.
///
/// A real detection or recognition model is tens of megabytes; four kilobytes is
/// far below anything published and far above the text file, JSON document or
/// empty download that usually arrives by mistake. It is a screen, not a
/// guarantee: the authoritative check is the loader's parse.
pub const MIN_MODEL_BYTES: usize = 4 * 1024;

/// The largest file this library will read into memory.
///
/// One gigabyte is fifty times the published weights and small enough that a
/// caller who named the wrong file by accident gets an error rather than an
/// out-of-memory kill.
pub const MAX_MODEL_BYTES: usize = 1024 * 1024 * 1024;

const RTEN_MAGIC: [u8; 4] = *b"RTEN";
const RTEN_V2_VERSION: u32 = 2;

/// The length of the `RTEN` v2 header: magic, version, and three 64-bit offsets.
const RTEN_V2_HEADER_LEN: usize = 4 + 4 + 8 + 8 + 8;

/// Which of the two models a file is.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ModelKind {
    /// Finds the words on a page.
    Detection,
    /// Reads the characters of a word.
    Recognition,
}

impl ModelKind {
    /// Both models, detection first.
    pub const ALL: [Self; 2] = [Self::Detection, Self::Recognition];

    /// The file name this model is expected to have in a model directory.
    pub fn file_name(self) -> &'static str {
        match self {
            Self::Detection => DETECTION_FILE_NAME,
            Self::Recognition => RECOGNITION_FILE_NAME,
        }
    }

    /// The name an error and a report use for this model.
    pub fn label(self) -> &'static str {
        match self {
            Self::Detection => "detection model",
            Self::Recognition => "recognition model",
        }
    }

    /// The short name a result document and an error field carry.
    pub fn name(self) -> &'static str {
        match self {
            Self::Detection => "detection",
            Self::Recognition => "recognition",
        }
    }

    /// The input shape `ocrs` needs for this model, in words an error can carry.
    pub fn expected_input_shape(self) -> &'static str {
        match self {
            Self::Detection => {
                "a rank-4 NCHW input [1, 1, height, width] with one channel and fixed height and width"
            }
            Self::Recognition => "a rank-4 NCHW input [1, 1, height, width] with one channel",
        }
    }

    /// The kind an ABI code names, if it names one.
    pub fn from_code(code: i32) -> Option<Self> {
        match code {
            0 => Some(Self::Detection),
            1 => Some(Self::Recognition),
            _ => None,
        }
    }
}

/// The container a model file is in, as far as its leading bytes tell.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ModelFormat {
    /// The `.rten` container: an `RTEN` header, a FlatBuffers graph, tensor data.
    Rten,
    /// An ONNX ModelProto, which is a serialized protocol-buffer message.
    Onnx,
}

impl ModelFormat {
    /// The name a report and an error field carry.
    pub fn name(self) -> &'static str {
        match self {
            Self::Rten => "rten",
            Self::Onnx => "onnx",
        }
    }
}

/// What exactly is wrong with a model file.
///
/// The variants carry the numbers that went into the decision, so the message a
/// caller reads says "12 bytes, 32 needed" rather than "invalid model".
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ModelDefect {
    /// The file holds no byte at all.
    Empty,
    /// The file is smaller than [`MIN_MODEL_BYTES`].
    TooSmall {
        /// The length observed.
        length: usize,
        /// The floor.
        minimum: usize,
    },
    /// The file is larger than [`MAX_MODEL_BYTES`].
    TooLarge {
        /// The length observed.
        length: usize,
        /// The ceiling.
        maximum: usize,
    },
    /// The file starts with neither `RTEN` nor an ONNX signature.
    UnrecognizedFormat {
        /// The first bytes, as hexadecimal, so the caller can see what arrived.
        leading: String,
    },
    /// The file starts with `RTEN` but is shorter than the header.
    TruncatedHeader {
        /// The length observed.
        length: usize,
        /// The header length.
        minimum: usize,
    },
    /// The file is an `RTEN` container of a version this crate cannot read.
    UnsupportedVersion {
        /// The version the header declares.
        version: u32,
    },
    /// The header's declared sections cannot all fit in the file as it is.
    InconsistentLayout {
        /// What the sections say, and where they disagree with the file.
        detail: String,
    },
    /// The parsed graph's input is not the shape `ocrs` needs.
    InputShape {
        /// The shape the model declares, in words.
        found: String,
        /// The shape the role needs, in words.
        expected: String,
    },
    /// The container is well formed but the loader cannot parse the graph in it.
    UnparsableGraph {
        /// What the loader said.
        error: String,
    },
    /// A model directory does not hold a file it must hold.
    MissingFile {
        /// The absolute path of the missing file.
        path: String,
    },
    /// The directory the caller named does not exist.
    MissingDirectory {
        /// The absolute path named.
        path: String,
    },
    /// The path the caller named is a file, not a directory.
    NotADirectory {
        /// The absolute path named.
        path: String,
    },
    /// The file is there but cannot be read.
    UnreadableFile {
        /// The absolute path of the file.
        path: String,
        /// What the operating system said.
        error: String,
    },
}

impl ModelDefect {
    /// The stable name a caller switches on. Never reworded: a host may match it.
    pub fn name(&self) -> &'static str {
        match self {
            Self::Empty => "empty",
            Self::TooSmall { .. } => "too_small",
            Self::TooLarge { .. } => "too_large",
            Self::UnrecognizedFormat { .. } => "unrecognized_format",
            Self::TruncatedHeader { .. } => "truncated_header",
            Self::UnsupportedVersion { .. } => "unsupported_version",
            Self::InconsistentLayout { .. } => "inconsistent_layout",
            Self::InputShape { .. } => "input_shape",
            Self::UnparsableGraph { .. } => "unparsable_graph",
            Self::MissingFile { .. } => "missing_file",
            Self::MissingDirectory { .. } => "missing_directory",
            Self::NotADirectory { .. } => "not_a_directory",
            Self::UnreadableFile { .. } => "unreadable_file",
        }
    }

    /// The file or directory this defect names, when it names one.
    pub fn path(&self) -> Option<&str> {
        match self {
            Self::MissingFile { path }
            | Self::MissingDirectory { path }
            | Self::NotADirectory { path }
            | Self::UnreadableFile { path, .. } => Some(path),
            _ => None,
        }
    }

    /// What is wrong, as a predicate that follows the subject of the sentence.
    fn detail(&self) -> String {
        match self {
            Self::Empty => {
                format!("is empty: an RTEN or ONNX model is at least {MIN_MODEL_BYTES} bytes")
            }
            Self::TooSmall { length, minimum } => format!(
                "is {length} bytes, below the {minimum}-byte floor for an RTEN or ONNX model; \
                 the file is truncated, or it is not a model"
            ),
            Self::TooLarge { length, maximum } => format!(
                "is {length} bytes, above the {maximum}-byte ceiling; this is not a model file \
                 or it was not meant to be read into memory"
            ),
            Self::UnrecognizedFormat { leading } => format!(
                "does not start with the RTEN magic or an ONNX model signature \
                 (first bytes: {leading}); pass the .rten or .onnx file the model was shipped as, \
                 not an archive, an image or a text file"
            ),
            Self::TruncatedHeader { length, minimum } => format!(
                "starts with the RTEN magic but is {length} bytes, short of the \
                 {minimum}-byte RTEN v2 header: the file is truncated"
            ),
            Self::UnsupportedVersion { version } => format!(
                "is an RTEN v{version} container; this library reads RTEN v{RTEN_V2_VERSION} \
                 (and ONNX), so re-export the model with a matching rten-convert"
            ),
            Self::InconsistentLayout { detail } => detail.clone(),
            Self::UnparsableGraph { error } => format!(
                "is a container this library recognizes, but the graph inside it could not be \
                 parsed: {error}"
            ),
            Self::InputShape { found, expected } => format!(
                "declares an input shape of {found}, but the engine needs {expected}; \
                 this model is not the one this role runs"
            ),
            Self::MissingFile { path } => format!("is missing: expected the file {path}"),
            Self::MissingDirectory { path } => {
                format!("is not a directory that exists: {path} does not exist")
            }
            Self::NotADirectory { path } => format!("is a file, not a directory: {path}"),
            Self::UnreadableFile { path, error } => {
                format!("could not be read from {path}: {error}")
            }
        }
    }
}

/// Turns a defect into the error that crosses the ABI, carrying the role, the
/// defect name and the file it concerns.
///
/// The fields are separate from the message because a host should be able to act
/// on them without parsing prose: the JSON is
/// `{"code":"model","defect":"…","role":"…","file":"…","message":"…"}`, and a
/// field is present only when it is known.
pub fn model_error(kind: Option<ModelKind>, file: Option<&str>, defect: ModelDefect) -> ToError {
    let file = file.or_else(|| defect.path());

    let subject = match (kind, file) {
        (Some(kind), Some(file)) => format!("{} ({file})", kind.label()),
        (Some(kind), None) => kind.label().to_string(),
        (None, Some(file)) => format!("model ({file})"),
        (None, None) => "model".to_string(),
    };

    let message = format!("{subject} {}", defect.detail());

    let mut fields: Vec<(&str, &str)> = vec![("defect", defect.name())];
    if let Some(kind) = kind {
        fields.push(("role", kind.name()));
    }
    if let Some(file) = file {
        fields.push(("file", file));
    }

    ToError::with_fields(TO_ERR_MODEL, message, &fields)
}

/// What one model file is, as a caller can record it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ModelFingerprint {
    /// Which role this file fills.
    pub kind: ModelKind,
    /// The file name, when the file was discovered in a directory.
    pub file_name: Option<String>,
    /// The container the leading bytes declare.
    pub format: ModelFormat,
    /// The length in bytes.
    pub length: usize,
    /// The SHA-256 of the bytes.
    pub sha256: [u8; 32],
}

impl ModelFingerprint {
    /// The digest as sixty-four lowercase hexadecimal characters.
    pub fn sha256_hex(&self) -> String {
        sha256::hex(&self.sha256)
    }

    /// The document a report carries for this file.
    pub fn to_json(&self) -> Value {
        let mut value = json!({
            "role": self.kind.name(),
            "format": self.format.name(),
            "length": self.length,
            "sha256": self.sha256_hex(),
        });

        if let Some(name) = &self.file_name {
            value["file"] = json!(name);
        }

        value
    }
}

/// The identity of a model pair: two fingerprints and one digest over both.
///
/// The pair digest is `SHA-256(detection_digest || recognition_digest)` — the two
/// 32-byte digests concatenated in that order. Order matters, so swapping the two
/// files changes the identity, and it is a pure function of the two files, so two
/// hosts that load the same weights report the same string.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ModelIdentity {
    detection: ModelFingerprint,
    recognition: ModelFingerprint,
    sha256: String,
}

impl ModelIdentity {
    /// The identity of a pair.
    pub fn new(detection: ModelFingerprint, recognition: ModelFingerprint) -> Self {
        let sha256 = sha256::hex(&sha256::digest_pair(&detection.sha256, &recognition.sha256));
        Self {
            detection,
            recognition,
            sha256,
        }
    }

    /// The document a result carries.
    pub fn to_json(&self) -> Value {
        json!({
            "detection": self.detection.to_json(),
            "recognition": self.recognition.to_json(),
            "set": self.sha256,
        })
    }
}

/// One validated model file: its bytes and what they are.
#[derive(Clone, Debug)]
pub struct ModelFile {
    /// The bytes the loader will parse.
    pub bytes: Vec<u8>,
    /// The file's fingerprint.
    pub fingerprint: ModelFingerprint,
}

impl ModelFile {
    /// Validates the container of `bytes` and fingerprints them.
    pub fn inspect(bytes: Vec<u8>, kind: ModelKind) -> Result<Self, ModelDefect> {
        let format = container_format(&bytes)?;
        let length = bytes.len();

        if length < MIN_MODEL_BYTES {
            return Err(ModelDefect::TooSmall {
                length,
                minimum: MIN_MODEL_BYTES,
            });
        }

        if length > MAX_MODEL_BYTES {
            return Err(ModelDefect::TooLarge {
                length,
                maximum: MAX_MODEL_BYTES,
            });
        }

        let fingerprint = ModelFingerprint {
            kind,
            file_name: None,
            format,
            length,
            sha256: sha256::digest(&bytes),
        };

        Ok(Self { bytes, fingerprint })
    }
}

/// Decides what container `bytes` is in, or why it is in none.
///
/// The `RTEN` check is the header of the v2 format: magic, version, then the
/// offset and length of the graph and the offset of the tensor data. The declared
/// sections are checked against the file's own length, which is what tells a
/// truncated download from a whole one. An ONNX model has no magic — it is a
/// protocol-buffer message — so it is recognized by the shape of its first field,
/// which is a screen and not a parse: `rten` parses it and refuses it if it is not
/// a model.
pub fn container_format(bytes: &[u8]) -> Result<ModelFormat, ModelDefect> {
    if bytes.is_empty() {
        return Err(ModelDefect::Empty);
    }

    if bytes.starts_with(&RTEN_MAGIC) {
        return rten_layout(bytes).map(|()| ModelFormat::Rten);
    }

    if looks_like_onnx(bytes) {
        return Ok(ModelFormat::Onnx);
    }

    Err(ModelDefect::UnrecognizedFormat {
        leading: leading_hex(bytes),
    })
}

fn rten_layout(bytes: &[u8]) -> Result<(), ModelDefect> {
    if bytes.len() < RTEN_V2_HEADER_LEN {
        return Err(ModelDefect::TruncatedHeader {
            length: bytes.len(),
            minimum: RTEN_V2_HEADER_LEN,
        });
    }

    let version = u32::from_le_bytes(word(&bytes[4..8]));
    if version != RTEN_V2_VERSION {
        return Err(ModelDefect::UnsupportedVersion { version });
    }

    let model_offset = u64::from_le_bytes(word(&bytes[8..16]));
    let model_len = u64::from_le_bytes(word(&bytes[16..24]));
    let tensor_offset = u64::from_le_bytes(word(&bytes[24..32]));
    let file_len = bytes.len() as u64;

    let inconsistent = |detail: String| ModelDefect::InconsistentLayout { detail };

    if model_offset < RTEN_V2_HEADER_LEN as u64 {
        return Err(inconsistent(format!(
            "declares the graph at offset {model_offset}, inside the \
             {RTEN_V2_HEADER_LEN}-byte header: the header is corrupt"
        )));
    }

    if model_len == 0 {
        return Err(inconsistent(
            "declares a zero-length graph: the file holds no model".to_string(),
        ));
    }

    let model_end = model_offset.checked_add(model_len).ok_or_else(|| {
        inconsistent(format!(
            "declares the graph at offset {model_offset} with length {model_len}, which overflows \
             a 64-bit offset: the header is corrupt"
        ))
    })?;

    if model_end > file_len {
        return Err(inconsistent(format!(
            "declares the graph from offset {model_offset} to {model_end}, past the end of the \
             {file_len}-byte file: the file is truncated"
        )));
    }

    // A zero tensor offset means the graph holds its weights inline, which a v2
    // file written without external tensors may legitimately do.
    if tensor_offset != 0 && tensor_offset < model_end {
        return Err(inconsistent(format!(
            "declares the tensor data at offset {tensor_offset}, before the graph it belongs to \
             ends at {model_end}: the header is corrupt"
        )));
    }

    if tensor_offset > file_len {
        return Err(inconsistent(format!(
            "declares the tensor data at offset {tensor_offset}, past the end of the \
             {file_len}-byte file: the file is truncated"
        )));
    }

    Ok(())
}

/// Whether the leading bytes look like an ONNX ModelProto.
///
/// Field 1 of `ModelProto` is `ir_version` (`int64`, so wire type 0, tag `0x08`)
/// and it is written first by every producer, with a small value. That is enough
/// to tell an ONNX file from a text file, an archive or a PNG; it is not enough to
/// tell a valid ONNX model from a corrupt one, which is the loader's job.
fn looks_like_onnx(bytes: &[u8]) -> bool {
    matches!(bytes, [0x08, version, ..] if (1..=64).contains(version))
}

fn leading_hex(bytes: &[u8]) -> String {
    bytes
        .iter()
        .take(4)
        .map(|byte| format!("{byte:02x}"))
        .collect::<Vec<_>>()
        .join(" ")
}

fn word<const N: usize>(bytes: &[u8]) -> [u8; N] {
    bytes.try_into().expect("the caller sliced exactly N bytes")
}

/// A detection model and a recognition model, validated and identified.
///
/// This is the pair a report names. It is built either from bytes the caller
/// already holds, or from a directory that holds `detection.rten` and
/// `recognition.rten`; there is no third way and no default path.
#[derive(Clone, Debug)]
pub struct ModelSet {
    directory: Option<PathBuf>,
    detection: ModelFile,
    recognition: ModelFile,
}

impl ModelSet {
    /// Validates two model files the caller holds.
    pub fn from_bytes(detection: &[u8], recognition: &[u8]) -> Result<Self, ToError> {
        let detection = ModelFile::inspect(detection.to_vec(), ModelKind::Detection)
            .map_err(|defect| model_error(Some(ModelKind::Detection), None, defect))?;
        let recognition = ModelFile::inspect(recognition.to_vec(), ModelKind::Recognition)
            .map_err(|defect| model_error(Some(ModelKind::Recognition), None, defect))?;

        Ok(Self {
            directory: None,
            detection,
            recognition,
        })
    }

    /// Reads, validates and identifies the two models of `directory`.
    ///
    /// The only file names this library ever looks for are
    /// `detection.rten` and `recognition.rten`; a missing one is an error that
    /// names the file it expected, never a download and never a fallback.
    pub fn from_directory(directory: &Path) -> Result<Self, ToError> {
        let named = directory.display().to_string();

        if !directory.exists() {
            return Err(model_error(
                None,
                None,
                ModelDefect::MissingDirectory { path: named },
            ));
        }

        if !directory.is_dir() {
            return Err(model_error(
                None,
                None,
                ModelDefect::NotADirectory { path: named },
            ));
        }

        let mut models = Vec::with_capacity(2);

        for kind in ModelKind::ALL {
            let path = directory.join(kind.file_name());
            let named = path.display().to_string();

            let bytes = std::fs::read(&path).map_err(|error| {
                let defect = if error.kind() == std::io::ErrorKind::NotFound {
                    ModelDefect::MissingFile {
                        path: named.clone(),
                    }
                } else {
                    ModelDefect::UnreadableFile {
                        path: named.clone(),
                        error: error.to_string(),
                    }
                };
                model_error(Some(kind), None, defect)
            })?;

            let file = ModelFile::inspect(bytes, kind)
                .map_err(|defect| model_error(Some(kind), Some(&named), defect))?;

            models.push(ModelFile {
                bytes: file.bytes,
                fingerprint: ModelFingerprint {
                    file_name: Some(kind.file_name().to_string()),
                    ..file.fingerprint
                },
            });
        }

        let recognition = models.pop().expect("two models were read");
        let detection = models.pop().expect("two models were read");

        Ok(Self {
            directory: Some(directory.to_path_buf()),
            detection,
            recognition,
        })
    }

    /// The detection model.
    pub fn detection(&self) -> &ModelFile {
        &self.detection
    }

    /// The recognition model.
    pub fn recognition(&self) -> &ModelFile {
        &self.recognition
    }

    /// The pair's identity.
    pub fn identity(&self) -> ModelIdentity {
        ModelIdentity::new(
            self.detection.fingerprint.clone(),
            self.recognition.fingerprint.clone(),
        )
    }

    /// The document a preflight report carries.
    pub fn to_json(&self) -> Value {
        let mut value = self.identity().to_json();
        if let Some(directory) = &self.directory {
            value["directory"] = json!(directory.display().to_string());
        }
        value
    }
}

/// Checks that a parsed model's input is what `ocrs` needs for `kind`.
///
/// This runs after the parse and before the engine is built. It exists because
/// `ocrs` dereferences these dimensions at recognition time — the recognition path
/// indexes the third dimension, the detection path destructures four fixed ones —
/// so a model of the wrong generation is refused here, by name, instead of
/// panicking inside a recognition call.
pub fn validate_input_shape(model: &Model, kind: ModelKind) -> Result<(), ToError> {
    let expected = kind.expected_input_shape();

    let Some(shape) = model.input_shape(0) else {
        return Err(model_error(
            Some(kind),
            None,
            ModelDefect::InputShape {
                found: "no input at all".to_string(),
                expected: expected.to_string(),
            },
        ));
    };

    let found = describe_shape(&shape);

    let refuse = |found: String| {
        model_error(
            Some(kind),
            None,
            ModelDefect::InputShape {
                found,
                expected: expected.to_string(),
            },
        )
    };

    if shape.len() != 4 {
        return Err(refuse(found));
    }

    if !is_one(&shape[0]) || !is_one(&shape[1]) {
        return Err(refuse(found));
    }

    if kind == ModelKind::Detection
        && (!matches!(shape[2], Dimension::Fixed(size) if size > 0)
            || !matches!(shape[3], Dimension::Fixed(size) if size > 0))
    {
        return Err(refuse(found));
    }

    // The recognition path reads the third dimension; a fixed zero would be a
    // model that can never take a line image.
    if let Dimension::Fixed(size) = shape[2]
        && size == 0
    {
        return Err(refuse(found));
    }

    Ok(())
}

/// The batch and channel dimensions must be one when they are fixed; a symbolic
/// dimension is left to the model, which resolves it at run time.
fn is_one(dimension: &Dimension) -> bool {
    match dimension {
        Dimension::Fixed(size) => *size == 1,
        Dimension::Symbolic(_) => true,
    }
}

fn describe_shape(shape: &[Dimension]) -> String {
    let dimensions = shape
        .iter()
        .map(|dimension| match dimension {
            Dimension::Fixed(size) => size.to_string(),
            Dimension::Symbolic(name) => name.clone(),
        })
        .collect::<Vec<_>>()
        .join(", ");

    format!("[{dimensions}]")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    /// A directory that removes itself, so a failing assertion does not leave one
    /// behind under the system temporary directory.
    struct TempDir {
        path: PathBuf,
    }

    impl TempDir {
        fn new(label: &str) -> Self {
            static COUNTER: AtomicUsize = AtomicUsize::new(0);
            let index = COUNTER.fetch_add(1, Ordering::Relaxed);
            let path = std::env::temp_dir().join(format!(
                "sharp-text-ocr-models-{}-{label}-{index}",
                std::process::id()
            ));
            std::fs::create_dir_all(&path).expect("a temporary directory can be made");
            Self { path }
        }

        fn write(&self, name: &str, bytes: &[u8]) -> PathBuf {
            let path = self.path.join(name);
            std::fs::write(&path, bytes).expect("a temporary file can be written");
            path
        }
    }

    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.path);
        }
    }

    /// A file whose header is a well-formed `RTEN` v2 header for a graph of
    /// `graph_len` bytes, padded to `total_len`.
    ///
    /// The graph itself is zeros, not FlatBuffers: this is what a caller's file
    /// looks like to the container check, which is the layer under test. The
    /// loader would refuse it, and `Engine::new` is where that is asserted.
    fn rten_file(graph_len: usize, total_len: usize) -> Vec<u8> {
        let model_offset = RTEN_V2_HEADER_LEN as u64;
        let tensor_offset = model_offset + graph_len as u64;
        let mut bytes = vec![0u8; total_len];

        bytes[..4].copy_from_slice(&RTEN_MAGIC);
        bytes[4..8].copy_from_slice(&RTEN_V2_VERSION.to_le_bytes());
        bytes[8..16].copy_from_slice(&model_offset.to_le_bytes());
        bytes[16..24].copy_from_slice(&(graph_len as u64).to_le_bytes());
        bytes[24..32].copy_from_slice(&tensor_offset.to_le_bytes());

        bytes
    }

    fn complete_directory_model_size() -> usize {
        MIN_MODEL_BYTES * 2
    }

    #[test]
    fn an_empty_file_is_refused_as_empty_and_names_the_floor() {
        let defect = ModelFile::inspect(Vec::new(), ModelKind::Detection)
            .expect_err("an empty file is not a model");

        assert_eq!(defect, ModelDefect::Empty);
        assert_eq!(defect.name(), "empty");

        let error = model_error(Some(ModelKind::Detection), Some("detection.rten"), defect);
        assert_eq!(error.code, TO_ERR_MODEL);
        assert!(
            error
                .json
                .contains("detection model (detection.rten) is empty"),
            "{}",
            error.json
        );
        assert!(
            error.json.contains("\"defect\":\"empty\""),
            "{}",
            error.json
        );
        assert!(
            error.json.contains("\"role\":\"detection\""),
            "{}",
            error.json
        );
    }

    #[test]
    fn a_truncated_half_download_is_refused_because_its_graph_runs_past_the_end() {
        // The header of a file that should weigh 8 KB, cut at 100 bytes: the
        // declared graph still runs to 32 + 1 KB, past where the file stops.
        let mut truncated = rten_file(1024, MIN_MODEL_BYTES * 2);
        truncated.truncate(100);

        let defect = ModelFile::inspect(truncated, ModelKind::Recognition)
            .expect_err("a truncated file is not a model");

        assert_eq!(defect.name(), "inconsistent_layout");
        let message = defect.detail();
        assert!(
            message.contains("past the end of the 100-byte file"),
            "{message}"
        );
        assert!(message.contains("truncated"), "{message}");
    }

    #[test]
    fn a_file_that_starts_with_the_magic_but_stops_inside_the_header_is_refused_by_length() {
        let defect = ModelFile::inspect(b"RTEN\x02\x00".to_vec(), ModelKind::Detection)
            .expect_err("6 bytes is not a file");

        assert_eq!(
            defect,
            ModelDefect::TruncatedHeader {
                length: 6,
                minimum: RTEN_V2_HEADER_LEN,
            }
        );
    }

    #[test]
    fn a_file_of_the_wrong_size_is_refused_by_the_floor() {
        let defect = ModelFile::inspect(vec![0u8; 512], ModelKind::Detection)
            .expect_err("512 bytes is not a model");

        // 512 bytes of zeros start with nothing recognizable, so the container
        // check speaks first: that is the honest order, and the message says both
        // what arrived and how small it is.
        assert_eq!(defect.name(), "unrecognized_format");

        let mut rten = rten_file(128, MIN_MODEL_BYTES);
        rten.truncate(512);
        let defect = ModelFile::inspect(rten, ModelKind::Detection)
            .expect_err("a 512-byte RTEN file is not a model");
        assert_eq!(
            defect,
            ModelDefect::TooSmall {
                length: 512,
                minimum: MIN_MODEL_BYTES,
            }
        );
    }

    #[test]
    fn a_file_with_an_invalid_magic_is_refused_naming_its_first_bytes() {
        let defect = ModelFile::inspect(b"not a model".to_vec(), ModelKind::Recognition)
            .expect_err("text is not a model");

        assert_eq!(
            defect,
            ModelDefect::UnrecognizedFormat {
                leading: "6e 6f 74 20".to_string(),
            }
        );

        let error = model_error(
            Some(ModelKind::Recognition),
            Some("recognition.rten"),
            defect,
        );
        assert!(
            error.json.contains("does not start with the RTEN magic"),
            "{}",
            error.json
        );
        assert!(error.json.contains("6e 6f 74 20"), "{}", error.json);
    }

    #[test]
    fn an_rten_container_of_another_version_is_refused_by_its_number() {
        let mut bytes = rten_file(128, complete_directory_model_size());
        bytes[4..8].copy_from_slice(&3u32.to_le_bytes());

        let defect = container_format(&bytes).expect_err("v3 is not v2");

        assert_eq!(defect, ModelDefect::UnsupportedVersion { version: 3 });
        assert!(defect.detail().contains("RTEN v3"), "{}", defect.detail());
        assert!(
            defect.detail().contains("reads RTEN v2"),
            "{}",
            defect.detail()
        );
    }

    #[test]
    fn a_zero_length_graph_is_refused_as_a_file_with_no_model_in_it() {
        let mut bytes = rten_file(128, complete_directory_model_size());
        bytes[16..24].copy_from_slice(&0u64.to_le_bytes());

        let defect = container_format(&bytes).expect_err("no graph is no model");

        assert_eq!(defect.name(), "inconsistent_layout");
        assert!(
            defect.detail().contains("zero-length graph"),
            "{}",
            defect.detail()
        );
    }

    #[test]
    fn an_onnx_model_is_accepted_by_its_signature_rather_than_a_magic() {
        let mut bytes = vec![0u8; complete_directory_model_size()];
        bytes[0] = 0x08;
        bytes[1] = 8;

        assert_eq!(container_format(&bytes), Ok(ModelFormat::Onnx));

        let file =
            ModelFile::inspect(bytes, ModelKind::Recognition).expect("a plausible ONNX file");
        assert_eq!(file.fingerprint.format, ModelFormat::Onnx);
        assert_eq!(file.fingerprint.format.name(), "onnx");
    }

    #[test]
    fn the_same_bytes_have_the_same_digest_and_other_bytes_do_not() {
        let first = ModelFile::inspect(rten_file(1024, MIN_MODEL_BYTES), ModelKind::Detection)
            .expect("a well-formed header");
        let same = ModelFile::inspect(rten_file(1024, MIN_MODEL_BYTES), ModelKind::Detection)
            .expect("a well-formed header");
        let mut other_bytes = rten_file(1024, MIN_MODEL_BYTES);
        other_bytes[100] = 0xff;
        let other =
            ModelFile::inspect(other_bytes, ModelKind::Detection).expect("a well-formed header");

        assert_eq!(
            first.fingerprint.sha256_hex(),
            same.fingerprint.sha256_hex()
        );
        assert_ne!(
            first.fingerprint.sha256_hex(),
            other.fingerprint.sha256_hex()
        );
        assert_eq!(first.fingerprint.sha256_hex().len(), 64);
        assert_eq!(first.fingerprint.length, MIN_MODEL_BYTES);
        assert_eq!(first.fingerprint.format, ModelFormat::Rten);
    }

    #[test]
    fn a_pair_identity_changes_when_either_model_changes() {
        let detection = ModelFile::inspect(rten_file(1024, MIN_MODEL_BYTES), ModelKind::Detection)
            .expect("a well-formed header");
        let recognition =
            ModelFile::inspect(rten_file(2048, MIN_MODEL_BYTES), ModelKind::Recognition)
                .expect("a well-formed header");

        let identity = ModelIdentity::new(
            detection.fingerprint.clone(),
            recognition.fingerprint.clone(),
        );

        assert_eq!(
            identity.to_json()["set"].as_str().expect("a string").len(),
            64
        );
        assert_eq!(identity.to_json()["detection"]["role"], "detection");
        assert_eq!(identity.to_json()["recognition"]["role"], "recognition");

        let mut swapped = recognition.fingerprint.clone();
        swapped.kind = ModelKind::Detection;
        let other = ModelIdentity::new(swapped, detection.fingerprint.clone());
        assert_ne!(identity.to_json()["set"], other.to_json()["set"]);
    }

    #[test]
    fn a_directory_missing_a_model_names_the_file_it_expected() {
        let directory = TempDir::new("incomplete");
        directory.write(RECOGNITION_FILE_NAME, &rten_file(1024, MIN_MODEL_BYTES));

        let error = ModelSet::from_directory(&directory.path).expect_err("detection is missing");
        let expected = directory
            .path
            .join(DETECTION_FILE_NAME)
            .display()
            .to_string();

        assert_eq!(error.code, TO_ERR_MODEL);
        assert!(
            error.json.contains("\"defect\":\"missing_file\""),
            "{}",
            error.json
        );
        assert!(
            error.json.contains("\"role\":\"detection\""),
            "{}",
            error.json
        );
        assert!(
            error.json.contains(&expected.replace('\\', "\\\\")),
            "{}",
            error.json
        );
        assert!(error.json.contains(DETECTION_FILE_NAME), "{}", error.json);
    }

    #[test]
    fn a_complete_directory_with_well_formed_headers_is_accepted_and_identified() {
        let directory = TempDir::new("complete");
        directory.write(DETECTION_FILE_NAME, &rten_file(1024, MIN_MODEL_BYTES));
        directory.write(RECOGNITION_FILE_NAME, &rten_file(2048, MIN_MODEL_BYTES));

        let set = ModelSet::from_directory(&directory.path).expect("both models are there");

        assert_eq!(
            set.to_json()["directory"],
            directory.path.display().to_string()
        );
        assert_eq!(
            set.detection().fingerprint.file_name.as_deref(),
            Some(DETECTION_FILE_NAME)
        );
        assert_eq!(
            set.recognition().fingerprint.file_name.as_deref(),
            Some(RECOGNITION_FILE_NAME)
        );
        assert_eq!(set.detection().fingerprint.format, ModelFormat::Rten);
        assert_ne!(
            set.detection().fingerprint.sha256_hex(),
            set.recognition().fingerprint.sha256_hex()
        );
        assert_eq!(
            set.identity().to_json()["set"]
                .as_str()
                .expect("a string")
                .len(),
            64
        );
        assert_eq!(
            set.to_json()["directory"],
            directory.path.display().to_string()
        );
        assert_eq!(set.to_json()["detection"]["file"], DETECTION_FILE_NAME);
    }

    #[test]
    fn a_directory_that_does_not_exist_is_refused_by_name() {
        let directory = TempDir::new("absent");
        let absent = directory.path.join("not-there");

        let error = ModelSet::from_directory(&absent).expect_err("no such directory");

        assert_eq!(error.code, TO_ERR_MODEL);
        assert!(
            error.json.contains("\"defect\":\"missing_directory\""),
            "{}",
            error.json
        );
        assert!(error.json.contains("not-there"), "{}", error.json);
    }

    #[test]
    fn a_path_that_is_a_file_is_refused_as_not_a_directory() {
        let directory = TempDir::new("not-a-directory");
        let file = directory.write("models.txt", b"hello");

        let error = ModelSet::from_directory(&file).expect_err("a file is not a directory");

        assert_eq!(error.code, TO_ERR_MODEL);
        assert!(error.json.contains("not_a_directory"), "{}", error.json);
    }

    #[test]
    fn a_well_formed_container_whose_graph_is_not_a_graph_is_refused_by_the_loader() {
        // The container check passes (the header is right), so the failure comes
        // from the loader and is reported against the role and the file, never as
        // a panic at inference time.
        let error = crate::engine::Engine::new(
            &rten_file(1024, MIN_MODEL_BYTES * 2),
            &rten_file(1024, MIN_MODEL_BYTES * 2),
            None,
        )
        .expect_err("zeros are not a FlatBuffers graph");

        assert_eq!(error.code, TO_ERR_MODEL);
        assert!(error.json.contains("detection model"), "{}", error.json);
        assert!(
            error.json.contains("\"defect\":\"unparsable_graph\""),
            "{}",
            error.json
        );
    }
}
