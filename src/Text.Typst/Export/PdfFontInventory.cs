using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Typst.Internal;

namespace M3scalinum.Text.Typst.Export;

/// <summary>How one embedded font is encoded in the PDF.</summary>
public enum PdfFontFormat
{
    /// <summary>The format was not recognised: a newer writer, or a font type this layer does not read.</summary>
    Unknown = 0,

    /// <summary>A TrueType outline, embedded as <c>/FontFile2</c>.</summary>
    TrueType,

    /// <summary>A bare CFF program, embedded as <c>/FontFile3</c> with <c>/CIDFontType0C</c>.</summary>
    Cff,

    /// <summary>A whole OpenType file, embedded as <c>/FontFile3</c> with <c>/OpenType</c>.</summary>
    OpenType,

    /// <summary>A Type 1 program, embedded as <c>/FontFile</c>.</summary>
    Type1,
}

/// <summary>One font file the PDF embeds: the face, its bytes, and whether it is a subset.</summary>
/// <remarks>
/// <see cref="Bytes"/> is the font stream's encoded size, which is what the file pays for it. The
/// same face costs several times more once inflated, so this is a size contribution, not a memory
/// budget.
/// </remarks>
/// <param name="Name">The PDF name, e.g. <c>QKJXZB+NewCM10-Regular</c>.</param>
/// <param name="Family">The name without its subset tag, e.g. <c>NewCM10-Regular</c>.</param>
/// <param name="SubsetTag">The six-letter subset tag, or <see langword="null"/> for a whole font.</param>
/// <param name="IsSubset">Whether the face is a subset of the font it came from.</param>
/// <param name="Format">How the outline is embedded.</param>
/// <param name="Bytes">Bytes the font file occupies in the PDF.</param>
public sealed record PdfEmbeddedFont(
    string Name,
    string Family,
    string? SubsetTag,
    bool IsSubset,
    PdfFontFormat Format,
    long Bytes);

/// <summary>
/// What a PDF export embeds as fonts: one entry per embedded font file, and the share of the file
/// they account for.
/// </summary>
/// <remarks>
/// <para>
/// Typst's PDF writer has no switch for font subsetting — every face is subsetted, and there is no
/// way to ask for whole fonts from this layer. This type exists because the other half of that
/// question is answerable and useful: which fonts the file carries, what each one costs, and
/// therefore how much of a document is type.
/// </para>
/// <para>
/// Every face Typst lays out is embedded; nothing is left as a reference to a system font, which is
/// what makes the output portable and what makes a system font's licence matter at export time.
/// </para>
/// </remarks>
public sealed class PdfFontInventory
{
    internal PdfFontInventory(PdfFontInventoryDto dto)
    {
        Bytes = dto.Bytes;
        FontBytes = dto.FontBytes;
        Fonts = (dto.Fonts ?? [])
            .Select(font => new PdfEmbeddedFont(
                font.Name ?? string.Empty,
                font.Family ?? font.Name ?? string.Empty,
                font.SubsetTag,
                font.Subset,
                ParseFormat(font.Format),
                font.Bytes))
            .ToList();
    }

    /// <summary>Total size of the PDF these numbers were read from.</summary>
    public long Bytes { get; }

    /// <summary>Bytes the embedded font files occupy.</summary>
    public long FontBytes { get; }

    /// <summary>
    /// The share of the file that is font data, between 0 and 1. Zero for an empty inventory.
    /// </summary>
    public double FontShare => Bytes == 0 ? 0d : (double)FontBytes / Bytes;

    /// <summary>One entry per embedded font file, in the order the PDF writes them.</summary>
    public IReadOnlyList<PdfEmbeddedFont> Fonts { get; }

    private static PdfFontFormat ParseFormat(string? format) => format switch
    {
        "truetype" => PdfFontFormat.TrueType,
        "cff" => PdfFontFormat.Cff,
        "opentype" => PdfFontFormat.OpenType,
        "type1" => PdfFontFormat.Type1,
        _ => PdfFontFormat.Unknown,
    };
}
