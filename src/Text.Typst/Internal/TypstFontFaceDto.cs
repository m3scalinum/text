using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Internal;

/// <summary>
/// The JSON object <c>tn_world_font_faces</c> returns for one face: its family, the variant of its
/// default instance, and the variation axes it carries.
/// </summary>
/// <remarks>
/// Property names are camelCase on the wire, from <see cref="TypstJsonContext"/>'s naming policy.
/// The Rust side writes them by name, so a rename on either side reads as a default; the Rust test
/// and <c>VariableFontTests</c> pin both ends.
/// </remarks>
internal sealed class TypstFontFaceDto
{
    /// <summary>The family name Typst resolves against.</summary>
    public string? Family { get; set; }

    /// <summary>The weight of the default instance, 100-900.</summary>
    public int Weight { get; set; }

    /// <summary>The width of the default instance, as a percentage of normal: 100 is normal.</summary>
    public double Stretch { get; set; }

    /// <summary><c>normal</c>, <c>italic</c> or <c>oblique</c>.</summary>
    public string? Style { get; set; }

    /// <summary>Whether the face carries an <c>fvar</c> table, and therefore axes.</summary>
    public bool Variable { get; set; }

    /// <summary>The axes the face carries, sorted by tag; empty for a static face.</summary>
    public List<TypstFontAxisDto>? Axes { get; set; }
}

/// <summary>One variation axis of a face: its tag and the range the font allows.</summary>
internal sealed class TypstFontAxisDto
{
    /// <summary>The OpenType tag, e.g. <c>wght</c>, <c>wdth</c>, <c>opsz</c> or a custom one.</summary>
    public string? Tag { get; set; }

    /// <summary>The smallest value the face accepts.</summary>
    public double Min { get; set; }

    /// <summary>The largest value the face accepts.</summary>
    public double Max { get; set; }

    /// <summary>The value the face was designed around, used when nothing asks for another one.</summary>
    public double Default { get; set; }
}
