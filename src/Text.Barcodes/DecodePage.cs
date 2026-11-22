using System.Collections.Generic;

namespace M3scalinum.Text.Barcodes;

/// <summary>Every barcode one image holds, in the order the engine found them.</summary>
/// <remarks>
/// <para>
/// A delivery note carries one symbol per line item, so "the barcode" of a page is a list, possibly
/// empty. This is that list, with the image size the engine decoded, and — when the list is empty —
/// why nothing was found.
/// </para>
/// <para>
/// An image without a symbol is this object with <see cref="Success"/> false, never an exception:
/// scanning a stack of documents should not throw once per blank page.
/// </para>
/// </remarks>
public sealed class DecodePage
{
    internal DecodePage(IReadOnlyList<DecodeResult> symbols, int width, int height, string? error)
    {
        Symbols = symbols;
        Width = width;
        Height = height;
        Error = error;
    }

    /// <summary>The symbols found, each with its text, symbology, points and bounding box.</summary>
    public IReadOnlyList<DecodeResult> Symbols { get; }

    /// <summary>How many symbols the page holds.</summary>
    public int Count => Symbols.Count;

    /// <summary>Whether at least one symbol was found and read.</summary>
    public bool Success => Symbols.Count > 0;

    /// <summary>Width of the decoded image, in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the decoded image, in pixels.</summary>
    public int Height { get; }

    /// <summary>Why nothing was decoded, or <see langword="null"/> when a symbol was read.</summary>
    public string? Error { get; }

    /// <summary>A readable rendering, for logs and for a failing assertion.</summary>
    /// <returns>The symbols one per line, or the reason there are none.</returns>
    public override string ToString()
        => Success
            ? string.Join("; ", Symbols)
            : $"no barcode: {Error}";
}
