using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstDataAndFormatTests
{
    [Fact]
    public void Formats_data_loaders()
    {
        Assert.Equal("read(\"a.txt\")", TypstData.Read("a.txt").ToTypst());
        Assert.Equal("json(\"a.json\")", TypstData.Json("a.json").ToTypst());
        Assert.Equal("yaml(\"a.yaml\")", TypstData.Yaml("a.yaml").ToTypst());
        Assert.Equal("toml(\"a.toml\")", TypstData.Toml("a.toml").ToTypst());
        Assert.Equal("csv(\"a.csv\")", TypstData.Csv("a.csv").ToTypst());
        Assert.Equal("cbor(\"a.cbor\")", TypstData.Cbor("a.cbor").ToTypst());
        Assert.Equal("xml(\"a.xml\")", TypstData.Xml("a.xml").ToTypst());
    }

    [Fact]
    public void Formats_a_plugin_call()
    {
        var plugin = TypstPlugin.Load("p.wasm");

        Assert.Equal(
            "plugin(\"p.wasm\").call(\"add\", 1, 2)",
            plugin.Call("add", TypstExpression.Number(1), TypstExpression.Number(2)).ToTypst());
    }

    [Fact]
    public void Formats_pdf_functions()
    {
        Assert.Equal("pdf.attach(\"a.pdf\")", TypstPdf.Attach("a.pdf").ToTypst());
        Assert.Equal(
            "pdf.attach(\"a.pdf\", description: \"invoice\")",
            TypstPdf.Attach("a.pdf", "invoice").ToTypst());
        Assert.Equal("pdf.artifact[x]", TypstPdf.Artifact(new TypstContent().Text("x")).ToTypst());
        Assert.Equal("pdf.data-cell[x]", TypstPdf.DataCell(new TypstContent().Text("x")).ToTypst());
        Assert.Equal("pdf.header-cell[x]", TypstPdf.HeaderCell(new TypstContent().Text("x")).ToTypst());
        Assert.Equal("pdf.table-summary(summary: \"s\", [x])", TypstPdf.TableSummary("s", new TypstContent().Text("x")).ToTypst());
    }

    [Fact]
    public void Formats_html_functions()
    {
        Assert.Equal(
            "html.elem(\"div\", attrs: (class: \"box\"))[x]",
            TypstHtml.Elem("div", new TypstContent().Text("x"), ("class", "box")).ToTypst());
        Assert.Equal("html.frame[x]", TypstHtml.Frame(new TypstContent().Text("x")).ToTypst());
        Assert.Equal(
            "html.p[A typed paragraph.]",
            TypstHtml.Typed("p", new TypstContent().Text("A typed paragraph.")).ToTypst());
    }
}