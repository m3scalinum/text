using System;
using System.Collections.Generic;
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
    private bool _disposed;

    internal OcrEngine(IOcrBackend backend) => _backend = backend;

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
    /// <returns>Either the text, the lines and their boxes, or a typed refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageBytes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="imageBytes"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A threshold is outside 0–1.</exception>
    /// <exception cref="ObjectDisposedException">The engine was disposed.</exception>
    /// <exception cref="OcrException">The image could not be decoded or read.</exception>
    public OcrRecognition Recognize(byte[] imageBytes, OcrOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ThrowIfDisposed();

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("The image is empty.", nameof(imageBytes));
        }

        options ??= new OcrOptions();
        options.Validate();

        return _backend.Recognize(imageBytes, options);
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
    /// <returns>One outcome per page, in page order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="pdf"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    /// <exception cref="ObjectDisposedException">The engine was disposed.</exception>
    /// <exception cref="OcrException">The document could not be read or rendered.</exception>
    public IReadOnlyList<OcrPageOutcome> RecognizePdf(byte[] pdf, OcrPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (pdf.Length == 0)
        {
            throw new ArgumentException("The document is empty.", nameof(pdf));
        }

        ThrowIfDisposed();

        options ??= new OcrPdfOptions();
        options.Validate();

        return _backend.RecognizePdf(pdf, options);
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
