using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The writer exists so an invoice built in C# can be attached to a PDF without a template. What
/// matters is that what it produces reads back as the same invoice and passes the rules.
/// </summary>
public sealed class WriterTests
{
    private static FacturXInvoice Sample() => new()
    {
        Number = "FA-2026-0099",
        Profile = FacturXProfile.En16931,
        Currency = "EUR",
        IssueDate = new DateOnly(2026, 4, 5),
        DueDate = new DateOnly(2026, 5, 5),
        BuyerReference = "SE-99",
        Note = "Livraison en une fois.",
        PaymentTerms = "Paiement à 30 jours",
        Iban = "FR7630006000011234567890189",
        Bic = "AGRIFRPP",
        Seller = new FacturXParty
        {
            Name = "Atelier Typographique",
            LegalId = "123456789",
            VatId = "FR40123456789",
            Line1 = "12 rue des Glyphes",
            Postcode = "75011",
            City = "Paris",
            Country = "FR",
        },
        Buyer = new FacturXParty
        {
            Name = "Société Exemple",
            VatId = "FR55987654321",
            Line1 = "5 avenue du Test",
            Postcode = "69003",
            City = "Lyon",
            Country = "FR",
        },
        Lines =
        [
            new FacturXLine
            {
                Id = "1",
                Name = "Journée de conseil",
                SellerAssignedId = "CONSULT-01",
                Quantity = 2,
                UnitCode = "C62",
                UnitPrice = 150.00m,
                LineNetAmount = 300.00m,
                TaxCategory = "S",
                TaxRate = 20m,
            },
        ],
        TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = 300.00m, Amount = 60.00m }],
        Totals = new FacturXTotals
        {
            LineTotal = 300.00m,
            TaxBasisTotal = 300.00m,
            TaxTotal = 60.00m,
            GrandTotal = 360.00m,
            Due = 360.00m,
        },
    };

    [Fact]
    public void The_written_document_reads_back_as_the_same_invoice()
    {
        var original = Sample();

        var xml = CiiWriter.Write(original);
        var read = FacturXReader.Read(xml);

        Assert.Equal(original.Number, read.Number);
        Assert.Equal(original.Profile, read.Profile);
        Assert.Equal(original.IssueDate, read.IssueDate);
        Assert.Equal(original.DueDate, read.DueDate);
        Assert.Equal(original.Seller.Name, read.Seller.Name);
        Assert.Equal(original.Seller.VatId, read.Seller.VatId);
        Assert.Equal(original.Buyer.Name, read.Buyer.Name);
        Assert.Equal(original.Iban, read.Iban);
        Assert.Equal(original.Bic, read.Bic);
        Assert.Equal(original.Lines.Count, read.Lines.Count);
        Assert.Equal(original.Lines[0].UnitPrice, read.Lines[0].UnitPrice);
        Assert.Equal(original.Lines[0].TaxRate, read.Lines[0].TaxRate);
        Assert.Equal(original.Totals.TaxTotal, read.Totals.TaxTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
        Assert.Equal(original.TaxBreakdown[0].Amount, read.TaxBreakdown[0].Amount);
    }

    [Fact]
    public void What_the_writer_produces_passes_the_rules()
    {
        Assert.True(FacturXValidator.Validate(FacturXReader.Read(CiiWriter.Write(Sample()))).IsValid);
    }

    [Fact]
    public void The_guideline_urn_follows_the_profile()
    {
        var document = XDocument.Parse(CiiWriter.Write(Sample().WithProfile(FacturXProfile.Basic)));
        var urn = document.Descendants()
            .Where(element => element.Name.LocalName == "ID")
            .Select(element => element.Value)
            .First(value => value.StartsWith("urn:", StringComparison.Ordinal));

        Assert.Equal("urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic", urn);
    }

    [Fact]
    public void Amounts_are_written_with_a_dot_and_two_decimals()
    {
        var xml = CiiWriter.Write(Sample());

        Assert.Contains("<ram:GrandTotalAmount>360.00</ram:GrandTotalAmount>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declaration_matches_the_encoding_of_the_bytes()
    {
        // Serializing into a StringBuilder makes XmlWriter declare utf-16 over UTF-8 bytes, and a
        // receiver that honours the declaration cannot read the file. The official validator
        // rejected exactly that.
        var bytes = CiiWriter.WriteBytes(Sample());
        var prolog = System.Text.Encoding.UTF8.GetString(bytes, 0, 60);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", prolog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", prolog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_elements_follow_the_order_the_official_schema_imposes()
    {
        // These three orderings are not stylistic: the EN 16931 schema rejects the document when
        // they are wrong, which is how each of them was found.
        var xml = CiiWriter.Write(Sample());

        Assert.True(
            xml.IndexOf("<ram:TypeCode>380</ram:TypeCode>", StringComparison.Ordinal)
            < xml.IndexOf("<ram:IssueDateTime>", StringComparison.Ordinal),
            "TypeCode must precede IssueDateTime in ExchangedDocument");

        Assert.True(
            xml.IndexOf("<ram:PostcodeCode>", StringComparison.Ordinal)
            < xml.IndexOf("<ram:LineOne>", StringComparison.Ordinal),
            "PostcodeCode must precede LineOne in the EN 16931 profile");

        var lineTax = xml.IndexOf("<ram:TypeCode>VAT</ram:TypeCode>", StringComparison.Ordinal);
        var category = xml.IndexOf("<ram:CategoryCode>", StringComparison.Ordinal);

        Assert.True(lineTax >= 0 && lineTax < category, "a line tax needs TypeCode before CategoryCode");
    }

    [Fact]
    public void A_line_item_is_in_the_reusable_aggregate_namespace()
    {
        // IncludedSupplyChainTradeLineItem is a ram: element; writing it under rsm: makes the
        // schema reject the whole transaction.
        var xml = CiiWriter.Write(Sample());

        Assert.Contains(":IncludedSupplyChainTradeLineItem", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<rsm:IncludedSupplyChainTradeLineItem", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_invoice_still_produces_well_formed_xml()
    {
        var xml = CiiWriter.Write(new FacturXInvoice { Number = "F-0", Profile = FacturXProfile.Minimum });

        var document = XDocument.Parse(xml);
        Assert.Equal("CrossIndustryInvoice", document.Root!.Name.LocalName);
    }
}
