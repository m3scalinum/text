using System;
using System.Collections.Generic;
using System.IO;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Renders the reference template through the generated record.</summary>
[Trait("Category", "Integration")]
public sealed class GeneratedTemplateTests
{
    private static string TemplateDirectory
        => Path.Combine(AppContext.BaseDirectory, "templates", "invoice");

    private static InvoiceOptions Options() => new()
    {
        Title = "Invoice 2026-0042",
        Number = "INV-2026-0042",
        Issued = new DateOnly(2026, 3, 1),
        Due = new DateOnly(2026, 3, 31),
        Seller = new Dictionary<string, object?>
        {
            ["name"] = "Northwind Systems, Inc.",
            ["logo"] = "assets/logo.svg",
            ["address"] = new[] { "1200 Market Street, Suite 400", "San Francisco, CA 94103" },
            ["legal"] = "Payment is due within 30 days of the invoice date.",
        },
        Customer = new Dictionary<string, object?>
        {
            ["name"] = "Cascade Health Group",
            ["address"] = new[] { "4820 Willamette Avenue, Floor 9", "Portland, OR 97205" },
        },
        Items = [],
    };

    [Fact]
    public void The_generated_record_carries_the_manifest_parameters()
    {
        var parameters = Options().ToParameters();

        Assert.Equal("Invoice 2026-0042", parameters["title"]);
        Assert.Equal("INV-2026-0042", parameters["number"]);
        Assert.Equal(new DateOnly(2026, 3, 1), parameters["issued"]);
        Assert.Equal(new DateOnly(2026, 3, 31), parameters["due"]);
        Assert.Equal("USD", parameters["currency"]);
        Assert.Equal(0.0825, parameters["tax_rate"]);
        Assert.Equal(0.0, parameters["amount_paid"]);
        Assert.Equal(string.Empty, parameters["notes"]);
    }

    [Fact]
    public void The_typed_template_exposes_its_manifest()
    {
        var template = TypstTemplate<InvoiceOptions>.Load(TemplateDirectory);

        Assert.Equal("invoice", template.Manifest.Name);
        Assert.Equal("template.typ", template.Manifest.Entry);
        Assert.Contains("title", template.Manifest.Parameters.Keys);
    }

    [Fact]
    public void The_typed_template_renders_a_document_for_another_format()
    {
        var template = TypstTemplate<InvoiceOptions>.Load(TemplateDirectory);

        using var document = template.RenderDocument(new TypstCompiler(), Options());

        Assert.True(document.PageCount > 0);
        Assert.NotEmpty(document.ExportPng());
    }

    [Fact]
    public void The_typed_template_loads_from_embedded_resources()
    {
        var template = TypstTemplate<InvoiceOptions>.LoadEmbedded(
            typeof(GeneratedTemplateTests).Assembly,
            "M3scalinum.Text.SharpTypst.Integration.Tests.templates.minimal");

        Assert.Equal("minimal", template.Manifest.Name);
    }

    [Fact]
    public void Renders_the_invoice_through_the_generated_record()
    {
        var template = TypstTemplate<InvoiceOptions>.Load(TemplateDirectory);

        var options = Options() with
        {
            Items =
            [
                new Dictionary<string, object?>
                {
                    ["category"] = "Hardware",
                    ["sku"] = "NX-2200",
                    ["description"] = "Edge gateway",
                    ["quantity"] = 4,
                    ["unit_price"] = 1249.00m,
                },
                new Dictionary<string, object?>
                {
                    ["category"] = "Hardware",
                    ["description"] = "Redundant power supply",
                    ["quantity"] = 4,
                    ["unit_price"] = 89.50m,
                },
            ],
        };

        var pdf = template.Render(new TypstCompiler(), options);

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
    }

    [Fact]
    public void The_manifest_and_the_record_agree_on_every_parameter()
    {
        var manifest = TypstTemplate.Load(TemplateDirectory).Manifest;
        var parameters = Options().ToParameters();

        Assert.Equal(manifest.Parameters.Count, parameters.Count);

        foreach (var name in manifest.Parameters.Keys)
        {
            Assert.True(parameters.ContainsKey(name), $"the record is missing '{name}'");
        }
    }
}
