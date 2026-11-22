using System;
using System.Collections.Generic;
using System.Text.Json;
using M3scalinum.Text.Barcodes.Internal;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// A GS1 element string: the Application Identifiers a label carries, parsed into named fields.
/// </summary>
/// <remarks>
/// <para>
/// This is the structure a symbology does not carry on its own. <see cref="Parse"/> reads the raw
/// form a reader returns (<c>0109501101530003</c> with ASCII 29 separators), the same form with a
/// symbol identifier in front (<c>]C1…</c>), or the printed form a label shows,
/// <c>(01)09501101530003(10)LOT-42</c>. <see cref="ToDigitalLink"/> and
/// <see cref="FromDigitalLink"/> are the URL serialisation.
/// </para>
/// <para>
/// Nothing is guessed: an identifier this build does not know, a value outside the length the
/// standard gives its identifier, or a check digit that does not match is a
/// <see cref="BarcodeGs1Exception"/>.
/// </para>
/// </remarks>
public sealed class Gs1Data
{
    private Gs1Data(IReadOnlyList<Gs1Element> elements, string raw, string humanReadable)
    {
        Elements = elements;
        Raw = raw;
        HumanReadable = humanReadable;
    }

    /// <summary>The elements, in the order the data carries them.</summary>
    public IReadOnlyList<Gs1Element> Elements { get; }

    /// <summary>
    /// The canonical raw form: identifiers and values concatenated, with a field separator (ASCII
    /// 29) after every value whose length is not fixed. This is what a GS1-128 carries, without
    /// the leading FNC1.
    /// </summary>
    public string Raw { get; }

    /// <summary>The printed form, <c>(01)09501101530003(10)LOT-42</c>.</summary>
    public string HumanReadable { get; }

    /// <summary>Parses a GS1 element string.</summary>
    /// <param name="data">The raw form, the raw form with a symbol identifier in front, or the
    /// printed form.</param>
    /// <returns>The parsed elements, with both serialisations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="BarcodeGs1Exception">The data is not a GS1 element string this build
    /// understands.</exception>
    public static Gs1Data Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            return FromJson(BarcodeNative.ParseGs1(data));
        }
        catch (NativeCallException error)
        {
            throw Barcode.TranslateGs1(error);
        }
    }

    /// <summary>Parses a GS1 Digital Link URL back into its elements.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL whose path carries the
    /// primary keys and whose query carries the rest.</param>
    /// <returns>The parsed elements, with both serialisations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is null.</exception>
    /// <exception cref="BarcodeGs1Exception">The URL is not a Digital Link this build
    /// understands.</exception>
    public static Gs1Data FromDigitalLink(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        try
        {
            return FromJson(BarcodeNative.FromDigitalLink(url));
        }
        catch (NativeCallException error)
        {
            throw Barcode.TranslateGs1(error);
        }
    }

    /// <summary>Builds the Digital Link URL for these elements.</summary>
    /// <param name="baseUrl">The resolver the URL hangs off, an absolute <c>http</c> or
    /// <c>https</c> URL without a query or a fragment. A path is allowed: it prefixes the
    /// identifiers.</param>
    /// <returns>The URL: primary keys in the path, every other element as a query parameter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseUrl"/> is null.</exception>
    /// <exception cref="BarcodeGs1Exception">The base is not an absolute URL, or the elements hold
    /// no primary key to identify the item.</exception>
    public string ToDigitalLink(string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        try
        {
            return BarcodeNative.DigitalLink(Raw, baseUrl);
        }
        catch (NativeCallException error)
        {
            throw Barcode.TranslateGs1(error);
        }
    }

    /// <summary>The printed form, so a log line reads the way the label does.</summary>
    /// <returns>The same string as <see cref="HumanReadable"/>.</returns>
    public override string ToString() => HumanReadable;

    /// <summary>Reads the JSON the native parser returns.</summary>
    internal static Gs1Data FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var elements = new List<Gs1Element>();
        foreach (var element in root.GetProperty("elements").EnumerateArray())
        {
            elements.Add(new Gs1Element(
                element.GetProperty("ai").GetString() ?? string.Empty,
                element.GetProperty("title").GetString() ?? string.Empty,
                element.GetProperty("value").GetString() ?? string.Empty,
                element.GetProperty("primaryKey").GetBoolean()));
        }

        return new Gs1Data(
            elements,
            root.GetProperty("raw").GetString() ?? string.Empty,
            root.GetProperty("humanReadable").GetString() ?? string.Empty);
    }
}
