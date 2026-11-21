using System;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The UBL writer emits the other syntax of the same model. What matters is that a receiver reads
/// it back as the same invoice and that the element order — a schema sequence, not a style — is the
/// one OASIS UBL 2.1 fixes.
/// </summary>
public sealed class UblWriterTests
{
    /// <summary>A document that uses every term the writer gained, so the round trip proves them.</summary>
    internal static FacturXInvoice Sample() => new()
    {
        Number = "FA-2026-0100",
        Profile = FacturXProfile.En16931,
        Currency = "EUR",
        TaxCurrency = "EUR",
        IssueDate = new DateOnly(2026, 4, 5),
        DueDate = new DateOnly(2026, 5, 5),
        Period = new FacturXPeriod { Start = new DateOnly(2026, 4, 1), End = new DateOnly(2026, 4, 30) },
        BuyerReference = "SE-99",
        OrderReference = "PO-1",
        ContractReference = "CT-9",
        ProjectReference = "PRJ-7",
        PaymentTerms = "Paiement à 30 jours",
        PaymentMeansCode = "59",
        PaymentReference = "RF18539007547034",
        Mandate = new FacturXMandate
        {
            Reference = "MANDATE-1",
            DebitedAccount = "FR1420041010050500013M02606",
        },
        Iban = "FR7630006000011234567890189",
        Bic = "AGRIFRPP",
        Seller = new FacturXParty
        {
            Name = "Atelier Typographique",
            LegalId = "123456789",
            LegalIdScheme = "0002",
            PartyId = "12345678900011",
            PartyIdScheme = "0009",
            ElectronicAddress = "factures@atelier.fr",
            ElectronicAddressScheme = "0225",
            VatId = "FR40123456789",
            Contact = new FacturXContact
            {
                Name = "Jeanne Martin",
                Phone = "+33123456789",
                Email = "jeanne@atelier.fr",
            },
            Line1 = "12 rue des Glyphes",
            Postcode = "75011",
            City = "Paris",
            Country = "FR",
        },
        Buyer = new FacturXParty
        {
            Name = "Société Exemple",
            VatId = "FR55987654321",
            Contact = new FacturXContact { Name = "Marc Durand", Email = "marc@exemple.fr" },
            Line1 = "5 avenue du Test",
            Postcode = "69003",
            City = "Lyon",
            Country = "FR",
        },
        SupportingDocuments =
        [
            new FacturXSupportingDocument
            {
                Id = "TS-2026-04",
                Scheme = "ABZ",
                Description = "Feuille de temps",
                Uri = "https://example.test/ts.pdf",
            },
        ],
        Lines =
        [
            new FacturXLine
            {
                Id = "1",
                Name = "Journée de conseil",
                SellerAssignedId = "CONSULT-01",
                Classifications = [new FacturXClassification { Code = "72000000", Scheme = "CPV" }],
                Quantity = 10,
                UnitCode = "C62",
                BaseQuantity = 5,
                UnitPrice = 140.00m,
                GrossUnitPrice = 150.00m,
                PriceDiscount = 10.00m,
                LineNetAmount = 300.00m,
                Period = new FacturXPeriod { Start = new DateOnly(2026, 4, 1), End = new DateOnly(2026, 4, 15) },
                AllowancesCharges =
                [
                    new FacturXAllowanceCharge
                    {
                        IsCharge = true,
                        Amount = 20.00m,
                        Reason = "Frais de port",
                        ReasonCode = "FC",
                    },
                ],
                TaxCategory = "S",
                TaxRate = 20m,
            },
        ],
        AllowancesCharges =
        [
            new FacturXAllowanceCharge
            {
                Amount = 30.00m,
                BaseAmount = 300.00m,
                Percentage = 10m,
                Reason = "Remise fidélité",
                ReasonCode = "95",
                VatCategory = "S",
                VatRate = 20m,
            },
            new FacturXAllowanceCharge
            {
                IsCharge = true,
                Amount = 10.00m,
                Reason = "Frais de dossier",
                VatCategory = "S",
                VatRate = 20m,
            },
        ],
        TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = 280.00m, Amount = 56.00m }],
        Totals = new FacturXTotals
        {
            LineTotal = 300.00m,
            AllowanceTotal = 30.00m,
            ChargeTotal = 10.00m,
            TaxBasisTotal = 280.00m,
            TaxTotal = 56.00m,
            GrandTotal = 336.00m,
            Due = 336.00m,
        },
    };

    [Fact]
    public void The_written_document_reads_back_as_the_same_invoice()
    {
        var original = Sample();
        var read = FacturXReader.Read(UblWriter.Write(original));

        Assert.Equal(original.Number, read.Number);
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
        Assert.Equal(original.Totals.AllowanceTotal, read.Totals.AllowanceTotal);
        Assert.Equal(original.Totals.ChargeTotal, read.Totals.ChargeTotal);
        Assert.Equal(original.Totals.GrandTotal, read.Totals.GrandTotal);
    }

    [Fact]
    public void The_parties_survive_the_round_trip_with_their_contacts()
    {
        var read = FacturXReader.Read(UblWriter.Write(Sample()));

        Assert.Equal("Atelier Typographique", read.Seller.Name);
        Assert.Equal("123456789", read.Seller.LegalId);
        Assert.Equal("0002", read.Seller.LegalIdScheme);
        Assert.Equal("12345678900011", read.Seller.PartyId);
        Assert.Equal("0009", read.Seller.PartyIdScheme);
        Assert.Equal("factures@atelier.fr", read.Seller.ElectronicAddress);
        Assert.Equal("0225", read.Seller.ElectronicAddressScheme);
        Assert.Equal("FR40123456789", read.Seller.VatId);
        Assert.Equal("Jeanne Martin", read.Seller.Contact?.Name);
        Assert.Equal("+33123456789", read.Seller.Contact?.Phone);
        Assert.Equal("jeanne@atelier.fr", read.Seller.Contact?.Email);
        Assert.Equal("75011", read.Seller.Postcode);
        Assert.Equal("Paris", read.Seller.City);
        Assert.Equal("FR", read.Seller.Country);

        Assert.Equal("Marc Durand", read.Buyer.Contact?.Name);
        Assert.Equal("marc@exemple.fr", read.Buyer.Contact?.Email);
    }

    [Fact]
    public void The_adjustments_the_periods_and_the_price_survive_the_round_trip()
    {
        var read = FacturXReader.Read(UblWriter.Write(Sample()));

        Assert.Equal(2, read.AllowancesCharges.Count);

        var allowance = read.AllowancesCharges[0];
        Assert.False(allowance.IsCharge);
        Assert.Equal(30.00m, allowance.Amount);
        Assert.Equal(300.00m, allowance.BaseAmount);
        Assert.Equal(10m, allowance.Percentage);
        Assert.Equal("Remise fidélité", allowance.Reason);
        Assert.Equal("95", allowance.ReasonCode);
        Assert.Equal("S", allowance.VatCategory);
        Assert.Equal(20m, allowance.VatRate);

        Assert.True(read.AllowancesCharges[1].IsCharge);
        Assert.Equal(10.00m, read.AllowancesCharges[1].Amount);

        var line = read.Lines[0];
        Assert.Equal(5m, line.BaseQuantity);
        Assert.Equal(150.00m, line.GrossUnitPrice);
        Assert.Equal(10.00m, line.PriceDiscount);
        Assert.Equal(140.00m, line.UnitPrice);
        Assert.Equal(new DateOnly(2026, 4, 1), line.Period?.Start);
        Assert.Equal(new DateOnly(2026, 4, 15), line.Period?.End);
        Assert.Single(line.AllowancesCharges);
        Assert.True(line.AllowancesCharges[0].IsCharge);
        Assert.Equal("FC", line.AllowancesCharges[0].ReasonCode);

        var classification = Assert.Single(line.Classifications);
        Assert.Equal("72000000", classification.Code);
        Assert.Equal("CPV", classification.Scheme);
    }

    [Fact]
    public void The_supporting_documents_survive_the_round_trip()
    {
        var read = FacturXReader.Read(UblWriter.Write(Sample()));

        var document = Assert.Single(read.SupportingDocuments);
        Assert.Equal("TS-2026-04", document.Id);
        Assert.Equal("ABZ", document.Scheme);
        Assert.Equal("Feuille de temps", document.Description);
        Assert.Equal("https://example.test/ts.pdf", document.Uri);
    }

    [Fact]
    public void A_credit_note_is_written_as_a_ubl_credit_note()
    {
        var credit = new FacturXInvoice
        {
            Number = "AV-2026-0001",
            TypeCode = "381",
            Profile = FacturXProfile.En16931,
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 4, 5),
            Seller = Sample().Seller,
            Buyer = Sample().Buyer,
            Lines =
            [
                new FacturXLine
                {
                    Id = "1",
                    Name = "Avoir",
                    Quantity = -1,
                    UnitCode = "C62",
                    UnitPrice = 100m,
                    LineNetAmount = -100m,
                    TaxCategory = "S",
                    TaxRate = 20m,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = -100m, Amount = -20m }],
            Totals = new FacturXTotals
            {
                LineTotal = -100m,
                TaxBasisTotal = -100m,
                TaxTotal = -20m,
                GrandTotal = -120m,
                Due = -120m,
            },
        };

        var document = XDocument.Parse(UblWriter.Write(credit));

        Assert.Equal("CreditNote", document.Root!.Name.LocalName);
        Assert.Equal(
            "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2",
            document.Root.Name.NamespaceName);
        Assert.Contains("<cbc:CreditNoteTypeCode>381</cbc:CreditNoteTypeCode>", UblWriter.Write(credit), StringComparison.Ordinal);
        Assert.Contains("<cbc:CreditedQuantity", UblWriter.Write(credit), StringComparison.Ordinal);
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "InvoiceLine");
    }

    [Fact]
    public void What_the_writer_produces_passes_the_rules()
    {
        var result = FacturXValidator.Validate(FacturXReader.Read(UblWriter.Write(Sample())));

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void The_root_is_in_the_invoice_namespace_with_the_cac_and_cbc_prefixes()
    {
        var document = XDocument.Parse(UblWriter.Write(Sample()));

        Assert.Equal("Invoice", document.Root!.Name.LocalName);
        Assert.Equal("urn:oasis:names:specification:ubl:schema:xsd:Invoice-2", document.Root.Name.NamespaceName);

        var xml = UblWriter.Write(Sample());
        Assert.Contains("cac:AccountingSupplierParty", xml, StringComparison.Ordinal);
        Assert.Contains("cbc:DocumentCurrencyCode", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_customization_id_carries_the_guideline_urn()
    {
        var xml = UblWriter.Write(Sample().WithProfile(FacturXProfile.Basic));

        Assert.Contains(
            "<cbc:CustomizationID>urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic</cbc:CustomizationID>",
            xml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_amount_states_the_invoice_currency()
    {
        var document = XDocument.Parse(UblWriter.Write(Sample()));

        var amounts = document.Descendants()
            .Where(element => element.Name.LocalName.EndsWith("Amount", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(amounts);
        Assert.All(amounts, amount => Assert.Equal("EUR", amount.Attribute("currencyID")?.Value));
    }

    [Fact]
    public void The_declaration_matches_the_encoding_of_the_bytes()
    {
        var bytes = UblWriter.WriteBytes(Sample());
        var prolog = System.Text.Encoding.UTF8.GetString(bytes, 0, 60);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", prolog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", prolog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_invoice_still_produces_well_formed_ubl()
    {
        var xml = UblWriter.Write(new FacturXInvoice { Number = "F-0", Profile = FacturXProfile.Minimum });

        var document = XDocument.Parse(xml);
        Assert.Equal("Invoice", document.Root!.Name.LocalName);
    }

    [Fact]
    public void The_root_elements_follow_the_order_the_official_schema_imposes()
    {
        var xml = UblWriter.Write(Sample());

        AssertInOrder(
            xml,
            "<cbc:UBLVersionID>",
            "<cbc:CustomizationID>",
            "<cbc:ID>",
            "<cbc:IssueDate>",
            "<cbc:DueDate>",
            "<cbc:InvoiceTypeCode>",
            "<cbc:DocumentCurrencyCode>",
            "<cbc:TaxCurrencyCode>",
            "<cac:InvoicePeriod>",
            "<cac:OrderReference>",
            "<cac:ContractDocumentReference>",
            "<cac:AdditionalDocumentReference>",
            "<cac:ProjectReference>",
            "<cac:AccountingSupplierParty>",
            "<cac:AccountingCustomerParty>",
            "<cac:PaymentMeans>",
            "<cac:PaymentTerms>",
            "<cac:AllowanceCharge>",
            "<cac:TaxTotal>",
            "<cac:LegalMonetaryTotal>",
            "<cac:InvoiceLine>");
    }

    [Fact]
    public void The_payment_means_follow_the_order_the_official_schema_imposes()
    {
        var xml = UblWriter.Write(Sample());

        AssertInOrder(
            xml,
            "<cbc:PaymentMeansCode>59</cbc:PaymentMeansCode>",
            "<cbc:PaymentID>",
            "<cac:PayerFinancialAccount>",
            "<cac:PayeeFinancialAccount>",
            "<cac:PaymentMandate>");
    }

    [Fact]
    public void An_allowance_charge_follows_the_order_the_official_schema_imposes()
    {
        var xml = UblWriter.Write(Sample());

        AssertInOrder(
            xml,
            "<cbc:ChargeIndicator>",
            "<cbc:AllowanceChargeReasonCode>95</cbc:AllowanceChargeReasonCode>",
            "<cbc:AllowanceChargeReason>Remise fidélité</cbc:AllowanceChargeReason>",
            "<cbc:MultiplierFactorNumeric>10</cbc:MultiplierFactorNumeric>",
            "<cbc:Amount currencyID=\"EUR\">30.00</cbc:Amount>",
            "<cbc:BaseAmount currencyID=\"EUR\">300.00</cbc:BaseAmount>",
            "<cac:TaxCategory>");
    }

    [Fact]
    public void The_legal_monetary_total_follows_the_order_the_official_schema_imposes()
    {
        var xml = UblWriter.Write(Sample());

        AssertInOrder(
            xml,
            "<cbc:LineExtensionAmount currencyID=\"EUR\">300.00</cbc:LineExtensionAmount>",
            "<cbc:TaxExclusiveAmount currencyID=\"EUR\">280.00</cbc:TaxExclusiveAmount>",
            "<cbc:TaxInclusiveAmount currencyID=\"EUR\">336.00</cbc:TaxInclusiveAmount>",
            "<cbc:AllowanceTotalAmount currencyID=\"EUR\">30.00</cbc:AllowanceTotalAmount>",
            "<cbc:ChargeTotalAmount currencyID=\"EUR\">10.00</cbc:ChargeTotalAmount>",
            "<cbc:PayableAmount currencyID=\"EUR\">336.00</cbc:PayableAmount>");
    }

    [Fact]
    public void An_invoice_line_follows_the_order_the_official_schema_imposes()
    {
        var xml = UblWriter.Write(Sample());

        AssertInOrder(
            xml,
            "<cac:InvoiceLine>",
            "<cbc:InvoicedQuantity",
            "<cbc:LineExtensionAmount",
            "<cac:InvoicePeriod>",
            "<cac:AllowanceCharge>",
            "<cac:Item>",
            "<cbc:Name>Journée de conseil</cbc:Name>",
            "<cac:SellersItemIdentification>",
            "<cac:CommodityClassification>",
            "<cac:ClassifiedTaxCategory>",
            "<cac:Price>",
            "<cbc:PriceAmount currencyID=\"EUR\">140.00</cbc:PriceAmount>",
            "<cbc:BaseQuantity unitCode=\"C62\">5</cbc:BaseQuantity>",
            "<cac:AllowanceCharge>");
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
