using System;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>World introspection, exercised against the fake native layer.</summary>
public sealed class WorldIntrospectionTests
{
    [Fact]
    public void Font_families_are_read_from_the_native_layer()
    {
        var native = new FakeTypstNative
        {
            FontFamiliesJson = """["Libertinus Serif","New Computer Modern","DejaVu Sans Mono"]""",
        };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        Assert.Equal(
            ["Libertinus Serif", "New Computer Modern", "DejaVu Sans Mono"],
            world.FontFamilies);
    }

    [Fact]
    public void An_empty_payload_yields_no_families()
    {
        var native = new FakeTypstNative { FontFamiliesJson = "[]" };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        Assert.Empty(world.FontFamilies);
    }

    [Fact]
    public void A_null_payload_yields_no_families()
    {
        var native = new FakeTypstNative { FontFamiliesJson = "null" };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        Assert.Empty(world.FontFamilies);
    }

    [Fact]
    public void The_result_reports_the_requested_kind()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();
        var compiler = new TypstCompiler(native);

        using var paged = compiler.Compile(world);
        using var html = compiler.Compile(world, new TypstCompilationOptions
        {
            DocumentKind = TypstDocumentKind.Html,
        });

        Assert.Equal(TypstDocumentKind.Paged, paged.Kind);
        Assert.Equal(TypstDocumentKind.Html, html.Kind);
    }

    [Fact]
    public void A_disposed_world_rejects_every_operation()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        world.Dispose();

        Assert.Throws<ObjectDisposedException>(() => world.Clone());
        Assert.Throws<ObjectDisposedException>(() => world.WithSource("main.typ", "= y"));
        Assert.Throws<ObjectDisposedException>(() => world.FontFamilies);
    }

    [Fact]
    public void WarningsAsErrors_throws_and_disposes_the_result()
    {
        var native = new FakeTypstNative
        {
            DiagnosticsJson = """
                {"diagnostics":[{"severity":"warning","message":"unknown font family","isInSource":true}]}
                """,
        };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler(native).Compile(
                world,
                new TypstCompilationOptions { WarningsAsErrors = true }));

        Assert.Single(exception.Diagnostics);
        Assert.Contains("warning", exception.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningsAsErrors_ignores_hints()
    {
        var native = new FakeTypstNative
        {
            DiagnosticsJson = """
                {"diagnostics":[{"severity":"hint","message":"a hint","isInSource":true}]}
                """,
        };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();
        using var result = new TypstCompiler(native).Compile(
            world,
            new TypstCompilationOptions { WarningsAsErrors = true });

        Assert.Single(result.Diagnostics);
        Assert.Equal(TypstDocumentKind.Paged, result.Kind);
    }
}
