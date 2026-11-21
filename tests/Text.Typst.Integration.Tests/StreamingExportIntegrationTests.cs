using System;
using System.IO;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The chunked export against the real compiler. The point is that the bytes are identical to the
/// array overload and that a multi-page document really does arrive in several reads.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StreamingExportIntegrationTests
{
    private const string MultiPage = """
        #set page(paper: "a4", margin: 2cm)
        #for i in range(1, 40) [
          = Section #i
          #lorem(120)
        ]
        """;

    private static CompilationResult Compile(string markup)
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        var result = new TypstCompiler().Compile(world);
        world.Dispose();
        return result;
    }

    [Fact]
    public void The_streamed_pdf_is_byte_identical_to_the_array()
    {
        using var result = Compile(MultiPage);
        var expected = result.ExportPdf();

        using var destination = new MemoryStream();
        result.ExportPdf(destination);

        Assert.Equal(expected, destination.ToArray());
    }

    [Fact]
    public void A_multi_page_pdf_arrives_in_several_chunks()
    {
        using var result = Compile(MultiPage);
        Assert.True(result.PageCount > 1, $"expected several pages, got {result.PageCount}");

        using var destination = new CountingStream();
        result.ExportPdf(destination);

        Assert.True(destination.Writes > 1, $"expected several writes, saw {destination.Writes}");
        Assert.Equal(0x25, (int)(destination.FirstByte ?? 0));
    }

    [Fact]
    public void The_streamed_pdf_honours_the_pdf_options()
    {
        using var result = Compile(MultiPage);

        using var destination = new MemoryStream();
        result.ExportPdf(destination, new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA2b],
            Timestamp = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
        });

        var text = Encoding.Latin1.GetString(destination.ToArray());
        Assert.Contains("pdfaid", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_static_helper_streams_a_one_off_document()
    {
        using var destination = new MemoryStream();
        TypstCompiler.CompileToPdf(destination, "= Hello");

        Assert.True(destination.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(destination.ToArray(), 0, 4));
    }

    private sealed class CountingStream : MemoryStream
    {
        public int Writes { get; private set; }

        public byte? FirstByte { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Record(buffer);
            base.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Record(buffer.AsSpan(offset, count));
            base.Write(buffer, offset, count);
        }

        private void Record(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            FirstByte ??= buffer.Length > 0 ? buffer[0] : null;
        }
    }
}
