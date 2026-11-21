using System;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The same model through the other syntax. UBL is not a second-class citizen: every term the model
/// gained with it has to survive the CII writer and the CII reader too, or a document read as UBL and
/// converted to CII would lose what it carried.
/// </summary>
public sealed class ModelParityTests
{
    private static FacturXInvoice Sample() => UblWriterTests.Sample();

    [Fact]
    public void The_full_model_survives_a_round_trip_through_cii()
    {
        var original = Sample();
        var read = FacturXReader.Read(CiiWriter.Write(original));

        AssertSameTerms(original, read);
    }

    [Fact]
    public void The_full_model_survives_a_round_trip_through_ubl()
    {
        var original = Sample();
        var read = FacturXReader.Read(UblWriter.Write(original));

        AssertSameTerms(original, read);
    }

    [Fact]
    public void The_full_model_survives_cii_to_ubl()
    {
        var original = Sample();

        // CII out, read, then UBL out and read again: a term lost in either writer shows up here.
        var throughCii = FacturXReader.Read(CiiWriter.Write(original));
        var read = FacturXReader.Read(UblWriter.Write(throughCii));

        AssertSameTerms(original, read);
    }

    [Fact]
    public void The_full_model_survives_ubl_to_cii()
    {
        var original = Sample();

        var throughUbl = FacturXReader.Read(UblWriter.Write(original));
        var read = FacturXReader.Read(CiiWriter.Write(throughUbl));

        AssertSameTerms(original, read);
    }

    [Fact]
    public void The_converted_document_still_passes_the_rules()
    {
        var throughCii = FacturXReader.Read(CiiWriter.Write(Sample()));
        var throughUbl = FacturXReader.Read(UblWriter.Write(Sample()));

        Assert.True(FacturXValidator.Validate(throughCii).IsValid);
        Assert.True(FacturXValidator.Validate(throughUbl).IsValid);
    }

    private static void AssertSameTerms(FacturXInvoice original, FacturXInvoice read)
    {
        Assert.Equal(original.Number, read.Number);
        Assert.Equal(original.TypeCode, read.TypeCode);
        Assert.Equal(original.Profile, read.Profile);
        Assert.Equal(original.Currency, read.Currency);
        Assert.Equal(original.TaxCurrency, read.TaxCurrency);
        Assert.Equal(original.IssueDate, read.IssueDate);
        Assert.Equal(original.DueDate, read.DueDate);
        Assert.Equal(original.Period?.Start, read.Period?.Start);
        Assert.Equal(original.Period?.End, read.Period?.End);
        Assert.Equal(original.BuyerReference, read.BuyerReference);
        Assert.Equal(original.OrderReference, read.OrderReference);
        Assert.Equal(original.ContractReference, read.ContractReference);
        Assert.Equal(original.ProjectReference, read.ProjectReference);
        Assert.Equal(original.PaymentTerms, read.PaymentTerms);
        Assert.Equal(original.PaymentMeansCode, read.PaymentMeansCode);
        Assert.Equal(original.PaymentReference, read.PaymentReference);
        Assert.Equal(original.Mandate?.Reference, read.Mandate?.Reference);
        Assert.Equal(original.Mandate?.DebitedAccount, read.Mandate?.DebitedAccount);
        Assert.Equal(original.Iban, read.Iban);
        Assert.Equal(original.Bic, read.Bic);

        foreach (var (expected, actual) in new[] { (original.Seller, read.Seller), (original.Buyer, read.Buyer) })
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.LegalId, actual.LegalId);
            Assert.Equal(expected.LegalIdScheme, actual.LegalIdScheme);
            Assert.Equal(expected.PartyId, actual.PartyId);
            Assert.Equal(expected.PartyIdScheme, actual.PartyIdScheme);
            Assert.Equal(expected.ElectronicAddress, actual.ElectronicAddress);
            Assert.Equal(expected.ElectronicAddressScheme, actual.ElectronicAddressScheme);
            Assert.Equal(expected.VatId, actual.VatId);
            Assert.Equal(expected.Contact?.Name, actual.Contact?.Name);
            Assert.Equal(expected.Contact?.Phone, actual.Contact?.Phone);
            Assert.Equal(expected.Contact?.Email, actual.Contact?.Email);
            Assert.Equal(expected.Line1, actual.Line1);
            Assert.Equal(expected.Postcode, actual.Postcode);
            Assert.Equal(expected.City, actual.City);
            Assert.Equal(expected.Country, actual.Country);
        }

        Assert.Equal(original.SupportingDocuments.Count, read.SupportingDocuments.Count);

        for (var index = 0; index < original.SupportingDocuments.Count; index++)
        {
            Assert.Equal(original.SupportingDocuments[index].Id, read.SupportingDocuments[index].Id);
            Assert.Equal(original.SupportingDocuments[index].Scheme, read.SupportingDocuments[index].Scheme);
            Assert.Equal(original.SupportingDocuments[index].Description, read.SupportingDocuments[index].Description);
            Assert.Equal(original.SupportingDocuments[index].Uri, read.SupportingDocuments[index].Uri);
        }

        Assert.Equal(original.AllowancesCharges.Count, read.AllowancesCharges.Count);

        for (var index = 0; index < original.AllowancesCharges.Count; index++)
        {
            var expected = original.AllowancesCharges[index];
            var actual = read.AllowancesCharges[index];

            Assert.Equal(expected.IsCharge, actual.IsCharge);
            Assert.Equal(expected.Amount, actual.Amount);
            Assert.Equal(expected.BaseAmount, actual.BaseAmount);
            Assert.Equal(expected.Percentage, actual.Percentage);
            Assert.Equal(expected.Reason, actual.Reason);
            Assert.Equal(expected.ReasonCode, actual.ReasonCode);
            Assert.Equal(expected.VatCategory, actual.VatCategory);
            Assert.Equal(expected.VatRate, actual.VatRate);
        }

        Assert.Equal(original.Lines.Count, read.Lines.Count);

        for (var index = 0; index < original.Lines.Count; index++)
        {
            var expected = original.Lines[index];
            var actual = read.Lines[index];

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.SellerAssignedId, actual.SellerAssignedId);
            Assert.Equal(expected.Quantity, actual.Quantity);
            Assert.Equal(expected.UnitCode, actual.UnitCode);
            Assert.Equal(expected.BaseQuantity, actual.BaseQuantity);
            Assert.Equal(expected.UnitPrice, actual.UnitPrice);
            Assert.Equal(expected.GrossUnitPrice, actual.GrossUnitPrice);
            Assert.Equal(expected.PriceDiscount, actual.PriceDiscount);
            Assert.Equal(expected.LineNetAmount, actual.LineNetAmount);
            Assert.Equal(expected.Period?.Start, actual.Period?.Start);
            Assert.Equal(expected.Period?.End, actual.Period?.End);
            Assert.Equal(expected.TaxCategory, actual.TaxCategory);
            Assert.Equal(expected.TaxRate, actual.TaxRate);
            Assert.Equal(expected.Classifications.Count, actual.Classifications.Count);

            for (var classification = 0; classification < expected.Classifications.Count; classification++)
            {
                Assert.Equal(expected.Classifications[classification].Code, actual.Classifications[classification].Code);
                Assert.Equal(expected.Classifications[classification].Scheme, actual.Classifications[classification].Scheme);
            }

            Assert.Equal(expected.AllowancesCharges.Count, actual.AllowancesCharges.Count);

            for (var adjustment = 0; adjustment < expected.AllowancesCharges.Count; adjustment++)
            {
                Assert.Equal(expected.AllowancesCharges[adjustment].IsCharge, actual.AllowancesCharges[adjustment].IsCharge);
                Assert.Equal(expected.AllowancesCharges[adjustment].Amount, actual.AllowancesCharges[adjustment].Amount);
                Assert.Equal(expected.AllowancesCharges[adjustment].ReasonCode, actual.AllowancesCharges[adjustment].ReasonCode);
            }
        }

        Assert.Equal(original.Totals.LineTotal, read.Totals.LineTotal);
        Assert.Equal(original.Totals.AllowanceTotal, read.Totals.AllowanceTotal);
        Assert.Equal(original.Totals.ChargeTotal, read.Totals.ChargeTotal);
        Assert.Equal(original.Totals.TaxBasisTotal, read.Totals.TaxBasisTotal);
        Assert.Equal(original.Totals.TaxTotal, read.Totals.TaxTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
        Assert.Equal(original.Totals.Due, read.Totals.Due);

        Assert.Equal(original.TaxBreakdown.Count, read.TaxBreakdown.Count);

        for (var index = 0; index < original.TaxBreakdown.Count; index++)
        {
            Assert.Equal(original.TaxBreakdown[index].Category, read.TaxBreakdown[index].Category);
            Assert.Equal(original.TaxBreakdown[index].Rate, read.TaxBreakdown[index].Rate);
            Assert.Equal(original.TaxBreakdown[index].Basis, read.TaxBreakdown[index].Basis);
            Assert.Equal(original.TaxBreakdown[index].Amount, read.TaxBreakdown[index].Amount);
        }
    }
}
