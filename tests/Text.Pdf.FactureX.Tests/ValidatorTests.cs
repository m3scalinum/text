using System;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

public sealed class ValidatorTests
{
    private static FacturXInvoice Valid() => FacturXReader.Read(ReaderTests.Fixture("invoice-cii.xml"));

    [Fact]
    public void The_fixture_passes_every_rule()
    {
        var result = FacturXValidator.Validate(Valid());

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void Both_syntaxes_validate_the_same_way()
    {
        var ubl = FacturXReader.Read(ReaderTests.Fixture("invoice-ubl.xml"));

        Assert.True(FacturXValidator.Validate(ubl).IsValid);
    }

    [Fact]
    public void A_wrong_grand_total_is_reported_against_the_arithmetic_rule()
    {
        var invoice = new FacturXInvoice
        {
            Number = "F-1",
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Seller = new FacturXParty { Name = "Seller" },
            Buyer = new FacturXParty { Name = "Buyer" },
            Lines = [new FacturXLine { Id = "1", Quantity = 1, UnitPrice = 100, LineNetAmount = 100, TaxCategory = "S", TaxRate = 20 }],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20, Basis = 100, Amount = 20 }],
            Totals = new FacturXTotals { LineTotal = 100, TaxBasisTotal = 100, TaxTotal = 20, GrandTotal = 999, Due = 999 },
        };

        var result = FacturXValidator.Validate(invoice);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "BR-CO-15");
    }

    [Fact]
    public void A_line_whose_amount_does_not_match_quantity_times_price_is_reported()
    {
        var invoice = Valid();

        var broken = new FacturXInvoice
        {
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = [new FacturXLine { Id = "1", Quantity = 3, UnitPrice = 100, LineNetAmount = 100, TaxCategory = "S", TaxRate = 20 }],
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
        };

        var result = FacturXValidator.Validate(broken);

        Assert.Contains(result.Issues, issue => issue.Code == "BR-CO-10");
    }

    [Fact]
    public void A_missing_invoice_number_is_reported()
    {
        var invoice = new FacturXInvoice
        {
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Profile = FacturXProfile.Minimum,
            Seller = new FacturXParty { Name = "Seller" },
            Buyer = new FacturXParty { Name = "Buyer" },
        };

        var result = FacturXValidator.Validate(invoice);

        Assert.Contains(result.Issues, issue => issue.Code == "BR-02");
    }

    [Fact]
    public void A_currency_that_is_not_a_code_is_reported()
    {
        var invoice = new FacturXInvoice
        {
            Number = "F-1",
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "euro",
            Profile = FacturXProfile.Minimum,
            Seller = new FacturXParty { Name = "Seller" },
            Buyer = new FacturXParty { Name = "Buyer" },
        };

        Assert.Contains(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code == "BR-05");
    }

    [Fact]
    public void An_en16931_invoice_without_lines_is_rejected_but_a_basic_wl_one_is_not()
    {
        var invoice = new FacturXInvoice
        {
            Number = "F-1",
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Profile = FacturXProfile.En16931,
            Seller = new FacturXParty { Name = "Seller", Country = "FR" },
            Buyer = new FacturXParty { Name = "Buyer", Country = "FR" },
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20, Basis = 0, Amount = 0 }],
        };

        Assert.Contains(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code == "BR-16");

        Assert.True(FacturXValidator.Validate(invoice.WithProfile(FacturXProfile.BasicWithoutLines)).IsValid);
    }

    [Fact]
    public void A_zero_rated_breakdown_without_a_reason_is_reported()
    {
        var invoice = new FacturXInvoice
        {
            Number = "F-1",
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Profile = FacturXProfile.En16931,
            Seller = new FacturXParty { Name = "Seller" },
            Buyer = new FacturXParty { Name = "Buyer" },
            Lines = [new FacturXLine { Id = "1", Quantity = 1, UnitPrice = 100, LineNetAmount = 100, TaxCategory = "E", TaxRate = 0 }],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "E", Rate = 0, Basis = 100, Amount = 0 }],
            Totals = new FacturXTotals { LineTotal = 100, TaxBasisTotal = 100, TaxTotal = 0, GrandTotal = 100, Due = 100 },
        };

        Assert.Contains(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code == "BR-E-10");
    }

    [Fact]
    public void ThrowIfInvalid_carries_every_issue()
    {
        var invoice = new FacturXInvoice { Profile = FacturXProfile.Minimum };

        var exception = Assert.Throws<FacturXValidationException>(
            () => FacturXValidator.Validate(invoice).ThrowIfInvalid());

        Assert.True(exception.Result.Issues.Count >= 3, exception.Message);
        Assert.Contains("BR-02", exception.Message, StringComparison.Ordinal);
    }
}
