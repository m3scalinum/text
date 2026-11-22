using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// What the produced PDF carries for a ligature and for a hyphenation break: measured on the bytes,
/// not assumed. Typst 0.15.1 writes no <c>/ActualText</c> at all; the ligature is recovered from the
/// font's <c>/ToUnicode</c> CMap, and the hyphenation break is a soft hyphen the font maps to
/// U+00AD. Both facts are what <c>15-pdf-conformance.md</c> §5 records.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActualTextIntegrationTests
{
    private const string Markup = """
        #set page(paper: "a4", margin: 1.5cm)
        #set text(lang: "fr", hyphenate: true)
        #set par(justify: true)
        #block(width: 5cm)[
        Le fi et l'œuvre, une finance, puis anticonstitutionnellement anticonstitutionnellement anticonstitutionnellement.
        ]
        """;

    private static CompilationResult Compile()
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", Markup).Build();
        return new TypstCompiler().Compile(world, new TypstCompilationOptions());
    }

    [Fact]
    public void The_ligature_is_recovered_through_the_to_unicode_map_and_not_through_actual_text()
    {
        using var result = Compile();
        var pdf = result.ExportPdf();

        Assert.Contains("Le fi et l’œuvre", result.ExtractText(0), StringComparison.Ordinal);

        var streams = DecompressedStreams(pdf);

        Assert.Contains(streams, stream => stream.Contains("<00660069>", StringComparison.Ordinal));
        Assert.DoesNotContain(streams, stream => stream.Contains("/ActualText", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hyphenation_break_is_a_soft_hyphen_in_the_page_text()
    {
        using var result = Compile();
        var page = result.ExtractText(0);

        Assert.Contains("anti\u00AD", page, StringComparison.Ordinal);
        Assert.Contains("constitutionnellement", page, StringComparison.Ordinal);
    }

    [Fact]
    public void No_page_of_the_document_carries_an_actual_text_entry()
    {
        using var result = Compile();
        var pdf = result.ExportPdf();

        Assert.Equal(0, Count(pdf, "/ActualText"));

        // The tagged structure is there: the page's text is inside marked-content spans, which is
        // where an /ActualText would sit if Typst wrote one.
        Assert.Contains(
            DecompressedStreams(pdf),
            stream => stream.Contains("/Span", StringComparison.Ordinal));
    }

    private static int Count(byte[] haystack, string needle)
    {
        var needleBytes = Encoding.ASCII.GetBytes(needle);
        var count = 0;

        for (var index = 0; index <= haystack.Length - needleBytes.Length; index++)
        {
            if (haystack.AsSpan(index, needleBytes.Length).SequenceEqual(needleBytes))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The Flate-compressed streams of the file, decoded. A stream that does not decompress — a font
    /// program, an image — is skipped: this reads the CMap and the content streams only.
    /// </summary>
    private static List<string> DecompressedStreams(byte[] pdf)
    {
        var streams = new List<string>();
        var start = 0;

        while (true)
        {
            var open = pdf.AsSpan(start).IndexOf("stream"u8);

            if (open < 0)
            {
                return streams;
            }

            var body = start + open + "stream".Length;

            while (body < pdf.Length && pdf[body] is (byte)'\r' or (byte)'\n')
            {
                body++;
            }

            var close = pdf.AsSpan(body).IndexOf("endstream"u8);

            if (close < 0)
            {
                return streams;
            }

            streams.AddRange(Decode(pdf[body..(body + close)]));
            start = body + close + "endstream".Length;
        }
    }

    private static IEnumerable<string> Decode(byte[] raw)
    {
        string? text = null;

        try
        {
            using var input = new MemoryStream(raw);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            text = Encoding.Latin1.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            // Not a Flate stream: a font program or an image, of no interest here.
        }

        if (text is not null)
        {
            yield return text;
        }
    }
}
