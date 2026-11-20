using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The rules that decide whether a receiver can book the document: closed code lists, the
/// breakdown matching the lines, and the credit note, which is the same document with the
/// opposite sign.
/// </summary>
public sealed class CodeListAndRuleTests
{
    /// <summary>A document that passes every rule, with the knobs each test turns.</summary>
    private static FacturXInvoice Valid(
        string typeCode = "380",
        string? sellerCountry = "FR",
        string? sellerVatId = "FR12345678901",
        string? paymentMeans = null,
        string? iban = null,
        string lineCategory = "S",
        decimal? lineRate = 20,
        string unitCode = "C62",
        decimal quantity = 1,
        decimal lineNet = 100,
        string taxCategory = "S",
        decimal? taxRate = 20,
        decimal basis = 100,
        decimal taxAmount = 20,
        string? exemptionReason = null)
    {
        var grand = basis + taxAmount;

        return new FacturXInvoice
        {
            Number = "F-1",
            TypeCode = typeCode,
            IssueDate = new DateOnly(2026, 1, 2),
            Currency = "EUR",
            Profile = FacturXProfile.En16931,
            PaymentMeansCode = paymentMeans,
            Iban = iban,
            Seller = new FacturXParty { Name = "Seller", Country = sellerCountry, VatId = sellerVatId },
            Buyer = new FacturXParty { Name = "Buyer", Country = "FR" },
            Lines =
            [
                new FacturXLine
                {
                    Id = "1",
                    Name = "Service",
                    Quantity = quantity,
                    UnitPrice = 100,
                    UnitCode = unitCode,
                    TaxCategory = lineCategory,
                    TaxRate = lineRate,
                    LineNetAmount = lineNet,
                },
            ],
            TaxBreakdown =
            [
                new FacturXTaxBreakdown
                {
                    Category = taxCategory,
                    Rate = taxRate,
                    Basis = basis,
                    Amount = taxAmount,
                    ExemptionReason = exemptionReason,
                },
            ],
            Totals = new FacturXTotals
            {
                LineTotal = lineNet,
                TaxBasisTotal = basis,
                TaxTotal = taxAmount,
                GrandTotal = grand,
                Due = grand,
            },
        };
    }

    private static string[] Codes(FacturXInvoice invoice)
        => FacturXValidator.Validate(invoice).Issues.Select(issue => issue.Code).ToArray();

    [Fact]
    public void The_reference_invoice_is_valid()
    {
        Assert.Empty(Codes(Valid()));
    }

    [Fact]
    public void A_document_type_code_that_is_not_three_digits_is_reported()
    {
        Assert.Contains("BR-04", Codes(Valid(typeCode: "38")));
    }

    [Fact]
    public void A_party_without_a_country_is_reported()
    {
        Assert.Contains("BR-08", Codes(Valid(sellerCountry: null)));
    }

    [Fact]
    public void A_vat_category_outside_the_closed_list_is_reported()
    {
        Assert.Contains("BR-CL-17", Codes(Valid(lineCategory: "X", taxCategory: "X")));
    }

    [Fact]
    public void A_payment_means_code_outside_the_closed_list_is_reported()
    {
        Assert.Contains("BR-CL-16", Codes(Valid(paymentMeans: "99")));
    }

    [Fact]
    public void A_known_payment_means_code_is_accepted()
    {
        Assert.Empty(Codes(Valid(paymentMeans: "58")));
    }

    [Fact]
    public void A_standard_rated_breakdown_without_a_rate_is_reported()
    {
        Assert.Contains("BR-S-08", Codes(Valid(taxRate: 0, taxAmount: 0)));
    }

    [Fact]
    public void An_exempt_category_carrying_a_rate_is_reported()
    {
        var invoice = Valid(
            lineCategory: "Z",
            lineRate: 20,
            taxCategory: "Z",
            taxRate: 20,
            exemptionReason: "Export");

        Assert.Contains("BR-Z-08", Codes(invoice));
    }

    [Fact]
    public void A_breakdown_that_does_not_match_its_lines_is_reported()
    {
        // The line is 100, the breakdown claims 90: the classic silent error.
        Assert.Contains("BR-CO-9", Codes(Valid(basis: 90, taxAmount: 18)));
    }

    [Fact]
    public void A_standard_rated_invoice_without_a_seller_vat_id_is_reported()
    {
        Assert.Contains("BR-18", Codes(Valid(sellerVatId: null)));
    }

    [Fact]
    public void A_unit_code_of_the_wrong_shape_is_reported()
    {
        Assert.Contains("BR-CL-23", Codes(Valid(unitCode: "PIECE")));
    }

    [Fact]
    public void A_credit_note_is_recognised_by_its_type_code()
    {
        Assert.True(Valid(typeCode: "381").IsCreditNote);
        Assert.False(Valid().IsCreditNote);
    }

    [Fact]
    public void A_credit_note_with_negative_amounts_is_valid()
    {
        var credit = Valid(
            typeCode: "381",
            quantity: -1,
            lineNet: -100,
            basis: -100,
            taxAmount: -20);

        Assert.Empty(Codes(credit));
    }

    [Fact]
    public void A_credit_note_keeps_its_type_code_through_the_writer()
    {
        var xml = CiiWriter.Write(Valid(typeCode: "381"));

        Assert.Contains("<ram:TypeCode>381</ram:TypeCode>", xml, StringComparison.Ordinal);
        Assert.True(FacturXReader.Read(xml).IsCreditNote);
    }

    [Fact]
    public void The_payment_means_code_survives_a_round_trip()
    {
        var xml = CiiWriter.Write(Valid(paymentMeans: "30", iban: "FR7630006000011234567890189"));

        Assert.Equal("30", FacturXReader.Read(xml).PaymentMeansCode);
    }

    [Fact]
    public void An_iban_without_a_code_is_written_as_a_sepa_transfer()
    {
        var xml = CiiWriter.Write(Valid(iban: "FR7630006000011234567890189"));

        Assert.Equal("58", FacturXReader.Read(xml).PaymentMeansCode);
    }

    [Fact]
    public void The_code_lists_are_the_ones_the_standard_publishes()
    {
        Assert.Equal(9, FacturXCodeLists.VatCategories.Count);
        Assert.True(FacturXCodeLists.IsVatCategory("AE"));
        Assert.False(FacturXCodeLists.IsVatCategory("ae"));
        Assert.True(FacturXCodeLists.IsPaymentMeansCode("58"));
        Assert.False(FacturXCodeLists.IsPaymentMeansCode("0"));
        Assert.True(FacturXCodeLists.IsCurrencyCode("EUR"));
        Assert.False(FacturXCodeLists.IsCurrencyCode("EURO"));
        Assert.True(FacturXCodeLists.IsUnitCode("C62"));
        Assert.False(FacturXCodeLists.IsUnitCode("C"));
    }
}
