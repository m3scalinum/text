using System;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The rules of the terms the model gained with UBL: the arithmetic of the allowances and charges,
/// the identifiers they need, the periods and the payment mandate. Each is decidable from the
/// document alone, which is the line this validator holds itself to.
/// </summary>
public sealed class AllowanceAndChargeTests
{
    /// <summary>
    /// A document that passes every rule: one line of 1 000, a 100 allowance, a 50 charge, so a
    /// tax basis of 950 and 190 of VAT at 20%.
    /// </summary>
    private static FacturXInvoice Valid(
        decimal allowanceAmount = 100m,
        decimal allowanceTotal = 100m,
        string? allowanceReason = "Remise",
        string? allowanceReasonCode = null,
        string? allowanceCategory = "S",
        decimal chargeAmount = 50m,
        decimal chargeTotal = 50m,
        string? chargeReason = "Port",
        string? chargeCategory = "S",
        decimal lineTotal = 1000m,
        decimal taxBasis = 950m,
        decimal taxAmount = 190m,
        FacturXPeriod? period = null,
        string? paymentReference = null,
        string? paymentMeansCode = "58",
        FacturXMandate? mandate = null,
        FacturXLine? line = null)
    {
        return new FacturXInvoice
        {
            Number = "F-1",
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Profile = FacturXProfile.En16931,
            Period = period,
            PaymentMeansCode = paymentMeansCode,
            PaymentReference = paymentReference,
            Mandate = mandate,
            Seller = new FacturXParty
            {
                Name = "Seller",
                Country = "FR",
                VatId = "FR12345678901",
            },
            Buyer = new FacturXParty { Name = "Buyer", Country = "FR" },
            Lines =
            [
                line ?? new FacturXLine
                {
                    Id = "1",
                    Name = "Service",
                    Quantity = 1,
                    UnitPrice = 1000m,
                    UnitCode = "C62",
                    TaxCategory = "S",
                    TaxRate = 20m,
                    LineNetAmount = 1000m,
                },
            ],
            AllowancesCharges =
            [
                new FacturXAllowanceCharge
                {
                    Amount = allowanceAmount,
                    BaseAmount = 1000m,
                    Percentage = 10m,
                    Reason = allowanceReason,
                    ReasonCode = allowanceReasonCode,
                    VatCategory = allowanceCategory,
                    VatRate = 20m,
                },
                new FacturXAllowanceCharge
                {
                    IsCharge = true,
                    Amount = chargeAmount,
                    Reason = chargeReason,
                    VatCategory = chargeCategory,
                    VatRate = 20m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = taxBasis, Amount = taxAmount }],
            Totals = new FacturXTotals
            {
                LineTotal = lineTotal,
                AllowanceTotal = allowanceTotal,
                ChargeTotal = chargeTotal,
                TaxBasisTotal = taxBasis,
                TaxTotal = taxAmount,
                GrandTotal = taxBasis + taxAmount,
                Due = taxBasis + taxAmount,
            },
        };
    }

    private static string[] Codes(FacturXInvoice invoice)
        => FacturXValidator.Validate(invoice).Issues.Select(issue => issue.Code).ToArray();

    [Fact]
    public void A_document_with_allowances_and_charges_passes_every_rule()
    {
        var result = FacturXValidator.Validate(Valid());

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void A_document_allowance_the_total_does_not_include_is_reported()
    {
        // BT-107 says 0 while a 100 allowance is on the document.
        Assert.Contains("BR-CO-11", Codes(Valid(allowanceTotal: 0m)));
    }

    [Fact]
    public void A_document_charge_the_total_does_not_include_is_reported()
    {
        Assert.Contains("BR-CO-12", Codes(Valid(chargeTotal: 0m)));
    }

    [Fact]
    public void A_tax_basis_that_ignores_the_adjustments_is_reported()
    {
        // 1 000 of lines, less 100, plus 50, is 950: a document that states 1 000 is wrong.
        Assert.Contains("BR-CO-13", Codes(Valid(taxBasis: 1000m)));
    }

    [Fact]
    public void An_allowance_with_neither_a_reason_nor_a_reason_code_is_reported()
    {
        Assert.Contains("BR-CO-21", Codes(Valid(allowanceReason: null)));
    }

    [Fact]
    public void A_reason_code_alone_is_enough()
    {
        Assert.DoesNotContain("BR-CO-21", Codes(Valid(allowanceReason: null, allowanceReasonCode: "95")));
    }

    [Fact]
    public void A_negative_document_allowance_is_reported()
    {
        // The indicator carries the direction; the amount stays positive.
        Assert.Contains("BR-27", Codes(Valid(allowanceAmount: -100m, allowanceTotal: -100m, taxBasis: 1150m, taxAmount: 230m)));
    }

    [Fact]
    public void An_allowance_without_a_vat_category_is_reported()
    {
        Assert.Contains("BR-32", Codes(Valid(allowanceCategory: null)));
    }

    [Fact]
    public void A_charge_without_a_vat_category_is_reported()
    {
        Assert.Contains("BR-36", Codes(Valid(chargeCategory: null)));
    }

    [Fact]
    public void A_line_adjustment_without_a_reason_is_reported()
    {
        var line = new FacturXLine
        {
            Id = "1",
            Quantity = 1,
            UnitPrice = 1050m,
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 1050m,
            AllowancesCharges = [new FacturXAllowanceCharge { Amount = 0m }],
        };

        Assert.Contains("BR-CO-22", Codes(Valid(line: line, lineTotal: 1050m, taxBasis: 1000m, taxAmount: 200m)));
    }

    [Fact]
    public void A_line_charge_enters_the_line_arithmetic()
    {
        var line = new FacturXLine
        {
            Id = "1",
            Quantity = 1,
            UnitPrice = 1000m,
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 1050m,
            AllowancesCharges =
            [
                new FacturXAllowanceCharge { IsCharge = true, Amount = 50m, Reason = "Port" },
            ],
        };

        var invoice = Valid(line: line, lineTotal: 1050m, taxBasis: 1000m, taxAmount: 200m);

        Assert.DoesNotContain("BR-CO-10", Codes(invoice));
    }

    [Fact]
    public void A_line_net_amount_that_ignores_a_line_charge_is_reported()
    {
        var line = new FacturXLine
        {
            Id = "1",
            Quantity = 1,
            UnitPrice = 1000m,
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 1000m,
            AllowancesCharges =
            [
                new FacturXAllowanceCharge { IsCharge = true, Amount = 50m, Reason = "Port" },
            ],
        };

        Assert.Contains("BR-CO-10", Codes(Valid(line: line, lineTotal: 1000m, taxBasis: 950m)));
    }

    [Fact]
    public void A_base_quantity_divides_the_price()
    {
        // 10 units at 140.00 for 5, the price being stated for a pack of five: 280.00.
        var line = new FacturXLine
        {
            Id = "1",
            Quantity = 10,
            BaseQuantity = 5,
            UnitPrice = 140m,
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 280m,
        };

        Assert.DoesNotContain("BR-CO-10", Codes(Valid(line: line, lineTotal: 280m, taxBasis: 230m, taxAmount: 46m)));
    }

    [Fact]
    public void An_invoice_period_with_neither_date_is_reported()
    {
        Assert.Contains("BR-CO-19", Codes(Valid(period: new FacturXPeriod())));
    }

    [Fact]
    public void A_line_period_with_neither_date_is_reported()
    {
        var line = new FacturXLine
        {
            Id = "1",
            Quantity = 1,
            UnitPrice = 1000m,
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 1000m,
            Period = new FacturXPeriod(),
        };

        Assert.Contains("BR-CO-20", Codes(Valid(line: line)));
    }

    [Fact]
    public void A_period_with_one_date_is_enough()
    {
        Assert.DoesNotContain(
            "BR-CO-19",
            Codes(Valid(period: new FacturXPeriod { Start = new DateOnly(2026, 1, 1) })));
    }

    [Fact]
    public void A_payment_reference_without_a_payment_means_code_is_reported()
    {
        Assert.Contains(
            "BR-49",
            Codes(Valid(paymentReference: "RF18539007547034", paymentMeansCode: null)));
    }

    [Fact]
    public void A_mandate_without_a_payment_means_code_is_reported()
    {
        Assert.Contains(
            "BR-49",
            Codes(Valid(mandate: new FacturXMandate { Reference = "M-1" }, paymentMeansCode: null)));
    }
}
