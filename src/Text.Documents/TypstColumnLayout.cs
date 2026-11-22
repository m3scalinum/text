using System;
using System.Globalization;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A column layout: how many columns a region is split into, and the gutter between them.
/// </summary>
/// <remarks>
/// <para>
/// The same layout serves the two Typst primitives, which do not behave alike. A page-level layout
/// (<c>#set page(columns: n)</c>) splits the page text area and paginates normally: the first column
/// of a page is filled before the second, and the flow continues on the next page. A flow-level
/// layout (<c>#columns(n)[…]</c>) is a container: it lays its content out in columns but the caller
/// has to break out of it for anything that must span the page, and Typst's own documentation points
/// a whole document at the page-level primitive.
/// </para>
/// <para>
/// The gutter is one setting either way. Typst reads the page-level gutter from the <c>columns</c>
/// element's style, so it is written as <c>#set columns(gutter: …)</c>; the flow-level call takes it
/// as an argument. Measured against Typst 0.15.1: <c>#set columns(gutter: 1em)</c> spaces the page
/// columns, and <c>#columns(2, gutter: 1em)</c> spaces the flow columns.
/// </para>
/// </remarks>
public sealed class TypstColumnLayout
{
    /// <summary>Creates a layout of <paramref name="count"/> columns.</summary>
    /// <param name="count">The number of columns, at least one.</param>
    /// <param name="gutter">The space between two columns; Typst's own default when null.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than one.</exception>
    public TypstColumnLayout(int count, TypstLength? gutter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Count = count;
        Gutter = gutter;
    }

    /// <summary>The number of columns.</summary>
    public int Count { get; }

    /// <summary>The space between two columns, or <see langword="null"/> for Typst's default.</summary>
    public TypstLength? Gutter { get; }

    /// <summary>The <c>#set page</c> argument that asks for the columns.</summary>
    internal string PageArgument => "columns: " + Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The <c>#set columns</c> rule that spaces the page-level columns, or <see langword="null"/>
    /// when the layout leaves the gutter to Typst.
    /// </summary>
    internal string? GutterRule
        => Gutter is { } gutter ? "#set columns(gutter: " + gutter + ")" : null;

    /// <summary>The arguments of a flow-level <c>#columns(…)</c> call.</summary>
    internal string CallArguments
        => Gutter is { } gutter
            ? Count.ToString(CultureInfo.InvariantCulture) + ", gutter: " + gutter
            : Count.ToString(CultureInfo.InvariantCulture);
}
