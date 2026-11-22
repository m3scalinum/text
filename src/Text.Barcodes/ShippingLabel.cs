using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Barcodes.Internal;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A one-page logistics label: a GS1-128 SSCC symbol, the same number printed for a human, the
/// delivery address and a parcel reference.
/// </summary>
/// <remarks>
/// <para>
/// This is the document a shipping desk produces in a dedicated labelling tool, brought into the
/// suite: <see cref="Create(string, ShippingLabelOptions)"/> takes a Serial Shipping Container Code
/// — the eighteen digits of AI <c>00</c>, check digit included — and returns a PDF that a label
/// printer prints and a scanner reads back to those eighteen digits.
/// </para>
/// <para>
/// <b>The code.</b> One GS1-128 symbol, at the nominal 0.495 mm X-dimension and 64 modules of bar
/// height unless the options say otherwise, with the 10X quiet zone per side the symbology requires.
/// The symbol is placed by its own geometry: <see cref="Code"/> reports the size it really prints at,
/// and the page centres it, so a wider symbol moves rather than shrinks.
/// </para>
/// <para>
/// <b>The human-readable part.</b> The GS1 element string in its printed form —
/// <c>(00)106141411234567897</c> — under the bars, then the address block, then the reference. The
/// address is text, not data: this class draws it with a standard font and refuses a line it cannot
/// fit rather than clipping one.
/// </para>
/// <para>
/// <b>What it deliberately is not.</b> Not ZPL, and no printer language at all: the label is a PDF a
/// driver renders. No colour, no logo, no second page, no sort or routing logic, and no GS1 AI other
/// than the ones the caller put in the <see cref="Gs1Data"/>. See §12 of the specification.
/// </para>
/// </remarks>
public sealed class ShippingLabel
{
    /// <summary>The most address lines the block holds before the symbol starts.</summary>
    public const int MaximumAddressLines = 5;

    /// <summary>The most characters one address line or the recipient name may hold.</summary>
    public const int MaximumAddressCharacters = 42;

    /// <summary>The most characters the parcel reference may hold.</summary>
    public const int MaximumReferenceCharacters = 32;

    private const double MillimetresToPoints = 72.0 / 25.4;
    private const double MarginMillimetres = 10;

    /// <summary>
    /// The clearance the symbol keeps from the paper's edge, smaller than the text margin: the
    /// symbol brings its own 10X quiet zone, so what it needs from the page is a margin to cut on,
    /// not a second quiet zone. At 100 mm wide this leaves 92 mm, which is a twenty-character
    /// GS1-128 at 0.495 mm and two quiet zones with room to spare.
    /// </summary>
    private const double SymbolClearanceMillimetres = 4;
    private const double LineHeightMillimetres = 5;
    private const double SymbolBottomMillimetres = 28;

    private ShippingLabel(
        Gs1Data data,
        string sscc,
        BarcodePrint code,
        ShippingLabelFormat format,
        byte[] pdf)
    {
        Data = data;
        Sscc = sscc;
        Code = code;
        Format = format;
        Pdf = pdf;
        WidthMillimetres = format.WidthMillimetres();
        HeightMillimetres = format.HeightMillimetres();
    }

    /// <summary>The GS1 data the symbol carries; AI <c>00</c> is always present.</summary>
    public Gs1Data Data { get; }

    /// <summary>The eighteen digits of the SSCC, check digit included, without the identifier.</summary>
    public string Sscc { get; }

    /// <summary>The printed symbol, its resolution and the size it really prints at.</summary>
    public BarcodePrint Code { get; }

    /// <summary>The page the label is laid out on.</summary>
    public ShippingLabelFormat Format { get; }

    /// <summary>The page width, in millimetres.</summary>
    public double WidthMillimetres { get; }

    /// <summary>The page height, in millimetres.</summary>
    public double HeightMillimetres { get; }

    /// <summary>
    /// The label as a one-page PDF. Deterministic: the same SSCC, address and options produce the
    /// same bytes, so a rebuild of a document that embeds labels is reproducible.
    /// </summary>
    public byte[] Pdf { get; }

    /// <summary>The human-readable line printed under the symbol, for example
    /// <c>(00)106141411234567897</c>.</summary>
    public string HumanReadable => Data.HumanReadable;

    /// <summary>Builds a label from an SSCC and the text fields around it.</summary>
    /// <param name="sscc">Eighteen digits: the extension digit, the sixteen-digit body, and the GS1
    /// mod-10 check digit. Seventeen or nineteen digits is refused, and so is a check digit that
    /// does not match — with both digits named.</param>
    /// <param name="options">The page and the text, or <see langword="null"/> for a 100 × 150 mm
    /// page with no recipient, address or reference — which is refused, because a label with no
    /// address is not a label.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sscc"/> is null.</exception>
    /// <exception cref="BarcodeGs1Exception"><paramref name="sscc"/> is not a valid SSCC.</exception>
    /// <exception cref="BarcodeException">An option is out of range, or the symbol and the text do
    /// not fit the page.</exception>
    public static ShippingLabel Create(string sscc, ShippingLabelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sscc);

        if (sscc.Length != 18 || !IsDigits(sscc))
        {
            throw new BarcodeGs1Exception(
                $"SSCC must be eighteen digits — an extension digit, a sixteen-digit body and the GS1 "
                + $"mod-10 check digit — got {sscc.Length} characters: '{sscc}'");
        }

        // The AI is added rather than expected: a caller holds the eighteen digits of a key printed
        // under a barcode, not the element string. Parsing goes through the GS1 table, so a check
        // digit that does not match is refused there, with both digits named.
        return Create(Gs1Data.Parse("00" + sscc), options);
    }

    /// <summary>Builds a label from parsed GS1 data, which must carry AI <c>00</c>.</summary>
    /// <param name="data">The element string the symbol carries.</param>
    /// <param name="options">The page and the text, as for <see cref="Create(string, ShippingLabelOptions)"/>.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="BarcodeGs1Exception"><paramref name="data"/> carries no AI <c>00</c>.</exception>
    /// <exception cref="BarcodeException">An option is out of range, or the symbol and the text do
    /// not fit the page.</exception>
    public static ShippingLabel Create(Gs1Data data, ShippingLabelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sscc = data.Elements
            .FirstOrDefault(element => element.Ai == "00")
            ?? throw new BarcodeGs1Exception(
                "a shipping label is keyed on AI 00 (SSCC), and this GS1 data has no SSCC element");

        options ??= new ShippingLabelOptions();
        options.Validate();

        var code = Barcode.Print(
            BarcodeSymbology.Gs1_128,
            data.Raw,
            new BarcodePrintOptions
            {
                Dpi = options.Dpi,
                ModuleMillimetres = options.ModuleMillimetres,
                BarHeight = options.BarHeightModules,
            });

        var width = options.Format.WidthMillimetres();
        var height = options.Format.HeightMillimetres();
        var symbolWidth = width - (2 * SymbolClearanceMillimetres);

        if (code.WidthMillimetres > symbolWidth)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"the symbol prints {code.WidthMillimetres:0.#} mm wide and the {width:0.#} mm page leaves "
                + $"{symbolWidth:0.#} mm for it; lower ModuleMillimetres, or choose a wider format");
        }

        var pdf = Render(data, code, options, width, height);

        return new ShippingLabel(data, sscc.Value, code, options.Format, pdf);
    }

    /// <summary>
    /// Checks one line of text against the width the template gives it.
    /// </summary>
    /// <param name="line">The text.</param>
    /// <param name="what">What the line is, for the message.</param>
    /// <param name="maximum">The characters it may hold.</param>
    /// <exception cref="BarcodeException">The line is empty, too long, or holds a control
    /// character.</exception>
    internal static void CheckLine(string line, string what, int maximum)
    {
        if (line.Trim().Length == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument, $"a {what} must not be empty");
        }

        if (line.Length > maximum)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"a {what} holds at most {maximum} characters, got {line.Length}: '{line}'");
        }

        foreach (var character in line)
        {
            if (char.IsControl(character))
            {
                throw new BarcodeException(
                    BarcodeErrorCode.InvalidArgument,
                    $"a {what} must not hold a control character: '{line}'");
            }
        }
    }

    private static bool IsDigits(string value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] Render(
        Gs1Data data, BarcodePrint code, ShippingLabelOptions options, double width, double height)
    {
        var page = new PdfLabelPage(width * MillimetresToPoints, height * MillimetresToPoints);

        double X(double millimetres) => millimetres * MillimetresToPoints;
        double Y(double millimetres) => millimetres * MillimetresToPoints;

        var left = X(MarginMillimetres);
        var right = X(width - MarginMillimetres);

        // Header: what the page is, and a rule under it.
        page.DrawText("SHIPPING LABEL", left, Y(height - 10), 7.5, bold: true, grey: 0.35);
        page.DrawRule(left, right, Y(height - 13), grey: 0.55);

        // The delivery address, from the top down.
        var line = height - 24;

        if (options.Recipient is { } recipient)
        {
            page.DrawText(recipient, left, Y(line), 14, bold: true, grey: 0);
            line -= 8;
        }

        foreach (var addressLine in options.AddressLines)
        {
            page.DrawText(addressLine, left, Y(line), 10, bold: false, grey: 0.15);
            line -= LineHeightMillimetres;
        }

        // The parcel reference, above the symbol and clear of the address block.
        if (options.Reference is { } reference)
        {
            page.DrawText("REF", left, Y(84), 7.5, bold: false, grey: 0.35);
            page.DrawText(reference, left, Y(77), 12, bold: true, grey: 0);
        }

        // The symbol: centred, its own width, its baseline the page's bottom margin.
        var symbolX = (width - code.WidthMillimetres) / 2;
        var raster = PngGreyscale.Decode(code.Png);

        page.DrawGreyscaleImage(
            raster.Pixels,
            raster.Width,
            raster.Height,
            X(symbolX),
            Y(SymbolBottomMillimetres),
            X(code.WidthMillimetres),
            Y(code.HeightMillimetres));

        // The machine half, printed for a human: captioned above, the element string below.
        page.DrawText(
            "SSCC (AI 00)",
            left,
            Y(SymbolBottomMillimetres + code.HeightMillimetres + 6),
            7.5,
            bold: false,
            grey: 0.35);

        page.DrawText(
            data.HumanReadable,
            left,
            Y(SymbolBottomMillimetres - 6),
            11,
            bold: true,
            grey: 0);

        return page.Build();
    }
}
