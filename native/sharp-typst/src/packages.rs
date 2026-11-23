//! Package mirrors: the `@namespace/name:version` packages a deployment may use, and the manifest
//! that makes a tampered one visible.
//!
//! A mirror is a directory laid out like Typst's own package cache —
//! `<namespace>/<name>/<version>/` — plus a `mirror.json` listing every file of every package with
//! its SHA-256 digest and its size.
//!
//! Why a manifest, when the cache already works: a cache is a directory somebody else can write to,
//! and the compiler reads whatever it finds there. Without a manifest, a file edited in place is
//! served without a word. With one, the digests are checked before a world is created, so a compile
//! that runs at all runs over the bytes the manifest describes. That is integrity, not
//! authenticity: a mirror that can rewrite a file can rewrite the manifest too, so the manifest
//! itself has to come from a source the deployment trusts — a signed image, a build artifact, an
//! immutable layer. The documentation states that limit; this module does not hide it.
//!
//! The manifest is a map of package spec to files, which is what a writer can produce without
//! knowing about the packages' contents:
//!
//! ```json
//! {
//!   "format": 1,
//!   "packages": {
//!     "@preview/cetz:0.3.1": [
//!       { "path": "lib.typ", "sha256": "…", "size": 4123 }
//!     ]
//!   }
//! }
//! ```
//!
//! Everything here is deliberately independent of `typst-kit`: a mirror is a contract between the
//! writer of the mirror (`tools/typst-mirror.py`) and the reader, and both spell the format out.

use std::collections::BTreeMap;
use std::fmt;
use std::path::{Path, PathBuf};

use serde::Deserialize;

use crate::ffi::{TN_ERR_FILE, TnError};

/// Name of the manifest inside a mirror directory.
pub const MANIFEST: &str = "mirror.json";

/// The only manifest format this build reads.
pub const FORMAT: u32 = 1;

/// One file of one package, as the manifest describes it.
#[derive(Debug, Clone, Deserialize)]
pub struct ManifestFile {
    /// Path relative to the package directory, with forward slashes.
    pub path: String,
    /// Lowercase hex SHA-256 of the file's bytes.
    pub sha256: String,
    /// Size in bytes, when the manifest records it.
    #[serde(default)]
    pub size: Option<u64>,
}

#[derive(Debug, Deserialize)]
struct ManifestDocument {
    format: u32,
    packages: BTreeMap<String, Vec<ManifestFile>>,
}

/// What a verification walked over, for a caller that wants to report it.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Verified {
    /// Packages the manifest declares.
    pub packages: usize,
    /// Files whose digest matched.
    pub files: usize,
    /// Bytes those files hold.
    pub bytes: u64,
}

/// A package mirror: a directory and the manifest that describes it.
#[derive(Debug, Clone)]
pub struct Mirror {
    root: PathBuf,
    packages: BTreeMap<String, Vec<ManifestFile>>,
}

impl Mirror {
    /// The manifest path of a mirror rooted at `root`.
    pub fn manifest_path(root: &Path) -> PathBuf {
        root.join(MANIFEST)
    }

    /// Reads the manifest of the mirror rooted at `root`.
    ///
    /// A mirror without a manifest is an error rather than an empty mirror: the caller asked for
    /// verified packages, and an unlisted directory cannot promise that.
    pub fn open(root: impl Into<PathBuf>) -> Result<Self, MirrorError> {
        let root = root.into();
        let manifest_path = Self::manifest_path(&root);

        let bytes = match std::fs::read(&manifest_path) {
            Ok(bytes) => bytes,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                return Err(MirrorError::MissingManifest {
                    path: manifest_path,
                });
            }
            Err(error) => {
                return Err(MirrorError::Unreadable {
                    path: manifest_path,
                    message: error.to_string(),
                });
            }
        };

        let document: ManifestDocument =
            serde_json::from_slice(&bytes).map_err(|error| MirrorError::Malformed {
                path: manifest_path.clone(),
                message: error.to_string(),
            })?;

        if document.format != FORMAT {
            return Err(MirrorError::UnsupportedFormat {
                path: manifest_path,
                format: document.format,
            });
        }

        Ok(Self {
            root,
            packages: document.packages,
        })
    }

    /// Whether the manifest declares `spec`.
    pub fn contains(&self, spec: &str) -> bool {
        self.packages.contains_key(spec)
    }

    /// The directory a spec occupies inside this mirror.
    ///
    /// `@preview/cetz:0.3.1` is `<root>/preview/cetz/0.3.1`, the layout the `typst` CLI writes and
    /// `typst-kit` reads.
    pub fn package_dir(&self, spec: &str) -> Option<PathBuf> {
        let (namespace, name, version) = split_spec(spec)?;
        Some(self.root.join(namespace).join(name).join(version))
    }

    /// Checks every file the manifest lists, and every file the mirror holds.
    ///
    /// Three failures, each naming the package and the file: a listed file is missing, a listed
    /// file's bytes are not the ones the manifest describes, and a file — or a whole package — sits
    /// in the mirror without being listed. The last one matters as much as the others: an unlisted
    /// file is a file resolution can serve and verification never saw.
    pub fn verify(&self) -> Result<Verified, MirrorError> {
        let mut verified = Verified {
            packages: self.packages.len(),
            ..Verified::default()
        };

        for (spec, files) in &self.packages {
            let directory = self
                .package_dir(spec)
                .ok_or_else(|| MirrorError::Malformed {
                    path: self.root.clone(),
                    message: format!("`{spec}` is not a `@namespace/name:version` spec"),
                })?;

            if !directory.is_dir() {
                return Err(MirrorError::MissingPackage {
                    package: spec.clone(),
                    directory,
                    root: self.root.clone(),
                });
            }

            let present = collect_files(spec, &directory, &self.root)?;

            for file in files {
                let path = directory.join(forward_slashes(&file.path));

                if !present.iter().any(|(relative, _)| relative == &file.path) {
                    return Err(MirrorError::Missing {
                        package: spec.clone(),
                        file: file.path.clone(),
                        root: self.root.clone(),
                    });
                }

                let bytes = std::fs::read(&path).map_err(|error| MirrorError::Unreadable {
                    path: path.clone(),
                    message: error.to_string(),
                })?;

                if let Some(size) = file.size
                    && size != bytes.len() as u64
                {
                    return Err(MirrorError::Size {
                        package: spec.clone(),
                        file: file.path.clone(),
                        expected: size,
                        actual: bytes.len() as u64,
                        root: self.root.clone(),
                    });
                }

                let actual = sha256_hex(&bytes);
                if !actual.eq_ignore_ascii_case(&file.sha256) {
                    return Err(MirrorError::Mismatch {
                        package: spec.clone(),
                        file: file.path.clone(),
                        expected: file.sha256.clone().into(),
                        actual: actual.clone().into(),
                        root: self.root.clone(),
                    });
                }

                verified.files += 1;
                verified.bytes += bytes.len() as u64;
            }

            // A file nobody listed is a file resolution can still serve.
            for (relative, _) in &present {
                if !files.iter().any(|file| &file.path == relative) {
                    return Err(MirrorError::Unlisted {
                        package: spec.clone(),
                        file: relative.clone(),
                        root: self.root.clone(),
                    });
                }
            }
        }

        self.refuse_undeclared_packages()?;

        Ok(verified)
    }

    /// Walks the mirror for `<namespace>/<name>/<version>` directories the manifest does not declare.
    ///
    /// Only three-level directories whose last segment looks like a version are considered: a
    /// mirror may legitimately hold a `README`, a lock file, or a `.git`. A namespace starting with
    /// a dot is ignored for the same reason.
    fn refuse_undeclared_packages(&self) -> Result<(), MirrorError> {
        for (namespace, name, version, directory) in version_directories(&self.root) {
            let spec = format!("@{namespace}/{name}:{version}");

            if !self.contains(&spec) {
                return Err(MirrorError::UnlistedPackage {
                    package: spec,
                    directory,
                    root: self.root.clone(),
                });
            }
        }

        Ok(())
    }
}

/// A mirror that failed, or a package inside one that failed.
///
/// Every variant that concerns a package names it, and every variant that concerns a file names
/// that too: a host that logs the message has to be able to tell an operator which package to
/// re-fetch without reading the mirror by hand.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum MirrorError {
    /// The mirror directory has no `mirror.json`.
    MissingManifest { path: PathBuf },
    /// A file could not be read.
    Unreadable { path: PathBuf, message: String },
    /// The manifest is not valid JSON, or not the shape described above.
    Malformed { path: PathBuf, message: String },
    /// The manifest declares a format this build does not read.
    UnsupportedFormat { path: PathBuf, format: u32 },
    /// A declared package has no directory in the mirror.
    MissingPackage {
        package: String,
        directory: PathBuf,
        root: PathBuf,
    },
    /// A declared file is not in the package's directory.
    Missing {
        package: String,
        file: String,
        root: PathBuf,
    },
    /// A file's digest is not the one the manifest records.
    Mismatch {
        package: String,
        file: String,
        expected: Box<str>,
        actual: Box<str>,
        root: PathBuf,
    },
    /// A file's size is not the one the manifest records.
    Size {
        package: String,
        file: String,
        expected: u64,
        actual: u64,
        root: PathBuf,
    },
    /// A file sits in a declared package without being listed.
    Unlisted {
        package: String,
        file: String,
        root: PathBuf,
    },
    /// A package sits in the mirror without being listed.
    UnlistedPackage {
        package: String,
        directory: PathBuf,
        root: PathBuf,
    },
    /// A package directory holds a symbolic link, which verification cannot follow safely.
    Symlink {
        package: String,
        file: String,
        root: PathBuf,
    },
}

impl MirrorError {
    /// The package this failure is about, when it is about one.
    pub fn package(&self) -> Option<&str> {
        match self {
            Self::MissingManifest { .. }
            | Self::Unreadable { .. }
            | Self::Malformed { .. }
            | Self::UnsupportedFormat { .. } => None,
            Self::MissingPackage { package, .. }
            | Self::Missing { package, .. }
            | Self::Mismatch { package, .. }
            | Self::Size { package, .. }
            | Self::Unlisted { package, .. }
            | Self::UnlistedPackage { package, .. }
            | Self::Symlink { package, .. } => Some(package),
        }
    }

    /// The file this failure is about, when it is about one.
    pub fn file(&self) -> Option<&str> {
        match self {
            Self::Missing { file, .. }
            | Self::Mismatch { file, .. }
            | Self::Size { file, .. }
            | Self::Unlisted { file, .. }
            | Self::Symlink { file, .. } => Some(file),
            _ => None,
        }
    }

    /// The directory of the mirror this failure is about, when it has one.
    pub fn root(&self) -> Option<&Path> {
        match self {
            Self::MissingManifest { path }
            | Self::Unreadable { path, .. }
            | Self::Malformed { path, .. }
            | Self::UnsupportedFormat { path, .. } => path.parent(),
            Self::MissingPackage { root, .. }
            | Self::Missing { root, .. }
            | Self::Mismatch { root, .. }
            | Self::Size { root, .. }
            | Self::Unlisted { root, .. }
            | Self::UnlistedPackage { root, .. }
            | Self::Symlink { root, .. } => Some(root),
        }
    }

    /// The failure as an ABI error: `TN_ERR_FILE`, with the package, the file and the mirror in the
    /// payload so a host can act on them without parsing the message.
    pub fn to_tn_error(&self) -> TnError {
        let json = serde_json::json!({
            "code": "file",
            "message": self.to_string(),
            "diagnostics": [],
            "mirror": self.root().map(|root| root.display().to_string()),
            "package": self.package(),
            "file": self.file(),
        })
        .to_string();

        TnError::with_json(TN_ERR_FILE, json)
    }
}

impl fmt::Display for MirrorError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::MissingManifest { path } => write!(
                f,
                "package mirror `{}` has no {MANIFEST}; create one with tools/typst-mirror.py",
                path.display()
            ),
            Self::Unreadable { path, message } => {
                write!(f, "`{}` could not be read: {message}", path.display())
            }
            Self::Malformed { path, message } => {
                write!(f, "`{}` is not a valid manifest: {message}", path.display())
            }
            Self::UnsupportedFormat { path, format } => write!(
                f,
                "`{}` declares manifest format {format}, and this build reads {FORMAT}",
                path.display()
            ),
            Self::MissingPackage {
                package,
                directory,
                root,
            } => write!(
                f,
                "package `{package}` is listed in the mirror at `{}` but `{}` does not exist",
                root.display(),
                directory.display()
            ),
            Self::Missing {
                package,
                file,
                root,
            } => write!(
                f,
                "package `{package}` in the mirror at `{}` is missing `{file}`",
                root.display()
            ),
            Self::Mismatch {
                package,
                file,
                expected,
                actual,
                root,
            } => write!(
                f,
                "package `{package}` in the mirror at `{}` has a `{file}` whose SHA-256 is \
                 {actual}, and the manifest records {expected}",
                root.display()
            ),
            Self::Size {
                package,
                file,
                expected,
                actual,
                root,
            } => write!(
                f,
                "package `{package}` in the mirror at `{}` has a `{file}` of {actual} bytes, and \
                 the manifest records {expected}",
                root.display()
            ),
            Self::Unlisted {
                package,
                file,
                root,
            } => write!(
                f,
                "package `{package}` in the mirror at `{}` holds `{file}`, which {MANIFEST} does \
                 not list",
                root.display()
            ),
            Self::UnlistedPackage {
                package,
                directory,
                root,
            } => write!(
                f,
                "the mirror at `{}` holds package `{package}` at `{}`, which {MANIFEST} does not \
                 list",
                root.display(),
                directory.display()
            ),
            Self::Symlink {
                package,
                file,
                root,
            } => write!(
                f,
                "package `{package}` in the mirror at `{}` holds a symbolic link at `{file}`, \
                 which a mirror may not contain",
                root.display()
            ),
        }
    }
}

impl std::error::Error for MirrorError {}

/// Splits `@namespace/name:version` into its three parts.
fn split_spec(spec: &str) -> Option<(&str, &str, &str)> {
    let rest = spec.strip_prefix('@')?;
    let (namespace, rest) = rest.split_once('/')?;
    let (name, version) = rest.split_once(':')?;

    let valid = |part: &str| {
        !part.is_empty()
            && part
                .chars()
                .all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_' || c == '.')
    };

    (valid(namespace) && valid(name) && valid(version)).then_some((namespace, name, version))
}

/// `a\b` → `a/b`, so a manifest written on Windows matches one written on Linux.
fn forward_slashes(path: &str) -> String {
    path.replace('\\', "/")
}

/// Every file under `directory`, as `(path relative to it, absolute path)`.
///
/// Symbolic links are refused: a link can point outside the mirror, where verification and the
/// compiler would read different bytes, and following it is exactly how a digest check gets
/// bypassed.
fn collect_files(
    spec: &str,
    directory: &Path,
    root: &Path,
) -> Result<Vec<(String, PathBuf)>, MirrorError> {
    fn walk(
        spec: &str,
        current: &Path,
        root: &Path,
        prefix: &str,
        files: &mut Vec<(String, PathBuf)>,
    ) -> Result<(), MirrorError> {
        let entries = std::fs::read_dir(current).map_err(|error| MirrorError::Unreadable {
            path: current.to_path_buf(),
            message: error.to_string(),
        })?;

        for entry in entries {
            let entry = entry.map_err(|error| MirrorError::Unreadable {
                path: current.to_path_buf(),
                message: error.to_string(),
            })?;
            let name = entry.file_name().to_string_lossy().into_owned();
            let path = entry.path();
            let relative = if prefix.is_empty() {
                name
            } else {
                format!("{prefix}/{name}")
            };

            let kind = entry.file_type().map_err(|error| MirrorError::Unreadable {
                path: path.clone(),
                message: error.to_string(),
            })?;

            if kind.is_symlink() {
                return Err(MirrorError::Symlink {
                    package: spec.to_string(),
                    file: relative,
                    root: root.to_path_buf(),
                });
            }

            if kind.is_dir() {
                walk(spec, &path, root, &relative, files)?;
            } else {
                files.push((relative, path));
            }
        }

        Ok(())
    }

    let mut files = Vec::new();
    walk(spec, directory, root, "", &mut files)?;
    Ok(files)
}

/// Every `<namespace>/<name>/<version>` directory under `root`.
///
/// The version test is what keeps a mirror's bookkeeping out of the audit: `README`, `mirror.json`
/// and a `.git` are three levels deep only by accident.
fn version_directories(root: &Path) -> Vec<(String, String, String, PathBuf)> {
    let mut found = Vec::new();

    let Ok(namespaces) = std::fs::read_dir(root) else {
        return found;
    };

    for namespace in namespaces.flatten() {
        let namespace_name = namespace.file_name().to_string_lossy().into_owned();
        if !namespace.path().is_dir() || namespace_name.starts_with('.') {
            continue;
        }

        let Ok(names) = std::fs::read_dir(namespace.path()) else {
            continue;
        };

        for name in names.flatten() {
            let name_value = name.file_name().to_string_lossy().into_owned();
            if !name.path().is_dir() {
                continue;
            }

            let Ok(versions) = std::fs::read_dir(name.path()) else {
                continue;
            };

            for version in versions.flatten() {
                let version_value = version.file_name().to_string_lossy().into_owned();
                if version.path().is_dir() && looks_like_version(&version_value) {
                    found.push((
                        namespace_name.clone(),
                        name_value.clone(),
                        version_value,
                        version.path(),
                    ));
                }
            }
        }
    }

    found
}

/// Whether a directory name is a `major.minor.patch` version.
fn looks_like_version(name: &str) -> bool {
    let mut parts = name.split('.');
    let three = (0..3).all(|_| {
        parts
            .next()
            .is_some_and(|part| !part.is_empty() && part.chars().all(|c| c.is_ascii_digit()))
    });

    three && parts.next().is_none()
}

/// SHA-256 of `bytes`, as lowercase hex.
///
/// Written out rather than pulled in: the crate has no other use for a digest library, and a
/// dependency that only hashes is one more thing to audit in a package whose whole point is that
/// its inputs are pinned. The implementation is the algorithm from FIPS 180-4, and the tests pin it
/// against the published vectors.
pub fn sha256_hex(bytes: &[u8]) -> String {
    const K: [u32; 64] = [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4,
        0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe,
        0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f,
        0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
        0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc,
        0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
        0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116,
        0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7,
        0xc67178f2,
    ];

    let mut state: [u32; 8] = [
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab,
        0x5be0cd19,
    ];

    // Padding: a one bit, zeroes, then the length in bits over eight bytes, big endian.
    let mut message = Vec::with_capacity(bytes.len() + 72);
    message.extend_from_slice(bytes);
    let bit_len = (bytes.len() as u64).wrapping_mul(8);
    message.push(0x80);
    while message.len() % 64 != 56 {
        message.push(0);
    }
    message.extend_from_slice(&bit_len.to_be_bytes());

    for chunk in message.chunks_exact(64) {
        let mut schedule = [0u32; 64];
        for (index, word) in chunk.chunks_exact(4).enumerate() {
            schedule[index] = u32::from_be_bytes([word[0], word[1], word[2], word[3]]);
        }
        for index in 16..64 {
            let low = schedule[index - 15];
            let high = schedule[index - 2];
            let s0 = low.rotate_right(7) ^ low.rotate_right(18) ^ (low >> 3);
            let s1 = high.rotate_right(17) ^ high.rotate_right(19) ^ (high >> 10);
            schedule[index] = schedule[index - 16]
                .wrapping_add(s0)
                .wrapping_add(schedule[index - 7])
                .wrapping_add(s1);
        }

        let [mut a, mut b, mut c, mut d, mut e, mut f, mut g, mut h] = state;

        for (index, constant) in K.iter().enumerate() {
            let s1 = e.rotate_right(6) ^ e.rotate_right(11) ^ e.rotate_right(25);
            let choose = (e & f) ^ (!e & g);
            let t1 = h
                .wrapping_add(s1)
                .wrapping_add(choose)
                .wrapping_add(*constant)
                .wrapping_add(schedule[index]);
            let s0 = a.rotate_right(2) ^ a.rotate_right(13) ^ a.rotate_right(22);
            let majority = (a & b) ^ (a & c) ^ (b & c);
            let t2 = s0.wrapping_add(majority);

            h = g;
            g = f;
            f = e;
            e = d.wrapping_add(t1);
            d = c;
            c = b;
            b = a;
            a = t1.wrapping_add(t2);
        }

        for (slot, value) in state.iter_mut().zip([a, b, c, d, e, f, g, h]) {
            *slot = slot.wrapping_add(value);
        }
    }

    state.iter().map(|word| format!("{word:08x}")).collect()
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicUsize, Ordering};

    use super::*;

    /// A mirror on disk that removes itself.
    ///
    /// The tests make and break real directories: the digests are checked against bytes the file
    /// system actually holds, not against bytes a mock claims to hold.
    struct Fixture {
        root: PathBuf,
    }

    impl Fixture {
        /// A mirror with two packages and a manifest that describes them.
        fn new(label: &str) -> Self {
            static COUNTER: AtomicUsize = AtomicUsize::new(0);

            let root = std::env::temp_dir().join(format!(
                "sharp-typst-mirror-{}-{}-{label}",
                std::process::id(),
                COUNTER.fetch_add(1, Ordering::Relaxed)
            ));
            let _ = std::fs::remove_dir_all(&root);
            std::fs::create_dir_all(&root).expect("mirror root");

            let fixture = Self { root };
            fixture.package(
                "@preview/testpkg:0.1.0",
                &[
                    ("typst.toml", "[package]\nname = \"testpkg\"\n"),
                    ("lib.typ", "#let hello() = [Hello from a package]\n"),
                ],
            );
            fixture.package(
                "@preview/other:1.0.0",
                &[("lib.typ", "#let other() = [Other]\n")],
            );
            fixture
        }

        /// Writes a package's files, so the manifest can be built from what landed on disk.
        fn package(&self, spec: &str, files: &[(&str, &str)]) -> &Self {
            let directory = directory_of(&self.root, spec);

            for (path, text) in files {
                let target = directory.join(path);
                std::fs::create_dir_all(target.parent().expect("a parent")).expect("file parent");
                std::fs::write(&target, text).expect("write file");
            }

            self
        }

        fn path(&self, spec: &str, file: &str) -> PathBuf {
            directory_of(&self.root, spec).join(file)
        }

        /// Writes a manifest listing `specs` exactly as they are on disk.
        fn manifest(&self, specs: &[&str]) -> PathBuf {
            let mut packages = serde_json::Map::new();

            for spec in specs {
                let mut files = Vec::new();
                for (relative, path) in
                    collect_files(spec, &directory_of(&self.root, spec), &self.root).expect("files")
                {
                    let bytes = std::fs::read(&path).expect("read");
                    files.push(serde_json::json!({
                        "path": relative,
                        "sha256": sha256_hex(&bytes),
                        "size": bytes.len(),
                    }));
                }

                packages.insert(spec.to_string(), serde_json::Value::Array(files));
            }

            let manifest = Mirror::manifest_path(&self.root);
            std::fs::write(
                &manifest,
                serde_json::json!({ "format": FORMAT, "packages": packages }).to_string(),
            )
            .expect("write manifest");

            manifest
        }

        fn open(&self) -> Mirror {
            Mirror::open(&self.root).expect("a mirror")
        }
    }

    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.root);
        }
    }

    /// `<root>/<namespace>/<name>/<version>`, the layout the `typst` CLI writes.
    fn directory_of(root: &Path, spec: &str) -> PathBuf {
        let (namespace, rest) = spec
            .trim_start_matches('@')
            .split_once('/')
            .expect("a name");
        let (name, version) = rest.split_once(':').expect("a version");
        root.join(namespace).join(name).join(version)
    }

    #[test]
    fn sha256_matches_the_published_vectors() {
        // FIPS 180-4 / RFC 6234 vectors, including the padding boundaries: an empty message, a
        // message that fits one block, and one that needs a second.
        assert_eq!(
            sha256_hex(b""),
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        );
        assert_eq!(
            sha256_hex(b"abc"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
        assert_eq!(
            sha256_hex(b"abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"),
            "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
        );
        assert_eq!(
            sha256_hex(&[b'a'; 1_000_000]),
            "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0"
        );
    }

    #[test]
    fn a_described_mirror_verifies() {
        let fixture = Fixture::new("verifies");
        fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        let mirror = fixture.open();
        let verified = mirror.verify().expect("verifies");

        assert_eq!(verified.packages, 2);
        assert_eq!(verified.files, 3);
        assert!(verified.bytes > 0);
        assert!(mirror.contains("@preview/testpkg:0.1.0"));
        assert!(!mirror.contains("@preview/testpkg:9.9.9"));
    }

    #[test]
    fn a_tampered_file_is_refused_and_named() {
        let fixture = Fixture::new("tampered");
        fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        std::fs::write(
            fixture.path("@preview/testpkg:0.1.0", "lib.typ"),
            "#let hello() = [Something else entirely]\n",
        )
        .expect("tamper");

        let error = fixture
            .open()
            .verify()
            .expect_err("a tampered file is refused");

        assert_eq!(error.package(), Some("@preview/testpkg:0.1.0"));
        assert_eq!(error.file(), Some("lib.typ"));

        let message = error.to_string();
        assert!(message.contains("@preview/testpkg:0.1.0"), "{message}");
        assert!(message.contains("lib.typ"), "{message}");
        assert!(
            message.contains(&fixture.root.display().to_string()),
            "the message names the mirror: {message}"
        );
    }

    #[test]
    fn a_file_of_the_wrong_size_is_refused() {
        let fixture = Fixture::new("size");
        let manifest = fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        // Only the recorded length is wrong: the message has to be about the length, not the
        // content, or an operator re-fetches for the wrong reason.
        let text = std::fs::read_to_string(&manifest).expect("manifest");
        let mut document: serde_json::Value = serde_json::from_str(&text).expect("json");

        // By path, not by index: the manifest lists a package's files in directory order.
        let entries = document["packages"]["@preview/testpkg:0.1.0"]
            .as_array_mut()
            .expect("files");
        let entry = entries
            .iter_mut()
            .find(|entry| entry["path"] == "lib.typ")
            .expect("lib.typ is listed");
        entry["size"] = serde_json::json!(1);

        std::fs::write(&manifest, document.to_string()).expect("rewrite");

        let error = fixture
            .open()
            .verify()
            .expect_err("a wrong size is refused");

        assert_eq!(error.file(), Some("lib.typ"));
        assert!(error.to_string().contains("bytes"), "{error}");
    }

    #[test]
    fn a_file_the_manifest_does_not_list_is_refused() {
        let fixture = Fixture::new("unlisted");
        fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        std::fs::write(
            fixture.path("@preview/testpkg:0.1.0", "extra.typ"),
            "= extra\n",
        )
        .expect("add a file");

        let error = fixture
            .open()
            .verify()
            .expect_err("an unlisted file is refused");

        assert_eq!(error.package(), Some("@preview/testpkg:0.1.0"));
        assert_eq!(error.file(), Some("extra.typ"));
    }

    #[test]
    fn a_listed_file_that_is_absent_is_refused() {
        let fixture = Fixture::new("missing");
        fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        std::fs::remove_file(fixture.path("@preview/testpkg:0.1.0", "lib.typ")).expect("remove");

        let error = fixture
            .open()
            .verify()
            .expect_err("a missing file is refused");

        assert_eq!(error.package(), Some("@preview/testpkg:0.1.0"));
        assert_eq!(error.file(), Some("lib.typ"));
    }

    #[test]
    fn a_package_the_manifest_does_not_list_is_refused() {
        let fixture = Fixture::new("undeclared-package");
        fixture.manifest(&["@preview/testpkg:0.1.0"]);

        let error = fixture
            .open()
            .verify()
            .expect_err("an undeclared package is refused");

        assert_eq!(error.package(), Some("@preview/other:1.0.0"));
        assert!(error.to_string().contains("does not list"), "{error}");
    }

    /// The manifest `tools/typst-mirror.py` writes: the two implementations have to agree on the
    /// format, and this is the shape the writer produces, including the key the reader ignores.
    #[test]
    fn the_manifest_the_python_tool_writes_is_read() {
        let fixture = Fixture::new("python-shape");
        let lib = "#let hello() = [Hello]\n";
        let toml = "[package]\nname = \"testpkg\"\n";

        std::fs::create_dir_all(
            fixture
                .path("@preview/testpkg:0.1.0", "typst.toml")
                .parent()
                .unwrap(),
        )
        .expect("directory");
        std::fs::write(fixture.path("@preview/testpkg:0.1.0", "lib.typ"), lib).expect("write");
        std::fs::write(fixture.path("@preview/testpkg:0.1.0", "typst.toml"), toml).expect("write");

        // The fixture holds a second package; this manifest is the writer's output for one, and an
        // undeclared package is a refusal of its own.
        let other = fixture.path("@preview/other:1.0.0", "lib.typ");
        std::fs::remove_dir_all(other.parent().unwrap().parent().unwrap()).expect("remove other");

        let document = format!(
            r#"{{
  "format": 1,
  "generatedBy": "typst-mirror/1 (+https://github.com/m3scalinum/text)",
  "packages": {{
    "@preview/testpkg:0.1.0": [
      {{
        "path": "lib.typ",
        "sha256": "{}",
        "size": {}
      }},
      {{
        "path": "typst.toml",
        "sha256": "{}",
        "size": {}
      }}
    ]
  }}
}}"#,
            sha256_hex(lib.as_bytes()),
            lib.len(),
            sha256_hex(toml.as_bytes()),
            toml.len()
        );

        std::fs::write(Mirror::manifest_path(&fixture.root), document).expect("manifest");

        let verified = fixture
            .open()
            .verify()
            .expect("the writer's manifest verifies");

        assert_eq!(verified.packages, 1);
        assert_eq!(verified.files, 2);
    }

    #[test]
    fn a_mirror_without_a_manifest_is_refused() {
        let fixture = Fixture::new("no-manifest");

        let error = Mirror::open(&fixture.root).expect_err("no manifest");

        assert!(matches!(error, MirrorError::MissingManifest { .. }));
        assert!(error.package().is_none());
        assert!(error.to_string().contains(MANIFEST), "{error}");
    }

    #[test]
    fn a_manifest_of_another_format_is_refused() {
        let fixture = Fixture::new("format");
        let manifest = fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        let text = std::fs::read_to_string(&manifest).expect("manifest");
        std::fs::write(&manifest, text.replace("\"format\":1", "\"format\":2")).expect("rewrite");

        let error = Mirror::open(&fixture.root).expect_err("an unknown format");

        assert!(matches!(
            error,
            MirrorError::UnsupportedFormat { format: 2, .. }
        ));
    }

    #[test]
    fn a_spec_the_world_cannot_resolve_is_refused() {
        let fixture = Fixture::new("spec");
        let manifest = Mirror::manifest_path(&fixture.root);
        std::fs::write(
            &manifest,
            serde_json::json!({ "format": FORMAT, "packages": { "cetz": [] } }).to_string(),
        )
        .expect("write manifest");

        let error = fixture.open().verify().expect_err("a bad spec is refused");

        assert!(matches!(error, MirrorError::Malformed { .. }));
        assert!(error.to_string().contains("cetz"), "{error}");
    }

    /// The refusal crosses the ABI as `TN_ERR_FILE`, with the package, the file and the mirror in
    /// the payload: a host that logs it can name what to re-fetch without parsing prose.
    #[test]
    fn the_refusal_carries_the_package_the_file_and_the_mirror() {
        let fixture = Fixture::new("payload");
        fixture.manifest(&["@preview/testpkg:0.1.0", "@preview/other:1.0.0"]);

        std::fs::write(
            fixture.path("@preview/testpkg:0.1.0", "lib.typ"),
            "= changed\n",
        )
        .expect("tamper");

        let error = fixture.open().verify().expect_err("refused").to_tn_error();
        let payload: serde_json::Value =
            serde_json::from_str(&error.json).expect("the payload is JSON");

        assert_eq!(error.code, TN_ERR_FILE);
        assert_eq!(payload["code"], "file");
        assert_eq!(payload["package"], "@preview/testpkg:0.1.0");
        assert_eq!(payload["file"], "lib.typ");
        assert_eq!(
            payload["mirror"],
            fixture.root.display().to_string(),
            "the mirror is part of the refusal"
        );
    }
}
