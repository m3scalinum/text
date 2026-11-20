namespace M3scalinum.Text.SharpTypst.Diagnostics;

/// <summary>
/// A position in a source file. Lines and columns are 1-based; columns count Unicode
/// scalar values, matching what an editor shows for a UTF-8 file.
/// </summary>
/// <param name="File">Virtual path of the file, for example <c>main.typ</c>.</param>
/// <param name="Line">1-based start line.</param>
/// <param name="Column">1-based start column.</param>
/// <param name="EndLine">1-based end line.</param>
/// <param name="EndColumn">1-based end column.</param>
/// <param name="IsInSource">
/// <see langword="false"/> when the position points into a file that is not a registered
/// source, such as a Typst package.
/// </param>
public sealed record TypstLocation(
    string File,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    bool IsInSource);
