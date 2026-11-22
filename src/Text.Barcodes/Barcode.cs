using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using M3scalinum.Text.Barcodes.Internal;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// Writes and reads barcodes in-process: no external tool, no service, no temporary file.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Encode"/> returns the same symbol as SVG and as PNG, from one module matrix.
/// <see cref="Decode"/> reads a PNG back to text. Both directions run inside the native library
/// the package ships per runtime identifier, so a caller needs nothing installed beyond the
/// package itself.
/// </para>
/// <para>
/// The engine is <c>rxing</c> 0.9, a Rust port of ZXing, Apache-2.0 like this repository. See
/// Spec 20 for why it was chosen over a .NET library and what each symbology guarantees.
/// </para>
/// </remarks>
public static class Barcode
{
    /// <summary>The largest payload, in bytes, that <see cref="Encode"/> accepts.</summary>
    /// <remarks>
    /// A QR Code tops out around 3 KB and a one-dimensional symbol far below that; anything past
    /// four is a mistake rather than a barcode, and the limit is checked before the engine is asked.
    /// </remarks>
    public const int MaxPayloadBytes = 4096;

    /// <summary>The symbologies this package writes and reads, as the native library lists them.</summary>
    /// <remarks>
    /// Read from the library rather than declared here: a build whose native half is older than the
    /// managed half reports a short list instead of pretending a symbology works.
    /// </remarks>
    public static IReadOnlyList<BarcodeSymbology> SupportedSymbologies
        => BarcodeNative.Symbologies()
            .Select(BarcodeSymbologyNames.FromAbiName)
            .Where(symbology => symbology is not null)
            .Select(symbology => symbology!.Value)
            .ToArray();

    /// <summary>
    /// The GS1 Application Identifiers this package understands, with the length each one takes
    /// and whether it is a Digital Link primary key.
    /// </summary>
    /// <remarks>
    /// Read from the native library, like <see cref="SupportedSymbologies"/>: the table the parser
    /// enforces and the table this property reports are the same one.
    /// </remarks>
    public static IReadOnlyList<Gs1ApplicationIdentifier> ApplicationIdentifiers
        => ParseIdentifiers(BarcodeNative.Gs1Identifiers());

    /// <summary>Whether this build can write <paramref name="symbology"/>.</summary>
    /// <param name="symbology">The symbology to check.</param>
    /// <returns><see langword="false"/> for a symbology the engine reads but cannot write: GS1
    /// DataBar, MaxiCode and Micro QR Code, in rxing 0.9.4.</returns>
    public static bool CanEncode(BarcodeSymbology symbology) => Encodeable.Contains(symbology);

    /// <summary>
    /// Encodes <paramref name="data"/> as a GS1-128.
    /// </summary>
    /// <param name="data">The elements to write. Nothing is reformatted: the AIs and values go into
    /// the symbol as they are, with FNC1 in lead position and a field separator after every
    /// variable-length value.</param>
    /// <param name="options">What to change about the rendering, or <see langword="null"/> for the
    /// defaults.</param>
    /// <returns>The module matrix, its SVG and its PNG.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="BarcodeFormatException">The payload cannot be represented in Code 128.</exception>
    /// <exception cref="BarcodeGs1Exception">An element is not one this build understands.</exception>
    public static BarcodeImage EncodeGs1(Gs1Data data, BarcodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Encode(BarcodeSymbology.Gs1_128, data.Raw, options);
    }

    /// <summary>
    /// Encodes <paramref name="payload"/> as <paramref name="symbology"/>.
    /// </summary>
    /// <param name="symbology">The symbology to write.</param>
    /// <param name="payload">The text to encode. ASCII is always safe; the symbologies that carry an
    /// ECI (QR Code, DataMatrix, Aztec, PDF417) also take UTF-8. For
    /// <see cref="BarcodeSymbology.Gs1_128"/> this is a GS1 element string, or the printed form
    /// <c>(01)…(10)…</c>; <see cref="EncodeGs1"/> is the typed way in.</param>
    /// <param name="options">What to change about the rendering, or <see langword="null"/> for the
    /// defaults: four pixels per module, the symbology's own quiet zone and bar height.</param>
    /// <returns>The module matrix, its SVG and its PNG.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <exception cref="BarcodeException"><paramref name="payload"/> is empty or holds a NUL, or
    /// <paramref name="options"/> holds a value out of range.</exception>
    /// <exception cref="BarcodeFormatException">The symbology cannot represent this payload: the
    /// wrong length for EAN-13, EAN-8, UPC-A or UPC-E, a character the symbology has no encoding
    /// for, a payload past the symbol's capacity.</exception>
    /// <exception cref="BarcodeUnsupportedException">The symbology is read only in this build.</exception>
    /// <exception cref="BarcodeGs1Exception">The payload is not a GS1 element string this build
    /// understands.</exception>
    public static BarcodeImage Encode(
        BarcodeSymbology symbology,
        string payload,
        BarcodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!CanEncode(symbology))
        {
            throw new BarcodeUnsupportedException(
                $"{symbology} is read only in this build: the engine has no writer for it, so it can be decoded but not encoded");
        }

        options ??= new BarcodeOptions();
        options.Validate(symbology);
        ValidatePayload(payload);

        try
        {
            using var handle = BarcodeNative.Encode(
                symbology.ToAbiName(),
                payload,
                options.QuietZone ?? -1,
                options.BarHeight ?? -1,
                options.ErrorCorrection.ToAbiName());

            var svg = BarcodeNative.Svg(handle);
            var png = BarcodeNative.Png(handle, options.Scale);

            return new BarcodeImage(
                symbology, payload, svg, png, handle.ModuleWidth, handle.ModuleHeight);
        }
        catch (NativeCallException error)
        {
            throw Translate(error, symbology);
        }
    }

    /// <summary>
    /// Reads the first barcode in a PNG.
    /// </summary>
    /// <param name="png">The image, as PNG bytes. An 8-bit greyscale image, a 1-bit image and a
    /// colour image all decode: the engine works on luminance.</param>
    /// <returns>
    /// The decoded text, the symbology and the detected points. A readable image without a barcode
    /// comes back with <see cref="DecodeResult.Success"/> false and
    /// <see cref="DecodeResult.Error"/> set, never as an exception.
    /// </returns>
    /// <remarks>
    /// This is the single-symbol fast path: one scanner pass, the first symbol it reads. Use
    /// <see cref="DecodeAll"/> for a page that may carry several.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="png"/> is null.</exception>
    /// <exception cref="BarcodeException"><paramref name="png"/> is empty.</exception>
    /// <exception cref="BarcodeImageException"><paramref name="png"/> is not a PNG this build can
    /// read.</exception>
    public static DecodeResult Decode(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        if (png.Length == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument, "the image is empty");
        }

        try
        {
            using var decoded = BarcodeNative.Decode(png);

            if (!BarcodeNative.Success(decoded))
            {
                return new DecodeResult(
                    success: false,
                    text: string.Empty,
                    symbology: null,
                    format: string.Empty,
                    points: [],
                    bounds: null,
                    error: BarcodeNative.Failure(decoded));
            }

            return Read(decoded);
        }
        catch (NativeCallException error)
        {
            throw Translate(error, symbology: null);
        }
    }

    /// <summary>
    /// Reads every barcode in an image, in the order the engine finds them.
    /// </summary>
    /// <param name="image">The encoded image, as PNG bytes. A page rendered by another package goes
    /// here unchanged: <c>M3scalinum.Text.Pdf.PdfRaster.Render(pdf, page).Bytes</c> is exactly the
    /// shape this method takes, and rendering the page is the caller's step rather than this
    /// package's — see the note on the PDF path in Spec 20 §9.</param>
    /// <param name="options">The pixel and byte ceilings, or <see langword="null"/> for the
    /// defaults: 64 megapixels and 256 MiB.</param>
    /// <returns>
    /// Every symbol the engine found, each with its bounding box, plus the size of the image it
    /// decoded. An image without a symbol is a page with <see cref="DecodePage.Success"/> false and
    /// <see cref="DecodePage.Error"/> set, never an exception.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A ceiling in
    /// <paramref name="options"/> is outside its accepted range.</exception>
    /// <exception cref="BarcodeException"><paramref name="image"/> is empty, or past a ceiling
    /// (<see cref="BarcodeErrorCode.Limit"/>).</exception>
    /// <exception cref="BarcodeImageException"><paramref name="image"/> is not a PNG this build can
    /// read.</exception>
    public static DecodePage DecodeAll(byte[] image, BarcodeDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Length == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument, "the image is empty");
        }

        options ??= new BarcodeDecodeOptions();
        options.Validate();

        try
        {
            using var page = BarcodeNative.DecodeImage(
                image, options.MaximumPixels, options.MaximumBytes);

            var count = page.SymbolCount;
            var symbols = new List<DecodeResult>(count);

            for (var index = 0; index < count; index++)
            {
                using var decoded = BarcodeNative.PageSymbol(page, index);
                symbols.Add(Read(decoded));
            }

            return new DecodePage(
                symbols,
                page.ImageWidth,
                page.ImageHeight,
                symbols.Count == 0 ? BarcodeNative.PageFailure(page) : null);
        }
        catch (NativeCallException error)
        {
            throw Translate(error, symbology: null);
        }
    }

    /// <summary>Builds the managed result from one live native handle.</summary>
    private static DecodeResult Read(DecodeHandle decoded)
    {
        var points = BarcodeNative.Points(decoded)
            .Select(point => new BarcodePoint(point.X, point.Y))
            .ToArray();

        return new DecodeResult(
            success: true,
            text: BarcodeNative.Text(decoded),
            symbology: BarcodeSymbologyNames.FromAbiName(BarcodeNative.Symbology(decoded)),
            format: BarcodeNative.Format(decoded),
            points: points,
            bounds: BarcodeNative.Bounds(decoded),
            error: null);
    }

    private static void ValidatePayload(string payload)
    {
        if (payload.Length == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument, "payload must not be empty");
        }

        if (payload.Contains('\0', StringComparison.Ordinal))
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                "payload must not contain a NUL character: it would truncate at the ABI");
        }
    }

    /// <summary>Maps a native failure on a GS1 call onto the public taxonomy.</summary>
    /// <param name="error">The failure from the interop layer.</param>
    /// <returns>The exception a caller should see.</returns>
    internal static BarcodeException TranslateGs1(NativeCallException error) => Translate(error, null);

    /// <summary>Maps the native status onto the public exception taxonomy, once, here.</summary>
    private static BarcodeException Translate(NativeCallException error, BarcodeSymbology? symbology)
    {
        var message = error.Describe();
        var context = symbology is null ? string.Empty : $"{symbology}: ";

        return error.Status switch
        {
            BarcodeStatus.InvalidArgument => new BarcodeException(
                BarcodeErrorCode.InvalidArgument, context + message),
            BarcodeStatus.Encode => new BarcodeFormatException(context + message),
            BarcodeStatus.Unsupported => new BarcodeUnsupportedException(context + message),
            BarcodeStatus.Gs1 => new BarcodeGs1Exception(message),
            BarcodeStatus.Decode => new BarcodeImageException(message),
            BarcodeStatus.Limit => new BarcodeException(BarcodeErrorCode.Limit, context + message),
            _ => new BarcodeNativeException(context + message, error.Traceback ?? string.Empty),
        };
    }

    /// <summary>
    /// The symbologies the loaded native library can write, read once from its capability report.
    /// </summary>
    /// <remarks>
    /// Lazily cached because it is consulted on every <see cref="Encode"/> call, and it is the
    /// native library's answer, not a table declared here.
    /// </remarks>
    private static readonly Lazy<IReadOnlySet<BarcodeSymbology>> EncodeableSet = new(() =>
        BarcodeNative.SymbologyCapabilities()
            .Where(capability => capability.CanEncode)
            .Select(capability => BarcodeSymbologyNames.FromAbiName(capability.Name))
            .Where(symbology => symbology is not null)
            .Select(symbology => symbology!.Value)
            .ToHashSet());

    private static IReadOnlySet<BarcodeSymbology> Encodeable => EncodeableSet.Value;

    /// <summary>Reads the AI table the native library reports.</summary>
    private static List<Gs1ApplicationIdentifier> ParseIdentifiers(string json)
    {
        if (json.Length == 0)
        {
            return [];
        }

        var identifiers = new List<Gs1ApplicationIdentifier>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            identifiers.Add(new Gs1ApplicationIdentifier(
                element.GetProperty("ai").GetString() ?? string.Empty,
                element.GetProperty("title").GetString() ?? string.Empty,
                element.GetProperty("min").GetInt32(),
                element.GetProperty("max").GetInt32(),
                element.GetProperty("numeric").GetBoolean(),
                element.GetProperty("checkDigit").GetBoolean(),
                element.GetProperty("primaryKey").GetBoolean()));
        }

        return identifiers;
    }
}
