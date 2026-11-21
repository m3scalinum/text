using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Pdf.FactureX;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The Peppol BIS Billing 3.0 CIUS: every rule of <see cref="PeppolBisValidator"/> is broken by one
/// document and satisfied by the conformant one, and the official sample invoice passes.
/// </summary>
/// <remarks>
/// The fixtures under <c>tests/fixtures/peppol</c> are the OpenPEPPOL publication's own example
/// documents, taken from <c>rules/examples</c> of the <c>OpenPEPPOL/peppol-bis-invoice-3</c>
/// repository; the rule codes and the EAS list were read from the Schematron of the same
/// publication. The documents that break a single rule are built from the model rather than edited
/// out of the sample: a mutation of the model is what a caller actually does wrong.
/// </remarks>
public sealed class PeppolConformityTests
{
    /// <summary>The conformant Peppol invoice, the positive case for every rule.</summary>
    private static FacturXInvoice Peppol(
        string typeCode = "380",
        string? businessProcess = PeppolBisValidator.BusinessProcess,
        string? specificationId = PeppolBisValidator.SpecificationId,
        string? buyerReference = "SE-42",
        string? orderReference = null,
        string? sellerAddress = "7310010000003",
        string? sellerScheme = "0088",
        string? buyerAddress = "123456789",
        string? buyerScheme = "0009",
        string? sellerPartyId = "12345678900011",
        string? sellerLegalId = "123456789",
        string? sellerVatId = "FR40123456789",
        string? buyerVatId = "FR55987654321",
        IReadOnlyList<FacturXNote>? notes = null,
        string? paymentMeans = "58",
        string? iban = "FR7630006000011234567890189",
        FacturXMandate? mandate = null,
        string? precedingInvoice = null,
        string taxCategory = "S",
        decimal? taxRate = 20m,
        decimal? breakdownRate = null,
        string? exemptionReason = null,
        IReadOnlyList<FacturXAllowanceCharge>? allowances = null,
        IReadOnlyList<FacturXTaxBreakdown>? breakdown = null)
    {
        var lines = new[]
        {
            new FacturXLine
            {
                Id = "1",
                Name = "Prestation de service",
                Quantity = 1m,
                UnitCode = "C62",
                UnitPrice = 300m,
                LineNetAmount = 300m,
                TaxCategory = taxCategory,
                TaxRate = taxRate,
            },
        };

        var adjustments = allowances ?? [];

        var lineTotal = lines.Sum(line => line.LineNetAmount);
        var allowanceTotal = adjustments.Where(adjustment => !adjustment.IsCharge).Sum(adjustment => adjustment.Amount);
        var chargeTotal = adjustments.Where(adjustment => adjustment.IsCharge).Sum(adjustment => adjustment.Amount);
        var basis = lineTotal - allowanceTotal + chargeTotal;
        var rate = breakdownRate ?? taxRate;
        var taxAmount = Math.Round(basis * (rate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);

        var taxes = breakdown
            ??
            [
                new FacturXTaxBreakdown
                {
                    Category = taxCategory,
                    Rate = rate,
                    Basis = basis,
                    Amount = taxAmount,
                    ExemptionReason = exemptionReason,
                },
            ];

        var taxTotal = taxes.Sum(tax => tax.Amount);

        return new FacturXInvoice
        {
            Number = "FA-2026-0200",
            TypeCode = typeCode,
            Profile = FacturXProfile.En16931,
            Currency = "EUR",
            IssueDate = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 7, 1),
            BuyerReference = buyerReference,
            OrderReference = orderReference,
            SpecificationId = specificationId,
            BusinessProcess = businessProcess,
            PrecedingInvoiceNumber = precedingInvoice,
            Notes = notes ?? [],
            PaymentMeansCode = paymentMeans,
            Iban = iban,
            Mandate = mandate,
            Seller = new FacturXParty
            {
                Name = "Atelier Typographique",
                PartyId = sellerPartyId,
                PartyIdScheme = "0009",
                LegalId = sellerLegalId,
                LegalIdScheme = "0002",
                ElectronicAddress = sellerAddress,
                ElectronicAddressScheme = sellerScheme,
                VatId = sellerVatId,
                Line1 = "12 rue des Glyphes",
                Postcode = "75011",
                City = "Paris",
                Country = "FR",
            },
            Buyer = new FacturXParty
            {
                Name = "Société Exemple",
                ElectronicAddress = buyerAddress,
                ElectronicAddressScheme = buyerScheme,
                VatId = buyerVatId,
                Line1 = "5 avenue du Test",
                Postcode = "69003",
                City = "Lyon",
                Country = "FR",
            },
            Lines = lines,
            AllowancesCharges = adjustments,
            TaxBreakdown = taxes,
            Totals = new FacturXTotals
            {
                LineTotal = lineTotal,
                AllowanceTotal = allowanceTotal,
                ChargeTotal = chargeTotal,
                TaxBasisTotal = basis,
                TaxTotal = taxTotal,
                GrandTotal = basis + taxTotal,
                Due = basis + taxTotal,
            },
        };
    }

    /// <summary>Every rule code the validator can report.</summary>
    public static TheoryData<string> RuleCodes { get; } = new()
    {
        "BR-PEPPOL-01", "BR-PEPPOL-02", "BR-PEPPOL-03", "BR-PEPPOL-04", "BR-PEPPOL-05",
        "BR-PEPPOL-06", "BR-PEPPOL-07", "BR-PEPPOL-08", "BR-PEPPOL-09", "BR-PEPPOL-10",
        "BR-PEPPOL-11", "BR-PEPPOL-12", "BR-PEPPOL-13", "BR-PEPPOL-14", "BR-PEPPOL-15",
        "BR-PEPPOL-16", "BR-PEPPOL-17", "BR-PEPPOL-18", "BR-PEPPOL-19", "BR-PEPPOL-20",
        "BR-PEPPOL-21", "BR-PEPPOL-22",
    };

    [Fact]
    public void The_conformant_invoice_produces_no_issue()
    {
        var result = FacturXValidator.Validate(Peppol(), PeppolBisValidator.Default);

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void The_official_peppol_sample_invoice_produces_no_issue()
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt("peppol/base-example.xml"));

        var result = FacturXValidator.Validate(invoice, PeppolBisValidator.Default);

        Assert.True(result.IsValid, result.Summary);
        Assert.Equal(PeppolBisValidator.BusinessProcess, invoice.BusinessProcess);
        Assert.Equal(PeppolBisValidator.SpecificationId, invoice.SpecificationId);
    }

    [Theory]
    [MemberData(nameof(RuleCodes))]
    public void The_conformant_invoice_satisfies_every_rule(string code)
    {
        Assert.DoesNotContain(FacturXValidator.Validate(Peppol()).Issues, issue => issue.Code == code);
    }

    [Theory]
    [MemberData(nameof(RuleCodes))]
    public void Every_rule_is_broken_by_one_document_and_by_that_one_only(string code)
    {
        var issues = FacturXValidator.Validate(Broken(code), PeppolBisValidator.Default).Issues;

        var peppol = issues
            .Where(issue => issue.Code.StartsWith("BR-PEPPOL", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(code, Assert.Single(peppol).Code);
    }

    /// <summary>
    /// The document that breaks one rule, and only that one: the mutations touch the term the rule
    /// is about, and the arithmetic is recomputed so the built-in rules stay quiet.
    /// </summary>
    private static FacturXInvoice Broken(string code) => code switch
    {
        // BT-23 missing; the CIUS is still declared through BT-24.
        "BR-PEPPOL-01" => Peppol(businessProcess: null),

        // A business process the CIUS does not publish.
        "BR-PEPPOL-02" => Peppol(businessProcess: "urn:peppol:bis:billing:2.0"),

        // The Factur-X guideline instead of the Peppol specification identifier.
        "BR-PEPPOL-03" => Peppol(specificationId: "urn:cen.eu:en16931:2017"),

        "BR-PEPPOL-04" => Peppol(sellerAddress: null),
        "BR-PEPPOL-05" => Peppol(buyerAddress: null),

        // An address the EAS list does not carry: nobody can route it.
        "BR-PEPPOL-06" => Peppol(buyerScheme: "9999"),

        "BR-PEPPOL-07" => Peppol(buyerReference: null),

        // Two notes: allowed only between German parties.
        "BR-PEPPOL-08" => Peppol(notes:
        [
            new FacturXNote { Content = "Première mention" },
            new FacturXNote { Content = "Seconde mention" },
        ]),

        // 396 is a credit note type code on a document written as an invoice.
        "BR-PEPPOL-09" => Peppol(typeCode: "396"),

        // A code that appears in neither list.
        "BR-PEPPOL-10" => Peppol(typeCode: "999"),

        // 384 is reserved to a German pair, or to a Slovak correction.
        "BR-PEPPOL-11" => Peppol(typeCode: "384", precedingInvoice: "FA-2026-0100"),

        // A credit note that names no invoice.
        "BR-PEPPOL-12" => Peppol(typeCode: "381"),

        // A direct debit without the mandate reference it is collected under.
        "BR-PEPPOL-13" => Peppol(paymentMeans: "59"),

        // A credit transfer without an account to transfer to.
        "BR-PEPPOL-14" => Peppol(iban: null),

        // Reverse charge, and the buyer the VAT is accounted by is not identified.
        "BR-PEPPOL-15" => Peppol(taxCategory: "AE", taxRate: 0m, exemptionReason: "Autoliquidation", buyerVatId: null),

        // Intra-community supply, same problem.
        "BR-PEPPOL-16" => Peppol(taxCategory: "K", taxRate: 0m, exemptionReason: "Livraison intracommunautaire", buyerVatId: null),

        // The line is standard-rated at 10%, the breakdown only knows 20%.
        "BR-PEPPOL-17" => Peppol(taxRate: 10m, breakdownRate: 20m),

        // No party identifier, no legal identifier, no VAT identifier.
        "BR-PEPPOL-18" => Peppol(sellerPartyId: null, sellerLegalId: null, sellerVatId: null),

        // A percentage without the base it applies to.
        "BR-PEPPOL-19" => Peppol(allowances:
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

        // A base amount without a percentage: the two go together.
        "BR-PEPPOL-20" => Peppol(allowances:
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
        "BR-PEPPOL-21" => Peppol(allowances:
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

        // An exempt category that carries a rate.
        "BR-PEPPOL-22" => Peppol(taxCategory: "E", taxRate: 20m, exemptionReason: "Exonération"),

        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "no document breaks that rule"),
    };

    [Fact]
    public void A_document_that_declares_the_cius_is_held_to_it_without_being_asked()
    {
        // BT-24 alone puts the document under the CIUS, and BT-23 is then missing.
        var invoice = Peppol(businessProcess: null);

        Assert.Contains(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code == "BR-PEPPOL-01");
    }

    [Fact]
    public void A_document_that_declares_nothing_is_not_held_to_the_cius()
    {
        var invoice = Peppol(businessProcess: null, specificationId: null, sellerAddress: null, buyerAddress: null);

        Assert.DoesNotContain(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code.StartsWith("BR-PEPPOL", StringComparison.Ordinal));
    }

    [Fact]
    public void The_peppol_rules_are_not_the_french_ones()
    {
        // A Peppol document is not a French-CIUS document: the French rules would demand the three
        // legal mentions and a SIREN, which the CIUS does not.
        var invoice = Peppol();

        Assert.DoesNotContain(
            FacturXValidator.Validate(invoice).Issues,
            issue => issue.Code.StartsWith("BR-FR", StringComparison.Ordinal));
    }

    [Fact]
    public void The_extension_point_is_invoked_and_its_issues_are_merged()
    {
        var recorder = new RecordingValidator();
        var invoice = Peppol(buyerAddress: null);

        var result = FacturXValidator.Validate(invoice, recorder);

        Assert.Same(invoice, recorder.Seen);
        Assert.Contains(result.Issues, issue => issue.Code == "BR-PEPPOL-05");
        Assert.Contains(result.Issues, issue => issue.Code == "PARTNER-01");
    }

    [Fact]
    public void An_issue_both_rule_sets_report_is_reported_once()
    {
        var invoice = Peppol(businessProcess: null);

        var result = FacturXValidator.Validate(invoice, new RepeatingValidator("BR-PEPPOL-01"));

        Assert.Single(result.Issues, issue => issue.Code == "BR-PEPPOL-01");
    }

    [Fact]
    public void The_written_document_passes_the_rules_it_was_written_for()
    {
        var written = FacturXReader.Read(UblWriter.Write(Peppol()));

        var result = FacturXValidator.Validate(written, PeppolBisValidator.Default);

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void The_ubl_writer_emits_the_two_identifiers_in_the_schema_order()
    {
        var xml = UblWriter.Write(Peppol());

        var customization = xml.IndexOf("<cbc:CustomizationID>", StringComparison.Ordinal);
        var profile = xml.IndexOf("<cbc:ProfileID>", StringComparison.Ordinal);
        var id = xml.IndexOf("<cbc:ID>", StringComparison.Ordinal);

        Assert.True(customization >= 0, xml);
        Assert.True(profile > customization, xml);
        Assert.True(id > profile, xml);
        Assert.Contains(PeppolBisValidator.SpecificationId, xml, StringComparison.Ordinal);
        Assert.Contains(PeppolBisValidator.BusinessProcess, xml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_identifiers_and_the_preceding_invoice_survive_a_ubl_round_trip()
    {
        var original = Peppol(typeCode: "381", precedingInvoice: "FA-2026-0100");

        var read = FacturXReader.Read(UblWriter.Write(original));

        Assert.Equal(PeppolBisValidator.SpecificationId, read.SpecificationId);
        Assert.Equal(PeppolBisValidator.BusinessProcess, read.BusinessProcess);
        Assert.Equal("FA-2026-0100", read.PrecedingInvoiceNumber);
        Assert.Equal("381", read.TypeCode);
    }

    [Fact]
    public void The_preceding_invoice_survives_a_cii_round_trip()
    {
        var original = Peppol(typeCode: "381", precedingInvoice: "FA-2026-0100");

        var read = FacturXReader.Read(CiiWriter.Write(original));

        Assert.Equal("FA-2026-0100", read.PrecedingInvoiceNumber);
    }

    [Fact]
    public void The_official_allowance_sample_matches_its_own_percentages()
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt("peppol/Allowance-example.xml"));

        var issues = FacturXValidator.Validate(invoice, PeppolBisValidator.Default).Issues;

        Assert.DoesNotContain(issues, issue => issue.Code is "BR-PEPPOL-19" or "BR-PEPPOL-20" or "BR-PEPPOL-21");
    }

    [Fact]
    public void The_official_credit_note_sample_names_the_invoice_it_corrects()
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt("peppol/base-creditnote-correction.xml"));

        Assert.Equal("381", invoice.TypeCode);
        Assert.False(string.IsNullOrEmpty(invoice.PrecedingInvoiceNumber));

        Assert.True(
            FacturXValidator.Validate(invoice, PeppolBisValidator.Default).IsValid,
            FacturXValidator.Validate(invoice, PeppolBisValidator.Default).Summary);
    }

    [Fact]
    public void The_official_direct_debit_sample_carries_its_mandate()
    {
        var invoice = FacturXReader.Read(ReaderTests.FixtureAt("peppol/direct-debit.xml"));

        Assert.Equal("59", invoice.PaymentMeansCode);
        Assert.NotNull(invoice.Mandate?.Reference);

        Assert.DoesNotContain(
            FacturXValidator.Validate(invoice, PeppolBisValidator.Default).Issues,
            issue => issue.Code == "BR-PEPPOL-13");
    }

    [Fact]
    public void Arbitrary_strings_are_not_mistaken_for_the_cius()
    {
        Assert.False(PeppolBisValidator.IsPeppol(Peppol(businessProcess: null, specificationId: null)));
        Assert.False(PeppolBisValidator.IsPeppolBusinessProcess("urn:fdc:peppol.eu:2017:poacc:billing:01:1"));
        Assert.False(PeppolBisValidator.IsPeppolSpecificationId("urn:cen.eu:en16931:2017"));
        Assert.False(PeppolBisValidator.IsPeppolBusinessProcess(null));
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

    private sealed class RepeatingValidator(string code) : IInvoiceValidator
    {
        public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
            => FacturXValidator.Validate(invoice).Issues
                .Where(issue => issue.Code == code)
                .ToList();
    }
}
