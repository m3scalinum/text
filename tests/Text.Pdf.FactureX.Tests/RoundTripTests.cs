using System;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// A round trip is the check that the two writers and the two readers agree with the model: the
/// same invoice goes out in one syntax and comes back with its values intact. Both fixtures are
/// used, in both directions, because a value lost on the way into UBL and one lost on the way out
/// of it are different defects.
/// </summary>
public sealed class RoundTripTests
{
    private static FacturXInvoice Cii() => FacturXReader.Read(ReaderTests.Fixture("invoice-cii.xml"));

    private static FacturXInvoice Ubl() => FacturXReader.Read(ReaderTests.Fixture("invoice-ubl.xml"));

    [Fact]
    public void The_cii_fixture_survives_a_round_trip_through_the_model()
    {
        var original = Cii();
        var read = FacturXReader.Read(CiiWriter.Write(original));

        AssertSameInvoice(original, read);
    }

    [Fact]
    public void The_ubl_fixture_survives_a_round_trip_through_the_model()
    {
        var original = Ubl();
        var read = FacturXReader.Read(UblWriter.Write(original));

        AssertSameInvoice(original, read);
    }

    [Fact]
    public void The_cii_fixture_converted_to_ubl_keeps_its_semantics()
    {
        var original = Cii();
        var converted = FacturXReader.Read(UblWriter.Write(original));

        AssertSameInvoice(original, converted);
    }

    [Fact]
    public void The_ubl_fixture_converted_to_cii_keeps_its_semantics()
    {
        var original = Ubl();
        var converted = FacturXReader.Read(CiiWriter.Write(original));

        AssertSameInvoice(original, converted);
    }

    [Fact]
    public void Both_syntaxes_and_both_directions_agree_on_the_figures()
    {
        var cii = Cii();
        var ubl = Ubl();
        var ciiToUbl = FacturXReader.Read(UblWriter.Write(cii));
        var ublToCii = FacturXReader.Read(CiiWriter.Write(ubl));

        foreach (var invoice in new[] { ciiToUbl, ublToCii })
        {
            Assert.Equal(1100.00m, invoice.Totals.LineTotal);
            Assert.Equal(1100.00m, invoice.Totals.TaxBasisTotal);
            Assert.Equal(104.00m, invoice.Totals.TaxTotal);
            Assert.Equal(1204.00m, invoice.Totals.GrandTotal);
            Assert.Equal(1204.00m, invoice.Totals.Due);
            Assert.Equal(2, invoice.Lines.Count);
            Assert.Equal(300.00m, invoice.Lines[0].LineNetAmount);
            Assert.Equal(20m, invoice.Lines[0].TaxRate);
            Assert.Equal(800.00m, invoice.Lines[1].LineNetAmount);
            Assert.Equal(5.5m, invoice.Lines[1].TaxRate);
            Assert.Equal(2, invoice.TaxBreakdown.Count);
        }
    }

    [Fact]
    public void A_converted_document_still_passes_the_rules()
    {
        Assert.True(FacturXValidator.Validate(FacturXReader.Read(UblWriter.Write(Cii()))).IsValid);
        Assert.True(FacturXValidator.Validate(FacturXReader.Read(CiiWriter.Write(Ubl()))).IsValid);
    }

    private static void AssertSameInvoice(FacturXInvoice original, FacturXInvoice read)
    {
        Assert.Equal(original.Number, read.Number);
        Assert.Equal(original.TypeCode, read.TypeCode);
        Assert.Equal(original.Profile, read.Profile);
        Assert.Equal(original.Currency, read.Currency);
        Assert.Equal(original.BuyerReference, read.BuyerReference);
        Assert.Equal(original.OrderReference, read.OrderReference);

        // UBL states dates as yyyy-MM-dd and CII as yyyyMMdd: the model holds one DateOnly, so a
        // difference here is a parsing defect, not a formatting one.
        Assert.Equal(original.IssueDate, read.IssueDate);
        Assert.Equal(original.DueDate, read.DueDate);

        Assert.Equal(original.Iban, read.Iban);
        Assert.Equal(original.Bic, read.Bic);
        Assert.Equal(original.PaymentMeansCode, read.PaymentMeansCode);

        foreach (var (expected, actual) in new[] { (original.Seller, read.Seller), (original.Buyer, read.Buyer) })
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.VatId, actual.VatId);
            Assert.Equal(expected.LegalId, actual.LegalId);
            Assert.Equal(expected.PartyId, actual.PartyId);
            Assert.Equal(expected.ElectronicAddress, actual.ElectronicAddress);
            Assert.Equal(expected.Line1, actual.Line1);
            Assert.Equal(expected.Postcode, actual.Postcode);
            Assert.Equal(expected.City, actual.City);
            Assert.Equal(expected.Country, actual.Country);
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
            Assert.Equal(expected.UnitPrice, actual.UnitPrice);
            Assert.Equal(expected.LineNetAmount, actual.LineNetAmount);
            Assert.Equal(expected.TaxCategory, actual.TaxCategory);
            Assert.Equal(expected.TaxRate, actual.TaxRate);
        }

        Assert.Equal(original.TaxBreakdown.Count, read.TaxBreakdown.Count);

        for (var index = 0; index < original.TaxBreakdown.Count; index++)
        {
            Assert.Equal(original.TaxBreakdown[index].Category, read.TaxBreakdown[index].Category);
            Assert.Equal(original.TaxBreakdown[index].Rate, read.TaxBreakdown[index].Rate);
            Assert.Equal(original.TaxBreakdown[index].Basis, read.TaxBreakdown[index].Basis);
            Assert.Equal(original.TaxBreakdown[index].Amount, read.TaxBreakdown[index].Amount);
        }

        Assert.Equal(original.Totals.LineTotal, read.Totals.LineTotal);
        Assert.Equal(original.Totals.TaxBasisTotal, read.Totals.TaxBasisTotal);
        Assert.Equal(original.Totals.TaxTotal, read.Totals.TaxTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
        Assert.Equal(original.Totals.Prepaid, read.Totals.Prepaid);
        Assert.Equal(original.Totals.Due, read.Totals.Due);
    }
}
