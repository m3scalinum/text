using System;
using System.Collections.Generic;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>A template parameter must produce a Typst literal the compiler accepts.</summary>
[Trait("Category", "Integration")]
public sealed class TemplateNumberTests
{
    private const string Manifest = """
        name = "probe"
        version = "1.0.0"
        entry = "template.typ"

        [parameters]
        value = { type = "number", required = true }
        """;

    private const string Entry = "#let render(p) = [value: #p.value]";

    private static TypstTemplate Template()
    {
        var manifest = TypstTemplateManifest.Parse(Manifest, "probe");

        return TypstTemplate.Load(manifest, new Dictionary<string, byte[]>
        {
            ["template.typ"] = Encoding.UTF8.GetBytes(Entry),
        });
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1.0 / 3.0)]
    [InlineData(1e300)]
    [InlineData(1e-300)]
    [InlineData(double.MaxValue)]
    [InlineData(double.Epsilon)]
    public void A_finite_number_renders(double value)
    {
        var pdf = Template().Render(
            new TypstCompiler(),
            new Dictionary<string, object?> { ["value"] = value });

        Assert.NotEmpty(pdf);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_number_names_the_parameter(double value)
    {
        // Without the guard, the binder emits `NaN` and the compiler answers
        // `unknown variable: NaN`, which names neither the parameter nor the real problem.
        var exception = Assert.Throws<TypstTemplateException>(() => Template().Render(
            new TypstCompiler(),
            new Dictionary<string, object?> { ["value"] = value }));

        Assert.Contains("finite", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("value", exception.Message, StringComparison.Ordinal);
    }
}
