using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A symbol added to a Typst document: the figure is in the document, and this is what the caller
/// needs to put the image behind it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Barcode.AddToDocument"/> adds a <c>#figure(image(…), alt: …)</c> to the document and
/// returns this. The document model refers to images by virtual path — it is write-only Typst
/// source and carries no bytes — so the raster comes back here under <see cref="AssetName"/>, ready
/// to be registered with the world builder the compiler is given:
/// </para>
/// <code>
/// var figure = Barcode.AddToDocument(document, BarcodeSymbology.QrCode, "https://example.test/42");
/// var builder = new TypstWorldBuilder().AddSource("main.typ", document.ToMarkup());
/// builder.AddAsset(figure.AssetName, figure.Png);
/// </code>
/// <para>
/// There is no temporary file and no manual encoding: the symbol was rendered in-process, named
/// deterministically, and placed in the document in one call.
/// </para>
/// </remarks>
public sealed class BarcodeFigure
{
    internal BarcodeFigure(string assetName, string altText, BarcodePrint print)
    {
        AssetName = assetName;
        AltText = altText;
        Print = print;
    }

    /// <summary>
    /// The virtual path the figure's <c>image(…)</c> refers to, for example
    /// <c>barcode-qrcode-3f2a91c4.png</c>. Deterministic: the same symbology, payload and print
    /// target always produce the same name, so a rebuild produces the same document.
    /// </summary>
    public string AssetName { get; }

    /// <summary>
    /// The alternative description the figure carries. Defaults to the symbology and the payload,
    /// which is the text a screen reader needs to replace the image with.
    /// </summary>
    public string AltText { get; }

    /// <summary>The printed symbol: the raster, the resolution and the physical size.</summary>
    public BarcodePrint Print { get; }

    /// <summary>The PNG to register under <see cref="AssetName"/>.</summary>
    public byte[] Png => Print.Png;

    /// <summary>Width of the printed symbol, in millimetres.</summary>
    public double WidthMillimetres => Print.WidthMillimetres;

    /// <summary>Height of the printed symbol, in millimetres.</summary>
    public double HeightMillimetres => Print.HeightMillimetres;
}
