using System;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>The hard caps on registered inputs, exercised against the real native library.</summary>
[Trait("Category", "Integration")]
public sealed class LimitsTests
{
    [Fact]
    public void Rejects_a_source_over_the_byte_cap()
    {
        var builder = new TypstWorldBuilder()
            .WithLimits(limits => limits.MaxSourceBytes = 16)
            .AddSource("main.typ", new string('x', 64));

        var exception = Assert.Throws<TypstNativeException>(() => builder.Build());

        Assert.Equal(TypstErrorCode.LimitExceeded, exception.Code);
        Assert.Contains("maxSourceBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_more_sources_than_the_cap()
    {
        var builder = new TypstWorldBuilder()
            .WithLimits(limits => limits.MaxSources = 2)
            .AddSource("main.typ", "= Title")
            .AddSource("a.typ", "a")
            .AddSource("b.typ", "b");

        var exception = Assert.Throws<TypstNativeException>(() => builder.Build());

        Assert.Equal(TypstErrorCode.LimitExceeded, exception.Code);
        Assert.Contains("too many sources", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_asset_over_the_byte_cap()
    {
        var builder = new TypstWorldBuilder()
            .WithLimits(limits => limits.MaxAssetBytes = 8)
            .AddSource("main.typ", "= Title")
            .AddAsset("data.bin", new byte[64]);

        var exception = Assert.Throws<TypstNativeException>(() => builder.Build());

        Assert.Equal(TypstErrorCode.LimitExceeded, exception.Code);
        Assert.Contains("maxAssetBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_inputs_at_the_cap()
    {
        var source = new string('x', 16);

        using var world = new TypstWorldBuilder()
            .WithLimits(limits => limits.MaxSourceBytes = 16)
            .AddSource("main.typ", source)
            .Build();

        Assert.Equal(1, world.SourceCount);
    }

    [Fact]
    public void The_defaults_are_the_documented_ones()
    {
        var limits = new TypstLimits();

        Assert.Equal(16 * 1024 * 1024, limits.MaxSourceBytes);
        Assert.Equal(64 * 1024 * 1024, limits.MaxAssetBytes);
        Assert.Equal(64 * 1024 * 1024, limits.MaxFontBytes);
        Assert.Equal(1024, limits.MaxSources);
        Assert.Equal(256, limits.MaxFonts);
    }

    [Fact]
    public void A_limit_failure_leaves_no_world_behind()
    {
        // The builder must not leak the native world when a registration fails after creation.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var builder = new TypstWorldBuilder()
                .WithLimits(limits => limits.MaxSources = 1)
                .AddSource("main.typ", "= Title")
                .AddSource("extra.typ", "x");

            Assert.Throws<TypstNativeException>(() => builder.Build());
        }

        // A world created after the failures still works.
        using var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        using var result = new TypstCompiler().Compile(world);

        Assert.Equal(1, result.PageCount);
    }
}
