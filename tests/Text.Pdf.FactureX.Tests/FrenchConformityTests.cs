using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The French CIUS: the identifiers a platform routes on, and the rules that decide whether a
/// document that claims a French billing framework can be delivered.
/// </summary>
public sealed class FrenchConformityTests
{
    private static FacturXInvoice French(
        string? sellerSiren = "784671695",
        string? sellerSiret = "78467169500035",
        string? buyerSiren = "552100554",
        string? sellerElectronic = "78467169500035",
        string? buyerElectronic = "552100554",
        string? businessProcess = "B1",
        string number = "FA-2026-0042",
        string typeCode = "380",
        DateOnly? issueDate = null,
        decimal lineRate = 20m,
        decimal quantity = 1m,
        string? sellerElectronicScheme = "0009")
    {
        var basis = 100m;
        var taxAmount = Math.Round(basis * lineRate / 100m, 2, MidpointRounding.AwayFromZero);
        var grand = basis + taxAmount;

        return new FacturXInvoice
        {
            Number = number,
            TypeCode = typeCode,
            IssueDate = issueDate ?? new DateOnly(2026, 2, 1),
            Currency = "EUR",
            Profile = FacturXProfile.En16931,
            BusinessProcess = businessProcess,
            Notes =
            [
                new FacturXNote { SubjectCode = "PMT", Content = "Frais de recouvrement : 40 EUR" },
                new FacturXNote { SubjectCode = "PMD", Content = "Pénalités de retard : 3 fois le taux d'intérêt légal" },
                new FacturXNote { SubjectCode = "AAB", Content = "Escompte pour paiement anticipé : néant" },
            ],
            Seller = new FacturXParty
            {
                Name = "ACME SAS",
                Country = "FR",
                VatId = "FR12345678901",
                LegalId = sellerSiren,
                LegalIdScheme = "0002",
                PartyId = sellerSiret,
                PartyIdScheme = "0009",
                ElectronicAddress = sellerElectronic,
                ElectronicAddressScheme = sellerElectronicScheme,
            },
            Buyer = new FacturXParty
            {
                Name = "Client SARL",
                Country = "FR",
                LegalId = buyerSiren,
                LegalIdScheme = "0002",
                ElectronicAddress = buyerElectronic,
                ElectronicAddressScheme = "0002",
            },
            Lines =
            [
                new FacturXLine
                {
                    Id = "1",
                    Name = "Service",
                    Quantity = quantity,
                    UnitPrice = basis,
                    UnitCode = "C62",
                    TaxCategory = "S",
                    TaxRate = lineRate,
                    LineNetAmount = basis,
                },
            ],
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = lineRate, Basis = basis, Amount = taxAmount }],
            Totals = new FacturXTotals
            {
                LineTotal = basis,
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
    public void A_complete_french_invoice_passes()
    {
        Assert.Empty(Codes(French()));
    }

    [Fact]
    public void A_document_without_a_billing_framework_is_not_held_to_the_french_rules()
    {
        // Same invoice, no BT-23: it is an EN 16931 document, not a French one.
        Assert.Empty(Codes(French(businessProcess: null, sellerSiren: null, sellerSiret: null, buyerSiren: null, sellerElectronic: null, buyerElectronic: null)));
    }

    [Fact]
    public void A_french_invoice_without_a_seller_siren_is_reported()
    {
        Assert.Contains("BR-FR-10", Codes(French(sellerSiren: null)));
    }

    [Fact]
    public void A_siren_that_is_not_nine_digits_is_reported()
    {
        Assert.Contains("BR-FR-32", Codes(French(sellerSiren: "78467169")));
        Assert.Contains("BR-FR-32", Codes(French(buyerSiren: "55210055X")));
    }

    [Fact]
    public void A_siret_that_does_not_start_with_the_siren_is_reported()
    {
        Assert.Contains("BR-FR-09", Codes(French(sellerSiret: "12345678900035")));
    }

    [Fact]
    public void A_french_invoice_without_routing_addresses_is_reported()
    {
        Assert.Contains("BR-FR-13", Codes(French(sellerElectronic: null)));
        Assert.Contains("BR-FR-12", Codes(French(buyerElectronic: null)));
    }

    [Fact]
    public void The_identifiers_survive_a_round_trip_with_their_schemes()
    {
        var xml = CiiWriter.Write(French());
        var read = FacturXReader.Read(xml);

        Assert.Equal("B1", read.BusinessProcess);

        Assert.Equal("784671695", read.Seller.LegalId);
        Assert.Equal("0002", read.Seller.LegalIdScheme);
        Assert.Equal("78467169500035", read.Seller.PartyId);
        Assert.Equal("0009", read.Seller.PartyIdScheme);
        Assert.Equal("78467169500035", read.Seller.ElectronicAddress);
        Assert.Equal("0009", read.Seller.ElectronicAddressScheme);

        Assert.Equal("552100554", read.Buyer.LegalId);
        Assert.Equal("0002", read.Buyer.LegalIdScheme);
        Assert.Equal("552100554", read.Buyer.ElectronicAddress);
        Assert.Equal("0002", read.Buyer.ElectronicAddressScheme);
    }

    [Fact]
    public void The_written_document_places_the_elements_where_the_schema_expects_them()
    {
        var xml = CiiWriter.Write(French());

        // BT-23 precedes the guideline, and the party identifier precedes the name.
        Assert.True(
            xml.IndexOf("BusinessProcessSpecifiedDocumentContextParameter", StringComparison.Ordinal)
            < xml.IndexOf("GuidelineSpecifiedDocumentContextParameter", StringComparison.Ordinal));

        Assert.True(
            xml.IndexOf("<ram:ID schemeID=\"0009\">78467169500035</ram:ID>", StringComparison.Ordinal)
            < xml.IndexOf("<ram:Name>ACME SAS</ram:Name>", StringComparison.Ordinal));

        Assert.Contains("<ram:ID schemeID=\"0002\">784671695</ram:ID>", xml, StringComparison.Ordinal);
        Assert.Contains("<ram:URIID schemeID=\"0009\">78467169500035</ram:URIID>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ubl_credit_note_is_read_with_its_lines()
    {
        // A UBL credit note carries CreditNoteLine and CreditedQuantity, not InvoiceLine.
        const string ubl = """
            <?xml version="1.0" encoding="UTF-8"?>
            <CreditNote xmlns="urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2"
                        xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                        xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:CustomizationID>urn:cen.eu:en16931:2017</cbc:CustomizationID>
              <cbc:ID>AV-2026-0007</cbc:ID>
              <cbc:CreditNoteTypeCode>381</cbc:CreditNoteTypeCode>
              <cbc:IssueDate>2026-02-10</cbc:IssueDate>
              <cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>
              <cac:AccountingSupplierParty><cac:Party>
                <cac:PartyName><cbc:Name>Seller</cbc:Name></cac:PartyName>
                <cac:PostalAddress><cac:Country><cbc:IdentificationCode>FR</cbc:IdentificationCode></cac:Country></cac:PostalAddress>
              </cac:Party></cac:AccountingSupplierParty>
              <cac:AccountingCustomerParty><cac:Party>
                <cac:PartyName><cbc:Name>Buyer</cbc:Name></cac:PartyName>
                <cac:PostalAddress><cac:Country><cbc:IdentificationCode>FR</cbc:IdentificationCode></cac:Country></cac:PostalAddress>
              </cac:Party></cac:AccountingCustomerParty>
              <cac:CreditNoteLine>
                <cbc:ID>1</cbc:ID>
                <cbc:CreditedQuantity unitCode="C62">1</cbc:CreditedQuantity>
                <cbc:LineExtensionAmount currencyID="EUR">-100.00</cbc:LineExtensionAmount>
                <cac:Item><cbc:Name>Remise</cbc:Name></cac:Item>
                <cac:Price><cbc:PriceAmount currencyID="EUR">100.00</cbc:PriceAmount></cac:Price>
              </cac:CreditNoteLine>
              <cac:LegalMonetaryTotal>
                <cbc:LineExtensionAmount currencyID="EUR">-100.00</cbc:LineExtensionAmount>
                <cbc:TaxExclusiveAmount currencyID="EUR">-100.00</cbc:TaxExclusiveAmount>
                <cbc:TaxInclusiveAmount currencyID="EUR">-120.00</cbc:TaxInclusiveAmount>
                <cbc:PayableAmount currencyID="EUR">-120.00</cbc:PayableAmount>
              </cac:LegalMonetaryTotal>
            </CreditNote>
            """;

        var credit = FacturXReader.Read(ubl);

        Assert.True(credit.IsCreditNote);
        Assert.Equal("AV-2026-0007", credit.Number);
        Assert.Single(credit.Lines);
        Assert.Equal(-100m, credit.Lines[0].LineNetAmount);
        Assert.Equal(1m, credit.Lines[0].Quantity);
    }

    [Fact]
    public void A_french_invoice_without_the_legal_mentions_is_reported()
    {
        var invoice = French();

        // Rebuild without the notes: the three mentions are mandatory.
        var bare = new FacturXInvoice
        {
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Profile = invoice.Profile,
            BusinessProcess = invoice.BusinessProcess,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = invoice.Lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
        };

        var codes = Codes(bare);

        Assert.Contains("BR-FR-05", codes);
        Assert.Equal(3, codes.Count(code => code == "BR-FR-05"));
    }

    [Fact]
    public void A_subject_code_used_twice_is_reported()
    {
        var invoice = French();
        var duplicated = new FacturXInvoice
        {
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Profile = invoice.Profile,
            BusinessProcess = invoice.BusinessProcess,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = invoice.Lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
            Notes =
            [
                new FacturXNote { SubjectCode = "PMT", Content = "a" },
                new FacturXNote { SubjectCode = "PMT", Content = "b" },
                new FacturXNote { SubjectCode = "PMD", Content = "c" },
                new FacturXNote { SubjectCode = "AAB", Content = "d" },
            ],
        };

        Assert.Contains("BR-FR-06", Codes(duplicated));
    }

    [Fact]
    public void The_notes_keep_their_subject_codes_through_the_writer()
    {
        var xml = CiiWriter.Write(French());
        var read = FacturXReader.Read(xml);

        Assert.Equal(3, read.Notes.Count);
        Assert.Contains(read.Notes, note => note.SubjectCode == "PMT");
        Assert.Contains(read.Notes, note => note.SubjectCode == "PMD");
        Assert.Contains(read.Notes, note => note.SubjectCode == "AAB");

        // NoteType sequences Content before SubjectCode.
        Assert.True(
            xml.IndexOf("<ram:Content>Frais de recouvrement", StringComparison.Ordinal)
            < xml.IndexOf("<ram:SubjectCode>PMT</ram:SubjectCode>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ctc_fr_profile_has_its_own_guideline_urn()
    {
        Assert.Equal(
            "urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr",
            FacturXProfile.ExtendedCtcFr.ToGuidelineUrn());

        // It is a subset of EXTENDED, so the XMP level is the same.
        Assert.Equal("EXTENDED", FacturXProfile.ExtendedCtcFr.ToConformanceLevel());

        Assert.True(FacturXProfiles.TryParse(
            "urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr",
            out var parsed));
        Assert.Equal(FacturXProfile.ExtendedCtcFr, parsed);
    }

    [Fact]
    public void The_delivery_address_survives_a_round_trip()
    {
        var invoice = French();
        var withDelivery = new FacturXInvoice
        {
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Profile = invoice.Profile,
            BusinessProcess = invoice.BusinessProcess,
            Notes = invoice.Notes,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = invoice.Lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
            DeliveryAddress = new FacturXParty
            {
                Name = "Entrepôt Nord",
                Line1 = "3 rue du Dépôt",
                Postcode = "59000",
                City = "Lille",
                Country = "FR",
            },
        };

        var read = FacturXReader.Read(CiiWriter.Write(withDelivery));

        Assert.NotNull(read.DeliveryAddress);
        Assert.Equal("Entrepôt Nord", read.DeliveryAddress!.Name);
        Assert.Equal("59000", read.DeliveryAddress.Postcode);
        Assert.Equal("Lille", read.DeliveryAddress.City);
        Assert.Equal("FR", read.DeliveryAddress.Country);
    }

    [Fact]
    public void A_document_without_a_delivery_address_writes_no_ship_to_party()
    {
        var xml = CiiWriter.Write(French());

        Assert.DoesNotContain("ShipToTradeParty", xml, StringComparison.Ordinal);
        Assert.Null(FacturXReader.Read(xml).DeliveryAddress);
    }

    [Fact]
    public void The_two_mentions_without_a_structured_home_are_printed()
    {
        var invoice = French();
        var withMentions = new FacturXInvoice
        {
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Profile = invoice.Profile,
            BusinessProcess = invoice.BusinessProcess,
            Notes = invoice.Notes,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = invoice.Lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
            OperationCategory = FacturXOperationCategory.Mixed,
            VatOnDebits = true,
        };

        var markup = FacturXRenderer.BuildMarkup(withMentions, new FacturXRenderOptions());

        Assert.Contains("Nature de l'opération : livraison de biens et prestation de services", markup, StringComparison.Ordinal);
        Assert.Contains("Option pour le paiement de la taxe d'après les débits", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mentions_are_absent_when_the_caller_says_nothing()
    {
        var markup = FacturXRenderer.BuildMarkup(French(), new FacturXRenderOptions());

        Assert.DoesNotContain("Nature de l'opération", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("d'après les débits", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BR-FR-01", "FA 2026")]
    [InlineData("BR-FR-03", "1999-01-01")]
    public void The_identifier_and_the_date_are_checked(string code, string value)
    {
        var invoice = code == "BR-FR-01"
            ? French(number: value)
            : French(issueDate: DateOnly.Parse(value));

        Assert.Contains(code, Codes(invoice));
    }

    [Fact]
    public void A_document_type_code_the_french_rules_refuse_is_reported()
    {
        // 385 is legal in EN 16931 and absent from the French list.
        Assert.Contains("BR-FR-04", Codes(French(typeCode: "385")));
        Assert.Empty(Codes(French(typeCode: "384")));
    }

    [Fact]
    public void A_billing_framework_outside_the_french_list_is_reported()
    {
        Assert.Contains("BR-FR-08", Codes(French(businessProcess: "X1")));
    }

    [Fact]
    public void The_french_vat_rates_are_narrower_than_the_european_ones()
    {
        // 12 % is a legal rate somewhere in the EU and not a French one.
        Assert.Contains("BR-FR-16", Codes(French(lineRate: 12m)));
        Assert.Empty(Codes(French(lineRate: 5.5m)));
    }

    [Fact]
    public void An_amount_with_more_than_two_decimals_is_reported()
    {
        var invoice = French();
        var precise = new FacturXInvoice
        {
            Number = invoice.Number,
            TypeCode = invoice.TypeCode,
            IssueDate = invoice.IssueDate,
            Currency = invoice.Currency,
            Profile = invoice.Profile,
            BusinessProcess = invoice.BusinessProcess,
            Notes = invoice.Notes,
            Seller = invoice.Seller,
            Buyer = invoice.Buyer,
            Lines = invoice.Lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = new FacturXTotals
            {
                LineTotal = 100.123m,
                TaxBasisTotal = 100.123m,
                TaxTotal = 20.02m,
                GrandTotal = 120.143m,
                Due = 120.143m,
            },
        };

        Assert.Contains("BR-FR-DEC-01", Codes(precise));
    }

    [Fact]
    public void A_quantity_with_more_than_four_decimals_is_reported()
    {
        Assert.Contains("BR-FR-DEC-02", Codes(French(quantity: 1.23456m)));

        // Four decimals exactly is the boundary the rule allows.
        Assert.DoesNotContain("BR-FR-DEC-02", Codes(French(quantity: 1.0000m)));
    }

    [Fact]
    public void An_email_address_declared_as_such_is_checked()
    {
        // Scheme 0225 means "e-mail"; a SIRET under it is a routing failure waiting to happen.
        Assert.Contains("BR-FR-23", Codes(French(sellerElectronic: "pas-un-mail", sellerElectronicScheme: "0225")));
        Assert.Empty(Codes(French(sellerElectronic: "facture@acme.fr", sellerElectronicScheme: "0225")));
    }

    [Fact]
    public void The_french_lists_are_the_ones_the_schematron_publishes()
    {
        Assert.Equal(7, FacturXCodeLists.FrenchVatCategories.Count);
        Assert.DoesNotContain("L", FacturXCodeLists.FrenchVatCategories);
        Assert.Equal(15, FacturXCodeLists.FrenchVatRates.Count);
        Assert.Contains(5.5m, FacturXCodeLists.FrenchVatRates);
        Assert.DoesNotContain(12m, FacturXCodeLists.FrenchVatRates);
        Assert.Equal(20, FacturXCodeLists.BillingModes.Count);
        Assert.Contains("B1", FacturXCodeLists.BillingModes);
        Assert.Equal(16, FacturXCodeLists.DocumentTypeCodes.Count);
        Assert.Equal(12, FacturXCodeLists.AttachmentCodes.Count);
        Assert.Equal(6, FacturXCodeLists.BarTreatments.Count);
    }

    [Fact]
    public void The_siren_and_siret_shapes_are_checked()
    {
        Assert.True(FacturXValidator.IsSiren("784671695"));
        Assert.False(FacturXValidator.IsSiren("78467169"));
        Assert.False(FacturXValidator.IsSiren("78467169A"));

        Assert.True(FacturXValidator.IsSiret("78467169500035"));
        Assert.False(FacturXValidator.IsSiret("7846716950003"));
    }
}
