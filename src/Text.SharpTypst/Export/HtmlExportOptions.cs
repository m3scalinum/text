namespace M3scalinum.Text.SharpTypst.Export;

/// <summary>Options for HTML export.</summary>
public sealed record HtmlExportOptions
{
    /// <summary>Indent the output for human reading.</summary>
    public bool Pretty { get; init; } = true;
}
