using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using M3scalinum.Text.Pdf.Internal;

namespace M3scalinum.Text.Pdf;

/// <summary>Renders a page of a PDF as an image, for a preview or a recogniser.</summary>
/// <remarks>
/// <para>
/// The renderer is <c>hayro</c>, a pure-Rust CPU rasteriser compiled into this package's own native
/// library: no PDF engine on the host, no system poppler, no font installed for the page to draw.
/// It is the renderer <c>M3scalinum.Text.Ocr</c> already carries, so the suite has one PDF renderer
/// rather than two.
/// </para>
/// <para>
/// What the render does not do is as much a part of the contract as what it does: annotations,
/// form fields, digital-signature appearances and JavaScript are not drawn, and a document that has
/// to be decrypted is refused rather than decrypted — <see cref="PdfEncryption.Open"/> is the entry
/// point for that. The specification is <c>docs/specifications/35-pdf-raster.md</c>.
/// </para>
/// </remarks>
public static class PdfRaster
{
    /// <summary>The largest pixel ceiling <see cref="PdfRasterOptions.MaximumPixels"/> may name.</summary>
    public const long HardMaximumPixels = 200_000_000;

    /// <summary>The largest byte ceiling <see cref="PdfRasterOptions.MaximumBytes"/> may name.</summary>
    public const long HardMaximumBytes = 1024L * 1024 * 1024;

    /// <summary>What an encrypted document is refused with, in the package's own words.</summary>
    private const string EncryptedMessage =
        "the document is encrypted, so it cannot be rendered until it is decrypted: " +
        "open it with PdfEncryption.Open and use the bytes it returns";

    /// <summary>
    /// How many pages the document holds.
    /// </summary>
    /// <param name="pdf">The bytes of a PDF.</param>
    /// <returns>The page count the document declares.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is null.</exception>
    /// <exception cref="PdfRasterException">
    /// The bytes are not a PDF, or the document is encrypted.
    /// </exception>
    public static int PageCount(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        RefuseEncrypted(pdf);
        PdfRasterNative.Initialize();

        int count;
        unsafe
        {
            fixed (byte* bytes = pdf)
            {
                var result = PdfRasterNativeMethods.PageCount(bytes, (nuint)pdf.Length, out var error);

                if (result < 0)
                {
                    throw PdfRasterNative.ToException(error);
                }

                count = (int)result;
            }
        }

        return count;
    }

    /// <summary>
    /// Renders one page as a PNG or a JPEG.
    /// </summary>
    /// <param name="pdf">The bytes of a PDF.</param>
    /// <param name="page">The page to render, numbered from 1 the way a reader numbers pages.</param>
    /// <param name="options">
    /// The resolution, the encoding, the JPEG quality and the ceilings, or null for the defaults:
    /// PNG at the default resolution.
    /// </param>
    /// <returns>The encoded image and the size it renders to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is below 1, or <paramref name="options"/> names a value outside its
    /// accepted range — including a <see cref="PdfPageImageFormat"/> that is not a member of the
    /// enum and a JPEG quality outside 1–100.
    /// </exception>
    /// <exception cref="PdfRasterException">
    /// The bytes are not a PDF, the document is encrypted, the page does not exist, or the page would
    /// render past the pixel or memory ceiling.
    /// </exception>
    public static PdfPageImage Render(byte[] pdf, int page, PdfRasterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page), page, "Pages are numbered from 1, the way a PDF reader numbers them.");
        }

        options ??= new PdfRasterOptions();
        options.Validate();

        RefuseEncrypted(pdf);
        PdfRasterNative.Initialize();

        // The format crosses as its own name rather than as the enum's number: the native side keeps
        // one table of the encodings it can write, and an enum renumbered here is not a silent
        // change of format there.
        var optionsJson = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"dpi\":{options.Dpi},\"maxPixels\":{options.MaximumPixels},\"maxBytes\":{options.MaximumBytes},\"format\":\"{FormatName(options.Format)}\",\"quality\":{options.Quality}}}");

        NativeBuffer image;
        NativeBuffer json;

        unsafe
        {
            fixed (byte* bytes = pdf)
            {
                var status = PdfRasterNativeMethods.PageRender(
                    bytes,
                    (nuint)pdf.Length,
                    (uint)page,
                    optionsJson,
                    out image,
                    out json,
                    out var error);

                if (status != 0)
                {
                    // A failed call leaves both buffers empty; the error object is the answer.
                    throw PdfRasterNative.ToException(error);
                }
            }
        }

        try
        {
            var encoded = image.ToArray();
            var metadata = json.ToArray();

            return ParseMetadata(encoded, metadata, page, options.Dpi);
        }
        finally
        {
            PdfRasterNativeMethods.BufferFree(image);
            PdfRasterNativeMethods.BufferFree(json);
        }
    }

    /// <summary>
    /// Refuses a document the trailer marks as encrypted, before any renderer sees it.
    /// </summary>
    /// <remarks>
    /// The check is here rather than in Rust because it is the package's policy, not the renderer's
    /// capability: <c>hayro</c> will open an encrypted document whose empty password happens to be the
    /// owner password, and rendering it would quietly do the decryption this package says it does not
    /// do. <see cref="PdfEncryption.Open"/> is the one entry point that returns plaintext bytes, and
    /// the caller decides what to do with them. A document whose trailer cannot be read is left to the
    /// native side, which refuses it as a PDF it cannot parse.
    /// </remarks>
    private static void RefuseEncrypted(byte[] pdf)
    {
        try
        {
            if (PdfFile.TryOpen(pdf, out var file) && file!.IsEncrypted)
            {
                throw new PdfRasterException(PdfRasterError.Encrypted, EncryptedMessage);
            }
        }
        catch (PdfEncryptionException)
        {
            // The security handler is declared but unreadable: still an encrypted document, and still
            // not this package's job to decrypt it.
            throw new PdfRasterException(PdfRasterError.Encrypted, EncryptedMessage);
        }
    }

    /// <summary>
    /// Builds the image from the metadata the native side wrote beside it.
    /// </summary>
    /// <remarks>
    /// The native side is the same build of this repository, so a missing field is a defect rather
    /// than a version skew to tolerate; it surfaces as a <see cref="PdfRasterError.Internal"/>
    /// exception naming the field instead of a silently mislabelled image.
    /// </remarks>
    private static PdfPageImage ParseMetadata(byte[] encoded, byte[] metadata, int page, int dpi)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;

            return new PdfPageImage(
                encoded,
                Format(root.GetProperty("format").GetString()),
                root.GetProperty("page").GetInt32(),
                root.GetProperty("pageCount").GetInt32(),
                root.GetProperty("width").GetInt32(),
                root.GetProperty("height").GetInt32(),
                root.GetProperty("dpi").GetInt32(),
                root.GetProperty("effectiveDpi").GetDouble());
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new PdfRasterException(
                PdfRasterError.Internal,
                $"the native library returned page {page} at {dpi} dpi without a readable description of it",
                exception);
        }
    }

    private static PdfPageImageFormat Format(string? format) => format switch
    {
        "png" => PdfPageImageFormat.Png,
        "jpeg" => PdfPageImageFormat.Jpeg,
        _ => throw new PdfRasterException(
            PdfRasterError.Internal,
            $"the native library returned a '{format}' image, which this package does not decode"),
    };

    /// <summary>The name the native library knows a format by.</summary>
    private static string FormatName(PdfPageImageFormat format) => format switch
    {
        PdfPageImageFormat.Png => "png",
        PdfPageImageFormat.Jpeg => "jpeg",
        // Validate refused everything else, and a value added to the enum without a name here would
        // otherwise reach the library as a number in the JSON.
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, "The format has no name in the native protocol."),
    };
}
