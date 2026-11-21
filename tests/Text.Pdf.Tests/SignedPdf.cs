using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>Reads a produced PDF the way a reader does: through its byte range and its CMS.</summary>
internal static class SignedPdf
{
    /// <summary>The file, one character per byte.</summary>
    public static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>Every <c>/ByteRange</c> in the file, oldest first.</summary>
    public static List<long[]> Ranges(byte[] pdf)
    {
        var ranges = new List<long[]>();

        foreach (Match match in Regex.Matches(Text(pdf), @"/ByteRange\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]"))
        {
            ranges.Add(
            [
                long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
            ]);
        }

        return ranges;
    }

    /// <summary>The bytes a signature covers: the two ranges, joined.</summary>
    public static byte[] Covered(byte[] pdf, long[] range)
    {
        var content = new byte[range[1] + range[3]];
        Buffer.BlockCopy(pdf, (int)range[0], content, 0, (int)range[1]);
        Buffer.BlockCopy(pdf, (int)range[2], content, (int)range[1], (int)range[3]);

        return content;
    }

    /// <summary>The DER structure held by the <paramref name="index"/>-th <c>/Contents</c>, unpadded.</summary>
    public static byte[] Contents(byte[] pdf, int index = 0)
    {
        var matches = Regex.Matches(Text(pdf), @"/Contents\s*<([0-9A-Fa-f]+)>");
        var hex = matches[index].Groups[1].Value;

        if (hex.Length % 2 != 0)
        {
            hex = hex.Substring(0, hex.Length - 1);
        }

        var raw = Convert.FromHexString(hex);
        var total = DerLength(raw);

        return total > 0 && total <= raw.Length ? raw.AsSpan(0, total).ToArray() : raw;
    }

    /// <summary>The CMS a signature carries, decoded.</summary>
    public static SignedCms Cms(byte[] pdf, int index = 0)
    {
        var ranges = Ranges(pdf);
        var cms = new SignedCms(new ContentInfo(Covered(pdf, ranges[index])), detached: true);
        cms.Decode(Contents(pdf, index));

        return cms;
    }

    private static int DerLength(byte[] raw)
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
