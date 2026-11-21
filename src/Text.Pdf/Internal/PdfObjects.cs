using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>A value read from a PDF file.</summary>
internal abstract class PdfValue
{
    /// <summary>The PDF <c>null</c> object.</summary>
    public static readonly PdfValue Null = new PdfNullValue();
}

/// <summary>The PDF <c>null</c> object.</summary>
internal sealed class PdfNullValue : PdfValue
{
    /// <inheritdoc />
    public override string ToString() => "null";
}

/// <summary>A boolean.</summary>
internal sealed class PdfBoolean(bool value) : PdfValue
{
    /// <summary>The value.</summary>
    public bool Value { get; } = value;

    /// <inheritdoc />
    public override string ToString() => Value ? "true" : "false";
}

/// <summary>A number, integer or real.</summary>
internal sealed class PdfNumber(double value) : PdfValue
{
    /// <summary>The value.</summary>
    public double Value { get; } = value;

    /// <summary>Whether the number is an integer, which references require.</summary>
    public bool IsInteger => Value >= long.MinValue && Value <= long.MaxValue && Math.Floor(Value) == Value;

    /// <summary>The value as an integer, rounded toward zero.</summary>
    public long AsInteger => (long)Value;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>A name, without its leading slash.</summary>
internal sealed class PdfName(string value) : PdfValue
{
    /// <summary>The name's text, without the slash.</summary>
    public string Value { get; } = value;

    /// <inheritdoc />
    public override string ToString() => "/" + Value;
}

/// <summary>A string, literal or hexadecimal.</summary>
internal sealed class PdfString(byte[] bytes) : PdfValue
{
    /// <summary>The decoded bytes.</summary>
    public byte[] Bytes { get; } = bytes;

    /// <summary>The bytes read as Latin-1, which is how a PDF string is usually compared.</summary>
    public string Text => Encoding.Latin1.GetString(Bytes);

    /// <summary>
    /// Interprets the string as a PDF text string: UTF-16BE when it starts with a byte-order mark,
    /// PDFDocEncoding otherwise, approximated by Latin-1.
    /// </summary>
    public string Decode()
    {
        if (Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
        }

        return Text;
    }
}

/// <summary>An array.</summary>
internal sealed class PdfArray : PdfValue
{
    /// <summary>The items, in order.</summary>
    public List<PdfValue> Items { get; } = [];

    /// <inheritdoc />
    public override string ToString() => $"[{Items.Count} items]";
}

/// <summary>A dictionary.</summary>
internal sealed class PdfDictionary : PdfValue
{
    /// <summary>The entries, keyed by name without the slash.</summary>
    public Dictionary<string, PdfValue> Entries { get; } = new(StringComparer.Ordinal);

    /// <summary>The value of <paramref name="key"/>, or <see langword="null"/>.</summary>
    public PdfValue? this[string key] => Entries.TryGetValue(key, out var value) ? value : null;

    /// <summary>Whether the entry exists.</summary>
    public bool ContainsKey(string key) => Entries.ContainsKey(key);

    /// <summary>Whether <c>/Type</c> names <paramref name="type"/>.</summary>
    public bool IsType(string type) => this["Type"] is PdfName name && string.Equals(name.Value, type, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => $"<{Entries.Count} entries>";
}

/// <summary>An indirect reference.</summary>
internal sealed class PdfReference(int number) : PdfValue
{
    /// <summary>The object number.</summary>
    public int Number { get; } = number;

    /// <inheritdoc />
    public override string ToString() => $"{Number} 0 R";
}

/// <summary>A stream: a dictionary and its bytes.</summary>
internal sealed class PdfStream(PdfDictionary dictionary, byte[] data) : PdfValue
{
    /// <summary>The stream dictionary.</summary>
    public PdfDictionary Dictionary { get; } = dictionary;

    /// <summary>The bytes between <c>stream</c> and <c>endstream</c>, still encoded.</summary>
    public byte[] Data { get; } = data;

    /// <inheritdoc />
    public override string ToString() => $"stream of {Data.Length} bytes";
}

/// <summary>The file is not a PDF this reader can parse, beyond what a report can describe.</summary>
internal sealed class PdfFormatException(string message) : Exception(message);
