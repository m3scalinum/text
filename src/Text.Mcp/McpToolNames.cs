namespace M3scalinum.Text.Mcp;

/// <summary>
/// The names of the tools <see cref="McpServer"/> exposes.
/// </summary>
/// <remarks>
/// Constants rather than a description table: a client that wires a tool call by name must be able
/// to reference it from C# without copying a string, and the protocol names are stable.
/// </remarks>
public static class McpToolNames
{
    /// <summary>Renders Typst source, or one layout of the template pack, to PDF.</summary>
    public const string RenderDocument = "render_document";

    /// <summary>Renders a Factur-X invoice payload to a PDF/A-3 with its XML attached.</summary>
    public const string RenderInvoice = "render_invoice";

    /// <summary>Lists the layouts of the template pack this server serves.</summary>
    public const string ListTemplates = "list_templates";
}
