namespace M3scalinum.Text.Typst.Internal;

/// <summary>
/// The JSON object <c>tn_document_metrics</c> returns: what the native library measured during the
/// compilation.
/// </summary>
/// <remarks>
/// Property names are camelCase on the wire, from <see cref="TypstJsonContext"/>'s naming policy.
/// They are read positionally by name, so a rename on either side of the ABI is a silent zero; the
/// Rust test and <c>CompilationMetricsTests</c> both pin them.
/// </remarks>
internal sealed class CompileMetricsDto
{
    /// <summary>Nanoseconds spent inside Typst, bundle export included.</summary>
    public long CompileNanos { get; set; }

    /// <summary>Pages the layout produced. Zero for HTML and bundle documents.</summary>
    public int Pages { get; set; }

    /// <summary>Fonts the world could resolve.</summary>
    public int FontsTotal { get; set; }

    /// <summary>How many of them are embedded in the native library.</summary>
    public int FontsEmbedded { get; set; }

    /// <summary>Bytes of distinct font data the world held.</summary>
    public long FontBytes { get; set; }
}
