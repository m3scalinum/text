namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Options for turning an invoice into a PDF.</summary>
public sealed record FacturXRenderOptions
{
    /// <summary>
    /// Whether to run <see cref="FacturXValidator"/> first. Default <see langword="true"/>: the PDF
    /// must not look authoritative when the document it carries breaks the rules.
    /// </summary>
    public bool Validate { get; init; } = true;

    /// <summary>The name of the embedded XML file. Factur-X fixes it as <c>factur-x.xml</c>.</summary>
    public string FileName { get; init; } = "factur-x.xml";
}