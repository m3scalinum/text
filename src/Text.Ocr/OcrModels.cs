using System;
using System.Globalization;
using System.IO;
using M3scalinum.Text.Ocr.Internal;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// Which of the two models a file or an error concerns.
/// </summary>
public enum OcrModelRole
{
    /// <summary>The role was not reported.</summary>
    Unspecified = 0,

    /// <summary>The text-detection model.</summary>
    Detection = 1,

    /// <summary>The text-recognition model.</summary>
    Recognition = 2,
}

/// <summary>
/// What is wrong with a model file.
/// </summary>
/// <remarks>
/// The names are the ones the native library reports in the <c>defect</c> field of
/// its error document, kept stable so a host can switch on them rather than match
/// <see cref="OcrException"/>'s message says the same thing in a sentence,
/// with the numbers that decided it.
/// </remarks>
public enum OcrModelDefect
{
    /// <summary>The defect was not reported.</summary>
    Unspecified = 0,

    /// <summary>The file holds no byte at all.</summary>
    Empty,

    /// <summary>The file is smaller than any real model.</summary>
    TooSmall,

    /// <summary>The file is larger than this library will read into memory.</summary>
    TooLarge,

    /// <summary>The file starts with neither the <c>RTEN</c> magic nor an ONNX signature.</summary>
    UnrecognizedFormat,

    /// <summary>The file starts with the <c>RTEN</c> magic but is shorter than its header.</summary>
    TruncatedHeader,

    /// <summary>The file is an <c>RTEN</c> container of a version the library cannot read.</summary>
    UnsupportedVersion,

    /// <summary>The header's declared sections cannot all fit in the file as it is.</summary>
    InconsistentLayout,

    /// <summary>The container is recognized but the graph inside it cannot be parsed.</summary>
    UnparsableGraph,

    /// <summary>The graph's input is not the shape the engine needs for its role.</summary>
    InputShape,

    /// <summary>The model directory does not hold a file it must hold.</summary>
    MissingFile,

    /// <summary>The directory named does not exist.</summary>
    MissingDirectory,

    /// <summary>The path named is a file, not a directory.</summary>
    NotADirectory,

    /// <summary>The file exists but could not be read.</summary>
    UnreadableFile,
}

/// <summary>
/// One validated model file, as a report can name it.
/// </summary>
public sealed class OcrModelFingerprint
{
    internal OcrModelFingerprint(
        OcrModelRole role,
        string? fileName,
        string format,
        long length,
        string sha256)
    {
        Role = role;
        FileName = fileName;
        Format = format;
        Length = length;
        Sha256 = sha256;
    }

    /// <summary>Which role this file fills.</summary>
    public OcrModelRole Role { get; }

    /// <summary>The file name, when the model was discovered in a directory.</summary>
    public string? FileName { get; }

    /// <summary>The container, <c>rten</c> or <c>onnx</c>, as the leading bytes declare it.</summary>
    public string Format { get; }

    /// <summary>The file's length in bytes.</summary>
    public long Length { get; }

    /// <summary>
    /// The SHA-256 of the file's bytes, as sixty-four lowercase hexadecimal
    /// characters. It is computed from the bytes the engine loaded, so two hosts
    /// that load the same file report the same string.
    /// </summary>
    public string Sha256 { get; }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} {1} model, {2} bytes, sha256 {3}{4}",
        Role,
        Format,
        Length,
        Sha256,
        FileName is null ? string.Empty : " (" + FileName + ")");
}

/// <summary>
/// The identity of the weights a reading was produced with: the two model
/// fingerprints and one digest over the pair.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sha256"/> is <c>SHA-256(detection_digest || recognition_digest)</c> —
/// the two 32-byte digests concatenated in that order. It is a pure function of the
/// two files, so it is stable across processes and machines, and swapping the two
/// files changes it.
/// </para>
/// <para>
/// A result carries the identity of the engine that produced it, so a report can
/// say exactly which weights a page was read with.
/// </para>
/// </remarks>
public sealed class OcrModelIdentity
{
    internal OcrModelIdentity(
        OcrModelFingerprint detection,
        OcrModelFingerprint recognition,
        string sha256)
    {
        Detection = detection;
        Recognition = recognition;
        Sha256 = sha256;
    }

    /// <summary>The detection model's fingerprint.</summary>
    public OcrModelFingerprint Detection { get; }

    /// <summary>The recognition model's fingerprint.</summary>
    public OcrModelFingerprint Recognition { get; }

    /// <summary>The digest of the pair.</summary>
    public string Sha256 { get; }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "models {0}: detection {1}, recognition {2}",
        Sha256,
        Detection.Sha256,
        Recognition.Sha256);
}

/// <summary>
/// The two models of a directory, validated and identified — without loading them.
/// </summary>
/// <remarks>
/// <para>
/// This is the preflight a caller runs when it wants to know which weights it is
/// about to trust, or to refuse a bad directory at startup rather than under the
/// first page it reads. Nothing is parsed or kept in memory beyond the files'
/// digests, so it costs a read and a hash rather than the tens of megabytes a
/// loaded engine holds.
/// </para>
/// <para>
/// The library downloads nothing and has no default path: the directory is the one
/// named here, and the only file names looked for are
/// <see cref="DetectionFileName"/> and <see cref="RecognitionFileName"/>. A missing
/// one is an <see cref="OcrException"/> whose <see cref="OcrException.Defect"/> is
/// <see cref="OcrModelDefect.MissingFile"/> and whose <see cref="OcrException.FilePath"/>
/// names the file that was expected.
/// </para>
/// </remarks>
public sealed class OcrModelSet
{
    /// <summary>The detection model's file name inside a model directory.</summary>
    public const string DetectionFileName = "detection.rten";

    /// <summary>The recognition model's file name inside a model directory.</summary>
    public const string RecognitionFileName = "recognition.rten";

    private OcrModelSet(string directory, OcrModelIdentity identity)
    {
        Directory = directory;
        Identity = identity;
        DetectionPath = Path.Combine(directory, DetectionFileName);
        RecognitionPath = Path.Combine(directory, RecognitionFileName);
    }

    /// <summary>The directory the models were read from.</summary>
    public string Directory { get; }

    /// <summary>The path of the detection model: <c>{Directory}/detection.rten</c>.</summary>
    public string DetectionPath { get; }

    /// <summary>The path of the recognition model: <c>{Directory}/recognition.rten</c>.</summary>
    public string RecognitionPath { get; }

    /// <summary>The identity of the pair.</summary>
    public OcrModelIdentity Identity { get; }

    /// <summary>
    /// Validates and identifies the two models of <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory holding <c>detection.rten</c> and <c>recognition.rten</c>.</param>
    /// <returns>The validated pair, without having loaded the weights.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="OcrException">
    /// The directory does not exist, is not a directory, is missing one of the two
    /// files, or holds a file that is not a model of the expected role; <see cref="OcrException.Defect"/>
    /// says which, and <see cref="OcrException.FilePath"/> names the file.
    /// </exception>
    /// <exception cref="OcrException">The native library is not available.</exception>
    public static OcrModelSet LoadFromDirectory(string directory)
        => LoadFromDirectory(NativeOcr.Library, directory);

    internal static OcrModelSet LoadFromDirectory(IOcrNative library, string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory.Length == 0)
        {
            throw new ArgumentException("The model directory is empty.", nameof(directory));
        }

        var (loaded, identity) = NativeOcr.InspectModelSet(library, directory);

        return new OcrModelSet(loaded, identity);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} ({1})",
        Directory,
        Identity);
}

/// <summary>
/// One model set, named by the language it reads.
/// </summary>
/// <remarks>
/// <para>
/// A document that mixes languages needs one pair of weights per language, and a
/// language is a tag the caller supplies — this library never guesses one from the
/// text. This type pairs a tag with the <see cref="OcrModelSet"/> that reads it, so
/// <see cref="OcrEngine.FromLanguages"/> can be handed a list of them.
/// </para>
/// <para>
/// The tag is compared trimmed and case-insensitively: <c>fr</c>, <c>FR</c> and
/// <c> fr </c> name the same set. It is a free-form string rather than an enum,
/// because the tags a host uses are its own; this package only needs them to match
/// the ones the caller passes per page.
/// </para>
/// </remarks>
public sealed class OcrLanguageModelSet
{
    /// <summary>Creates the pairing.</summary>
    /// <param name="language">The tag the set reads, such as <c>fr</c> or <c>en</c>.</param>
    /// <param name="models">The validated model set.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="language"/> is empty.</exception>
    public OcrLanguageModelSet(string language, OcrModelSet models)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(models);

        if (language.Trim().Length == 0)
        {
            throw new ArgumentException("The language is empty.", nameof(language));
        }

        Language = language.Trim();
        Models = models;
    }

    /// <summary>The language this set reads, as the caller named it.</summary>
    public string Language { get; }

    /// <summary>The validated pair of models.</summary>
    public OcrModelSet Models { get; }

    /// <summary>
    /// Validates a directory and pairs its models with a language.
    /// </summary>
    /// <param name="language">The tag the set reads.</param>
    /// <param name="directory">The directory holding <c>detection.rten</c> and <c>recognition.rten</c>.</param>
    /// <returns>The validated pairing, without having loaded the weights.</returns>
    /// <exception cref="OcrException">The directory is not a model set; <see cref="OcrException.Defect"/> says why.</exception>
    /// <exception cref="OcrException">The native library is not available.</exception>
    public static OcrLanguageModelSet Load(string language, string directory)
        => new(language, OcrModelSet.LoadFromDirectory(directory));

    /// <inheritdoc />
    public override string ToString() => $"{Language}: {Models.Identity.Sha256}";
}
