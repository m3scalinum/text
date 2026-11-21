namespace M3scalinum.Text.Typst.Export;

/// <summary>Output format of a compiled document.</summary>
public enum ExportFormat
{
    /// <summary>Portable Document Format. Requires a paged document.</summary>
    Pdf,

    /// <summary>Scalable Vector Graphics, one page or merged. Requires a paged document.</summary>
    Svg,

    /// <summary>Raster image, one page or merged. Requires a paged document.</summary>
    Png,

    /// <summary>HTML. Requires a document compiled as <see cref="Compilation.TypstDocumentKind.Html"/>.</summary>
    Html,

    /// <summary>Markdown derived from the HTML document.</summary>
    Markdown,

    /// <summary>Plain text derived from the HTML document.</summary>
    Text,
}
