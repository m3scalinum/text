using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class RemainingSurfaceTests
{
    [Fact]
    public void Emits_an_import_as_a_module()
    {
        var markup = TypstDocument.Create().ImportAs("lib.typ", "m").ToMarkup();

        Assert.Contains("#import \"lib.typ\" as m", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_function_definition_in_the_preamble()
    {
        var markup = TypstDocument.Create()
            .Function(TypstFunction.Define("inc").Parameter("x").Body("x + 1"))
            .Paragraph("body")
            .ToMarkup();

        Assert.Contains("#let inc(x) = x + 1", markup, StringComparison.Ordinal);
        Assert.True(
            markup.IndexOf("#let inc", StringComparison.Ordinal)
            < markup.IndexOf("body", StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_smart_quote_settings()
    {
        Assert.Contains(
            "#set smartquote(enabled: false)",
            TypstDocument.Create().SmartQuotes(false).ToMarkup(),
            StringComparison.Ordinal);
        Assert.Contains(
            "#set smartquote(enabled: true)",
            TypstDocument.Create().SmartQuotes().ToMarkup(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_asset()
    {
        var markup = TypstDocument.Create().Asset("out.txt", "data").ToMarkup();

        Assert.Contains("#asset(\"out.txt\", \"data\")", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_repeat()
    {
        Assert.Contains(
            "#repeat[repeated]",
            TypstDocument.Create().Repeat("repeated").ToMarkup(),
            StringComparison.Ordinal);

        Assert.Contains(
            "#repeat(gap: 1em, justify: true)[repeated]",
            TypstDocument.Create().Repeat("repeated", TypstLength.Em(1), justify: true).ToMarkup(),
            StringComparison.Ordinal);
    }
}