using System;

namespace M3scalinum.Text.Pdf;

/// <summary>How a page is rasterised, and what it is allowed to cost.</summary>
/// <remarks>
/// <para>
/// The two ceilings are the security property of <see cref="PdfRaster"/>: a page's size comes from
/// the document, and a document arrives from a sender. A 200-inch square page at 1200 DPI is 24
/// gigapixels, which is not a page and must not be allowed to become an allocation. The ceiling is
/// checked against the page geometry before the renderer allocates anything.
/// </para>
/// <para>
/// A ceiling may be lowered as far as the caller likes. Raising it past the crate's hard maximum is
/// refused with an <see cref="ArgumentOutOfRangeException"/> rather than clamped: a silently lowered
/// cap is a promise the caller believes it has.
/// </para>
/// </remarks>
public sealed class PdfRasterOptions
{
    /// <summary>
    /// The resolution a call gets when it names none. 150 DPI is a preview that stays sharp at 100 %
    /// on a HiDPI screen — A4 is 1240×1754 — and is the floor the OCR documentation calls readable.
    /// A caller that feeds the image to a recogniser asks for 300.
    /// </summary>
    public const int DefaultDpi = 150;

    /// <summary>The lowest accepted resolution.</summary>
    public const int MinimumDpi = 72;

    /// <summary>The highest accepted resolution.</summary>
    public const int MaximumDpi = 1200;

    /// <summary>
    /// The default ceiling on the pixels one page may produce: 50 megapixels, roughly A2 at 300 DPI.
    /// </summary>
    public const long DefaultMaximumPixels = 50_000_000;

    /// <summary>
    /// The default ceiling on the bytes one page may produce: 256 MiB, 67 megapixels of RGBA8.
    /// </summary>
    public const long DefaultMaximumBytes = 256L * 1024 * 1024;

    /// <summary>
    /// The resolution the page is rendered at, between <see cref="MinimumDpi"/> and
    /// <see cref="MaximumDpi"/>. Defaults to <see cref="DefaultDpi"/>.
    /// </summary>
    public int Dpi { get; init; } = DefaultDpi;

    /// <summary>
    /// The largest number of pixels the page may render to. Defaults to
    /// <see cref="DefaultMaximumPixels"/>.
    /// </summary>
    public long MaximumPixels { get; init; } = DefaultMaximumPixels;

    /// <summary>
    /// The largest number of bytes of pixels the page may render to. Defaults to
    /// <see cref="DefaultMaximumBytes"/>.
    /// </summary>
    public long MaximumBytes { get; init; } = DefaultMaximumBytes;

    /// <summary>Refuses a combination the native library would refuse, before anything is loaded.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its accepted range.</exception>
    internal void Validate()
    {
        if (Dpi is < MinimumDpi or > MaximumDpi)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Dpi), Dpi, $"The DPI must be between {MinimumDpi} and {MaximumDpi}.");
        }

        if (MaximumPixels <= 0 || MaximumPixels > PdfRaster.HardMaximumPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumPixels),
                MaximumPixels,
                $"The pixel ceiling must be between 1 and {PdfRaster.HardMaximumPixels}.");
        }

        if (MaximumBytes <= 0 || MaximumBytes > PdfRaster.HardMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBytes),
                MaximumBytes,
                $"The byte ceiling must be between 1 and {PdfRaster.HardMaximumBytes}.");
        }
    }
}
