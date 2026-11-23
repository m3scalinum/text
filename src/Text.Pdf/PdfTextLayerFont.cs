using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Builds the font program an invisible text layer embeds: a TrueType font with one glyph per
/// character the layer draws and no outline in any of them.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Type0</c> font requires a CID font, and a CID font this package can ship requires an embedded
/// font program; the repository carries no font file to subset. What the layer needs from the program
/// is a glyph identifier for every character and an advance for every glyph — the characters are never
/// painted (<c>3 Tr</c>), so an outline would never be seen. This builds exactly that: a valid
/// TrueType font whose glyphs are empty and whose advance is <see cref="Advance"/> of
/// <see cref="UnitsPerEm"/> for every one of them.
/// </para>
/// <para>
/// The result is a real sfnt: nine tables (<c>cmap</c>, <c>glyf</c>, <c>head</c>, <c>hhea</c>,
/// <c>hmtx</c>, <c>loca</c>, <c>maxp</c>, <c>name</c>, <c>post</c>), correct checksums, and a
/// <c>head.checkSumAdjustment</c> that makes the whole file sum to <c>0xB1B0AFBA</c>. A reader that
/// validates the program accepts it; a reader that shapes the text finds no ink, which is the point.
/// </para>
/// </remarks>
internal static class PdfTextLayerFont
{
    /// <summary>Font units per em. 1000 keeps the advance arithmetic identical to a Type1 font's.</summary>
    public const int UnitsPerEm = 1000;

    /// <summary>The advance of every glyph, in font units: half an em.</summary>
    public const int Advance = 500;

    /// <summary>The ascender the font declares, in font units.</summary>
    public const int Ascent = 800;

    /// <summary>The descender the font declares, in font units.</summary>
    public const int Descent = -200;

    /// <summary>The family name the font declares.</summary>
    public const string FamilyName = "M3scalinum Text Layer";

    /// <summary>The checksum every sfnt file sums to, with its adjustment applied.</summary>
    private const uint Magic = 0xB1B0AFBA;

    /// <summary>
    /// The creation and modification time the program declares, in seconds since 1904: the first of
    /// January 2026. A fixed value keeps the same document and the same words producing the same
    /// bytes, which is what lets a test assert on the program it built.
    /// </summary>
    private const uint Created = 3_850_070_400;

    /// <summary>
    /// Builds the program for <paramref name="glyphCount"/> glyphs, glyph 0 being <c>.notdef</c> and
    /// every other glyph empty.
    /// </summary>
    /// <param name="glyphCount">How many glyphs the font holds, including <c>.notdef</c>.</param>
    /// <returns>The bytes of a TrueType font file.</returns>
    public static byte[] Build(int glyphCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(glyphCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(glyphCount, ushort.MaxValue);

        // The directory is alphabetical, as the format requires.
        (string Tag, byte[] Data)[] tables =
        [
            ("cmap", Cmap()),
            ("glyf", new byte[4]),
            ("head", Head()),
            ("hhea", Hhea(glyphCount)),
            ("hmtx", Hmtx(glyphCount)),
            ("loca", Loca(glyphCount)),
            ("maxp", Maxp(glyphCount)),
            ("name", Name()),
            ("post", Post()),
        ];

        return Assemble(tables);
    }

    /// <summary>The <c>head</c> table: the font's global metrics, with the checksum adjustment still zero.</summary>
    private static byte[] Head()
    {
        var sink = new Sink();

        sink.U32(0x00010000);           // version
        sink.U32(0x00010000);           // fontRevision
        sink.U32(0);                    // checkSumAdjustment, filled in once the file is assembled
        sink.U32(0x5F0F3CF5);           // magicNumber
        sink.U16(0x0003);               // flags: baseline at y=0, left side bearing at x=0
        sink.U16(UnitsPerEm);
        sink.U32(0);                    // created, high word
        sink.U32(Created);              // created, low word
        sink.U32(0);                    // modified, high word
        sink.U32(Created);
        sink.I16(0);                    // xMin
        sink.I16(0);                    // yMin
        sink.I16(Advance);              // xMax
        sink.I16(Ascent);               // yMax
        sink.U16(0);                    // macStyle
        sink.U16(8);                    // lowestRecPPEM
        sink.I16(2);                    // fontDirectionHint
        sink.I16(0);                    // indexToLocFormat: short offsets
        sink.I16(0);                    // glyphDataFormat

        return sink.ToArray();
    }

    /// <summary>The <c>hhea</c> table: the horizontal metrics every glyph shares.</summary>
    /// <param name="glyphCount">How many glyphs the font holds.</param>
    private static byte[] Hhea(int glyphCount)
    {
        var sink = new Sink();

        sink.U32(0x00010000);           // version
        sink.I16(Ascent);               // ascender
        sink.I16(Descent);              // descender
        sink.I16(0);                    // lineGap
        sink.U16(Advance);              // advanceWidthMax
        sink.I16(0);                    // minLeftSideBearing
        sink.I16(0);                    // minRightSideBearing
        sink.I16(Advance);              // xMaxExtent
        sink.I16(1);                    // caretSlopeRise
        sink.I16(0);                    // caretSlopeRun
        sink.I16(0);                    // caretOffset
        sink.I16(0);                    // reserved
        sink.I16(0);
        sink.I16(0);
        sink.I16(0);
        sink.I16(0);                    // metricDataFormat
        sink.U16(glyphCount);           // numberOfHMetrics

        return sink.ToArray();
    }

    /// <summary>The <c>hmtx</c> table: one advance and one side bearing per glyph.</summary>
    /// <param name="glyphCount">How many glyphs the font holds.</param>
    private static byte[] Hmtx(int glyphCount)
    {
        var sink = new Sink();

        for (var index = 0; index < glyphCount; index++)
        {
            sink.U16(Advance);
            sink.I16(0);
        }

        return sink.ToArray();
    }

    /// <summary>The <c>loca</c> table: every glyph starts where the previous one ended, so all are empty.</summary>
    /// <param name="glyphCount">How many glyphs the font holds.</param>
    private static byte[] Loca(int glyphCount)
    {
        var sink = new Sink();

        for (var index = 0; index <= glyphCount; index++)
        {
            sink.U16(0);
        }

        return sink.ToArray();
    }

    /// <summary>The <c>maxp</c> table, version 1.0, which a reader expects even when a font has no outlines.</summary>
    /// <param name="glyphCount">How many glyphs the font holds.</param>
    private static byte[] Maxp(int glyphCount)
    {
        var sink = new Sink();

        sink.U32(0x00010000);
        sink.U16(glyphCount);
        sink.U16(0);                    // maxPoints
        sink.U16(0);                    // maxContours
        sink.U16(0);                    // maxCompositePoints
        sink.U16(0);                    // maxCompositeContours
        sink.U16(1);                    // maxZones
        sink.U16(0);                    // maxTwilightPoints
        sink.U16(0);                    // maxStorage
        sink.U16(0);                    // maxFunctionDefs
        sink.U16(0);                    // maxInstructionDefs
        sink.U16(0);                    // maxStackElements
        sink.U16(0);                    // maxSizeOfInstructions
        sink.U16(0);                    // maxComponentElements
        sink.U16(0);                    // maxComponentDepth

        return sink.ToArray();
    }

    /// <summary>
    /// The <c>cmap</c> table: one format 4 subtable whose only segment maps <c>U+FFFF</c> to glyph 0,
    /// which is what a font whose glyphs are reachable by glyph identifier alone declares.
    /// </summary>
    private static byte[] Cmap()
    {
        var sink = new Sink();

        sink.U16(0);                    // version
        sink.U16(1);                    // numTables
        sink.U16(3);                    // platformID: Windows
        sink.U16(1);                    // encodingID: Unicode BMP
        sink.U32(12);                   // offset of the subtable

        sink.U16(4);                    // format
        sink.U16(24);                   // length
        sink.U16(0);                    // language
        sink.U16(2);                    // segCountX2
        sink.U16(2);                    // searchRange
        sink.U16(0);                    // entrySelector
        sink.U16(0);                    // rangeShift
        sink.U16(0xFFFF);               // endCode[0], the segment the format requires
        sink.U16(0);                    // reservedPad
        sink.U16(0xFFFF);               // startCode[0]
        sink.U16(1);                    // idDelta[0]: 0xFFFF maps to glyph 0
        sink.U16(0);                    // idRangeOffset[0]

        return sink.ToArray();
    }

    /// <summary>The <c>name</c> table: one Windows Unicode record holding the family name.</summary>
    private static byte[] Name()
    {
        var family = System.Text.Encoding.BigEndianUnicode.GetBytes(FamilyName);
        var sink = new Sink();

        sink.U16(0);                    // format
        sink.U16(1);                    // count
        sink.U16(6 + 12);               // stringOffset
        sink.U16(3);                    // platformID: Windows
        sink.U16(1);                    // encodingID: Unicode BMP
        sink.U16(0x0409);               // languageID: en-US
        sink.U16(1);                    // nameID: family
        sink.U16((ushort)family.Length);
        sink.U16(0);                    // offset of the string from stringOffset

        foreach (var value in family)
        {
            sink.U8(value);
        }

        return sink.ToArray();
    }

    /// <summary>The <c>post</c> table, version 3.0: no glyph names, which is what a subset font declares.</summary>
    private static byte[] Post()
    {
        var sink = new Sink();

        sink.U32(0x00030000);
        sink.I32(0);                    // italicAngle
        sink.I16(0);                    // underlinePosition
        sink.I16(0);                    // underlineThickness
        sink.U32(0);                    // isFixedPitch
        sink.U32(0);                    // minMemType42
        sink.U32(0);                    // maxMemType42
        sink.U32(0);                    // minMemType1
        sink.U32(0);                    // maxMemType1

        return sink.ToArray();
    }

    /// <summary>Lays the tables out after one directory, then applies the checksum adjustment.</summary>
    /// <param name="tables">The tables, in directory order.</param>
    private static byte[] Assemble((string Tag, byte[] Data)[] tables)
    {
        const int HeaderLength = 12;
        const int EntryLength = 16;

        var directory = HeaderLength + (EntryLength * tables.Length);
        var offsets = new int[tables.Length];
        var length = directory;

        for (var index = 0; index < tables.Length; index++)
        {
            offsets[index] = length;
            length += Align(tables[index].Data.Length);
        }

        var font = new byte[length];
        var sink = new Sink(font);

        sink.U32(0x00010000);           // sfntVersion
        sink.U16((ushort)tables.Length);

        var selector = 0;
        while (1 << (selector + 1) <= tables.Length)
        {
            selector++;
        }

        var searchRange = 16 * (1 << selector);
        sink.U16((ushort)searchRange);
        sink.U16((ushort)selector);
        sink.U16((ushort)((tables.Length * 16) - searchRange));

        for (var index = 0; index < tables.Length; index++)
        {
            foreach (var tag in tables[index].Tag)
            {
                sink.U8((byte)tag);
            }

            sink.U32(Checksum(tables[index].Data, tables[index].Data.Length));
            sink.U32((uint)offsets[index]);
            sink.U32((uint)tables[index].Data.Length);
        }

        for (var index = 0; index < tables.Length; index++)
        {
            sink.Position = offsets[index];
            sink.Bytes(tables[index].Data);
        }

        var adjustment = unchecked(Magic - Checksum(font, 0, font.Length));
        var head = 0;
        while (tables[head].Tag != "head")
        {
            head++;
        }

        var at = offsets[head] + 8;
        font[at] = (byte)(adjustment >> 24);
        font[at + 1] = (byte)(adjustment >> 16);
        font[at + 2] = (byte)(adjustment >> 8);
        font[at + 3] = (byte)adjustment;

        return font;
    }

    /// <summary>The sfnt checksum: the sum of the big-endian 32-bit words, with the tail padded with zeroes.</summary>
    /// <param name="data">The bytes to sum.</param>
    /// <param name="start">Where to start.</param>
    /// <param name="length">How many bytes to read.</param>
    private static uint Checksum(ReadOnlySpan<byte> data, int start, int length)
    {
        var sum = 0u;

        for (var index = start; index < start + length; index += 4)
        {
            var word = 0u;

            for (var offset = 0; offset < 4; offset++)
            {
                word <<= 8;
                if (index + offset < start + length)
                {
                    word |= data[index + offset];
                }
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }

    /// <summary>A table's checksum, over its own bytes.</summary>
    /// <param name="data">The table.</param>
    /// <param name="length">Its length.</param>
    private static uint Checksum(byte[] data, int length) => Checksum(data, 0, length);

    /// <summary>Rounds a table length up to the four-byte boundary the format requires.</summary>
    /// <param name="length">The length.</param>
    private static int Align(int length) => (length + 3) & ~3;

    /// <summary>A big-endian writer over a growable list or over a fixed buffer.</summary>
    private sealed class Sink
    {
        private readonly List<byte> _bytes = [];
        private readonly byte[]? _fixed;

        public Sink()
        {
        }

        public Sink(byte[] buffer) => _fixed = buffer;

        /// <summary>Where the next byte goes in a fixed buffer; ignored by a growable one.</summary>
        public int Position { get; set; }

        public void U8(byte value)
        {
            if (_fixed is null)
            {
                _bytes.Add(value);
                return;
            }

            _fixed[Position++] = value;
        }

        public void U16(int value)
        {
            U8((byte)(value >> 8));
            U8((byte)value);
        }

        public void I16(int value) => U16(value & 0xFFFF);

        public void U32(uint value)
        {
            U8((byte)(value >> 24));
            U8((byte)(value >> 16));
            U8((byte)(value >> 8));
            U8((byte)value);
        }

        public void U32(int value) => U32((uint)value);

        public void I32(int value) => U32((uint)value);

        public void Bytes(ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
            {
                U8(value);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
