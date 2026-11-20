using System;
using System.Collections.Generic;
using M3scalinum.Text.SharpTypst.Internal;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TemplateBinderTests
{
    private static TypstTemplateManifest Manifest(string parameters) =>
        TypstTemplateManifest.Parse($"""
                                     name = "t"
                                     version = "1"
                                     entry = "a.typ"

                                     [parameters]
                                     {parameters}
                                     """);

    [Fact]
    public void Emits_strings_numbers_booleans_and_dates()
    {
        var manifest = Manifest("""
                                title = { type = "string", required = true }
                                count = { type = "number", required = true }
                                flag = { type = "boolean", required = true }
                                issued = { type = "date", required = true }
                                """);

        var result = TemplateBinder.Bind(manifest, new Dictionary<string, object?>
        {
            ["title"] = "Q3 \"report\"",
            ["count"] = 3,
            ["flag"] = true,
            ["issued"] = new DateOnly(2026, 3, 1),
        });

        Assert.Equal(
            (string?)"(title: \"Q3 \\\"report\\\"\", count: 3, flag: true, issued: datetime(year: 2026, month: 3, day: 1))",
            (string?)result);
    }

    [Fact]
    public void Emits_arrays_of_objects()
    {
        var manifest = Manifest("items = { type = \"array\", of = \"object\", required = true }");

        var result = TemplateBinder.Bind(manifest, new Dictionary<string, object?>
        {
            ["items"] = new[]
            {
                new Dictionary<string, object?> { ["description"] = "Consulting", ["quantity"] = 3 },
                new Dictionary<string, object?> { ["description"] = "Support", ["quantity"] = 1 },
            },
        });

        Assert.Equal(
            (string?)"(items: ((description: \"Consulting\", quantity: 3), (description: \"Support\", quantity: 1)))",
            (string?)result);
    }

    [Fact]
    public void Emits_a_single_element_array_with_a_trailing_comma()
    {
        var manifest = Manifest("tags = { type = \"array\", of = \"string\", required = true }");

        var result = TemplateBinder.Bind(manifest, new Dictionary<string, object?>
        {
            ["tags"] = new[] { "only" },
        });

        // `(x)` is a parenthesized value in Typst; `(x,)` is a one-element array.
        Assert.Equal((string?)"(tags: (\"only\",))", (string?)result);
    }

    [Fact]
    public void Applies_the_default_when_the_value_is_null()
    {
        var manifest = Manifest("currency = { type = \"string\", default = \"EUR\" }");

        var result = TemplateBinder.Bind(manifest, new Dictionary<string, object?>
        {
            ["currency"] = null,
        });

        Assert.Equal((string?)"(currency: \"EUR\")", (string?)result);
    }

    [Fact]
    public void Omits_an_optional_parameter_without_default()
    {
        var manifest = Manifest("notes = { type = \"string\" }");

        Assert.Equal((string?)"()", (string?)TemplateBinder.Bind(manifest, new Dictionary<string, object?>()));
    }

    [Fact]
    public void Rejects_a_missing_required_parameter()
    {
        var manifest = Manifest("title = { type = \"string\", required = true }");

        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateBinder.Bind(manifest, new Dictionary<string, object?>()));

        Assert.Equal("title", exception.ParameterName);
        Assert.Contains("requires parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_unknown_parameter()
    {
        var manifest = Manifest("title = { type = \"string\" }");

        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateBinder.Bind(manifest, new Dictionary<string, object?> { ["other"] = 1 }));

        Assert.Equal("other", exception.ParameterName);
    }

    [Fact]
    public void Rejects_a_type_mismatch()
    {
        var manifest = Manifest("count = { type = \"number\", required = true }");

        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateBinder.Bind(manifest, new Dictionary<string, object?> { ["count"] = "three" }));

        Assert.Equal("count", exception.ParameterName);
    }
}
