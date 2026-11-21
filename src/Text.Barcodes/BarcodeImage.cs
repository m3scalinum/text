namespace M3scalinum.Text.Barcodes;

/// <summary>
/// An encoded symbol: the module matrix, its vector rendering and its raster rendering.
/// </summary>
/// <remarks>
/// Both renderings come from the same matrix, so they cannot disagree about where a dark module
/// sits. <see cref="Svg"/> is emitted at one unit per module; a consumer that needs a physical size
/// scales it, which is why nothing here carries a resolution.
/// </remarks>
public sealed class BarcodeImage
{
    internal BarcodeImage(
        BarcodeSymbology symbology,
        string payload,
        string svg,
        byte[] png,
        int moduleWidth,
        int moduleHeight)
    {
        Symbology = symbology;
        Payload = payload;
        Svg = svg;
        Png = png;
        ModuleWidth = moduleWidth;
        ModuleHeight = moduleHeight;
    }

    /// <summary>The symbology the symbol was written in.</summary>
    public BarcodeSymbology Symbology { get; }

    /// <summary>The payload, exactly as it was encoded.</summary>
    public string Payload { get; }

    /// <summary>
    /// The symbol as SVG, one user-space unit per module, with a white background and a single
    /// black <c>&lt;path&gt;</c>. Ready to be embedded in a document or scaled by a layout engine.
    /// </summary>
    public string Svg { get; }

    /// <summary>
    /// The symbol as an 8-bit greyscale PNG, <see cref="BarcodeOptions.Scale"/> pixels per module,
    /// black and white only. A second pass decodes it back to <see cref="Payload"/>.
    /// </summary>
    public byte[] Png { get; }

    /// <summary>Width of the module matrix, in modules. Equals the SVG's width in units.</summary>
    public int ModuleWidth { get; }

    /// <summary>
    /// Height of the module matrix, in modules. Equals the SVG's height in units: one row for a
    /// two-dimensional symbol, <see cref="BarcodeOptions.BarHeight"/> for a one-dimensional one.
    /// </summary>
    public int ModuleHeight { get; }

    /// <summary>
    /// The aspect ratio of the symbol, width over height, in modules. A one-dimensional symbol is
    /// a wide strip; a QR Code is square.
    /// </summary>
    public double AspectRatio => ModuleHeight == 0 ? 0 : (double)ModuleWidth / ModuleHeight;
}
