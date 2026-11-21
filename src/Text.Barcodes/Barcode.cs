using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Encodes <paramref name="payload"/> as <paramref name="symbology"/>.
    /// </summary>
    /// <param name="symbology">The symbology to write.</param>
    /// <param name="payload">The text to encode. ASCII is always safe; the symbologies that carry an
    /// ECI (QR Code, DataMatrix, Aztec, PDF417) also take UTF-8.</param>
    /// <param name="options">What to change about the rendering, or <see langword="null"/> for the
    /// defaults: four pixels per module, the symbology's own quiet zone and bar height.</param>
    /// <returns>The module matrix, its SVG and its PNG.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <exception cref="BarcodeException"><paramref name="payload"/> is empty or holds a NUL, or
    /// <paramref name="options"/> holds a value out of range.</exception>
    /// <exception cref="BarcodeFormatException">The symbology cannot represent this payload: the
    /// wrong length for EAN-13 or EAN-8, a character the symbology has no encoding for, a payload
    /// past the symbol's capacity.</exception>
    public static BarcodeImage Encode(
        BarcodeSymbology symbology,
        string payload,
        BarcodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

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
                    error: BarcodeNative.Failure(decoded));
            }

            var points = BarcodeNative.Points(decoded)
                .Select(point => new BarcodePoint(point.X, point.Y))
                .ToArray();

            return new DecodeResult(
                success: true,
                text: BarcodeNative.Text(decoded),
                symbology: BarcodeSymbologyNames.FromAbiName(BarcodeNative.Symbology(decoded)),
                format: BarcodeNative.Format(decoded),
                points: points,
                error: null);
        }
        catch (NativeCallException error)
        {
            throw Translate(error, symbology: null);
        }
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
            BarcodeStatus.Decode => new BarcodeImageException(message),
            BarcodeStatus.Limit => new BarcodeException(BarcodeErrorCode.Limit, context + message),
            _ => new BarcodeNativeException(context + message, error.Traceback ?? string.Empty),
        };
    }
}
