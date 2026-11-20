using System;
using System.IO;
using System.Text;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Export;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// The streaming export exists so a large PDF is copied out in chunks instead of being
/// materialised a second time in a managed array. What is pinned here is the contract: the same
/// bytes as the array overload, the same export options, and a failure that surfaces as the usual
/// translated exception.
/// </summary>
public sealed class StreamingExportTests
{
    private static (FakeTypstNative Native, CompilationResult Result) NewResult()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        var result = new TypstCompiler(native).Compile(world);
        world.Dispose();
        return (native, result);
    }

    [Fact]
    public void ExportPdf_writes_the_same_bytes_as_the_array_overload()
    {
        var (native, result) = NewResult();
        using var _ = result;
        var expected = result.ExportPdf();

        using var destination = new MemoryStream();
        result.ExportPdf(destination);

        Assert.Equal(expected, destination.ToArray());
        Assert.Equal("pdf", native.StreamedExports[0].Format);
    }

    [Fact]
    public void The_streaming_export_reaches_the_native_reader_in_several_chunks()
    {
        var (native, result) = NewResult();
        using var _ = result;

        // Larger than the fake's chunk size, so more than one write must happen.
        native.ExportResults["pdf"] = Encoding.ASCII.GetBytes(new string('x', 10_000));
        native.StreamChunkSize = 1024;

        using var destination = new ChunkCountingStream();
        result.ExportPdf(destination);

        Assert.Equal(10_000, destination.Length);
        Assert.True(destination.Writes > 1, $"expected several writes, saw {destination.Writes}");
    }

    [Fact]
    public void Exporting_to_a_stream_sends_the_pdf_options()
    {
        var (native, result) = NewResult();
        using var _ = result;

        using var destination = new MemoryStream();
        result.ExportPdf(destination, new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA3b],
            PageRanges = "1-2",
        });

        using var json = System.Text.Json.JsonDocument.Parse(native.StreamedExports[0].Options!);
        var standards = json.RootElement.GetProperty("pdf").GetProperty("standards");
        Assert.Equal("a-3b", standards[0].GetString());
        Assert.Equal("1-2", json.RootElement.GetProperty("pdf").GetProperty("pageRanges").GetString());
    }

    [Fact]
    public void Exporting_to_a_stream_honours_the_requested_format()
    {
        var (native, result) = NewResult();
        using var _ = result;

        using var destination = new MemoryStream();
        result.Export(ExportFormat.Svg, destination);

        Assert.Equal("svg", native.StreamedExports[0].Format);
    }

    [Fact]
    public void A_native_failure_surfaces_as_the_translated_exception()
    {
        var (native, result) = NewResult();
        using var _ = result;

        native.ExportFailure = new M3scalinum.Text.SharpTypst.Native.NativeCallException(
            M3scalinum.Text.SharpTypst.Native.NativeStatus.Export,
            string.Empty,
            "export failed");

        using var destination = new MemoryStream();
        Assert.Throws<TypstExportException>(() => result.ExportPdf(destination));
    }

    [Fact]
    public void A_disposed_document_refuses_to_stream()
    {
        var (_, result) = NewResult();
        result.Dispose();

        using var destination = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => result.ExportPdf(destination));
    }

    [Fact]
    public void A_null_stream_is_rejected()
    {
        var (_, result) = NewResult();
        using var _ = result;

        Assert.Throws<ArgumentNullException>(() => result.ExportPdf((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => result.Export(ExportFormat.Pdf, (Stream)null!));
    }

    private sealed class ChunkCountingStream : MemoryStream
    {
        public int Writes { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            base.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            base.Write(buffer, offset, count);
        }
    }
}
