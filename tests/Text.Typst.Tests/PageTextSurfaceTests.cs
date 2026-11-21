using System;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The per-page text surface without the native library: what reaches the boundary, and how a
/// native failure becomes the typed exception.
/// </summary>
public sealed class PageTextSurfaceTests
{
    private static CompilationResult Compile(FakeTypstNative native)
    {
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        return new TypstCompiler(native).Compile(world);
    }

    [Fact]
    public void Reads_the_page_the_caller_asked_for()
    {
        var native = new FakeTypstNative();
        native.PageTexts[0] = "first\n";
        native.PageTexts[1] = "second\n";

        using var result = Compile(native);

        Assert.Equal("second\n", result.ExtractText(1));
        Assert.Contains(1, native.PageTextRequests);
    }

    [Fact]
    public void Returns_an_empty_string_when_the_page_has_no_text()
    {
        var native = new FakeTypstNative();

        using var result = Compile(native);

        Assert.Equal(string.Empty, result.ExtractText(0));
    }

    [Fact]
    public void A_document_with_no_pages_yields_no_page_text()
    {
        var native = new FakeTypstNative();

        using var result = Compile(native);

        // The fake's handle reports no pages, so the walk stops before touching the boundary.
        Assert.Empty(result.ExtractTextPages());
        Assert.Empty(native.PageTextRequests);
    }

    [Fact]
    public void Surfaces_a_native_failure()
    {
        var native = new FakeTypstNative
        {
            ExportFailure = new NativeCallException(
                NativeStatus.InvalidArgument,
                """{"code":"invalid-argument","message":"page text requires a paged document"}""",
                "read page text"),
        };

        using var result = Compile(native);

        var exception = Assert.Throws<TypstNativeException>(() => result.ExtractText(0));

        Assert.Equal(TypstErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void A_disposed_result_refuses_to_be_read()
    {
        var native = new FakeTypstNative();
        var result = Compile(native);

        result.Dispose();

        Assert.Throws<ObjectDisposedException>(() => result.ExtractText(0));
        Assert.Throws<ObjectDisposedException>(result.ExtractTextPages);
    }
}
