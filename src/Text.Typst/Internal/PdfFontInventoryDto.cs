using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Internal;

/// <summary>
/// The JSON object the <c>pdf-fonts</c> export returns: the fonts the encoded PDF carries and the
/// bytes each one costs.
/// </summary>
/// <remarks>
/// Property names are camelCase on the wire, from <see cref="TypstJsonContext"/>'s naming policy.
/// The Rust side writes them by name, so a rename on either side reads as a zero; the Rust test and
/// <c>PdfFontInventoryTests</c> pin both ends.
/// </remarks>
internal sealed class PdfFontInventoryDto
{
    /// <summary>Total size of the PDF these numbers were read from.</summary>
    public long Bytes { get; set; }

    /// <summary>Bytes the embedded font streams occupy in that file.</summary>
    public long FontBytes { get; set; }

    /// <summary>One entry per embedded font file.</summary>
    public List<PdfEmbeddedFontDto>? Fonts { get; set; }
}

/// <summary>One embedded font, as one font-file stream in the PDF.</summary>
internal sealed class PdfEmbeddedFontDto
{
    /// <summary>The PDF name, subset tag included when present.</summary>
    public string? Name { get; set; }

    /// <summary>The name without its subset tag.</summary>
    public string? Family { get; set; }

    /// <summary>The six-letter subset tag, or <see langword="null"/> for a whole font.</summary>
    public string? SubsetTag { get; set; }

    /// <summary>Whether the face is a subset.</summary>
    public bool Subset { get; set; }

    /// <summary>The outline format: <c>truetype</c>, <c>cff</c>, <c>opentype</c> or <c>type1</c>.</summary>
    public string? Format { get; set; }

    /// <summary>Bytes the font stream occupies in the file.</summary>
    public long Bytes { get; set; }
}
