using System;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TypstWorldBuilderTests
{
    [Fact]
    public void Sends_fixed_clock_by_default()
    {
        var native = new FakeTypstNative();

        using var _ = new TypstWorldBuilder(native).Build();

        Assert.NotNull(native.WorldConfigJson);
        using var document = JsonDocument.Parse(native.WorldConfigJson!);
        Assert.Equal("fixed", document.RootElement.GetProperty("clock").GetProperty("kind").GetString());
        Assert.Equal(2024, document.RootElement.GetProperty("clock").GetProperty("year").GetInt32());
    }

    [Fact]
    public void Sends_system_clock_when_requested()
    {
        var native = new FakeTypstNative();

        using var _ = new TypstWorldBuilder(native).WithSystemClock().Build();

        using var document = JsonDocument.Parse(native.WorldConfigJson!);
        Assert.Equal("system", document.RootElement.GetProperty("clock").GetProperty("kind").GetString());
    }

    [Fact]
    public void Registers_sources_assets_and_fonts()
    {
        var native = new FakeTypstNative();
        var font = new byte[64];

        using var world = new TypstWorldBuilder(native)
            .AddSource("main.typ", "= Title")
            .AddSource("partials/a.typ", "A")
            .AddAsset("logo.png", new byte[] { 1, 2, 3 })
            .AddFont(font)
            .Build();

        Assert.Equal(2, native.Sources.Count);
        Assert.Contains(("main.typ", "= Title"), native.Sources);
        Assert.Single(native.Assets);
        Assert.Equal("logo.png", native.Assets[0].Path);
        Assert.Single(native.Fonts);
    }

    [Fact]
    public void Adding_the_same_source_twice_keeps_the_last_value()
    {
        var native = new FakeTypstNative();

        using var _ = new TypstWorldBuilder(native)
            .AddSource("main.typ", "first")
            .AddSource("main.typ", "second")
            .Build();

        Assert.Single(native.Sources);
        Assert.Equal("second", native.Sources[0].Text);
    }

    [Fact]
    public void Disposing_the_world_releases_the_handle()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "x").Build();

        world.Dispose();
        world.Dispose();
    }

    [Fact]
    public void WithSource_returns_a_clone_and_leaves_the_original_alone()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native)
            .AddSource("main.typ", "one")
            .AddSource("partials/a.typ", "A")
            .Build();

        using var specialised = world.WithSource("main.typ", "two");

        Assert.Equal(1, native.CloneCount);
        Assert.Contains(("main.typ", "two"), native.Sources);
        Assert.Equal(2, native.Sources.Count);
    }

    [Fact]
    public void WithSource_adds_a_path_that_was_not_registered()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "one").Build();

        using var specialised = world.WithSource("extra.typ", "two");

        Assert.Contains(("extra.typ", "two"), native.Sources);
    }

    [Fact]
    public void Clone_shares_the_fonts_and_copies_the_sources()
    {
        var native = new FakeTypstNative();
        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "one").Build();

        using var clone = world.Clone();

        Assert.Equal(1, native.CloneCount);
        Assert.Single(native.Sources);
    }
}
