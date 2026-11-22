using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>What a decode is allowed to cost, and how large an image it will agree to read.</summary>
/// <remarks>
/// <para>
/// The dimensions of an image come from whoever produced it, and that is not this library: a scan
/// arrives from a sender. Both ceilings are applied to the image's own header before any pixel is
/// allocated, so a PNG that claims a gigapixel is refused rather than decoded into memory.
/// </para>
/// <para>
/// The pixel ceiling is inclusive: an image of exactly <see cref="MaximumPixels"/> pixels decodes.
/// A ceiling may be lowered as far as the caller likes; raising it past the package's hard maximum
/// is an <see cref="ArgumentOutOfRangeException"/> rather than a silent clamp.
/// </para>
/// </remarks>
public sealed class BarcodeDecodeOptions
{
    /// <summary>
    /// The default pixel ceiling: 64 megapixels, an A4 page at 600 DPI with room to spare.
    /// </summary>
    public const long DefaultMaximumPixels = 64_000_000;

    /// <summary>
    /// The default byte ceiling: 256 MiB, the same order as the PDF rasteriser's, so a page cannot
    /// be rendered small enough to pass there and too large here.
    /// </summary>
    public const long DefaultMaximumBytes = 256L * 1024 * 1024;

    /// <summary>The largest pixel ceiling a caller may name: 250 megapixels.</summary>
    public const long HardMaximumPixels = 250_000_000;

    /// <summary>The largest byte ceiling a caller may name: 1 GiB.</summary>
    public const long HardMaximumBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// The largest number of pixels the image may hold. Defaults to
    /// <see cref="DefaultMaximumPixels"/>.
    /// </summary>
    public long MaximumPixels { get; init; } = DefaultMaximumPixels;

    /// <summary>
    /// The largest number of bytes the encoded image may occupy. Defaults to
    /// <see cref="DefaultMaximumBytes"/>.
    /// </summary>
    public long MaximumBytes { get; init; } = DefaultMaximumBytes;

    /// <summary>Refuses a value the native library would refuse, before anything is decoded.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its accepted range.</exception>
    internal void Validate()
    {
        if (MaximumPixels <= 0 || MaximumPixels > HardMaximumPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumPixels),
                MaximumPixels,
                $"The pixel ceiling must be between 1 and {HardMaximumPixels}.");
        }

        if (MaximumBytes <= 0 || MaximumBytes > HardMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBytes),
                MaximumBytes,
                $"The byte ceiling must be between 1 and {HardMaximumBytes}.");
        }
    }
}
