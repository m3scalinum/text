using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The XRechnung CIUS: every rule of <see cref="XRechnungValidator"/> is broken by one document and
/// satisfied by the conformant one, and the overlay is selected by declaration.
/// </summary>
/// <remarks>
/// The rule codes and the identifiers were read from the KoSIT publication — the Schematron in
/// <c>itplr-kosit/xrechnung-schematron</c> and the specification of XRechnung 3.0.2 — and the
/// documents that break a single rule are built from the model rather than edited out of a sample,
/// because a mutation of the model is what a caller actually does wrong. The official Schematron
/// remains the reference: it needs an XSLT 2.0 processor and the XML, and it is plugged in through
/// <see cref="IInvoiceValidator"/>.
/// </remarks>
public sealed class XRechnungConformityTests
{
    /// <summary>The Peppol billing process, which is the documented default for <c>BT-23</c>.</summary>
    private const string BillingProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <summary>The conformant XRechnung invoice, the positive case for every rule.</summary>
    private static FacturXInvoice XRechnung(
        string typeCode = "380",
        string? businessProcess = BillingProcess,
        string? specificationId = XRechnungValidator.SpecificationId,
        string? buyerReference = "04011000-1234512345-06",
        string? sellerCity = "Berlin",
        string? sellerPostcode = "10115",
        string? buyerCity = "Bonn",
        string? buyerPostcode = "53113",
        FacturXParty? deliveryAddress = null,
        FacturXContact? contact = null,
        bool noContact = false,
        string? sellerContactName = "Erika Musterfrau",
        string? sellerContactPhone = "+49 30 1234567",
        string? sellerContactEmail = "rechnung@atelier-glyphe.de",
        string? sellerAddress = "rechnung@atelier-glyphe.de",
        string? buyerAddress = "04011000-1234512345-06",
        string? sellerVatId = "DE123456789",
        string? paymentMeans = "58",
        string? iban = "DE89370400440532013000",
        string? paymentTerms = null,
        FacturXMandate? mandate = null,
        string? precedingInvoice = null,
        string? taxCurrency = null,
        decimal? baseQuantity = null,
        decimal? grossPrice = null,
        decimal? priceDiscount = null,
        FacturXPeriod? invoicePeriod = null,
        FacturXPeriod? linePeriod = null,
        string taxCategory = "S",
        decimal? taxRate = 20m,
        decimal? breakdownRate = null,
        bool omitBreakdownRate = false,
        IReadOnlyList<FacturXAllowanceCharge>? allowances = null,
        string? parentLineId = null)
    {
        var lines = new[]
        {
            new FacturXLine
            {
                Id = "1",
                Name = "Typografische Leistung",
                Quantity = 1m,
                UnitCode = "C62",
                UnitPrice = 300m,
                GrossUnitPrice = grossPrice,
                PriceDiscount = priceDiscount,
                BaseQuantity = baseQuantity,
                LineNetAmount = 300m,
                Period = linePeriod,
                ParentLineId = parentLineId,
                TaxCategory = taxCategory,
                TaxRate = taxRate,
            },
        };

        var adjustments = allowances ?? [];

        var lineTotal = lines.Sum(line => line.LineNetAmount);
        var allowanceTotal = adjustments.Where(adjustment => !adjustment.IsCharge).Sum(adjustment => adjustment.Amount);
        var chargeTotal = adjustments.Where(adjustment => adjustment.IsCharge).Sum(adjustment => adjustment.Amount);
        var basis = lineTotal - allowanceTotal + chargeTotal;
        var rate = omitBreakdownRate ? null : breakdownRate ?? taxRate;
        var taxAmount = Math.Round(basis * (rate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);

        return new FacturXInvoice
        {
            Number = "XR-2026-0001",
            TypeCode = typeCode,
            Profile = FacturXProfile.En16931,
            Currency = "EUR",
            TaxCurrency = taxCurrency,
            IssueDate = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 7, 1),
            Period = invoicePeriod,
            BuyerReference = buyerReference,
            SpecificationId = specificationId,
            BusinessProcess = businessProcess,
            PrecedingInvoiceNumber = precedingInvoice,
            PaymentMeansCode = paymentMeans,
            PaymentTerms = paymentTerms,
            Iban = iban,
            Bic = "COBADEFFXXX",
            Mandate = mandate,
            Notes = [new FacturXNote { Content = "Rechnung gemäß XRechnung 3.0" }],
            Seller = new FacturXParty
            {
                Name = "Atelier Glyphe GmbH",
                LegalId = "HRB12345",
                LegalIdScheme = "0204",
                ElectronicAddress = sellerAddress,
                ElectronicAddressScheme = "0225",
                VatId = sellerVatId,
                Contact = noContact
                    ? null
                    : contact ?? new FacturXContact
                    {
                        Name = sellerContactName,
                        Phone = sellerContactPhone,
                        Email = sellerContactEmail,
                    },
                Line1 = "Chausseestraße 12",
                Postcode = sellerPostcode,
                City = sellerCity,
                Country = "DE",
            },
            Buyer = new FacturXParty
            {
                Name = "Bundesamt für Beispiel",
                ElectronicAddress = buyerAddress,
                ElectronicAddressScheme = "0204",
                Line1 = "Adenauerallee 1",
                Postcode = buyerPostcode,
                City = buyerCity,
                Country = "DE",
            },
            DeliveryAddress = deliveryAddress,
            Lines = lines,
            AllowancesCharges = adjustments,
            TaxBreakdown =
            [
                new FacturXTaxBreakdown
                {
                    Category = taxCategory,
                    Rate = rate,
                    Basis = basis,
                    Amount = taxAmount,
                    ExemptionReason = taxCategory is "S" ? null : "Steuerfrei",
                },
            ],
            Totals = new FacturXTotals
            {
                LineTotal = lineTotal,
                AllowanceTotal = allowanceTotal,
                ChargeTotal = chargeTotal,
                TaxBasisTotal = basis,
                TaxTotal = taxAmount,
                GrandTotal = basis + taxAmount,
                Due = basis + taxAmount,
            },
        };
    }

    /// <summary>Every rule code the validator can report.</summary>
    public static TheoryData<string> RuleCodes { get; } = new()
    {
        "BR-XR-01", "BR-XR-02", "BR-XR-03", "BR-XR-04", "BR-XR-05", "BR-XR-06", "BR-XR-07",
        "BR-XR-08", "BR-XR-09", "BR-XR-10", "BR-XR-11", "BR-XR-12", "BR-XR-13", "BR-XR-14",
        "BR-XR-15", "BR-XR-16", "BR-XR-17", "BR-XR-18", "BR-XR-19", "BR-XR-20", "BR-XR-21",
        "BR-XR-22", "BR-XR-23", "BR-XR-24", "BR-XR-25", "BR-XR-26", "BR-XR-27", "BR-XR-28",
        "BR-XR-29", "BR-XR-30", "BR-XR-31", "BR-XR-32", "BR-XR-33", "BR-XR-34", "BR-XR-35",
        "BR-XR-36", "BR-XR-37", "BR-XR-38", "BR-XR-39", "BR-XR-40",
    };

    [Fact]
    public void The_conformant_invoice_produces_no_issue()
    {
        var result = FacturXValidator.Validate(XRechnung());

        Assert.True(result.IsValid, result.Summary);
    }

    [Theory]
    [MemberData(nameof(RuleCodes))]
    public void The_conformant_invoice_satisfies_every_rule(string code)
    {
        Assert.DoesNotContain(FacturXValidator.Validate(XRechnung()).Issues, issue => issue.Code == code);
    }

    [Theory]
    [MemberData(nameof(RuleCodes))]
    public void Every_rule_is_broken_by_one_document_and_by_that_one_only(string code)
    {
        var issues = FacturXValidator.Validate(Broken(code), XRechnungValidator.Default).Issues;

        var german = issues
            .Where(issue => issue.Code.StartsWith("BR-XR", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(code, Assert.Single(german).Code);
    }

    /// <summary>
    /// The document that breaks one rule, and only that one: the mutations touch the term the rule
    /// is about, and the arithmetic is recomputed so the built-in rules stay quiet.
    /// </summary>
    private static FacturXInvoice Broken(string code) => code switch
    {
        // BT-24 missing.
        "BR-XR-01" => XRechnung(specificationId: null),

        // The plain European identifier instead of the German one.
        "BR-XR-02" => XRechnung(specificationId: "urn:cen.eu:en16931:2017"),

        // BT-23 missing; the CIUS is still declared through BT-24.
        "BR-XR-03" => XRechnung(businessProcess: null),

        // No Leitweg-ID to route on.
        "BR-XR-04" => XRechnung(buyerReference: null),

        // No contact at all, and therefore no name, number or address.
        "BR-XR-05" => XRechnung(noContact: true),

        "BR-XR-06" => XRechnung(sellerContactName: null),
        "BR-XR-07" => XRechnung(sellerContactPhone: null),

        // A number nobody can dial.
        "BR-XR-08" => XRechnung(sellerContactPhone: "12"),

        "BR-XR-09" => XRechnung(sellerContactEmail: null),

        // An address without an @.
        "BR-XR-10" => XRechnung(sellerContactEmail: "rechnung.atelier-glyphe.de"),

        "BR-XR-11" => XRechnung(sellerCity: null),
        "BR-XR-12" => XRechnung(sellerPostcode: null),
        "BR-XR-13" => XRechnung(buyerCity: null),
        "BR-XR-14" => XRechnung(buyerPostcode: null),

        // A deliver-to group that is there, and incomplete.
        "BR-XR-15" => XRechnung(deliveryAddress: new FacturXParty { Postcode = "53113", Country = "DE" }),
        "BR-XR-16" => XRechnung(deliveryAddress: new FacturXParty { City = "Bonn", Country = "DE" }),

        "BR-XR-17" => XRechnung(sellerAddress: null),
        "BR-XR-18" => XRechnung(buyerAddress: null),

        // A type code the CIUS does not name.
        "BR-XR-19" => XRechnung(typeCode: "999"),

        // No payment instructions at all.
        "BR-XR-20" => XRechnung(paymentMeans: null),

        // A credit transfer without an account.
        "BR-XR-21" => XRechnung(iban: null),

        // A credit transfer and a direct debit mandate in the same document.
        "BR-XR-22" => XRechnung(mandate: new FacturXMandate { Reference = "MANDATE-1" }),

        // A card payment with an account to transfer to.
        "BR-XR-23" => XRechnung(paymentMeans: "48"),

        // A card payment and a direct debit mandate.
        "BR-XR-24" => XRechnung(paymentMeans: "48", iban: null, mandate: new FacturXMandate { Reference = "MANDATE-1" }),

        // A direct debit collected under no mandate.
        "BR-XR-25" => XRechnung(paymentMeans: "59", iban: null),

        // A direct debit with a payee account.
        "BR-XR-26" => XRechnung(paymentMeans: "59", mandate: new FacturXMandate { Reference = "MANDATE-1" }),

        // One digit of the account transposed.
        "BR-XR-27" => XRechnung(iban: "DE89370400440532013001"),

        // A Skonto offer with no decimals in its percentage.
        "BR-XR-28" => XRechnung(paymentTerms: "Zahlbar innerhalb von 14 Tagen.\n#SKONTO#TAGE=14#PROZENT=2#\n"),

        // A breakdown with no rate.
        "BR-XR-29" => XRechnung(omitBreakdownRate: true),

        // A VAT category that needs an identified seller, and no seller VAT identifier.
        "BR-XR-30" => XRechnung(sellerVatId: null),

        // A price base of zero.
        "BR-XR-31" => XRechnung(baseQuantity: 0m),

        // A gross price that does not add up to the net price.
        "BR-XR-32" => XRechnung(grossPrice: 320m),

        // A line period that starts before the invoice period.
        "BR-XR-33" => XRechnung(
            invoicePeriod: new FacturXPeriod { Start = new DateOnly(2026, 5, 1), End = new DateOnly(2026, 5, 31) },
            linePeriod: new FacturXPeriod { Start = new DateOnly(2026, 4, 15), End = new DateOnly(2026, 5, 15) }),

        // A line period that ends after it.
        "BR-XR-34" => XRechnung(
            invoicePeriod: new FacturXPeriod { Start = new DateOnly(2026, 5, 1), End = new DateOnly(2026, 5, 31) },
            linePeriod: new FacturXPeriod { Start = new DateOnly(2026, 5, 1), End = new DateOnly(2026, 6, 15) }),

        // A percentage without the base it applies to.
        "BR-XR-35" => XRechnung(allowances:
        [
            new FacturXAllowanceCharge
            {
                Amount = 30m,
                Percentage = 10m,
                ReasonCode = "95",
                VatCategory = "S",
                VatRate = 20m,
            },
        ]),

        // A base amount without a percentage.
        "BR-XR-36" => XRechnung(allowances:
        [
            new FacturXAllowanceCharge
            {
                Amount = 30m,
                BaseAmount = 300m,
                ReasonCode = "95",
                VatCategory = "S",
                VatRate = 20m,
            },
        ]),

        // 10% of 300 is 30, and the document states 25.
        "BR-XR-37" => XRechnung(allowances:
        [
            new FacturXAllowanceCharge
            {
                Amount = 25m,
                BaseAmount = 300m,
                Percentage = 10m,
                ReasonCode = "95",
                VatCategory = "S",
                VatRate = 20m,
            },
        ]),

        // A corrected invoice that names what it corrects.
        "BR-XR-38" => XRechnung(typeCode: "384"),

        // A VAT accounting currency that is the invoice currency.
        "BR-XR-39" => XRechnung(taxCurrency: "EUR"),

        // A sub invoice line under the CIUS, which only its extension supports.
        "BR-XR-40" => XRechnung(
            specificationId: XRechnungValidator.ExtensionSpecificationId,
            parentLineId: "0"),

        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "no document breaks that rule"),
    };

    [Fact]
    public void A_document_that_declares_the_cius_is_held_to_it_without_being_asked()
    {
        // BT-24 alone puts the document under the CIUS.
        var invoice = XRechnung(buyerReference: null);

        Assert.Contains(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code == "BR-XR-04");
    }

    [Fact]
    public void A_document_that_declares_nothing_is_not_held_to_the_cius()
    {
        var invoice = XRechnung(
            businessProcess: null,
            specificationId: null,
            buyerReference: null,
            sellerAddress: null,
            sellerContactEmail: null);

        Assert.DoesNotContain(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code.StartsWith("BR-XR", StringComparison.Ordinal));
    }

    [Fact]
    public void The_peppol_billing_process_alone_does_not_declare_xrechnung()
    {
        // BT-23 is the buyer's process, and its documented default under XRechnung is the Peppol
        // billing process: reading it as a declaration would claim every Peppol invoice.
        Assert.False(XRechnungValidator.IsXRechnungBusinessProcess(BillingProcess));
        Assert.False(XRechnungValidator.IsXRechnungSpecificationId("urn:cen.eu:en16931:2017"));
        Assert.False(XRechnungValidator.IsXRechnungBusinessProcess(null));
        Assert.False(XRechnungValidator.IsXRechnungSpecificationId(PeppolBisValidator.SpecificationId));

        Assert.True(XRechnungValidator.IsXRechnungSpecificationId(XRechnungValidator.SpecificationId));
        Assert.True(XRechnungValidator.IsXRechnungSpecificationId(XRechnungValidator.ExtensionSpecificationId));
        Assert.True(XRechnungValidator.IsXRechnungSpecificationId(XRechnungValidator.DraftSpecificationId));
    }

    [Fact]
    public void The_xrechnung_rules_are_not_the_peppol_or_the_french_ones()
    {
        // A German document carries the Peppol billing process in BT-23 while declaring the German
        // CIUS in BT-24: it is held to XRechnung, and to neither of the other two overlays. The
        // seller electronic address is a term all three overlays look at, so it tells them apart.
        var issues = FacturXValidator.Validate(XRechnung(sellerAddress: null, buyerReference: null)).Issues;

        Assert.Contains(issues, issue => issue.Code == "BR-XR-17");
        Assert.Contains(issues, issue => issue.Code == "BR-XR-04");
        Assert.DoesNotContain(issues, issue => issue.Code.StartsWith("BR-PEPPOL", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, issue => issue.Code.StartsWith("BR-FR", StringComparison.Ordinal));
    }

    [Fact]
    public void A_peppol_document_is_not_held_to_xrechnung()
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt("peppol/base-example.xml"));

        Assert.True(PeppolBisValidator.IsPeppol(invoice));
        Assert.False(XRechnungValidator.IsXRechnung(invoice));

        Assert.DoesNotContain(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code.StartsWith("BR-XR", StringComparison.Ordinal));
    }

    [Fact]
    public void A_french_document_is_not_held_to_xrechnung()
    {
        // BT-23 states a French billing framework, which is what the French overlay gates on.
        var invoice = XRechnung(businessProcess: "B1", specificationId: null);

        var issues = FacturXValidator.Validate(invoice).Issues;

        Assert.DoesNotContain(issues, issue => issue.Code.StartsWith("BR-XR", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Code.StartsWith("BR-FR", StringComparison.Ordinal));
    }

    [Fact]
    public void The_extension_point_is_invoked_and_its_issues_are_merged()
    {
        var recorder = new RecordingValidator();
        var invoice = XRechnung(buyerReference: null);

        var result = FacturXValidator.Validate(invoice, recorder);

        Assert.Same(invoice, recorder.Seen);
        Assert.Contains(result.Issues, issue => issue.Code == "BR-XR-04");
        Assert.Contains(result.Issues, issue => issue.Code == "PARTNER-01");
    }

    [Fact]
    public void The_cius_can_be_required_from_a_document_that_does_not_declare_it()
    {
        var invoice = XRechnung(businessProcess: null, specificationId: null, buyerReference: null);

        var result = FacturXValidator.Validate(invoice, XRechnungValidator.Default);

        Assert.Contains(result.Issues, issue => issue.Code == "BR-XR-04");
        Assert.Contains(result.Issues, issue => issue.Code == "BR-XR-01");
    }

    [Fact]
    public void The_written_ubl_document_passes_the_rules_it_was_written_for()
    {
        var written = FacturXReader.Read(UblWriter.Write(XRechnung()));

        var result = FacturXValidator.Validate(written);

        Assert.True(result.IsValid, result.Summary);
        Assert.Equal(XRechnungValidator.SpecificationId, written.SpecificationId);
        Assert.Equal(BillingProcess, written.BusinessProcess);
    }

    [Fact]
    public void The_written_cii_document_carries_the_cius_identifier_and_passes_the_rules()
    {
        // CII is a published XRechnung syntax: BT-24 carries the CIUS identifier, not the Factur-X
        // guideline URN, or the document would be read as plain EN 16931.
        var xml = CiiWriter.Write(XRechnung());

        Assert.Contains(XRechnungValidator.SpecificationId, xml, StringComparison.Ordinal);

        var written = FacturXReader.Read(xml);
        var result = FacturXValidator.Validate(written);

        Assert.True(result.IsValid, result.Summary);
        Assert.Equal(XRechnungValidator.SpecificationId, written.SpecificationId);
        Assert.True(XRechnungValidator.IsXRechnung(written));
    }

    [Fact]
    public void The_contacts_and_the_addresses_survive_a_round_trip()
    {
        var written = FacturXReader.Read(CiiWriter.Write(XRechnung()));

        Assert.Equal("Erika Musterfrau", written.Seller.Contact?.Name);
        Assert.Equal("+49 30 1234567", written.Seller.Contact?.Phone);
        Assert.Equal("rechnung@atelier-glyphe.de", written.Seller.Contact?.Email);
        Assert.Equal("10115", written.Seller.Postcode);
        Assert.Equal("Bonn", written.Buyer.City);
    }

    [Fact]
    public void An_iban_is_checked_by_its_check_digits()
    {
        Assert.True(XRechnungValidator.IsIban("DE89370400440532013000"));
        Assert.True(XRechnungValidator.IsIban("DE89 3704 0044 0532 0130 00"));
        Assert.False(XRechnungValidator.IsIban("DE89370400440532013001"));
        Assert.False(XRechnungValidator.IsIban("DE89"));
        Assert.False(XRechnungValidator.IsIban("D389370400440532013000"));
    }

    private sealed class RecordingValidator : IInvoiceValidator
    {
        public FacturXInvoice? Seen { get; private set; }

        public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
        {
            Seen = invoice;
            return [new("PARTNER-01", "the partner's own rule")];
        }
    }
}
