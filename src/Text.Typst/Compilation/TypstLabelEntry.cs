namespace M3scalinum.Text.Typst.Compilation;

/// <summary>One labelled element.</summary>
/// <param name="Name">The label name, without the angle brackets.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="X">Horizontal position on the page, in points.</param>
/// <param name="Y">Vertical position on the page, in points.</param>
public sealed record TypstLabelEntry(string Name, int Page, double X, double Y);