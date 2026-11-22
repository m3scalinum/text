namespace M3scalinum.Text.Pdf;

/// <summary>One page of a PDF, rendered as an image.</summary>
/// <remarks>
/// <see cref="Bytes"/> is the encoded image, ready to write to a file, put in an HTTP response, or
/// hand to a recogniser. The pixel size is what the renderer produced, and
/// <see cref="EffectiveDpi"/> is the resolution those pixels actually carry: the renderer truncates
/// each dimension to a whole pixel, so a 595.28 pt page at 150 DPI is 1240 px wide and reaches
/// 149.94 DPI rather than 150.
/// </remarks>
public sealed class PdfPageImage
{
    internal PdfPageImage(
        byte[] bytes,
        PdfPageImageFormat format,
        int page,
        int pageCount,
        int width,
        int height,
        int dpi,
        double effectiveDpi)
    {
        Bytes = bytes;
        Format = format;
        Page = page;
        PageCount = pageCount;
        Width = width;
        Height = height;
        Dpi = dpi;
        EffectiveDpi = effectiveDpi;
    }

    /// <summary>The encoded image, owned by the caller.</summary>
    public byte[] Bytes { get; }

    /// <summary>The encoding of <see cref="Bytes"/>.</summary>
    public PdfPageImageFormat Format { get; }

    /// <summary>The page this image is, numbered from 1.</summary>
    public int Page { get; }

    /// <summary>How many pages the document it came from holds.</summary>
    public int PageCount { get; }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The resolution asked for, in dots per inch.</summary>
    public int Dpi { get; }

    /// <summary>The resolution the pixels actually carry, derived from the page's width in points.</summary>
    public double EffectiveDpi { get; }
}
