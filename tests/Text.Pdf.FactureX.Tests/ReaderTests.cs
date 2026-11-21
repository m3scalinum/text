using System;
using System.IO;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// One reader serves both syntaxes because it matches on local names. These tests pin the fields
/// the renderer and the validator depend on, in both.
/// </summary>
public sealed class ReaderTests
{
    internal static string Fixture(string name) => FixtureAt(Path.Combine("facturx", name));

    /// <summary>A fixture by its path below <c>tests/fixtures</c>.</summary>
    internal static string FixtureAt(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", relative);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find tests/fixtures/{relative} above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Reads_a_cii_invoice()
    {
        var invoice = FacturXReader.Read(Fixture("invoice-cii.xml"));

        Assert.Equal("FA-2026-0042", invoice.Number);
        Assert.Equal(FacturXProfile.En16931, invoice.Profile);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(new DateOnly(2026, 2, 1), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 3, 3), invoice.DueDate);
        Assert.Equal("SE-42", invoice.BuyerReference);
        Assert.Equal("PO-777", invoice.OrderReference);
    }

    [Fact]
    public void Reads_the_parties_and_the_payment_details_from_cii()
    {
        var invoice = FacturXReader.Read(Fixture("invoice-cii.xml"));

        Assert.Equal("Atelier Typographique", invoice.Seller.Name);
        Assert.Equal("FR40123456789", invoice.Seller.VatId);
        Assert.Equal("123456789", invoice.Seller.LegalId);
        Assert.Equal("75011", invoice.Seller.Postcode);
        Assert.Equal("Paris", invoice.Seller.City);
        Assert.Equal("FR", invoice.Seller.Country);

        Assert.Equal("Société Exemple", invoice.Buyer.Name);
        Assert.Equal("FR55987654321", invoice.Buyer.VatId);

        Assert.Equal("FR7630006000011234567890189", invoice.Iban);
        Assert.Equal("AGRIFRPP", invoice.Bic);
        Assert.Equal("Paiement à 30 jours", invoice.PaymentTerms);
    }

    [Fact]
    public void Reads_the_lines_and_the_totals_from_cii()
    {
        var invoice = FacturXReader.Read(Fixture("invoice-cii.xml"));

        Assert.Equal(2, invoice.Lines.Count);

        var line = invoice.Lines[0];
        Assert.Equal("1", line.Id);
        Assert.Equal("Journée de conseil", line.Name);
        Assert.Equal("CONSULT-01", line.SellerAssignedId);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal("C62", line.UnitCode);
        Assert.Equal(150.00m, line.UnitPrice);
        Assert.Equal(300.00m, line.LineNetAmount);
        Assert.Equal(20m, line.TaxRate);

        Assert.Equal(1100.00m, invoice.Totals.LineTotal);
        Assert.Equal(104.00m, invoice.Totals.TaxTotal);
        Assert.Equal(1204.00m, invoice.Totals.GrandTotal);
        Assert.Equal(1204.00m, invoice.Totals.Due);

        Assert.Equal(2, invoice.TaxBreakdown.Count);
        Assert.Equal(60.00m, invoice.TaxBreakdown[0].Amount);
        Assert.Equal(5.5m, invoice.TaxBreakdown[1].Rate);
    }

    [Fact]
    public void Reads_a_ubl_invoice_into_the_same_model()
    {
        var invoice = FacturXReader.Read(Fixture("invoice-ubl.xml"));

        Assert.Equal("FA-2026-0042", invoice.Number);
        Assert.Equal(FacturXProfile.En16931, invoice.Profile);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(new DateOnly(2026, 2, 1), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 3, 3), invoice.DueDate);
        Assert.Equal("Atelier Typographique", invoice.Seller.Name);
        Assert.Equal("FR40123456789", invoice.Seller.VatId);
        Assert.Equal("Société Exemple", invoice.Buyer.Name);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(300.00m, invoice.Lines[0].LineNetAmount);
        Assert.Equal(20m, invoice.Lines[0].TaxRate);
        Assert.Equal("FR7630006000011234567890189", invoice.Iban);
        Assert.Equal(1204.00m, invoice.Totals.GrandTotal);
        Assert.Equal(104.00m, invoice.Totals.TaxTotal);
        Assert.Equal(2, invoice.TaxBreakdown.Count);
    }

    [Fact]
    public void Both_syntaxes_produce_the_same_invoice()
    {
        var cii = FacturXReader.Read(Fixture("invoice-cii.xml"));
        var ubl = FacturXReader.Read(Fixture("invoice-ubl.xml"));

        Assert.Equal(cii.Number, ubl.Number);
        Assert.Equal(cii.IssueDate, ubl.IssueDate);
        Assert.Equal(cii.DueDate, ubl.DueDate);
        Assert.Equal(cii.Seller.Name, ubl.Seller.Name);
        Assert.Equal(cii.Seller.VatId, ubl.Seller.VatId);
        Assert.Equal(cii.Lines.Count, ubl.Lines.Count);
        Assert.Equal(cii.Lines[0].LineNetAmount, ubl.Lines[0].LineNetAmount);
        Assert.Equal(cii.Lines[1].TaxRate, ubl.Lines[1].TaxRate);
        Assert.Equal(cii.Totals.GrandTotal, ubl.Totals.GrandTotal);
        Assert.Equal(cii.TaxBreakdown.Count, ubl.TaxBreakdown.Count);
        Assert.Equal(cii.TaxBreakdown.Sum(tax => tax.Amount), ubl.TaxBreakdown.Sum(tax => tax.Amount));
    }

    [Fact]
    public void An_unknown_root_is_rejected()
    {
        var exception = Assert.Throws<FacturXFormatException>(
            () => FacturXReader.Read("<Order xmlns=\"urn:x\"><ID>1</ID></Order>"));

        Assert.Contains("CrossIndustryInvoice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_xml_is_rejected_with_the_underlying_reason()
    {
        var exception = Assert.Throws<FacturXFormatException>(
            () => FacturXReader.Read("<CrossIndustryInvoice><unclosed>"));

        Assert.Contains("well-formed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dtd_is_refused()
    {
        // An entity expansion is the classic way to turn a 1 KB invoice into gigabytes of memory.
        const string Hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;">]>
            <rsm:CrossIndustryInvoice xmlns:rsm="urn:x"><rsm:ExchangedDocument><ram:ID xmlns:ram="urn:x">&lol2;</ram:ID></rsm:ExchangedDocument></rsm:CrossIndustryInvoice>
            """;

        Assert.Throws<FacturXFormatException>(() => FacturXReader.Read(Hostile));
    }

    [Fact]
    public void The_profile_is_read_from_the_guideline_urn()
    {
        var basic = Fixture("invoice-cii.xml").Replace(
            "urn:cen.eu:en16931:2017",
            "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic",
            StringComparison.Ordinal);

        Assert.Equal(FacturXProfile.Basic, FacturXReader.Read(basic).Profile);
    }

    [Fact]
    public void The_stream_and_byte_overloads_read_the_same_invoice()
    {
        var xml = Fixture("invoice-cii.xml");

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var fromStream = FacturXReader.Read(stream);

        var fromBytes = FacturXReader.Read(System.Text.Encoding.UTF8.GetBytes(xml));

        Assert.Equal("FA-2026-0042", fromStream.Number);
        Assert.Equal(fromStream.Number, fromBytes.Number);
        Assert.Equal(fromStream.Totals.GrandTotal, fromBytes.Totals.GrandTotal);
    }

    [Fact]
    public void The_syntax_is_recognized_without_loading_the_document()
    {
        using var cii = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Fixture("invoice-cii.xml")));
        using var ubl = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Fixture("invoice-ubl.xml")));

        Assert.Equal("CII", FacturXReader.PeekSyntax(cii));
        Assert.Equal("UBL", FacturXReader.PeekSyntax(ubl));
    }

    [Fact]
    public void A_document_with_no_root_element_is_refused()
    {
        using var empty = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>"));

        var exception = Assert.Throws<FacturXFormatException>(() => FacturXReader.PeekSyntax(empty));

        // The XML reader reports the missing root as a well-formedness error; the public API must
        // surface it as its own exception, never as an XmlException.
        Assert.Contains("well-formed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_root_is_refused_by_the_syntax_probe_too()
    {
        using var order = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<Order><ID>1</ID></Order>"));

        Assert.Throws<FacturXFormatException>(() => FacturXReader.PeekSyntax(order));
    }
}
