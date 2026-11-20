namespace M3scalinum.Text.SharpTypst.Export;

/// <summary>Options for SVG export.</summary>
public sealed record SvgExportOptions
{
    /// <summary>0-based page to export. <see langword="null"/> exports the first page.</summary>
    public int? Page { get; init; }

    /// <summary>Indent the output for human reading.</summary>
    public bool Pretty { get; init; }
}
