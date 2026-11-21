namespace M3scalinum.Text.Typst.Export;

/// <summary>Options for PNG export.</summary>
public sealed record PngExportOptions
{
    /// <summary>0-based page to export. <see langword="null"/> exports the first page.</summary>
    public int? Page { get; init; }

    /// <summary>Resolution in pixels per inch. 72 is one pixel per point; 300 is print quality.</summary>
    public float Ppi { get; init; } = 144f;
}
