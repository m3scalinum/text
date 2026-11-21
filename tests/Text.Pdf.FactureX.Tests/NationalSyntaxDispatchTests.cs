using System;
using System.IO;
using System.Text;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// A caller should not have to know which syntax arrived. All four are sniffed from the root
/// element, and the probe answers without loading the tree.
/// </summary>
public sealed class NationalSyntaxDispatchTests
{
    [Theory]
    [InlineData("facturx/invoice-cii.xml", "CII")]
    [InlineData("facturx/invoice-ubl.xml", "UBL")]
    [InlineData("national/fattura-pa.xml", "FatturaPA")]
    [InlineData("national/facturae.xml", "Facturae")]
    public void The_syntax_is_sniffed_from_the_root_element(string fixture, string expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ReaderTests.FixtureAt(fixture)));

        Assert.Equal(expected, FacturXReader.PeekSyntax(stream));
    }

    [Theory]
    [InlineData("national/fattura-pa.xml", "FA-2026-0100")]
    [InlineData("national/facturae.xml", "FA-2026-0100")]
    public void The_shared_reader_returns_the_same_invoice_from_a_national_syntax(string fixture, string number)
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt(fixture));

        Assert.Equal(number, invoice.Number);
        Assert.Equal(463.80m, invoice.Totals.GrandTotal);
        Assert.Equal("Atelier", invoice.Seller.Name![..7]);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(2, invoice.TaxBreakdown.Count);
    }

    [Fact]
    public void The_two_national_syntaxes_describe_the_same_invoice()
    {
        var italian = FacturXReader.Read(ReaderTests.FixtureAt("national/fattura-pa.xml"));
        var spanish = FacturXReader.Read(ReaderTests.FixtureAt("national/facturae.xml"));

        Assert.Equal(italian.Number, spanish.Number);
        Assert.Equal(italian.IssueDate, spanish.IssueDate);
        Assert.Equal(italian.DueDate, spanish.DueDate);
        Assert.Equal(italian.Currency, spanish.Currency);
        Assert.Equal(italian.Totals.LineTotal, spanish.Totals.LineTotal);
        Assert.Equal(italian.Totals.AllowanceTotal, spanish.Totals.AllowanceTotal);
        Assert.Equal(italian.Totals.TaxBasisTotal, spanish.Totals.TaxBasisTotal);
        Assert.Equal(italian.Totals.TaxTotal, spanish.Totals.TaxTotal);
        Assert.Equal(italian.Totals.GrandTotal, spanish.Totals.GrandTotal);
        Assert.Equal(italian.Lines.Count, spanish.Lines.Count);
        Assert.Equal(italian.TaxBreakdown.Count, spanish.TaxBreakdown.Count);
        Assert.Equal(italian.TaxBreakdown[0].Rate, spanish.TaxBreakdown[0].Rate);
        Assert.Equal(italian.TaxBreakdown[1].Rate, spanish.TaxBreakdown[1].Rate);
    }

    [Fact]
    public void An_invoice_can_be_converted_from_one_national_syntax_to_the_other()
    {
        var italian = FatturaPaReader.Read(ReaderTests.FixtureAt("national/fattura-pa.xml"));
        var spanish = FacturXReader.Read(FacturaeWriter.Write(italian));

        Assert.Equal(italian.Number, spanish.Number);
        Assert.Equal(italian.Totals.GrandTotal, spanish.Totals.GrandTotal);
        Assert.Equal(italian.Totals.TaxBasisTotal, spanish.Totals.TaxBasisTotal);
        Assert.Equal(italian.Lines[0].LineNetAmount, spanish.Lines[0].LineNetAmount);
        Assert.Equal(italian.TaxBreakdown[1].Amount, spanish.TaxBreakdown[1].Amount);

        // Both syntaxes state B2B identifiers the other keeps: the Italian seller's VAT number and
        // the Spanish party's address survive, because both are EN 16931 terms.
        Assert.Equal(italian.Seller.VatId, spanish.Seller.VatId);
        Assert.Equal(italian.Seller.Name, spanish.Seller.Name);
    }

    [Fact]
    public void A_namespaced_fattura_pa_is_read_the_same_way()
    {
        // Producers differ on whether they write the namespace the schema does not declare; the
        // reader matches on local names, so both spellings are the same document.
        var namespaced = ReaderTests.FixtureAt("national/fattura-pa.xml")
            .Replace("<FatturaElettronica versione=", "<p:FatturaElettronica xmlns:p=\"urn:it:fattura\" versione=", StringComparison.Ordinal)
            .Replace("</FatturaElettronica>", "</p:FatturaElettronica>", StringComparison.Ordinal);

        var invoice = FacturXReader.Read(namespaced);

        Assert.Equal("FA-2026-0100", invoice.Number);
        Assert.Equal(463.80m, invoice.Totals.GrandTotal);
    }
}
