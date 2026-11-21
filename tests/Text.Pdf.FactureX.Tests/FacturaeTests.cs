using System;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// Facturae 3.2.2 is a national schema aligned with EN 16931, not a rule overlay: it states the
/// same invoice in its own structure. These tests pin the mapping in both directions, the
/// rectificative spelling of a credit note, and the round trip.
/// </summary>
public sealed class FacturaeTests
{
    private static string Fixture() => ReaderTests.FixtureAt("national/facturae.xml");

    [Fact]
    public void Reads_a_facturae_document()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal("FA-2026-0100", invoice.Number);
        Assert.Equal("380", invoice.TypeCode);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal("EUR", invoice.TaxCurrency);
        Assert.Equal(new DateOnly(2026, 4, 5), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 5, 5), invoice.DueDate);
        Assert.Equal("Consultoría editorial abril 2026", invoice.Note);
    }

    [Fact]
    public void Reads_the_two_parties_with_the_country_the_address_states()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal("Atelier Tipográfico S.L.", invoice.Seller.Name);
        Assert.Equal("ESB12345678", invoice.Seller.VatId);
        Assert.Equal("Calle de los Glifos 12", invoice.Seller.Line1);
        Assert.Equal("28001", invoice.Seller.Postcode);
        Assert.Equal("Madrid", invoice.Seller.City);
        Assert.Equal("ES", invoice.Seller.Country);
        Assert.Equal("+34912345678", invoice.Seller.Contact?.Phone);
        Assert.Equal("admin@atelier.es", invoice.Seller.Contact?.Email);

        Assert.Equal("Sociedad Ejemplo S.A.", invoice.Buyer.Name);
        Assert.Equal("ESA87654321", invoice.Buyer.VatId);
        Assert.Equal("08001", invoice.Buyer.Postcode);
        Assert.Equal("Barcelona", invoice.Buyer.City);
        Assert.Equal("ES", invoice.Buyer.Country);
    }

    [Fact]
    public void Reads_the_lines_with_their_unit_and_their_rate()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(2, invoice.Lines.Count);

        var line = invoice.Lines[0];
        Assert.Equal("Consultoría editorial", line.Name);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal("01", line.UnitCode);
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal(300m, line.LineNetAmount);
        Assert.Equal("S", line.TaxCategory);
        Assert.Equal(22m, line.TaxRate);

        Assert.Equal(10m, invoice.Lines[1].TaxRate);
        Assert.Equal("Encuadernación", invoice.Lines[1].Name);
    }

    [Fact]
    public void A_document_with_several_rates_keeps_one_tax_entry_per_rate()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(2, invoice.TaxBreakdown.Count);
        Assert.All(invoice.TaxBreakdown, tax => Assert.Equal("S", tax.Category));
        Assert.Equal(22m, invoice.TaxBreakdown[0].Rate);
        Assert.Equal(290m, invoice.TaxBreakdown[0].Basis);
        Assert.Equal(63.80m, invoice.TaxBreakdown[0].Amount);
        Assert.Equal(10m, invoice.TaxBreakdown[1].Rate);
        Assert.Equal(10m, invoice.TaxBreakdown[1].Amount);
    }

    [Fact]
    public void Reads_the_totals_the_general_discount_and_the_installment()
    {
        var invoice = FacturXReader.Read(Fixture());

        Assert.Equal(400m, invoice.Totals.LineTotal);
        Assert.Equal(10m, invoice.Totals.AllowanceTotal);
        Assert.Equal(0m, invoice.Totals.ChargeTotal);
        Assert.Equal(390m, invoice.Totals.TaxBasisTotal);
        Assert.Equal(73.80m, invoice.Totals.TaxTotal);
        Assert.Equal(463.80m, invoice.Totals.GrandTotal);
        Assert.Equal(463.80m, invoice.Totals.Due);
        Assert.Equal(0m, invoice.Totals.Prepaid);

        var adjustment = Assert.Single(invoice.AllowancesCharges);
        Assert.False(adjustment.IsCharge);
        Assert.Equal(10m, adjustment.Amount);
        Assert.Equal(2.5m, adjustment.Percentage);
        Assert.Equal("Descuento comercial", adjustment.Reason);

        // 04 is a credit transfer, which UNTDID 4461 states as 30.
        Assert.Equal("30", invoice.PaymentMeansCode);
        Assert.Equal("RF18539007547034", invoice.PaymentReference);
        Assert.Equal("ES7620770024003102575766", invoice.Iban);
        Assert.Equal("CAIXESBBXXX", invoice.Bic);
    }

    [Fact]
    public void The_written_document_reads_back_as_the_same_invoice()
    {
        var original = FacturXReader.Read(Fixture());
        var read = FacturXReader.Read(FacturaeWriter.Write(original));

        Assert.Equal(original.Number, read.Number);
        Assert.Equal(original.TypeCode, read.TypeCode);
        Assert.Equal(original.Currency, read.Currency);
        Assert.Equal(original.TaxCurrency, read.TaxCurrency);
        Assert.Equal(original.IssueDate, read.IssueDate);
        Assert.Equal(original.DueDate, read.DueDate);
        Assert.Equal(original.Note, read.Note);

        Assert.Equal(original.Seller.Name, read.Seller.Name);
        Assert.Equal(original.Seller.VatId, read.Seller.VatId);
        Assert.Equal(original.Seller.Line1, read.Seller.Line1);
        Assert.Equal(original.Seller.Postcode, read.Seller.Postcode);
        Assert.Equal(original.Seller.City, read.Seller.City);
        Assert.Equal(original.Seller.Country, read.Seller.Country);
        Assert.Equal(original.Seller.Contact?.Phone, read.Seller.Contact?.Phone);
        Assert.Equal(original.Seller.Contact?.Email, read.Seller.Contact?.Email);

        Assert.Equal(original.Buyer.Name, read.Buyer.Name);
        Assert.Equal(original.Buyer.VatId, read.Buyer.VatId);
        Assert.Equal(original.Buyer.Postcode, read.Buyer.Postcode);
        Assert.Equal(original.Buyer.City, read.Buyer.City);

        Assert.Equal(original.Lines.Count, read.Lines.Count);
        Assert.Equal(original.Lines[0].Name, read.Lines[0].Name);
        Assert.Equal(original.Lines[0].Quantity, read.Lines[0].Quantity);
        Assert.Equal(original.Lines[0].UnitCode, read.Lines[0].UnitCode);
        Assert.Equal(original.Lines[0].UnitPrice, read.Lines[0].UnitPrice);
        Assert.Equal(original.Lines[0].LineNetAmount, read.Lines[0].LineNetAmount);
        Assert.Equal(original.Lines[0].TaxRate, read.Lines[0].TaxRate);
        Assert.Equal(original.Lines[0].TaxCategory, read.Lines[0].TaxCategory);

        Assert.Equal(original.AllowancesCharges.Count, read.AllowancesCharges.Count);
        Assert.Equal(original.AllowancesCharges[0].Amount, read.AllowancesCharges[0].Amount);
        Assert.Equal(original.AllowancesCharges[0].Percentage, read.AllowancesCharges[0].Percentage);
        Assert.Equal(original.AllowancesCharges[0].Reason, read.AllowancesCharges[0].Reason);

        Assert.Equal(original.TaxBreakdown.Count, read.TaxBreakdown.Count);
        Assert.Equal(original.TaxBreakdown[0].Rate, read.TaxBreakdown[0].Rate);
        Assert.Equal(original.TaxBreakdown[0].Basis, read.TaxBreakdown[0].Basis);
        Assert.Equal(original.TaxBreakdown[0].Amount, read.TaxBreakdown[0].Amount);

        Assert.Equal(original.Totals.LineTotal, read.Totals.LineTotal);
        Assert.Equal(original.Totals.AllowanceTotal, read.Totals.AllowanceTotal);
        Assert.Equal(original.Totals.TaxBasisTotal, read.Totals.TaxBasisTotal);
        Assert.Equal(original.Totals.TaxTotal, read.Totals.TaxTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
        Assert.Equal(original.Totals.Due, read.Totals.Due);

        Assert.Equal(original.PaymentMeansCode, read.PaymentMeansCode);
        Assert.Equal(original.PaymentReference, read.PaymentReference);
        Assert.Equal(original.Iban, read.Iban);
        Assert.Equal(original.Bic, read.Bic);
    }

    [Fact]
    public void A_credit_note_is_written_as_a_rectificative_invoice_with_negative_amounts()
    {
        var credit = new FacturXInvoice
        {
            Number = "FA-2026-0100-R1",
            TypeCode = "381",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 6),
            PrecedingInvoiceNumber = "FA-2026-0100",
            Seller = new FacturXParty
            {
                Name = "Atelier Tipográfico S.L.",
                VatId = "ESB12345678",
                Line1 = "Calle de los Glifos 12",
                Postcode = "28001",
                City = "Madrid",
                Country = "ES",
            },
            Buyer = new FacturXParty
            {
                Name = "Sociedad Ejemplo S.A.",
                VatId = "ESA87654321",
                Line1 = "Calle del Test 5",
                Postcode = "08001",
                City = "Barcelona",
                Country = "ES",
            },
            Lines =
            [
                new FacturXLine
                {
                    Name = "Abono",
                    Quantity = -1m,
                    UnitPrice = 100m,
                    LineNetAmount = -100m,
                    TaxCategory = "S",
                    TaxRate = 22m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 22m, Basis = -100m, Amount = -22m }],
            Totals = new FacturXTotals
            {
                LineTotal = -100m,
                TaxBasisTotal = -100m,
                TaxTotal = -22m,
                GrandTotal = -122m,
                Due = -122m,
            },
        };

        var document = XDocument.Parse(FacturaeWriter.Write(credit));
        var invoice = document.Descendants().First(element => element.Name.LocalName == "Invoice");

        Assert.Equal("OR", invoice.Child("InvoiceHeader")!.Child("InvoiceClass")!.Value);
        Assert.Equal("FA-2026-0100", invoice.Child("InvoiceHeader")!.Child("Corrective")!.Child("InvoiceNumber")!.Value);

        var read = FacturXReader.Read(FacturaeWriter.Write(credit));
        Assert.Equal("381", read.TypeCode);
        Assert.Equal(-122m, read.Totals.GrandTotal);
        Assert.Equal(-100m, read.Totals.TaxBasisTotal);
        Assert.Equal("FA-2026-0100", read.PrecedingInvoiceNumber);
    }

    [Fact]
    public void A_corrected_invoice_of_positive_total_reads_back_as_a_correction()
    {
        var invoice = new FacturXInvoice
        {
            Number = "FA-2026-0101",
            TypeCode = "384",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 9),
            PrecedingInvoiceNumber = "FA-2026-0100",
            Seller = new FacturXParty { Name = "Atelier", VatId = "ESB12345678", Country = "ES" },
            Buyer = new FacturXParty { Name = "Ejemplo", VatId = "ESA87654321", Country = "ES" },
            Lines = [new FacturXLine { Name = "Servicio", Quantity = 1m, UnitPrice = 100m, LineNetAmount = 100m, TaxCategory = "S", TaxRate = 22m }],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 22m, Basis = 100m, Amount = 22m }],
            Totals = new FacturXTotals { LineTotal = 100m, TaxBasisTotal = 100m, TaxTotal = 22m, GrandTotal = 122m, Due = 122m },
        };

        Assert.Equal("384", FacturXReader.Read(FacturaeWriter.Write(invoice)).TypeCode);
    }

    [Fact]
    public void The_root_is_in_the_3_2_2_namespace()
    {
        var document = XDocument.Parse(FacturaeWriter.Write(FacturXReader.Read(Fixture())));

        Assert.Equal("Facturae", document.Root!.Name.LocalName);
        Assert.Equal(
            "http://www.facturae.gob.es/formato/Versiones/Facturaev3_2_2.xml",
            document.Root.Name.NamespaceName);
        Assert.Equal("3.2.2", document.Descendants().First(element => element.Name.LocalName == "SchemaVersion").Value);
    }

    [Fact]
    public void The_elements_follow_the_order_the_official_schema_imposes()
    {
        var xml = FacturaeWriter.Write(FacturXReader.Read(Fixture()));

        AssertInOrder(
            xml,
            "<fe:FileHeader>",
            "<fe:SchemaVersion>",
            "<fe:Batch>",
            "<fe:BatchIdentifier>",
            "<fe:InvoicesCount>",
            "<fe:TotalInvoicesAmount>",
            "<fe:TotalOutstandingAmount>",
            "<fe:TotalExecutableAmount>",
            "<fe:InvoiceCurrencyCode>",
            "<fe:Parties>",
            "<fe:SellerParty>",
            "<fe:TaxIdentification>",
            "<fe:LegalEntity>",
            "<fe:CorporateName>",
            "<fe:AddressInSpain>",
            "<fe:BuyerParty>",
            "<fe:Invoices>",
            "<fe:InvoiceHeader>",
            "<fe:InvoiceNumber>",
            "<fe:InvoiceDocumentType>",
            "<fe:InvoiceClass>",
            "<fe:InvoiceIssueData>",
            "<fe:IssueDate>",
            "<fe:InvoiceCurrencyCode>",
            "<fe:TaxCurrencyCode>",
            "<fe:LanguageName>",
            "<fe:TaxesOutputs>",
            "<fe:TaxTypeCode>",
            "<fe:TaxRate>",
            "<fe:TaxableBase>",
            "<fe:TaxAmount>",
            "<fe:InvoiceTotals>",
            "<fe:TotalGrossAmount>",
            "<fe:GeneralDiscounts>",
            "<fe:TotalGeneralDiscounts>",
            "<fe:TotalGrossAmountBeforeTaxes>",
            "<fe:TotalTaxOutputs>",
            "<fe:TotalTaxesWithheld>",
            "<fe:InvoiceTotal>",
            "<fe:TotalOutstandingAmount>",
            "<fe:TotalExecutableAmount>",
            "<fe:Items>",
            "<fe:InvoiceLine>",
            "<fe:ItemDescription>",
            "<fe:Quantity>",
            "<fe:UnitOfMeasure>",
            "<fe:UnitPriceWithoutTax>",
            "<fe:TotalCost>",
            "<fe:GrossAmount>",
            "<fe:PaymentDetails>",
            "<fe:Installment>",
            "<fe:InstallmentDueDate>",
            "<fe:InstallmentAmount>",
            "<fe:PaymentMeans>");
    }

    [Fact]
    public void A_foreign_party_is_written_as_an_overseas_address()
    {
        var invoice = new FacturXInvoice
        {
            Number = "FA-2026-0102",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 10),
            Seller = new FacturXParty
            {
                Name = "Atelier Typographique",
                VatId = "FR40123456789",
                Line1 = "12 rue des Glyphes",
                Postcode = "75011",
                City = "Paris",
                Country = "FR",
            },
            Buyer = new FacturXParty { Name = "Ejemplo", VatId = "ESA87654321", Country = "ES" },
            Lines = [new FacturXLine { Name = "Service", Quantity = 1m, UnitPrice = 100m, LineNetAmount = 100m, TaxCategory = "S", TaxRate = 22m }],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 22m, Basis = 100m, Amount = 22m }],
            Totals = new FacturXTotals { LineTotal = 100m, TaxBasisTotal = 100m, TaxTotal = 22m, GrandTotal = 122m, Due = 122m },
        };

        var xml = FacturaeWriter.Write(invoice);

        Assert.Contains("<fe:OverseasAddress>", xml, StringComparison.Ordinal);
        Assert.Contains("<fe:CountryCode>FR</fe:CountryCode>", xml, StringComparison.Ordinal);
        Assert.Contains("<fe:PostCodeAndTown>75011 Paris</fe:PostCodeAndTown>", xml, StringComparison.Ordinal);

        var read = FacturXReader.Read(xml);
        Assert.Equal("FR", read.Seller.Country);
        Assert.Equal("FR40123456789", read.Seller.VatId);
        Assert.Equal("75011 Paris", read.Seller.City);
    }

    [Fact]
    public void A_document_without_a_breakdown_gets_one_derived_from_its_lines()
    {
        var invoice = new FacturXInvoice
        {
            Number = "FA-2026-0103",
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 11),
            Seller = new FacturXParty { Name = "Atelier", VatId = "ESB12345678", Country = "ES" },
            Buyer = new FacturXParty { Name = "Ejemplo", VatId = "ESA87654321", Country = "ES" },
            Lines =
            [
                new FacturXLine { Name = "A", Quantity = 1m, UnitPrice = 100m, LineNetAmount = 100m, TaxCategory = "S", TaxRate = 22m },
                new FacturXLine { Name = "B", Quantity = 1m, UnitPrice = 100m, LineNetAmount = 100m, TaxCategory = "S", TaxRate = 10m },
            ],
            Totals = new FacturXTotals { LineTotal = 200m, TaxBasisTotal = 200m, TaxTotal = 32m, GrandTotal = 232m, Due = 232m },
        };

        var read = FacturXReader.Read(FacturaeWriter.Write(invoice));

        Assert.Equal(2, read.TaxBreakdown.Count);
        Assert.Equal(22m, read.TaxBreakdown[0].Amount);
        Assert.Equal(10m, read.TaxBreakdown[1].Amount);
    }

    [Fact]
    public void An_empty_invoice_still_produces_well_formed_facturae()
    {
        var xml = FacturaeWriter.Write(new FacturXInvoice { Number = "F-0" });

        Assert.Equal("Facturae", XDocument.Parse(xml).Root!.Name.LocalName);
    }

    [Fact]
    public void The_written_bytes_declare_the_encoding_they_use()
    {
        var bytes = FacturaeWriter.WriteBytes(FacturXReader.Read(Fixture()));
        var prolog = System.Text.Encoding.UTF8.GetString(bytes, 0, 60);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", prolog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", prolog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_rates_are_written_with_the_two_decimals_facturae_states()
    {
        var xml = FacturaeWriter.Write(FacturXReader.Read(Fixture()));

        Assert.Contains("<fe:TaxRate>22.00</fe:TaxRate>", xml, StringComparison.Ordinal);
        Assert.Contains("<fe:DiscountRate>2.50</fe:DiscountRate>", xml, StringComparison.Ordinal);
        Assert.Contains("<fe:DiscountReason>Descuento comercial</fe:DiscountReason>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_is_written_in_additional_data_where_facturae_states_free_text()
    {
        var document = XDocument.Parse(FacturaeWriter.Write(FacturXReader.Read(Fixture())));
        var invoice = document.Descendants().First(element => element.Name.LocalName == "Invoice");

        Assert.Null(invoice.Child("InvoiceIssueData")!.Child("InvoiceDescription"));
        Assert.Equal(
            "Consultoría editorial abril 2026",
            invoice.Child("AdditionalData")!.Child("InvoiceAdditionalInformation")!.Value);
    }

    private static void AssertInOrder(string xml, params string[] fragments)
    {
        var previous = -1;

        foreach (var fragment in fragments)
        {
            var index = xml.IndexOf(fragment, previous + 1, StringComparison.Ordinal);

            Assert.True(index >= 0, $"`{fragment}` is missing from the document or out of the order the schema imposes");
            previous = index;
        }
    }
}
