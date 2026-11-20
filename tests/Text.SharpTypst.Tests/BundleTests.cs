using System;
using System.Text;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Native;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class BundleTests
{
    private static (TypstCompiler Compiler, FakeTypstNative Native) Create()
    {
        var native = new FakeTypstNative();
        return (new TypstCompiler(native), native);
    }

    [Fact]
    public void Compiles_with_the_bundle_document_kind()
    {
        var (compiler, native) = Create();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "x").Build();

        using var result = compiler.Compile(world, new TypstCompilationOptions
        {
            DocumentKind = TypstDocumentKind.Bundle,
        });

        Assert.Contains("\"document\":\"bundle\"", native.CompileOptionsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_manifest_and_every_file()
    {
        var (compiler, native) = Create();
        native.ExportResults["bundle"] = Encoding.UTF8.GetBytes(
            """{"assets":[{"path":"index.html","size":5},{"path":"data.txt","size":3}]}""");
        native.ExportResults["html"] = Encoding.UTF8.GetBytes("<html></html>");
        native.BundleAssets[0] = Encoding.UTF8.GetBytes("<html></html>");
        native.BundleAssets[1] = Encoding.UTF8.GetBytes("abc");

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "x").Build();
        var bundle = compiler.CompileBundle(world);

        Assert.Equal("<html></html>", bundle.Html);
        Assert.Equal(2, bundle.Files.Count);
        Assert.Equal("index.html", bundle.Files[0].Path);
        Assert.Equal("data.txt", bundle.Files[1].Path);
        Assert.Equal("abc", Encoding.UTF8.GetString(bundle.Files[1].Data));
        Assert.Equal([0, 1], native.BundleAssetRequests);
    }

    [Fact]
    public void Handles_an_empty_bundle()
    {
        var (compiler, native) = Create();
        native.ExportResults["bundle"] = Encoding.UTF8.GetBytes("""{"assets":[]}""");

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "x").Build();
        var bundle = compiler.CompileBundle(world);

        Assert.Empty(bundle.Files);
        Assert.Empty(native.BundleAssetRequests);
    }

    [Fact]
    public void Rejects_a_bundle_export_on_a_paged_document()
    {
        var (compiler, native) = Create();
        native.ExportFailure = new NativeCallException(
            NativeStatus.InvalidArgument,
            """{"code":"invalid-argument","message":"the bundle manifest requires a bundle document"}""",
            "export 'bundle'");

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "x").Build();
        using var result = compiler.Compile(world);

        Assert.Throws<TypstNativeException>(() => result.ExportBundleFiles());
    }
}
