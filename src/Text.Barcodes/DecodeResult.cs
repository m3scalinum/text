using System.Collections.Generic;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// The outcome of reading one image: the text, the symbology, the points the detector found, and —
/// when nothing was found — why.
/// </summary>
/// <remarks>
/// A page without a barcode is this object with <see cref="Success"/> false, not an exception: a
/// caller scanning a stack of documents has no use for a throw per blank page.
/// </remarks>
public sealed class DecodeResult
{
    internal DecodeResult(
        bool success,
        string text,
        BarcodeSymbology? symbology,
        string format,
        IReadOnlyList<BarcodePoint> points,
        string? error)
    {
        Success = success;
        Text = text;
        Symbology = symbology;
        Format = format;
        Points = points;
        Error = error;
    }

    /// <summary>Whether a symbol was found and read.</summary>
    public bool Success { get; }

    /// <summary>The decoded text, empty when <see cref="Success"/> is false.</summary>
    public string Text { get; }

    /// <summary>
    /// The symbology, when the format is one <see cref="BarcodeSymbology"/> names. A symbol read in a
    /// format this package does not encode — a UPC-A, say — leaves this null and sets
    /// <see cref="Format"/>.
    /// </summary>
    public BarcodeSymbology? Symbology { get; }

    /// <summary>
    /// The format the engine detected, in the engine's own spelling (<c>QR_CODE</c>,
    /// <c>EAN_13</c>…), empty when <see cref="Success"/> is false.
    /// </summary>
    public string Format { get; }

    /// <summary>The points the detector reported, in pixels of the input image.</summary>
    public IReadOnlyList<BarcodePoint> Points { get; }

    /// <summary>Why nothing was decoded, or <see langword="null"/> when a symbol was read.</summary>
    public string? Error { get; }

    /// <summary>A readable rendering, for logs and for a failing assertion.</summary>
    /// <returns>The text when a symbol was read, the failure reason otherwise.</returns>
    public override string ToString()
        => Success ? $"{Format}: {Text}" : $"no barcode: {Error}";
}
