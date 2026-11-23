using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>One variation axis of a font face, and the range the font allows for it.</summary>
/// <remarks>
/// The axis is what makes an intermediate weight mean something: a face whose <c>wght</c> axis runs
/// from 300 to 700 renders <c>#set text(weight: 350)</c> at 350, while a static face has no axis and
/// serves the nearest cut it ships. Use <see cref="Contains"/> to answer "is this value reachable"
/// before asking the layout for it.
/// </remarks>
/// <param name="Tag">The OpenType tag, e.g. <c>wght</c>, <c>wdth</c>, <c>opsz</c> or a custom one.</param>
/// <param name="Min">The smallest value the face accepts.</param>
/// <param name="Max">The largest value the face accepts.</param>
/// <param name="Default">The value the face was designed around, used when nothing asks otherwise.</param>
public sealed record TypstFontAxis(string Tag, double Min, double Max, double Default)
{
    /// <summary>Whether <paramref name="value"/> falls inside the range this axis allows.</summary>
    /// <remarks>
    /// Typst clamps a variation to the range rather than refusing it, so a value outside it is not
    /// an error — it renders at the nearest end.
    /// </remarks>
    public bool Contains(double value) => value >= Min && value <= Max;
}

/// <summary>
/// One face a world can resolve: its family, the variant of its default instance, and the axes it
/// carries.
/// </summary>
/// <remarks>
/// This is the answer to "does 350 mean anything here". Typst's <c>weight</c> accepts a number from
/// 100 to 900 and passes it straight to the <c>wght</c> axis, so an intermediate value renders only
/// when the face has that axis. A static family — the case of every font embedded in this package —
/// has an empty <see cref="Axes"/> and is selected by nearest weight instead, which is why a document
/// that asks for 350 in a static family looks exactly like the same document at 400.
/// </remarks>
/// <param name="Family">The family name Typst resolves against.</param>
/// <param name="Weight">The weight of the default instance, 100-900.</param>
/// <param name="Stretch">The width of the default instance, as a percentage of normal: 100 is normal.</param>
/// <param name="Style"><c>normal</c>, <c>italic</c> or <c>oblique</c>.</param>
/// <param name="IsVariable">Whether the face carries an <c>fvar</c> table, and therefore axes.</param>
/// <param name="Axes">The axes the face carries; empty for a static face.</param>
public sealed record TypstFontFace(
    string Family,
    int Weight,
    double Stretch,
    string Style,
    bool IsVariable,
    IReadOnlyList<TypstFontAxis> Axes)
{
    /// <summary>The axis with this tag, or <see langword="null"/> when the face does not carry it.</summary>
    /// <param name="tag">An OpenType tag, e.g. <c>wght</c>. Case-sensitive.</param>
    public TypstFontAxis? Axis(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return Axes.FirstOrDefault(axis => axis.Tag == tag);
    }
}
