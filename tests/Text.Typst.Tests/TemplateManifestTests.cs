using System;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TemplateManifestTests
{
    private const string Manifest = """
                                    name = "invoice"
                                    version = "1.0.0"
                                    entry = "template.typ"
                                    description = "An invoice"
                                    authors = ["M3scalinum.Text.Typst"]

                                    [parameters]
                                    title = { type = "string", required = true }
                                    currency = { type = "string", default = "EUR" }
                                    items = { type = "array", of = "object", required = true }
                                    """;

    [Fact]
    public void Parses_the_declared_parameters()
    {
        var manifest = TypstTemplateManifest.Parse(Manifest);

        Assert.Equal("invoice", manifest.Name);
        Assert.Equal("template.typ", manifest.Entry);
        Assert.Equal(["M3scalinum.Text.Typst"], manifest.Authors);
        Assert.Equal(3, manifest.Parameters.Count);

        var title = manifest.Parameters["title"];
        Assert.Equal(TypstParameterType.String, title.Type);
        Assert.True(title.Required);

        var currency = manifest.Parameters["currency"];
        Assert.False(currency.Required);
        Assert.Equal("EUR", currency.Default);

        var items = manifest.Parameters["items"];
        Assert.Equal(TypstParameterType.Array, items.Type);
        Assert.Equal(TypstParameterType.Object, items.ItemType);
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var exception = Assert.Throws<TypstTemplateException>(
            () => TypstTemplateManifest.Parse("version = \"1.0.0\"\nentry = \"a.typ\""));

        Assert.Contains("missing 'name'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_unknown_parameter_type()
    {
        const string text = """
                            name = "t"
                            version = "1"
                            entry = "a.typ"

                            [parameters]
                            x = { type = "colour" }
                            """;

        var exception = Assert.Throws<TypstTemplateException>(() => TypstTemplateManifest.Parse(text));

        Assert.Equal("x", exception.ParameterName);
        Assert.Contains("unknown type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_array_without_item_type()
    {
        const string text = """
                            name = "t"
                            version = "1"
                            entry = "a.typ"

                            [parameters]
                            x = { type = "array" }
                            """;

        var exception = Assert.Throws<TypstTemplateException>(() => TypstTemplateManifest.Parse(text));

        Assert.Contains("needs 'of'", exception.Message, StringComparison.Ordinal);
    }
}