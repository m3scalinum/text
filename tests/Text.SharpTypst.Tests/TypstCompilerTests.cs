using System;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Export;
using M3scalinum.Text.SharpTypst.Native;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TypstCompilerTests
{
    private const string ErrorPayload = """
        {
          "code": "compilation",
          "message": "1 error(s), first: unknown variable: `totl` at main.typ:1:1",
          "diagnostics": [
            {
              "severity": "error",
              "message": "unknown variable: `totl`",
              "hints": [],
              "trace": [],
              "file": "main.typ",
              "line": 1,
              "column": 1,
              "endLine": 1,
              "endColumn": 5,
              "isInSource": true
            }
          ]
        }
        """;

    private static (FakeTypstNative Native, TypstWorld World) NewWorld()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();
        return (native, world);
    }

    [Fact]
    public void Sends_the_root_and_document_kind()
    {
        var (native, world) = NewWorld();
        using var _ = world;

        using var result = new TypstCompiler(native).Compile(
            world,
            new TypstCompilationOptions { Root = "main.typ", DocumentKind = TypstDocumentKind.Paged });

        using var options = JsonDocument.Parse(native.CompileOptionsJson!);
        Assert.Equal("main.typ", options.RootElement.GetProperty("root").GetString());
        Assert.Equal("paged", options.RootElement.GetProperty("document").GetString());
    }

    [Fact]
    public void Maps_a_compilation_failure_to_a_typed_exception()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        native.CompileFailure = new NativeCallException(
            NativeStatus.Compilation, ErrorPayload, "compile failed");

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler(native).Compile(world));

        Assert.Equal(TypstErrorCode.Compilation, exception.Code);
        Assert.Single(exception.Diagnostics);
        Assert.Equal("main.typ", exception.Diagnostics[0].Location!.File);
        Assert.Contains("unknown variable", exception.Message);
    }

    [Fact]
    public void Maps_a_panic_to_a_native_exception()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        native.CompileFailure = new NativeCallException(
            NativeStatus.Panic, "{\"code\":\"panic\",\"message\":\"boom\"}", "compile failed");

        var exception = Assert.Throws<TypstNativeException>(
            () => new TypstCompiler(native).Compile(world));

        Assert.Equal(TypstErrorCode.Panic, exception.Code);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public void Exposes_warnings_from_a_successful_compilation()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        native.DiagnosticsJson = """
            { "code": "ok", "message": "", "diagnostics": [
                { "severity": "warning", "message": "unknown font family: Inter", "hints": [], "trace": [] } ] }
            """;

        using var result = new TypstCompiler(native).Compile(world);

        Assert.Single(result.Diagnostics);
        Assert.Equal(M3scalinum.Text.SharpTypst.Diagnostics.TypstSeverity.Warning, result.Diagnostics[0].Severity);
    }

    [Fact]
    public void Exports_pdf_bytes()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        using var result = new TypstCompiler(native).Compile(world);

        var bytes = result.ExportPdf();

        Assert.Equal(native.ExportBytes, bytes);
    }

    [Fact]
    public void Maps_an_unsupported_format_to_a_native_exception()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        using var result = new TypstCompiler(native).Compile(world);
        native.ExportFailure = new NativeCallException(
            NativeStatus.Unsupported, "{\"code\":\"unsupported\",\"message\":\"not yet\"}", "export failed");

        var exception = Assert.Throws<TypstNativeException>(
            () => result.Export(ExportFormat.Svg));

        Assert.Equal(TypstErrorCode.Unsupported, exception.Code);
    }

    [Fact]
    public void Exporting_after_dispose_throws()
    {
        var (native, world) = NewWorld();
        using var _ = world;
        var result = new TypstCompiler(native).Compile(world);
        result.Dispose();

        Assert.Throws<ObjectDisposedException>(() => result.ExportPdf());
    }
}
