using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A symbol rendered for paper: the raster, the resolution it was written for, and the size it
/// really prints at.
/// </summary>
/// <remarks>
/// <para>
/// This is what closes the gap a PNG leaves open. <see cref="Barcode.Encode"/> returns pixels at
/// <see cref="BarcodeOptions.Scale"/> per module and says nothing about the paper;
/// <see cref="Barcode.Print"/> takes a target — dots per inch and a width or a module size — and
/// returns the same raster with the arithmetic done: <see cref="ModuleMillimetres"/>,
/// <see cref="WidthMillimetres"/> and <see cref="HeightMillimetres"/> are what the printer will
/// put on the label.
/// </para>
/// <para>
/// The numbers are the raster's, not the request's. A raster is whole pixels, so a module lands on
/// a whole number of dots and the width is that many dots wide: ask for 30 mm at 300 dpi and the
/// answer may be 29.5 mm, because 29 modules cannot each be exactly the same fraction of a pixel
/// and still be sharp.
/// </para>
/// </remarks>
public sealed class BarcodePrint
{
    internal BarcodePrint(
        BarcodeSymbology symbology,
        string payload,
        byte[] png,
        int dpi,
        double moduleMillimetres,
        int modulePixels,
        int quietZone,
        int widthModules,
        int heightModules,
        double widthMillimetres,
        double heightMillimetres,
        int widthPixels,
        int heightPixels)
    {
        Symbology = symbology;
        Payload = payload;
        Png = png;
        Dpi = dpi;
        ModuleMillimetres = moduleMillimetres;
        ModulePixels = modulePixels;
        QuietZone = quietZone;
        WidthModules = widthModules;
        HeightModules = heightModules;
        WidthMillimetres = widthMillimetres;
        HeightMillimetres = heightMillimetres;
        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
    }

    /// <summary>The symbology the symbol was written in.</summary>
    public BarcodeSymbology Symbology { get; }

    /// <summary>The payload, exactly as it was encoded.</summary>
    public string Payload { get; }

    /// <summary>
    /// The symbol as an 8-bit greyscale PNG at <see cref="Dpi"/>, black and white only, sized to
    /// <see cref="WidthPixels"/> by <see cref="HeightPixels"/>.
    /// </summary>
    public byte[] Png { get; }

    /// <summary>The output resolution the raster was written for, in dots per inch.</summary>
    public int Dpi { get; }

    /// <summary>
    /// The printed size of one module, in millimetres — the X-dimension a scanner cares about. It
    /// is never below the symbology's normative minimum; a request that would go under is refused.
    /// </summary>
    public double ModuleMillimetres { get; }

    /// <summary>The integer scale of the raster: printer dots per module.</summary>
    public int ModulePixels { get; }

    /// <summary>
    /// The quiet zone actually rendered, in modules on each side. At least
    /// <see cref="Barcode.MinimumQuietZone"/>, possibly wider when the caller asked for more.
    /// </summary>
    public int QuietZone { get; }

    /// <summary>Width of the symbol, quiet zone included, in modules.</summary>
    public int WidthModules { get; }

    /// <summary>Height of the symbol, quiet zone included, in modules.</summary>
    public int HeightModules { get; }

    /// <summary>Width of the printed symbol, in millimetres.</summary>
    public double WidthMillimetres { get; }

    /// <summary>Height of the printed symbol, in millimetres.</summary>
    public double HeightMillimetres { get; }

    /// <summary>Width of the raster, in pixels.</summary>
    public int WidthPixels { get; }

    /// <summary>Height of the raster, in pixels.</summary>
    public int HeightPixels { get; }

    /// <summary>
    /// The aspect ratio of the printed symbol: a one-dimensional symbol is a wide strip, a QR Code
    /// is square.
    /// </summary>
    public double AspectRatio => HeightMillimetres == 0 ? 0 : WidthMillimetres / HeightMillimetres;
}
