namespace M3scalinum.Text.SharpTypst.Compilation;

/// <summary>One heading of the outline.</summary>
/// <param name="Level">Nesting depth, starting at one.</param>
/// <param name="Title">The heading's plain text.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="X">Horizontal position on the page, in points.</param>
/// <param name="Y">Vertical position on the page, in points.</param>
/// <param name="Label">The heading's label, when it has one.</param>
public sealed record TypstOutlineEntry(
    int Level,
    string Title,
    int Page,
    double X,
    double Y,
    string? Label);