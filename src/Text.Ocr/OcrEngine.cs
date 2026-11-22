using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// Reads text from page images and scanned PDFs, in process, on the CPU.
/// </summary>
/// <remarks>
/// <para>
/// The engine holds two machine-learning models and runs them through
/// <c>ocrs</c> and <c>rten</c>. Neither this package nor the native library
/// downloads a model, ships one, or reads one from a default path: the caller
/// passes the model bytes to <see cref="FromModels"/>, which keeps roughly 40 MB of
/// weights out of the package and every byte of input visible to the host, or names
/// a directory for <see cref="FromDirectory"/>.
/// </para>
/// <para>
/// Either way the files are validated before they are loaded — magic, size,
/// container layout, and the input shape the model's role needs — and identified by
/// SHA-256. A file that is not a model, or a model of the wrong generation, is
/// refused with an <see cref="OcrException"/> whose <see cref="OcrException.Defect"/>
/// names what is wrong; it is never a wrong reading and never a crash under the first
/// page.
/// </para>
/// <para>
/// An engine is thread-safe for reads once created: <see cref="Recognize"/> and
/// <see cref="RecognizePdf"/> may be called concurrently. Dispose it when done;
/// disposing releases the models.
/// </para>
/// </remarks>
public sealed class OcrEngine : IDisposable
{
    private readonly IOcrBackend _backend;
    private readonly bool _hasLanguageSets;
    private bool _disposed;

    internal OcrEngine(IOcrBackend backend, bool hasLanguageSets = false)
    {
        _backend = backend;
        _hasLanguageSets = hasLanguageSets;
    }

    /// <summary>
    /// Creates an engine from model bytes the caller already holds.
    /// </summary>
    /// <param name="detectionModel">The text-detection model, in <c>rten</c> or ONNX form.</param>
    /// <param name="recognitionModel">The text-recognition model, in <c>rten</c> or ONNX form.</param>
    /// <param name="characterDictionary">
    /// The alphabet the recognition model was trained on, as one UTF-8 string, or
    /// <see langword="null"/> for the default Latin alphabet of the published models.
    /// </param>
    /// <returns>A loaded engine.</returns>
    /// <exception cref="ArgumentNullException">A model argument is null.</exception>
    /// <exception cref="ArgumentException">A model argument is empty.</exception>
    /// <exception cref="OcrException">The native side could not load a model.</exception>
    public static OcrEngine FromModels(
        byte[] detectionModel,
        byte[] recognitionModel,
        byte[]? characterDictionary = null)
    {
        ArgumentNullException.ThrowIfNull(detectionModel);
        ArgumentNullException.ThrowIfNull(recognitionModel);

        if (detectionModel.Length == 0)
        {
            throw new ArgumentException("The detection model is empty.", nameof(detectionModel));
        }

        if (recognitionModel.Length == 0)
        {
            throw new ArgumentException("The recognition model is empty.", nameof(recognitionModel));
        }

        return new OcrEngine(NativeOcrBackend.Create(detectionModel, recognitionModel, characterDictionary));
    }

    /// <summary>
    /// Creates an engine from the two models of a directory: <c>detection.rten</c>
    /// and <c>recognition.rten</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the convenience for a host that keeps its weights on disk. The only
    /// file names looked for are those two, nothing is downloaded, and no other path
    /// is tried: a directory missing one of them is refused with an
    /// <see cref="OcrException"/> whose <see cref="OcrException.FilePath"/>
    /// names the file that was expected.
    /// </para>
    /// <para>
    /// The same validation as <see cref="FromModels"/> applies, and the files must be
    /// UTF-8 paths; the recognition model's alphabet is the default Latin one. Use
    /// <see cref="FromModels"/> when the recognition model needs a custom alphabet.
    /// </para>
    /// </remarks>
    /// <param name="directory">The directory holding the two model files.</param>
    /// <returns>A loaded engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="OcrException">A file is missing, unreadable or not a model of its role.</exception>
    /// <exception cref="OcrException">The native library is not available.</exception>
    public static OcrEngine FromDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory.Length == 0)
        {
            throw new ArgumentException("The model directory is empty.", nameof(directory));
        }

        return new OcrEngine(NativeOcrBackend.CreateFromDirectory(directory));
    }

    /// <summary>
    /// Creates an engine holding one model set per language, with one of them as the
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A document that mixes a French page and an English page cannot be read well by
    /// one pair of weights: the recognition model's alphabet is the language's. This
    /// overload loads a pair per language, and <see cref="Recognize"/> and
    /// <see cref="RecognizePdf"/> then take a language for the image or for each page.
    /// </para>
    /// <para>
    /// A page whose language is not in this plan is <em>not</em> an error and is
    /// <em>not</em> silent: it is read with the default set, and the result's
    /// <see cref="OcrRecognition.Language"/> reports the language asked for, the one
    /// used and the reason. That is the fallback rule, and it is the only one there is.
    /// </para>
    /// <para>
    /// Each set costs one copy of the weights in memory. The tag is matched trimmed
    /// and case-insensitively, and each tag may name only one set.
    /// </para>
    /// </remarks>
    /// <param name="defaultLanguage">The tag whose set reads a page that names no language, or one this plan does not hold.</param>
    /// <param name="modelSets">The validated model set of each language; at least one, and one must be the default.</param>
    /// <param name="characterDictionary">
    /// The alphabet every recognition model was trained on, as one UTF-8 string, or
    /// <see langword="null"/> for the default Latin alphabet. One alphabet applies to
    /// all the sets.
    /// </param>
    /// <returns>A loaded engine holding every set.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The plan is empty, repeats a tag, or its default has no set.</exception>
    /// <exception cref="OcrException">A model directory is missing, unreadable, or not a model of its role.</exception>
    /// <exception cref="OcrException">The native library is not available, or predates language selection.</exception>
    public static OcrEngine FromLanguages(
        string defaultLanguage,
        IEnumerable<OcrLanguageModelSet> modelSets,
        byte[]? characterDictionary = null)
    {
        ArgumentNullException.ThrowIfNull(defaultLanguage);
        ArgumentNullException.ThrowIfNull(modelSets);

        var defaultTag = defaultLanguage.Trim();

        if (defaultTag.Length == 0)
        {
            throw new ArgumentException("The default language is empty.", nameof(defaultLanguage));
        }

        var sets = modelSets.ToList();

        if (sets.Count == 0)
        {
            throw new ArgumentException("The language plan holds no model set.", nameof(modelSets));
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in sets)
        {
            if (set is null)
            {
                throw new ArgumentException("A model set is null.", nameof(modelSets));
            }

            if (!tags.Add(set.Language))
            {
                throw new ArgumentException(
                    $"The language '{set.Language}' has more than one model set.", nameof(modelSets));
            }
        }

        if (!tags.Contains(defaultTag))
        {
            throw new ArgumentException(
                $"The default language '{defaultTag}' has no model set.", nameof(defaultLanguage));
        }

        var plan = LanguagePlan(defaultTag, sets, characterDictionary);

        return new OcrEngine(NativeOcrBackend.CreateLanguages(plan), hasLanguageSets: true);
    }

    /// <summary>
    /// The JSON plan the native side loads: the default, the optional alphabet, and one
    /// entry per language.
    /// </summary>
    private static byte[] LanguagePlan(
        string defaultLanguage,
        List<OcrLanguageModelSet> sets,
        byte[]? characterDictionary)
    {
        var builder = new StringBuilder();
        builder.Append("{\"default\":");
        AppendJsonString(builder, defaultLanguage);
        builder.Append(",\"dictionary\":");

        if (characterDictionary is { Length: > 0 })
        {
            AppendJsonString(builder, Encoding.UTF8.GetString(characterDictionary));
        }
        else
        {
            builder.Append("null");
        }

        builder.Append(",\"sets\":[");

        for (var index = 0; index < sets.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"language\":");
            AppendJsonString(builder, sets[index].Language);
            builder.Append(",\"directory\":");
            AppendJsonString(builder, sets[index].Models.Directory);
            builder.Append('}');
        }

        builder.Append("]}");

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendJsonString(StringBuilder builder, string value)
        => builder.Append('"').Append(JsonEncodedText.Encode(value).ToString()).Append('"');

    /// <summary>
    /// The identity of the weights this engine runs: the two model fingerprints and
    /// one digest over the pair.
    /// </summary>
    /// <remarks>
    /// It is read from the native side once, and every result this engine produces
    /// carries the same identity. <see langword="null"/> only for a backend that does
    /// not know its weights — the test doubles — never for the native library.
    /// </remarks>
    public OcrModelIdentity? Models
        => _backend is IModelSource source ? source.Models : null;

    /// <summary>
    /// Recognises the text of one encoded image: PNG, JPEG, BMP, GIF, TIFF or WebP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reading that fails a threshold is not returned as text: the result is then an
    /// <see cref="OcrUnreadable"/> naming the rule that refused it.
    /// </para>
    /// <para>
    /// A page turned by a scanner or photographed at an angle is read after the
    /// preprocessing <see cref="OcrOptions.DetectRotation"/>, <see cref="OcrOptions.Deskew"/>
    /// and <see cref="OcrOptions.Denoise"/> ask for; all three are off by default, and a
    /// page that needs none of them is read exactly as before.
    /// </para>
    /// </remarks>
    /// <param name="imageBytes">The image file, as it would be read from disk.</param>
    /// <param name="options">
    /// The confidence thresholds and the preprocessing switches; defaults to
    /// <see cref="OcrOptions"/>'s defaults, which leave a correct scan untouched.
    /// </param>
    /// <param name="language">
    /// Which model set reads the image, when the engine was built by
    /// <see cref="FromLanguages"/>; <see langword="null"/> or empty means the default
    /// set. A language the engine does not hold is not an error: the image is read with
    /// the default set and <see cref="OcrRecognition.Language"/> says so.
    /// </param>
    /// <returns>Either the text, the lines and their boxes, or a typed refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageBytes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="imageBytes"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A threshold is outside 0–1.</exception>
    /// <exception cref="ObjectDisposedException">The engine was disposed.</exception>
    /// <exception cref="InvalidOperationException">A language was named on an engine that holds one model set.</exception>
    /// <exception cref="OcrException">The image could not be decoded or read.</exception>
    public OcrRecognition Recognize(
        byte[] imageBytes,
        OcrOptions? options = null,
        string? language = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ThrowIfDisposed();

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("The image is empty.", nameof(imageBytes));
        }

        options ??= new OcrOptions();
        options.Validate();

        if (language is null || language.Trim().Length == 0)
        {
            return _backend.Recognize(imageBytes, options);
        }

        RequireLanguageSets();

        return _backend.RecognizeLanguage(imageBytes, options, language.Trim());
    }

    /// <summary>
    /// Renders each requested page of a PDF at the options' resolution and recognises it.
    /// </summary>
    /// <remarks>
    /// The rendering happens in the native library, which carries its own CPU PDF
    /// rasteriser: this package never references <c>M3scalinum.Text.Pdf</c>, and a
    /// scanned document needs no text layer to be read. Each page is assessed on its
    /// own: a page that fails a threshold comes back as an <see cref="OcrPageUnreadable"/>
    /// while the pages around it stay readings. Each page is also preprocessed on its
    /// own, so one page a scanner turned does not turn the others.
    /// </remarks>
    /// <param name="pdf">The document bytes.</param>
    /// <param name="options">
    /// The resolution, the page range and the confidence thresholds; defaults to 300 DPI
    /// from the first page, with <see cref="OcrOptions"/>'s default thresholds.
    /// </param>
    /// <param name="languageForPage">
    /// Which model set reads each page, given its zero-based index in the document,
    /// when the engine was built by <see cref="FromLanguages"/>; <see langword="null"/>
    /// reads every page with the default set. Returning <see langword="null"/> or an
    /// empty string for a page names no language, which is the default set and not a
    /// fallback. A tag the engine does not hold is read with the default set and
    /// reported as a fallback on that page. The rule is asked once per page, in page
    /// order, before the document is rendered.
    /// </param>
    /// <returns>One outcome per page, in page order.</returns>
    /// <remarks>
    /// The language is the caller's fact, so it is the caller's rule: this library does
    /// not detect the language of a page. Guessing it from the text — or from the page
    /// before — would make a reading depend on the other pages of the document, and a
    /// mis-guess would be indistinguishable from a real reading.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pdf"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    /// <exception cref="ObjectDisposedException">The engine was disposed.</exception>
    /// <exception cref="InvalidOperationException">A rule was supplied to an engine that holds one model set.</exception>
    /// <exception cref="OcrException">The document could not be read or rendered.</exception>
    public IReadOnlyList<OcrPageOutcome> RecognizePdf(
        byte[] pdf,
        OcrPdfOptions? options = null,
        Func<int, string?>? languageForPage = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (pdf.Length == 0)
        {
            throw new ArgumentException("The document is empty.", nameof(pdf));
        }

        ThrowIfDisposed();

        options ??= new OcrPdfOptions();
        options.Validate();

        if (languageForPage is null)
        {
            return _backend.RecognizePdf(pdf, options);
        }

        RequireLanguageSets();

        return _backend.RecognizePdfLanguages(pdf, options, languageForPage);
    }

    /// <summary>
    /// Refuses a language on an engine that holds a single set: there is nothing to
    /// choose from, so naming one is a mistake in the caller rather than a page to read.
    /// </summary>
    private void RequireLanguageSets()
    {
        if (!_hasLanguageSets)
        {
            throw new InvalidOperationException(
                "This engine holds one model set. Create it with FromLanguages to name a language.");
        }
    }

    /// <summary>
    /// Releases the engine and the models it holds.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_backend is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
