using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>
/// Every layout, rendered by the real compiler through <c>SHARPTYPST_NATIVE_PATH</c>: page counts,
/// a string in the embedded text, and the typed exception a missing required parameter raises.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TemplateRenderTests
{
    private static readonly TypstCompiler Compiler = new();

    [Fact]
    public void The_invoice_renders_one_page_from_a_minimal_dataset()
    {
        var (pages, text, pdf) = Render(TemplateIds.Invoice, Samples.MinimalInvoice());

        Assert.Equal(1, pages);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.Contains("Audit technique", text, StringComparison.Ordinal);
        Assert.Contains("DÉSIGNATION", text, StringComparison.Ordinal);
        Assert.Contains("950,00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invoice_breaks_across_pages_for_a_realistic_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Invoice, Samples.RealisticInvoice());

        Assert.Equal(2, pages);
        Assert.Contains("Prestation de conseil 34", text, StringComparison.Ordinal);
        Assert.Contains("Prestation de conseil 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quotation_renders_one_page_from_a_minimal_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Quotation, Samples.MinimalQuotation());

        Assert.Equal(1, pages);
        Assert.Contains("Mise en service", text, StringComparison.Ordinal);
        Assert.Contains("DÉSIGNATION", text, StringComparison.Ordinal);
        Assert.Contains("1 200,00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quotation_renders_one_page_for_a_realistic_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Quotation, Samples.RealisticQuotation());

        Assert.Equal(2, pages);
        Assert.Contains("Prestation de conseil 12", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_statement_renders_one_page_from_a_minimal_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Statement, Samples.MinimalStatement());

        Assert.Equal(1, pages);
        Assert.Contains("Virement reçu", text, StringComparison.Ordinal);
        Assert.Contains("MVT-0001", text, StringComparison.Ordinal);
        Assert.Contains("12 741,25", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_statement_breaks_across_pages_for_a_realistic_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Statement, Samples.RealisticStatement());

        Assert.Equal(2, pages);
        Assert.Contains("MVT-0046", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_attestation_renders_one_page_from_a_minimal_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Attestation, Samples.MinimalAttestation());

        Assert.Equal(1, pages);
        Assert.Contains("Samir Benali", text, StringComparison.Ordinal);
        Assert.Contains("atteste par la présente que", text, StringComparison.Ordinal);
        Assert.Contains("01/09/2021", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_attestation_renders_one_page_for_a_realistic_dataset()
    {
        var (pages, text, _) = Render(TemplateIds.Attestation, Samples.RealisticAttestation());

        Assert.Equal(1, pages);
        Assert.Contains("dépôt d'un dossier de location", text, StringComparison.Ordinal);
        Assert.Contains("depuis le 01/09/2021", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TemplateIds.Invoice, TemplateLanguage.English)]
    [InlineData(TemplateIds.Invoice, TemplateLanguage.French)]
    [InlineData(TemplateIds.Quotation, TemplateLanguage.English)]
    [InlineData(TemplateIds.Quotation, TemplateLanguage.French)]
    [InlineData(TemplateIds.Statement, TemplateLanguage.English)]
    [InlineData(TemplateIds.Statement, TemplateLanguage.French)]
    [InlineData(TemplateIds.Attestation, TemplateLanguage.English)]
    [InlineData(TemplateIds.Attestation, TemplateLanguage.French)]
    public void Every_layout_renders_in_both_languages(string id, TemplateLanguage language)
    {
        var parameters = Minimal(id);
        parameters["labels"] = TemplateLabels.For(id, language);

        var (pages, text, pdf) = Render(id, parameters);

        Assert.True(pages >= 1);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void The_english_attestation_reads_as_english()
    {
        var parameters = Samples.MinimalAttestation();
        parameters["labels"] = TemplateLabels.For(TemplateIds.Attestation, TemplateLanguage.English);

        var (_, text, _) = Render(TemplateIds.Attestation, parameters);

        Assert.Contains("hereby certifies that", text, StringComparison.Ordinal);
        Assert.Contains("September", text, StringComparison.Ordinal);
        Assert.Contains("2021", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_english_labels_render_the_same_layout_in_english()
    {
        var parameters = Samples.MinimalInvoice();
        parameters["labels"] = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.English);

        var (_, text, _) = Render(TemplateIds.Invoice, parameters);

        Assert.Contains("DESCRIPTION", text, StringComparison.Ordinal);
        Assert.Contains("QTY", text, StringComparison.Ordinal);
        Assert.Contains("950.00", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DÉSIGNATION", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_french_labels_render_the_same_layout_in_french()
    {
        var (_, text, _) = Render(TemplateIds.Invoice, Samples.MinimalInvoice());

        Assert.Contains("DÉSIGNATION", text, StringComparison.Ordinal);
        Assert.Contains("QTÉ", text, StringComparison.Ordinal);
        Assert.Contains("950,00", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DESCRIPTION", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TemplateIds.Invoice, "seller")]
    [InlineData(TemplateIds.Quotation, "valid_until")]
    [InlineData(TemplateIds.Statement, "transactions")]
    [InlineData(TemplateIds.Attestation, "employee")]
    public void A_missing_required_parameter_raises_the_typed_template_exception(string id, string parameter)
    {
        var parameters = Minimal(id);
        Assert.True(parameters.Remove(parameter), $"the minimal dataset for '{id}' has no '{parameter}'");

        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateCatalog.Default.Get(id).Template.Render(Compiler, parameters));

        Assert.Equal(parameter, exception.ParameterName);
        Assert.Equal(id, exception.TemplateName);
    }

    [Fact]
    public void An_unknown_parameter_raises_the_typed_template_exception()
    {
        var parameters = Samples.MinimalInvoice();
        parameters["discount"] = "10";

        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateCatalog.Default.Get(TemplateIds.Invoice).Template.Render(Compiler, parameters));

        Assert.Equal("discount", exception.ParameterName);
    }

    [Fact]
    public void The_typed_invoice_record_renders_through_the_wrapper()
    {
        var template = TemplateCatalog.Default.Get(TemplateIds.Invoice).LoadTyped<InvoiceOptions>();

        var options = new InvoiceOptions
        {
            Title = "Facture 2026-0042",
            Number = "FA-2026-0042",
            Issued = new DateOnly(2026, 3, 1),
            Due = new DateOnly(2026, 3, 31),
            Seller = Samples.Seller(),
            Customer = Samples.Customer(),
            Items =
            [
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["description"] = "Audit technique",
                    ["quantity"] = 2,
                    ["unit_price"] = 950.00m,
                },
            ],
            Labels = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French),
        };

        using var paged = template.RenderDocument(Compiler, options);
        using var html = template.RenderDocument(Compiler, options, TypstDocumentKind.Html);

        Assert.Equal(1, paged.PageCount);
        Assert.Equal("%PDF"u8.ToArray(), paged.ExportPdf()[..4]);
        Assert.Contains("Audit technique", html.ExportText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_statement_record_renders_one_page()
    {
        var template = TemplateCatalog.Default.Get(TemplateIds.Statement).LoadTyped<StatementOptions>();

        var options = new StatementOptions
        {
            Title = "Relevé de compte 03/2026",
            Number = "RC-2026-0031",
            Issued = new DateOnly(2026, 4, 1),
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Account = Samples.Account(),
            OpeningBalance = 12500.75,
            Transactions = [],
            Labels = TemplateLabels.For(TemplateIds.Statement, TemplateLanguage.English),
        };

        using var paged = template.RenderDocument(Compiler, options);

        Assert.Equal(1, paged.PageCount);
        Assert.Equal("%PDF"u8.ToArray(), paged.ExportPdf()[..4]);
    }

    private static Dictionary<string, object?> Minimal(string id) => id switch
    {
        TemplateIds.Invoice => Samples.MinimalInvoice(),
        TemplateIds.Quotation => Samples.MinimalQuotation(),
        TemplateIds.Statement => Samples.MinimalStatement(),
        _ => Samples.MinimalAttestation(),
    };

    private static (int Pages, string Text, byte[] Pdf) Render(
        string id,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var descriptor = TemplateCatalog.Default.Get(id);

        using var paged = descriptor.Template.RenderDocument(Compiler, parameters);
        var pdf = paged.ExportPdf();

        using var html = descriptor.Template.RenderDocument(Compiler, parameters, TypstDocumentKind.Html);

        // The text export wraps at a fixed width, so a sentence can arrive split across lines; the
        // assertions are about words, not layout.
        var text = Regex.Replace(html.ExportText(), @"\s+", " ");

        return (paged.PageCount, text, pdf);
    }
}
