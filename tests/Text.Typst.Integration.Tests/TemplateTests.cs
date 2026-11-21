using System;
using System.Collections.Generic;
using System.IO;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>The reference invoice template, rendered by the real compiler.</summary>
[Trait("Category", "Integration")]
public sealed class TemplateTests
{
    private static string TemplateDirectory
        => Path.Combine(AppContext.BaseDirectory, "templates", "invoice");

    private static Dictionary<string, object?> Invoice() => new()
    {
        ["title"] = "Invoice 2026-0042",
        ["number"] = "INV-2026-0042",
        ["issued"] = new DateOnly(2026, 3, 1),
        ["due"] = new DateOnly(2026, 3, 31),
        ["seller"] = new Dictionary<string, object?>
        {
            ["name"] = "Northwind Systems, Inc.",
            ["logo"] = "assets/logo.svg",
            ["address"] = new[] { "1200 Market Street, Suite 400", "San Francisco, CA 94103" },
            ["phone"] = "+1 (415) 555-0142",
            ["email"] = "billing@northwind.example",
            ["tax_id"] = "EIN 84-2719930",
            ["legal"] = "Payment is due within 30 days of the invoice date.",
        },
        ["customer"] = new Dictionary<string, object?>
        {
            ["name"] = "Cascade Health Group",
            ["address"] = new[] { "4820 Willamette Avenue, Floor 9", "Portland, OR 97205" },
        },
        ["items"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["category"] = "Hardware",
                ["sku"] = "NX-2200",
                ["description"] = "Edge gateway",
                ["quantity"] = 2,
                ["unit_price"] = 1249.00m,
            },
            new Dictionary<string, object?>
            {
                ["category"] = "Software and licenses",
                ["description"] = "Control Plane subscription",
                ["quantity"] = 1,
                ["unit_price"] = 14400.00m,
            },
        },
        ["amount_paid"] = 5000,
        ["notes"] = "Payable by wire or ACH within 30 days.",
    };

    [Fact]
    public void Loads_the_manifest_and_its_parameters()
    {
        var template = TypstTemplate.Load(TemplateDirectory);

        Assert.Equal("invoice", template.Manifest.Name);
        Assert.Equal(11, template.Manifest.Parameters.Count);
        Assert.True(template.Manifest.Parameters["title"].Required);
        Assert.True(template.Manifest.Parameters["seller"].Required);
        Assert.Equal("USD", template.Manifest.Parameters["currency"].Default);
    }

    [Fact]
    public void Renders_a_pdf()
    {
        var template = TypstTemplate.Load(TemplateDirectory);
        var compiler = new TypstCompiler();

        var pdf = template.Render(compiler, Invoice());

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Renders_one_page()
    {
        var template = TypstTemplate.Load(TemplateDirectory);
        var compiler = new TypstCompiler();
        using var result = template.RenderDocument(compiler, Invoice());

        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void Rejects_a_missing_required_parameter()
    {
        var template = TypstTemplate.Load(TemplateDirectory);
        var compiler = new TypstCompiler();
        var parameters = Invoice();
        parameters.Remove("seller");

        var exception = Assert.Throws<TypstTemplateException>(() => template.Render(compiler, parameters));

        Assert.Equal("seller", exception.ParameterName);
    }

    [Fact]
    public void Rejects_an_unknown_parameter()
    {
        var template = TypstTemplate.Load(TemplateDirectory);
        var compiler = new TypstCompiler();
        var parameters = Invoice();
        parameters["discount"] = "10";

        var exception = Assert.Throws<TypstTemplateException>(() => template.Render(compiler, parameters));

        Assert.Equal("discount", exception.ParameterName);
    }

    [Fact]
    public void Renders_through_a_typed_options_record()
    {
        var template = TypstTemplate<InvoiceOptions>.Load(TemplateDirectory);
        var compiler = new TypstCompiler();

        var pdf = template.Render(compiler, new InvoiceOptions
        {
            Title = "Invoice 2026-0043",
            Number = "INV-2026-0043",
            Issued = new DateOnly(2026, 4, 1),
            Due = new DateOnly(2026, 5, 1),
            Seller = new Dictionary<string, object?> { ["name"] = "Northwind Systems, Inc." },
            Customer = new Dictionary<string, object?> { ["name"] = "Cascade Health Group" },
            Items =
            [
                new Dictionary<string, object?>
                {
                    ["description"] = "Audit",
                    ["quantity"] = 1,
                    ["unit_price"] = 1200.00m,
                },
            ],
        });

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public void Reports_a_typst_error_inside_the_template()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sharp-typst-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "template.toml"), """
                name = "broken"
                version = "1.0.0"
                entry = "template.typ"

                [parameters]
                title = { type = "string", required = true }
                """);

            File.WriteAllText(Path.Combine(directory, "template.typ"), """
                #let render(p) = {
                  [#p.title]
                  #undefined_function()
                }
                """);

            var template = TypstTemplate.Load(directory);
            var compiler = new TypstCompiler();

            var exception = Assert.Throws<TypstCompilationException>(
                () => template.Render(compiler, new Dictionary<string, object?> { ["title"] = "x" }));

            Assert.NotEmpty(exception.Diagnostics);
            Assert.Equal("template.typ", exception.Diagnostics[0].Location!.File);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record InvoiceOptions : ITypstTemplateOptions
    {
        public required string Title { get; init; }

        public required string Number { get; init; }

        public required DateOnly Issued { get; init; }

        public required DateOnly Due { get; init; }

        public required IReadOnlyDictionary<string, object?> Seller { get; init; }

        public required IReadOnlyDictionary<string, object?> Customer { get; init; }

        public required IReadOnlyList<Dictionary<string, object?>> Items { get; init; }

        public IReadOnlyDictionary<string, object?> ToParameters() => new Dictionary<string, object?>
        {
            ["title"] = Title,
            ["number"] = Number,
            ["issued"] = Issued,
            ["due"] = Due,
            ["seller"] = Seller,
            ["customer"] = Customer,
            ["items"] = Items,
        };
    }
}
