using System;
using M3scalinum.Text.SharpTypst.Internal;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// The TOML parser recurses for arrays and inline tables. Without a bound, a malformed manifest
/// raises an uncatchable <c>StackOverflowException</c> and takes the process with it.
/// </summary>
public sealed class TomlDepthTests
{
    private static string Nested(int depth)
        => "value = " + new string('[', depth) + new string(']', depth);

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(64)]
    public void A_reasonable_nesting_parses(int depth)
    {
        var table = Toml.Parse(Nested(depth));

        Assert.True(table.TryGet("value", out _));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void A_deep_nesting_is_rejected_instead_of_overflowing(int depth)
    {
        var exception = Assert.Throws<FormatException>(() => Toml.Parse(Nested(depth)));

        Assert.Contains("nest deeper than", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deep_nesting_in_a_manifest_surfaces_as_a_template_error()
    {
        var manifest = $"""
            name = "probe"
            version = "1.0.0"
            entry = "template.typ"
            value = {new string('[', 1_000)}{new string(']', 1_000)}
            """;

        var exception = Assert.Throws<TypstTemplateException>(
            () => TypstTemplateManifest.Parse(manifest, "probe"));

        Assert.Contains("invalid template manifest", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nest deeper than", exception.Message, StringComparison.Ordinal);
    }
}
