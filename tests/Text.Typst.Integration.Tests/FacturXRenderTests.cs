using System;
using System.IO;
using System.Text;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// A Factur-X PDF is four things at once: a PDF/A-3, an attachment, an XMP extension and a page.
/// These tests check them against the real compiler.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FacturXRenderTests
{
    private static string InvoiceXml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "facturx", "invoice-cii.xml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find tests/fixtures/facturx/invoice-cii.xml above {AppContext.BaseDirectory}");
    }

    private static FacturXInvoice Invoice() => FacturXReader.Read(InvoiceXml());

    [Fact]
    public void The_pdf_is_pdf_a3_with_the_invoice_attached()
    {
        var pdf = Encoding.Latin1.GetString(FacturXRenderer.Render(Invoice()));

        Assert.StartsWith("%PDF", pdf, StringComparison.Ordinal);
        Assert.Contains("factur-x.xml", pdf, StringComparison.Ordinal);
        Assert.Contains("pdfaid", pdf, StringComparison.Ordinal);
        Assert.Contains("/EmbeddedFiles", pdf, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_xmp_declares_the_facturx_profile()
    {
        var pdf = Encoding.Latin1.GetString(FacturXRenderer.Render(Invoice()));

        Assert.Contains("urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#", pdf, StringComparison.Ordinal);
        Assert.Contains("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>", pdf, StringComparison.Ordinal);
        Assert.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>", pdf, StringComparison.Ordinal);
        Assert.Contains("<fx:DocumentType>INVOICE</fx:DocumentType>", pdf, StringComparison.Ordinal);
        Assert.Contains("<fx:Version>1.0</fx:Version>", pdf, StringComparison.Ordinal);
        Assert.Contains("Factur-X PDFA Extension Schema", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_patch_keeps_the_original_metadata_intact()
    {
        var pdf = Encoding.Latin1.GetString(FacturXRenderer.Render(Invoice()));

        // The incremental update adds; it must not drop what Typst wrote.
        Assert.Contains("pdfaid:part", pdf, StringComparison.Ordinal);
        Assert.Contains("Facture FA-2026-0042", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_change_is_carried_into_the_xmp()
    {
        var pdf = Encoding.Latin1.GetString(FacturXRenderer.Render(Invoice().WithProfile(FacturXProfile.Basic)));

        Assert.Contains("<fx:ConformanceLevel>BASIC</fx:ConformanceLevel>", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_markup_carries_the_invoice_number_and_the_amounts()
    {
        var markup = FacturXRenderer.BuildMarkup(Invoice(), new FacturXRenderOptions());

        Assert.Contains("FA-2026-0042", markup, StringComparison.Ordinal);
        Assert.Contains("1 204,00", markup, StringComparison.Ordinal);
        Assert.Contains("Atelier Typographique", markup, StringComparison.Ordinal);
        Assert.Contains("Journée de conseil", markup, StringComparison.Ordinal);
        Assert.Contains("pdf.attach", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_invoice_is_refused_before_a_pdf_is_written()
    {
        var invoice = new FacturXInvoice { Number = "F-1", Profile = FacturXProfile.En16931 };

        Assert.Throws<FacturXValidationException>(() => FacturXRenderer.Render(invoice));
    }

    [Fact]
    public void Validation_can_be_skipped_deliberately()
    {
        var invoice = Invoice();

        var pdf = FacturXRenderer.Render(
            invoice,
            CiiWriter.WriteBytes(invoice),
            new FacturXRenderOptions { Validate = false });

        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void A_different_file_name_reaches_both_the_attachment_and_the_xmp()
    {
        var pdf = Encoding.Latin1.GetString(
            FacturXRenderer.Render(Invoice(), new FacturXRenderOptions { FileName = "zugferd-invoice.xml" }));

        Assert.Contains("<fx:DocumentFileName>zugferd-invoice.xml</fx:DocumentFileName>", pdf, StringComparison.Ordinal);
        Assert.Contains("zugferd-invoice.xml", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invoice_can_be_read_back_from_the_pdf_byte_for_byte()
    {
        var xml = InvoiceXml();
        var invoice = FacturXReader.Read(xml);
        var embedded = Encoding.UTF8.GetBytes(xml);

        var pdf = FacturXRenderer.Render(invoice, embedded);

        Assert.True(FacturXReader.TryExtractXml(pdf, out var name, out var extracted));
        Assert.Equal("factur-x.xml", name);
        Assert.Equal(embedded, extracted);

        var read = FacturXReader.ReadPdf(pdf);
        Assert.Equal(invoice.Number, read.Number);
        Assert.Equal(invoice.IssueDate, read.IssueDate);
        Assert.Equal(invoice.Seller.Name, read.Seller.Name);
        Assert.Equal(invoice.Lines.Count, read.Lines.Count);
        Assert.Equal(invoice.Totals.GrandTotal, read.Totals.GrandTotal);
    }

    [Fact]
    public void A_ubl_source_is_read_back_as_the_cii_that_was_embedded()
    {
        var ubl = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(InvoiceXmlPath())!, "invoice-ubl.xml"));

        var pdf = FacturXRenderer.ReadAndRender(ubl);
        var read = FacturXReader.ReadPdf(pdf);

        // Factur-X only accepts CII, so the round trip returns the converted document, not the UBL.
        Assert.Equal("FA-2026-0042", read.Number);
        Assert.Equal(1204.00m, read.Totals.GrandTotal);
        Assert.Equal(2, read.Lines.Count);
    }

    private static string InvoiceXmlPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "facturx", "invoice-cii.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find tests/fixtures/facturx/invoice-cii.xml above {AppContext.BaseDirectory}");
    }
}
