using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Reads a produced PDF through its object graph and its byte range, never through a regex over the
/// file as text.
/// </summary>
/// <remarks>
/// The <c>/Contents</c> hex string is fixed-width and zero-padded, so trimming zeros from it — as an
/// earlier helper did — cuts real bytes whenever the DER happens to end in <c>0x00</c>, which is one
/// signature in a hundred and twenty-eight. Here the bytes are located from the byte range the
/// signature declares, and the length comes from the ASN.1 header.
/// </remarks>
internal static class PdfLtaSignatureSlice
{
    /// <summary>The signature dictionaries of the file, oldest first, as the object table holds them.</summary>
    public static IReadOnlyList<PdfSignatureDictionary> Dictionaries(byte[] pdf)
        => PdfDocument.Parse(Encoding.Latin1.GetString(pdf)).SignatureDictionaries();

    /// <summary>
    /// Checks that the <c>/Contents</c> of <paramref name="dictionary"/> sits exactly in the gap
    /// between the two ranges, and returns the hex string that fills it.
    /// </summary>
    public static string Hex(byte[] pdf, PdfSignatureDictionary dictionary)
    {
        var range = dictionary.ByteRange;

        // The gap a signature leaves is its own /Contents: '<' where the first range stops, '>'
        // where the second one starts.
        Assert.True(range[1] >= 0 && range[2] > range[1] && range[2] <= pdf.Length, "the byte range does not fit the file");
        Assert.Equal((byte)'<', pdf[range[1]]);
        Assert.Equal((byte)'>', pdf[range[2] - 1]);

        Assert.Equal(range[1] + 1, dictionary.ContentsStart);
        Assert.Equal(range[2], dictionary.ContentsStart + dictionary.ContentsLength + 1);

        return Encoding.Latin1.GetString(pdf, dictionary.ContentsStart, dictionary.ContentsLength);
    }

    /// <summary>The bytes a signature covers: the two ranges, joined.</summary>
    public static byte[] Covered(byte[] pdf, PdfSignatureDictionary dictionary)
    {
        var range = dictionary.ByteRange;
        var content = new byte[range[1] + range[3]];

        Buffer.BlockCopy(pdf, (int)range[0], content, 0, (int)range[1]);
        Buffer.BlockCopy(pdf, (int)range[2], content, (int)range[1], (int)range[3]);

        return content;
    }

    /// <summary>The DER structure held by <c>/Contents</c>, cut to the length its header declares.</summary>
    public static byte[] Contents(byte[] pdf, int index = 0) => Contents(pdf, Dictionaries(pdf)[index]);

    /// <summary>The DER structure held by the <c>/Contents</c> of <paramref name="dictionary"/>.</summary>
    public static byte[] Contents(byte[] pdf, PdfSignatureDictionary dictionary)
    {
        var raw = Convert.FromHexString(Hex(pdf, dictionary));
        var declared = DeclaredLength(raw);

        Assert.True(declared > 0 && declared <= raw.Length, $"the /Contents header declares {declared} bytes of {raw.Length}");

        return raw.AsSpan(0, declared).ToArray();
    }

    /// <summary>The length the DER header at byte zero declares, or -1 when there is no valid one.</summary>
    public static int DeclaredLength(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 2 || raw[0] != 0x30)
        {
            return -1;
        }

        var first = raw[1];
        if ((first & 0x80) == 0)
        {
            return 2 + first;
        }

        var octets = first & 0x7F;
        if (octets is 0 or > 4 || raw.Length < 2 + octets)
        {
            return -1;
        }

        var length = 0;
        for (var index = 0; index < octets; index++)
        {
            length = (length << 8) | raw[2 + index];
        }

        return 2 + octets + length;
    }
}
