using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The <c>WinAnsiEncoding</c> code-to-Unicode table, and the <c>/ToUnicode</c> CMap built from it.
/// </summary>
/// <remarks>
/// <para>
/// PDF/A requires a Unicode mapping for every font that is drawn. A simple font whose bytes are
/// <c>WinAnsiEncoding</c> already has a determinate mapping — the encoding is a published table, not
/// a guess — so writing the CMap states what the file already meant. The codes the encoding leaves
/// undefined (0x81, 0x8D, 0x8F, 0x90, 0x9D and 0x7F) are left out of the CMap rather than mapped to
/// U+FFFD.
/// </para>
/// <para>
/// Nothing else is guessed: a font with <c>/Differences</c>, with an encoding this table does not
/// describe, or with a symbolic built-in encoding is refused by the converter instead.
/// </para>
/// </remarks>
internal static class PdfAWinAnsi
{
    /// <summary>The characters <c>WinAnsiEncoding</c> assigns to 0x80..0x9F.</summary>
    private static readonly Dictionary<int, char> High =
        new()
        {
            [0x80] = '\u20AC',
            [0x82] = '\u201A',
            [0x83] = '\u0192',
            [0x84] = '\u201E',
            [0x85] = '\u2026',
            [0x86] = '\u2020',
            [0x87] = '\u2021',
            [0x88] = '\u02C6',
            [0x89] = '\u2030',
            [0x8A] = '\u0160',
            [0x8B] = '\u2039',
            [0x8C] = '\u0152',
            [0x8E] = '\u017D',
            [0x91] = '\u2018',
            [0x92] = '\u2019',
            [0x93] = '\u201C',
            [0x94] = '\u201D',
            [0x95] = '\u2022',
            [0x96] = '\u2013',
            [0x97] = '\u2014',
            [0x98] = '\u02DC',
            [0x99] = '\u2122',
            [0x9A] = '\u0161',
            [0x9B] = '\u203A',
            [0x9C] = '\u0153',
            [0x9E] = '\u017E',
            [0x9F] = '\u0178',
        };

    /// <summary>The Unicode value of <paramref name="code"/>, or <see langword="null"/> when undefined.</summary>
    public static char? Map(int code)
    {
        if (code is >= 0x20 and <= 0x7E)
        {
            return (char)code;
        }

        if (High.TryGetValue(code, out var high))
        {
            return high;
        }

        // 0xA0..0xFF follows Latin-1 except for the soft hyphen, which is U+00AD as well: the whole
        // range maps to itself.
        return code is >= 0xA0 and <= 0xFF ? (char)code : null;
    }

    /// <summary>Builds the <c>/ToUnicode</c> CMap for a one-byte <c>WinAnsiEncoding</c> font.</summary>
    public static byte[] ToUnicodeCMap()
    {
        var pairs = new List<(int Code, char Value)>(256);
        for (var code = 0; code <= 0xFF; code++)
        {
            if (Map(code) is { } value)
            {
                pairs.Add((code, value));
            }
        }

        var builder = new StringBuilder(pairs.Count * 24);
        builder.Append("/CIDInit /ProcSet findresource begin\n");
        builder.Append("12 dict begin\n");
        builder.Append("begincmap\n");
        builder.Append("/CIDSystemInfo<</Registry(Adobe)/Ordering(UCS)/Supplement 0>>def\n");
        builder.Append("/CMapName/Adobe-Identity-UCS def\n");
        builder.Append("/CMapType 2 def\n");
        builder.Append("1 begincodespacerange\n<00><ff>\nendcodespacerange\n");

        // The format allows at most 100 entries per bfchar block.
        for (var start = 0; start < pairs.Count; start += 100)
        {
            var count = Math.Min(100, pairs.Count - start);
            builder.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");

            for (var index = start; index < start + count; index++)
            {
                var (code, value) = pairs[index];
                builder.Append('<')
                    .Append(code.ToString("X2", CultureInfo.InvariantCulture))
                    .Append("><")
                    .Append(((int)value).ToString("X4", CultureInfo.InvariantCulture))
                    .Append(">\n");
            }

            builder.Append("endbfchar\n");
        }

        builder.Append("endcmap\n");
        builder.Append("CMapName currentdict /CMap defineresource pop\n");
        builder.Append("end\nend\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
