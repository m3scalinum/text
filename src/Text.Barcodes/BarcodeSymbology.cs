using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The symbologies this package writes and reads — every one rxing can encode, which is also every
/// one it can decode from a PNG.
/// </summary>
/// <remarks>
/// The set is closed on purpose: an enum value is a promise that both directions work, and each one
/// here is covered by an encode→PNG→decode round trip in the test suite.
/// </remarks>
public enum BarcodeSymbology
{
    /// <summary>QR Code: square, high capacity, error correction L, M, Q or H.</summary>
    QrCode = 0,

    /// <summary>DataMatrix (ECC 200): square or rectangular, the symbology of GS1 and of the
    /// invoice headers.</summary>
    DataMatrix = 1,

    /// <summary>Aztec: a bullseye finder, no quiet zone needed, good for a dense payload on a
    /// small label. Needs at least two pixels per module to be read back.</summary>
    Aztec = 2,

    /// <summary>PDF417: stacked rows, the symbology of identity documents and boarding passes.</summary>
    Pdf417 = 3,

    /// <summary>Code 128: full ASCII, compact, the default for a serial number or a tracking
    /// reference.</summary>
    Code128 = 4,

    /// <summary>Code 39: uppercase letters, digits and a few symbols; wide bars, low density, the
    /// symbology industry has used longest.</summary>
    Code39 = 5,

    /// <summary>EAN-13: thirteen digits, the check digit included. Twelve digits is refused rather
    /// than completed.</summary>
    Ean13 = 6,

    /// <summary>EAN-8: eight digits, the check digit included.</summary>
    Ean8 = 7,

    /// <summary>ITF (Interleaved 2 of 5): digits only, an even number of them.</summary>
    Itf = 8,

    /// <summary>Codabar: digits and a few symbols, framed by a start and a stop character
    /// (<c>A</c>…<c>B</c>), which a reader strips.</summary>
    Codabar = 9,

    /// <summary>GS1-128: Code 128 with FNC1 in lead position and a field separator after every
    /// variable-length value. Encode it from a <see cref="Gs1Data"/>; a decoded one comes back
    /// with its symbology identifier stripped and its field separators kept.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "GS1-128 is the name of the specification; renaming the member would hide it.")]
    Gs1_128 = 10,

    /// <summary>UPC-A: twelve digits, the check digit included, read back as twelve digits.</summary>
    UpcA = 11,

    /// <summary>UPC-E: eight digits, number system 0 or 1, the check digit included. Needs the
    /// nine-module quiet zone its specification gives it, which is the default here.</summary>
    UpcE = 12,

    /// <summary>Code 93: full ASCII, denser than Code 39, two check characters the writer
    /// computes.</summary>
    Code93 = 13,

    /// <summary>Telepen: full ASCII in a sixteen-module alphabet per character.</summary>
    Telepen = 14,

    /// <summary>GS1 DataBar Omnidirectional (RSS-14). Read only: the engine has no writer for
    /// it, so it is decoded but never encoded.</summary>
    DataBarOmnidirectional = 15,

    /// <summary>GS1 DataBar Expanded (RSS Expanded). Read only, for the same reason.</summary>
    DataBarExpanded = 16,

    /// <summary>MaxiCode. Read only: the engine has no writer for it.</summary>
    MaxiCode = 17,

    /// <summary>Micro QR Code, and its rectangular variant. Read only: the engine has no writer
    /// for it.</summary>
    MicroQr = 18,
}

/// <summary>The spellings of <see cref="BarcodeSymbology"/> across the native ABI.</summary>
internal static class BarcodeSymbologyNames
{
    /// <summary>The ABI name, which the native library parses case-insensitively.</summary>
    public static string ToAbiName(this BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.QrCode => "QRCode",
        BarcodeSymbology.DataMatrix => "DataMatrix",
        BarcodeSymbology.Aztec => "Aztec",
        BarcodeSymbology.Pdf417 => "PDF417",
        BarcodeSymbology.Code128 => "Code128",
        BarcodeSymbology.Code39 => "Code39",
        BarcodeSymbology.Ean13 => "EAN13",
        BarcodeSymbology.Ean8 => "EAN8",
        BarcodeSymbology.Itf => "ITF",
        BarcodeSymbology.Codabar => "Codabar",
        BarcodeSymbology.Gs1_128 => "GS1-128",
        BarcodeSymbology.UpcA => "UPCA",
        BarcodeSymbology.UpcE => "UPCE",
        BarcodeSymbology.Code93 => "Code93",
        BarcodeSymbology.Telepen => "Telepen",
        BarcodeSymbology.DataBarOmnidirectional => "DataBarOmnidirectional",
        BarcodeSymbology.DataBarExpanded => "DataBarExpanded",
        BarcodeSymbology.MaxiCode => "MaxiCode",
        BarcodeSymbology.MicroQr => "MicroQR",
        _ => throw new BarcodeUnsupportedException($"unknown symbology '{symbology}'"),
    };

    /// <summary>The symbology an ABI name denotes, or <see langword="null"/> for an empty name.</summary>
    public static BarcodeSymbology? FromAbiName(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        foreach (var symbology in Enum.GetValues<BarcodeSymbology>())
        {
            if (string.Equals(symbology.ToAbiName(), name, StringComparison.OrdinalIgnoreCase))
            {
                return symbology;
            }
        }

        return null;
    }
}
