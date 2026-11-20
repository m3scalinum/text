using System.Text.Json;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class CompileBothTests
{
    [Fact]
    public void Compiles_the_paged_pass_first_then_the_html_pass()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

        using var both = new TypstCompiler(native).CompileBoth(world);

        Assert.Equal(2, native.CompileOptions.Count);

        using var paged = JsonDocument.Parse(native.CompileOptions[0]);
        Assert.Equal("paged", paged.RootElement.GetProperty("document").GetString());

        using var html = JsonDocument.Parse(native.CompileOptions[1]);
        Assert.Equal("html", html.RootElement.GetProperty("document").GetString());
    }

    [Fact]
    public void Propagates_the_root_to_both_passes()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native)
            .AddSource("report.typ", "= Title")
            .Build();

        using var both = new TypstCompiler(native).CompileBoth(
            world, new TypstCompilationOptions { Root = "report.typ" });

        foreach (var options in native.CompileOptions)
        {
            using var json = JsonDocument.Parse(options);
            Assert.Equal("report.typ", json.RootElement.GetProperty("root").GetString());
        }
    }

    [Fact]
    public void Exposes_both_documents()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

        using var both = new TypstCompiler(native).CompileBoth(world);

        Assert.NotNull(both.Paged);
        Assert.NotNull(both.Html);
        Assert.NotSame(both.Paged, both.Html);
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

        var both = new TypstCompiler(native).CompileBoth(world);
        both.Dispose();
        both.Dispose();
    }
}
