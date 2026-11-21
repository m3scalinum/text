using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using M3scalinum.Text.Templates;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>
/// The labels are data: a layout reads every word it prints from the set the caller passes, and the
/// two languages describe the same keys. No native library is involved.
/// </summary>
public sealed class TemplateLabelTests
{
    private static readonly Regex LabelReference = new(
        "labels\\.at\\(\"([a-z_]+)\"\\)", RegexOptions.CultureInvariant);

    public static TheoryData<string> Layouts => new()
    {
        TemplateIds.Invoice,
        TemplateIds.Quotation,
        TemplateIds.Statement,
        TemplateIds.Attestation,
        TemplateIds.Contract,
        TemplateIds.Payslip,
        TemplateIds.PurchaseOrder,
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_label_the_layout_reads_is_in_both_sets(string id)
    {
        var read = Referenced(id);
        Assert.NotEmpty(read);

        foreach (var language in Enum.GetValues<TemplateLanguage>())
        {
            var labels = TemplateLabels.For(id, language);
            var missing = read.Where(key => !labels.ContainsKey(key)).ToList();

            Assert.Empty(missing);
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_two_languages_describe_the_same_keys(string id)
    {
        var english = TemplateLabels.For(id, TemplateLanguage.English);
        var french = TemplateLabels.For(id, TemplateLanguage.French);

        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            french.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.All(english.Values, value => Assert.IsType<string>(value));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_french_set_is_not_a_copy_of_the_english_one(string id)
    {
        var english = TemplateLabels.For(id, TemplateLanguage.English);
        var french = TemplateLabels.For(id, TemplateLanguage.French);

        var differing = english.Count(pair => !string.Equals(
            (string?)pair.Value,
            (string?)french[pair.Key],
            StringComparison.Ordinal));

        Assert.True(differing > 1, $"'{id}' translates only {differing} label(s)");
    }

    [Fact]
    public void The_layout_source_carries_no_uppercase_english_word()
    {
        // The labels are the only place a word can live: a layout that spells one out would render
        // the same text in both languages, which is the defect this pack exists to avoid.
        foreach (var id in new[]
        {
            TemplateIds.Invoice,
            TemplateIds.Quotation,
            TemplateIds.Statement,
            TemplateIds.Attestation,
            TemplateIds.Contract,
            TemplateIds.Payslip,
            TemplateIds.PurchaseOrder,
        })
        {
            var source = Samples.Source(id);

            foreach (var word in new[]
            {
                "INVOICE",
                "QUOTATION",
                "ACCOUNT STATEMENT",
                "CONTRACT",
                "PAYSLIP",
                "PURCHASE ORDER",
                "Bill to",
                "Subtotal",
            })
            {
                Assert.DoesNotContain(word, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void For_throws_the_typed_template_exception_for_an_unknown_layout()
    {
        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateLabels.For("delivery-note", TemplateLanguage.English));

        Assert.Equal("delivery-note", exception.TemplateName);
    }

    [Fact]
    public void For_rejects_a_blank_id()
    {
        Assert.ThrowsAny<ArgumentException>(() => TemplateLabels.For("", TemplateLanguage.English));
    }

    [Fact]
    public void For_rejects_an_undefined_language()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TemplateLabels.For(TemplateIds.Invoice, (TemplateLanguage)99));
    }

    [Fact]
    public void For_ignores_the_case_of_an_id()
    {
        var labels = TemplateLabels.For("InVoIcE", TemplateLanguage.French);

        Assert.Equal("FACTURE", labels["document"]);
    }

    [Fact]
    public void The_french_invoice_uses_french_conventions()
    {
        var labels = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French);

        Assert.Equal("N° de facture", labels["invoice_number"]);
        Assert.Equal("Net à payer", labels["balance_due"]);
        Assert.Equal("[day]/[month]/[year]", labels["date_format"]);
        Assert.Equal(",", labels["decimal_separator"]);
        Assert.Equal(" ", labels["thousand_separator"]);
    }

    [Fact]
    public void The_english_invoice_uses_english_conventions()
    {
        var labels = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.English);

        Assert.Equal("Invoice no.", labels["invoice_number"]);
        Assert.Equal("Balance due", labels["balance_due"]);
        Assert.Equal("[month repr:long] [day], [year]", labels["date_format"]);
        Assert.Equal(".", labels["decimal_separator"]);
        Assert.Equal(",", labels["thousand_separator"]);
    }

    [Fact]
    public void Every_set_returns_a_fresh_dictionary()
    {
        var first = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.English);
        var second = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.English);

        Assert.NotSame(first, second);
        Assert.Equal(first.Count, second.Count);
    }

    private static HashSet<string> Referenced(string id)
        => LabelReference
            .Matches(Samples.Source(id))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
