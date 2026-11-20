using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class DocumentPreambleTests
{
    [Fact]
    public void Emits_let_set_show_import_and_include()
    {
        var markup = TypstDocument.Create()
            .Let("total", TypstExpression.Number(1200))
            .Set("text(size: 12pt)")
            .Show("heading", "it => it")
            .Import("lib.typ", "helper")
            .Include("part.typ")
            .Paragraph("body")
            .ToMarkup();

        Assert.Contains("#let total = 1200", markup, StringComparison.Ordinal);
        Assert.Contains("#set text(size: 12pt)", markup, StringComparison.Ordinal);
        Assert.Contains("#show heading: it => it", markup, StringComparison.Ordinal);
        Assert.Contains("#import \"lib.typ\": helper", markup, StringComparison.Ordinal);
        Assert.Contains("#include \"part.typ\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_import_without_names()
    {
        var markup = TypstDocument.Create().Import("lib.typ").ToMarkup();

        Assert.Contains("#import \"lib.typ\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_expression_block()
    {
        var markup = TypstDocument.Create().Expression(TypstExpression.Number(1)).ToMarkup();

        Assert.Contains("#(1)", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_the_preamble_before_the_body()
    {
        var markup = TypstDocument.Create()
            .Paragraph("body")
            .Let("x", TypstExpression.Number(1))
            .ToMarkup();

        Assert.True(
            markup.IndexOf("#let x = 1", StringComparison.Ordinal)
            < markup.IndexOf("body", StringComparison.Ordinal));
    }
}