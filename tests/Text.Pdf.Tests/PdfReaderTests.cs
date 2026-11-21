using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The reader's own tests: cross-reference sections, filters and predictors, object streams, and the
/// syntax errors a producer can leave behind. Each test states a small PDF and asserts what the
/// reader reports or returns, so a change in the parser shows up as a changed fact rather than as a
/// changed coverage number.
/// </summary>
public sealed class PdfReaderTests
{
    private static PdfFile Open(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the bytes were not read as a PDF");

        return file!;
    }

    private static PdfConformanceIssue Only(PdfConformanceReport report, string code)
    {
        var matches = report.Issues.Where(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)).ToList();

        Assert.True(matches.Count == 1, $"expected one {code}, found {matches.Count} in {report}");

        return matches[0];
    }

    [Fact]
    public void A_document_without_startxref_cannot_be_located()
    {
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.7\n1 0 obj\n<</Type/Catalog>>\nendobj\n");
        var file = Open(pdf);

        var problem = Assert.Single(file.Problems);
        Assert.Equal(PdfConformanceCodes.XrefInvalid, problem.Code);
        Assert.Contains("no startxref", problem.Message, StringComparison.Ordinal);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);
        Assert.True(report.Has(PdfConformanceCodes.XrefInvalid));
        Assert.True(report.Has(PdfConformanceCodes.CatalogMissing));
    }

    [Fact]
    public void A_startxref_outside_the_file_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Replace(PdfTestDocument.Minimal(), "startxref\n", "startxref\n99"),
            PdfConformanceStandard.Pdf20);

        Assert.Contains(
            "outside a file",
            Only(report, PdfConformanceCodes.XrefInvalid).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_xref_entry_outside_the_file_is_reported()
    {
        var text = Encoding.Latin1.GetString(PdfTestDocument.Minimal());
        var index = text.IndexOf(" 00000 n", StringComparison.Ordinal) - 10;
        var broken = string.Concat(text.AsSpan(0, index), "9999999999", text.AsSpan(index + 10));

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(broken), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            "outside a file",
            Only(report, PdfConformanceCodes.XrefInvalid).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_xref_entry_pointing_at_another_object_is_reported()
    {
        var text = Encoding.Latin1.GetString(PdfTestDocument.Minimal());
        var index = text.IndexOf(" 00000 n", StringComparison.Ordinal) - 10;
        var offset = int.Parse(text.AsSpan(index, 10), CultureInfo.InvariantCulture) + 1;
        var broken = string.Concat(
            text.AsSpan(0, index),
            offset.ToString("D10", CultureInfo.InvariantCulture),
            text.AsSpan(index + 10));

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(broken), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            "does not hold object",
            Only(report, PdfConformanceCodes.XrefInvalid).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void More_than_twenty_broken_entries_are_summarised()
    {
        var bodies = Enumerable.Range(0, 25).Select(index => $"<</N {index}>>").ToList();
        bodies[0] = "<</Type/Catalog/Pages 2 0 R>>";

        var text = Encoding.Latin1.GetString(PdfTestDocument.Document(bodies));
        var start = text.IndexOf("0000000000 65535 f", StringComparison.Ordinal);
        var end = text.IndexOf("trailer", start, StringComparison.Ordinal);
        var table = text[start..end];

        // Shift every in-use offset by one byte, keeping the fixed width.
        var shifted = string.Join(
            '\n',
            table.Split('\n').Select(line =>
                line.Contains(" n ", StringComparison.Ordinal)
                    ? string.Concat(
                        (int.Parse(line.AsSpan(0, 10), CultureInfo.InvariantCulture) + 1)
                            .ToString("D10", CultureInfo.InvariantCulture),
                        line.AsSpan(10))
                    : line));

        var broken = Encoding.Latin1.GetBytes(text[..start] + shifted + text[end..]);
        var report = PdfConformance.Check(broken, PdfConformanceStandard.Pdf20);

        Assert.True(report.Has(PdfConformanceCodes.XrefInvalid));
        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("more than 20", StringComparison.Ordinal));
        Assert.True(report.Issues.Count(issue => issue.Code == PdfConformanceCodes.XrefInvalid) <= 21);
    }

    [Fact]
    public void An_xref_table_without_a_trailer_is_reported()
    {
        // startxref comes first, so the table is followed by end of file instead of a trailer.
        const string Text = "%PDF-1.7\nstartxref\n22\n"
            + "xref\n0 2\n0000000000 65535 f \n0000000018 00000 n \n";

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(Text), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("without a trailer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_truncated_xref_subsection_is_reported()
    {
        const string Text = "%PDF-1.7\nxref\n0\nstartxref\n9\n%%EOF\n";

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(Text), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("not two integers", StringComparison.Ordinal));
    }

    [Fact]
    public void An_implausible_xref_count_is_reported()
    {
        const string Text = "%PDF-1.7\nxref\n0 99999999\nstartxref\n9\n%%EOF\n";

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(Text), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("not plausible", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_entry_without_a_marker_is_reported()
    {
        const string Text = "%PDF-1.7\nxref\n0 1\n0000000000 65535 x \n"
            + "trailer\n<</Size 2/Root 1 0 R>>\nstartxref\n9\n%%EOF\n";

        var report = PdfConformance.Check(Encoding.Latin1.GetBytes(Text), PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("no in-use or free marker", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_without_widths_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/Length {length}>>",
            [],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("no /W array", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_with_an_impossible_width_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[9 4 2]/Length {length}>>",
            [],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("field width of 9", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_with_empty_rows_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[0 0 0]/Length {length}>>",
            [],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("empty rows", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_with_a_malformed_index_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[1 4 2]/Index[0 (a)]/Length {length}>>",
            [],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("not a list of integer pairs", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_with_an_unsupported_filter_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[1 4 2]/Filter/LZWDecode/Length {length}>>",
            [],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("could not be decoded", StringComparison.Ordinal));
    }

    [Fact]
    public void An_xref_stream_shorter_than_its_entries_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[1 4 2]/Index[0 2]/Length {length}>>",
            [1, 2, 3],
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("shorter than its /W", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_in_an_undeclared_object_stream_is_reported()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 3/Root 1 0 R/W[1 4 2]/Index[0 3]/Length {length}>>",
            [
                .. PdfTestDocument.StreamEntry(0, 0, 65535),
                .. PdfTestDocument.StreamEntry(1, 9),
                .. PdfTestDocument.StreamEntry(2, 99),
            ],
            out _,
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>");

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void A_compressed_entry_that_is_not_an_object_stream_resolves_to_nothing()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[1 4 2]/Index[0 4]/Length {length}>>",
            [
                .. PdfTestDocument.StreamEntry(0, 0, 65535),
                .. PdfTestDocument.StreamEntry(1, 9),
                .. PdfTestDocument.StreamEntry(2, 3),
                .. PdfTestDocument.StreamEntry(1, 60),
            ],
            out _,
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</NotAnObjectStream true>>");

        var file = Open(pdf);

        Assert.Null(file.Resolve(new PdfReference(2)));
        Assert.NotNull(file.Catalog);
    }

    [Fact]
    public void An_object_stream_with_no_objects_is_empty()
    {
        var pdf = PdfTestDocument.XrefStreamDocument(
            "<</Type/XRef/Size 4/Root 1 0 R/W[1 4 2]/Index[0 4]/Length {length}>>",
            [
                .. PdfTestDocument.StreamEntry(0, 0, 65535),
                .. PdfTestDocument.StreamEntry(1, 9),
                .. PdfTestDocument.StreamEntry(2, 3),
                .. PdfTestDocument.StreamEntry(1, 60),
            ],
            out _,
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Type/ObjStm/N 0/First 0/Length 0>>\nstream\n\nendstream");

        var file = Open(pdf);

        Assert.Null(file.Resolve(new PdfReference(2)));
    }

    [Fact]
    public void A_stream_without_endstream_is_reported()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog>>",
            "<</Length 999>>\nstream\nnot terminated",
        ]);

        var file = Open(pdf);
        Assert.IsType<PdfStream>(file.GetObject(2));

        Assert.Contains(
            file.Problems,
            problem => problem.Message.Contains("not terminated by endstream", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stream_without_a_length_is_read_to_endstream()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog>>",
            "<</NoLength true>>\nstream\nhello\nendstream",
        ]);

        var file = Open(pdf);

        var stream = Assert.IsType<PdfStream>(file.GetObject(2));
        Assert.Equal("hello", Encoding.ASCII.GetString(stream.Data));
    }

    [Fact]
    public void A_length_given_by_reference_is_followed()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog>>",
            "<</Length 3 0 R>>\nstream\nhello\nendstream",
            "5",
        ]);

        var file = Open(pdf);

        var stream = Assert.IsType<PdfStream>(file.GetObject(2));
        Assert.Equal("hello", Encoding.ASCII.GetString(stream.Data));
    }

    [Fact]
    public void A_stream_introduced_by_a_carriage_return_is_read()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog>>",
            "<</Length 5>>\r\nstream\r\nhello\nendstream",
        ]);

        var file = Open(pdf);
        var stream = Assert.IsType<PdfStream>(file.GetObject(2));

        Assert.Equal("hello", Encoding.ASCII.GetString(stream.Data));
    }

    [Fact]
    public void An_ascii_hex_stream_is_decoded()
    {
        var file = Open(PdfTestDocument.Minimal());
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(("Filter", new PdfName("ASCIIHexDecode"))),
            Encoding.ASCII.GetBytes("48 65 6C 6C 6F>ignored"));

        var decoded = file.Decode(stream);

        Assert.NotNull(decoded);
        Assert.Equal("Hello", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void An_ascii_hex_stream_with_an_odd_digit_is_padded()
    {
        var file = Open(PdfTestDocument.Minimal());
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(("Filter", new PdfName("AHx"))),
            Encoding.ASCII.GetBytes("414"));

        Assert.Equal(new byte[] { 0x41, 0x40 }, file.Decode(stream));
    }

    [Fact]
    public void An_unsupported_image_filter_decodes_to_nothing()
    {
        var file = Open(PdfTestDocument.Minimal());
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(("Filter", new PdfName("DCTDecode"))),
            [1, 2, 3]);

        Assert.Null(file.Decode(stream));
    }

    [Fact]
    public void A_filter_array_is_applied_in_order()
    {
        var file = Open(PdfTestDocument.Minimal());
        var hex = Convert.ToHexString(Compress(Encoding.ASCII.GetBytes("Hello")));
        var filters = new PdfArray();
        filters.Items.Add(new PdfName("ASCIIHexDecode"));
        filters.Items.Add(new PdfName("FlateDecode"));

        var stream = new PdfStream(
            PdfTestDocument.Dictionary(("Filter", filters)),
            Encoding.ASCII.GetBytes(hex + ">"));

        var decoded = file.Decode(stream);

        Assert.NotNull(decoded);
        Assert.Equal("Hello", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void A_raw_deflate_stream_without_a_zlib_header_is_decoded()
    {
        var file = Open(PdfTestDocument.Minimal());
        using var buffer = new MemoryStream();

        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write("Hello"u8);
        }

        var stream = new PdfStream(
            PdfTestDocument.Dictionary(("Filter", new PdfName("FlateDecode"))),
            buffer.ToArray());

        var decoded = file.Decode(stream);

        Assert.NotNull(decoded);
        Assert.Equal("Hello", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void A_TIFF_predictor_is_undone()
    {
        var file = Open(PdfTestDocument.Minimal());
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(
                ("Filter", new PdfName("FlateDecode")),
                ("DecodeParms", PdfTestDocument.Dictionary(
                    ("Predictor", new PdfNumber(2)),
                    ("Columns", new PdfNumber(3))))),
            Compress([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 3, 6 }, file.Decode(stream));
    }

    [Fact]
    public void A_TIFF_predictor_with_no_columns_leaves_the_data_alone()
    {
        var file = Open(PdfTestDocument.Minimal());
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(
                ("DecodeParms", PdfTestDocument.Dictionary(
                    ("Predictor", new PdfNumber(2)),
                    ("Columns", new PdfNumber(0))))),
            [1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, file.Decode(stream));
    }

    [Fact]
    public void A_predictor_of_one_leaves_the_data_alone()
    {
        var file = Open(PdfTestDocument.Minimal());
        var data = new byte[] { 9, 8, 7 };
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(
                ("DecodeParms", PdfTestDocument.Dictionary(("Predictor", new PdfNumber(1))))),
            data);

        Assert.Same(data, file.Decode(stream));
    }

    [Fact]
    public void A_PNG_predictor_is_undone_for_every_row_filter()
    {
        var file = Open(PdfTestDocument.Minimal());
        byte[] data =
        [
            0, 1, 2, 3,
            1, 1, 1, 1,
            2, 1, 1, 1,
            3, 1, 1, 1,
            4, 0, 0, 0,
        ];

        var stream = new PdfStream(
            PdfTestDocument.Dictionary(
                ("Filter", new PdfName("FlateDecode")),
                ("DecodeParms", PdfTestDocument.Dictionary(
                    ("Predictor", new PdfNumber(12)),
                    ("Columns", new PdfNumber(3))))),
            Compress(data));

        Assert.Equal(
            new byte[]
            {
                1, 2, 3,
                1, 2, 3,
                2, 3, 4,
                2, 3, 4,
                2, 3, 4,
            },
            file.Decode(stream));
    }

    [Fact]
    public void A_PNG_predictor_with_no_columns_leaves_the_data_alone()
    {
        var file = Open(PdfTestDocument.Minimal());
        var data = new byte[] { 0, 1, 2, 3 };
        var stream = new PdfStream(
            PdfTestDocument.Dictionary(
                ("DecodeParms", PdfTestDocument.Dictionary(
                    ("Predictor", new PdfNumber(12)),
                    ("Columns", new PdfNumber(0))))),
            data);

        Assert.Same(data, file.Decode(stream));
    }

    [Fact]
    public void A_free_entry_resolves_to_nothing()
    {
        var text = Encoding.Latin1.GetString(PdfTestDocument.Minimal()).Replace(
            "00000 n ",
            "00000 f ",
            StringComparison.Ordinal);

        var file = Open(Encoding.Latin1.GetBytes(text));

        Assert.Null(file.GetObject(2));
    }

    [Fact]
    public void A_reference_to_a_missing_object_resolves_to_null()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Dangling 99 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));

        Assert.Null(file.Resolve(catalog["Dangling"]));
        Assert.Null(file.GetObject(99));
        Assert.False(file.TryResolveReference(new PdfNumber(1), out _, out _));
    }

    [Fact]
    public void A_broken_object_is_reported_and_the_check_does_not_throw()
    {
        var pdf = PdfTestDocument.Document(["<</Type/Catalog @>", "<</Type/Pages/Count 0/Kids[]>>"]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.True(report.Has(PdfConformanceCodes.ObjectUnreadable));
        Assert.True(report.Has(PdfConformanceCodes.CatalogMissing));
        Assert.False(report.IsConformant);
    }

    [Theory]
    [InlineData("<</X (unterminated>>")]
    [InlineData("<</X <6162>")]
    [InlineData("<</X [1 2>>")]
    [InlineData("<</X <</Y 1>>")]
    [InlineData("<</X 1 2>>")]
    [InlineData("<</X @>>")]
    [InlineData("<</X <zz>>>")]
    public void A_syntax_error_in_an_object_is_reported_as_unreadable(string body)
    {
        var pdf = PdfTestDocument.Document([body, "<</Type/Pages/Count 0/Kids[]>>"]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.True(report.Has(PdfConformanceCodes.ObjectUnreadable), report.ToString());
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void An_object_nested_beyond_the_limit_is_reported_as_unreadable()
    {
        var nested = new StringBuilder("<</X ");

        for (var depth = 0; depth < 70; depth++)
        {
            nested.Append('[');
        }

        nested.Append('1');

        for (var depth = 0; depth < 70; depth++)
        {
            nested.Append(']');
        }

        nested.Append(">>");

        var pdf = PdfTestDocument.Document([nested.ToString(), "<</Type/Pages/Count 0/Kids[]>>"]);
        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Message.Contains("nested more than", StringComparison.Ordinal));
    }

    [Fact]
    public void Escaped_literal_strings_are_decoded()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Lang(a\\(b\\)c\\\\d\\101\\102\\n\\r\\t\\b\\f\\\nz)>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));
        var language = Assert.IsType<PdfString>(file.Resolve(catalog["Lang"]));

        Assert.Equal("a(b)c\\dAB\n\r\t\b\fz", language.Text);
    }

    [Fact]
    public void A_nested_literal_string_keeps_its_parentheses()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Lang(a(b(c))d)>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));
        var language = Assert.IsType<PdfString>(file.Resolve(catalog["Lang"]));

        Assert.Equal("a(b(c))d", language.Text);
    }

    [Fact]
    public void A_name_with_a_hex_escape_is_read()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Ty#70e/Catalog/Pages 2 0 R/MarkInfo<</Marked true>>/Lang(en)/StructTreeRoot 4 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/StructParents 0>>",
            "<</Type/StructTreeRoot/K[5 0 R]/ParentTree<</Nums[0 7 0 R]>>>>",
            "<</Type/StructElem/S/Document/P 4 0 R/K[6 0 R]>>",
            "<</Type/StructElem/S/P/P 5 0 R/K[0]>>",
            "[6 0 R]",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1);

        Assert.False(report.Has(PdfConformanceCodes.CatalogInvalid));
        Assert.True(report.Has(PdfConformanceCodes.MetadataMissing));
    }

    [Fact]
    public void A_hexadecimal_string_is_read_with_or_without_whitespace()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Lang<66 72>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));
        var language = Assert.IsType<PdfString>(file.Resolve(catalog["Lang"]));

        Assert.Equal("fr", language.Text);
    }

    [Fact]
    public void A_UTF_16_text_string_is_decoded()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Lang<FEFF0066007200E9>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));
        var language = Assert.IsType<PdfString>(file.Resolve(catalog["Lang"]));

        Assert.Equal("fré", language.Decode());
    }

    [Fact]
    public void A_null_value_is_read_as_null()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Nothing null/Flag true>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var file = Open(pdf);
        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));

        Assert.Null(file.Resolve(catalog["Nothing"]));
        Assert.True(Assert.IsType<PdfBoolean>(catalog["Flag"]).Value);
    }

    [Fact]
    public void A_page_inherits_resources_from_the_page_tree()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);

        Assert.True(report.Has(PdfConformanceCodes.FontNotEmbedded));
        Assert.True(report.Has(PdfConformanceCodes.FontWithoutToUnicode));
    }

    [Fact]
    public void A_font_with_a_descriptor_but_no_program_is_not_embedded()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FontDescriptor 5 0 R/ToUnicode 6 0 R>>",
            "<</Type/FontDescriptor/FontName/Helvetica>>",
            "<</Length 0>>\nstream\n\nendstream",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);
        var issue = Only(report, PdfConformanceCodes.FontNotEmbedded);

        Assert.Equal("obj 4", issue.Target);
        Assert.False(report.Has(PdfConformanceCodes.FontWithoutToUnicode));
    }

    [Fact]
    public void A_font_with_no_descriptor_is_not_embedded()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/ToUnicode 5 0 R>>",
            "<</Length 0>>\nstream\n\nendstream",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);

        Assert.True(report.Has(PdfConformanceCodes.FontNotEmbedded));
    }

    [Fact]
    public void A_Type3_font_needs_no_font_file_and_no_ToUnicode()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Font/Subtype/Type3/BaseFont/Shapes/CharProcs<</a 5 0 R>>>>",
            "<</Length 0>>\nstream\n\nendstream",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);

        Assert.False(report.Has(PdfConformanceCodes.FontNotEmbedded));
        Assert.False(report.Has(PdfConformanceCodes.FontWithoutToUnicode));
    }

    [Fact]
    public void A_form_xobject_brings_its_own_fonts_into_the_scan()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</XObject<</Fm 4 0 R>>>>>>",
            "<</Type/XObject/Subtype/Form/Resources<</Font<</F1 5 0 R>>>>/Length 0>>\nstream\n\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        ]);

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfA2b);

        Assert.Equal("obj 5", Only(report, PdfConformanceCodes.FontNotEmbedded).Target);
    }

    [Fact]
    public void A_page_tree_without_a_pages_entry_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog>>"]),
            PdfConformanceStandard.Pdf20);

        Assert.Contains(
            "names no page tree",
            Only(report, PdfConformanceCodes.PagesInvalid).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_pages_entry_that_is_not_a_page_tree_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog/Pages 2 0 R>>", "<</Type/NotPages/Kids[]>>"]),
            PdfConformanceStandard.Pdf20);

        Assert.Contains(
            report.Issues,
            issue => issue.Code == PdfConformanceCodes.PagesInvalid
                && issue.Message.Contains("not /Type /Pages", StringComparison.Ordinal));
    }

    [Fact]
    public void A_page_tree_that_holds_no_page_is_reported()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Count 0/Kids[]>>"]),
            PdfConformanceStandard.Pdf20);

        Assert.Contains(
            "holds no page",
            Only(report, PdfConformanceCodes.PagesInvalid).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_in_the_catalog_raises_the_header_version()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog/Pages 2 0 R/Version/2.0>>", "<</Type/Pages/Count 0/Kids[]>>"]),
            PdfConformanceStandard.Pdf20);

        Assert.False(report.Has(PdfConformanceCodes.VersionMismatch));
        Assert.Equal(new PdfVersion(2, 0), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_catalog_version_below_the_header_does_not_lower_it()
    {
        var report = PdfConformance.Check(
            PdfTestDocument.Document(["<</Type/Catalog/Pages 2 0 R/Version/1.7>>", "<</Type/Pages/Count 0/Kids[]>>"]),
            PdfConformanceStandard.Pdf17);

        Assert.Equal(new PdfVersion(1, 7), report.Version.GetValueOrDefault());
        Assert.False(report.Has(PdfConformanceCodes.VersionMismatch));
    }

    private static byte[] Compress(byte[] data)
    {
        using var buffer = new MemoryStream();

        using (var deflate = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return buffer.ToArray();
    }
}
