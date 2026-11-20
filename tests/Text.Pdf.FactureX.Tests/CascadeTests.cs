using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// Cascade invoicing: a <c>GROUP</c> line stands for a sub-invoice issued by another seller, and
/// the detail lines below it carry that seller's own lines. These are the rules a platform applies
/// when a contractor re-bills what its subcontractors invoiced.
/// </summary>
public sealed class CascadeTests
{
    private static FacturXParty Subcontractor(string siren = "552100554") => new()
    {
        Name = "Sous-traitant SAS",
        Country = "FR",
        LegalId = siren,
        LegalIdScheme = "0002",
    };

    /// <summary>A cascade invoice: one root group line, two detail lines below it.</summary>
    private static FacturXInvoice Cascade(
        string framework = "B8",
        string typeCode = "380",
        string? rootObjectId = "SUB-2026-0001",
        decimal groupTotal = 300m,
        decimal groupTax = 60m,
        string? statusReason = "GROUP",
        string childSellerSiren = "552100554",
        string childObjectId = "SUB-2026-0001",
        bool addChild = true)
    {
        var lines = new System.Collections.Generic.List<FacturXLine>
        {
            new()
            {
                Id = "1",
                Name = "Sous-facture sous-traitant",
                Quantity = 1,
                UnitPrice = groupTotal,
                UnitCode = "C62",
                TaxCategory = "S",
                TaxRate = 20m,
                LineNetAmount = groupTotal,
                TaxTotal = groupTax,
                StatusReason = statusReason,
                LineSeller = Subcontractor(),
                ObjectIdentifier = rootObjectId,
                ObjectIdentifierScheme = "AFL",
            },
        };

        if (addChild)
        {
            lines.Add(new FacturXLine
            {
                Id = "1.1",
                Name = "Main d'oeuvre",
                Quantity = 2,
                UnitPrice = 100,
                UnitCode = "C62",
                TaxCategory = "S",
                TaxRate = 20m,
                LineNetAmount = 200,
                TaxTotal = 40m,
                StatusReason = "DETAIL",
                ParentLineId = "1",
                LineSeller = Subcontractor(childSellerSiren),
                ObjectIdentifier = childObjectId,
                ObjectIdentifierScheme = "AFL",
            });

            lines.Add(new FacturXLine
            {
                Id = "1.2",
                Name = "Fourniture",
                Quantity = 1,
                UnitPrice = 100,
                UnitCode = "C62",
                TaxCategory = "S",
                TaxRate = 20m,
                LineNetAmount = 100,
                TaxTotal = 20m,
                StatusReason = "DETAIL",
                ParentLineId = "1",
                LineSeller = Subcontractor(childSellerSiren),
                ObjectIdentifier = childObjectId,
                ObjectIdentifierScheme = "AFL",
            });
        }

        return new FacturXInvoice
        {
            Number = "FA-2026-0100",
            TypeCode = typeCode,
            IssueDate = new DateOnly(2026, 2, 1),
            Currency = "EUR",
            Profile = FacturXProfile.ExtendedCtcFr,
            BusinessProcess = framework,
            Notes =
            [
                new FacturXNote { SubjectCode = "PMT", Content = "Frais de recouvrement : 40 EUR" },
                new FacturXNote { SubjectCode = "PMD", Content = "Pénalités de retard : 3 fois le taux d'intérêt légal" },
                new FacturXNote { SubjectCode = "AAB", Content = "Escompte pour paiement anticipé : néant" },
            ],
            Seller = new FacturXParty
            {
                Name = "Entreprise générale",
                Country = "FR",
                VatId = "FR12345678901",
                LegalId = "784671695",
                LegalIdScheme = "0002",
                PartyId = "78467169500035",
                PartyIdScheme = "0009",
                ElectronicAddress = "78467169500035",
                ElectronicAddressScheme = "0009",
            },
            Buyer = new FacturXParty
            {
                Name = "Client final",
                Country = "FR",
                LegalId = "552100554",
                LegalIdScheme = "0002",
                ElectronicAddress = "552100554",
                ElectronicAddressScheme = "0002",
            },
            Lines = lines,
            TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = groupTotal, Amount = groupTax }],
            Totals = new FacturXTotals
            {
                LineTotal = groupTotal,
                TaxBasisTotal = groupTotal,
                TaxTotal = groupTax,
                GrandTotal = groupTotal + groupTax,
                Due = groupTotal + groupTax,
            },
        };
    }

    private static string[] Codes(FacturXInvoice invoice)
        => FacturXValidator.Validate(invoice).Issues.Select(issue => issue.Code).ToArray();

    [Fact]
    public void A_well_formed_cascade_invoice_passes()
    {
        Assert.Empty(Codes(Cascade()));
    }

    [Fact]
    public void Every_line_must_say_whether_it_is_a_group_or_a_detail()
    {
        Assert.Contains("BR-FR-MV-01", Codes(Cascade(statusReason: null)));
    }

    [Fact]
    public void A_cascade_invoice_needs_a_root_group_line()
    {
        // The only line becomes a detail line, so nothing roots the cascade.
        Assert.Contains("BR-FR-MV-02", Codes(Cascade(statusReason: "DETAIL", addChild: false)));
    }

    [Fact]
    public void A_group_line_must_carry_its_sub_invoice_number()
    {
        Assert.Contains("BR-FR-MV-03", Codes(Cascade(rootObjectId: null)));
    }

    [Fact]
    public void A_group_line_total_must_match_its_sub_lines()
    {
        Assert.Contains("BR-FR-MV-05", Codes(Cascade(groupTotal: 400m)));
    }

    [Fact]
    public void A_detail_line_must_agree_with_its_parent_on_the_seller()
    {
        Assert.Contains("BR-FR-MV-06", Codes(Cascade(childSellerSiren: "123456789")));
    }

    [Fact]
    public void A_detail_line_must_agree_with_its_parent_on_the_sub_invoice()
    {
        Assert.Contains("BR-FR-MV-07", Codes(Cascade(childObjectId: "SUB-OTHER")));
    }

    [Fact]
    public void Sub_invoice_numbers_of_root_lines_must_be_unique()
    {
        var invoice = Cascade();
        var lines = invoice.Lines.ToList();

        lines.Add(new FacturXLine
        {
            Id = "2",
            Name = "Deuxième sous-traitant",
            Quantity = 1,
            UnitPrice = 0,
            UnitCode = "C62",
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 0,
            TaxTotal = 0,
            StatusReason = "GROUP",
            LineSeller = Subcontractor("987654321"),
            ObjectIdentifier = "SUB-2026-0001",
            ObjectIdentifierScheme = "AFL",
        });

        var duplicated = new FacturXInvoice
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
            Lines = lines,
            TaxBreakdown = invoice.TaxBreakdown,
            Totals = invoice.Totals,
        };

        Assert.Contains("BR-FR-MV-12", Codes(duplicated));
    }

    [Fact]
    public void A_self_billed_type_is_refused_under_a_cascade_framework_and_required_under_a_bidirectional_one()
    {
        // 380 is an ordinary invoice: legal under B8, refused under S9.
        Assert.DoesNotContain("BR-FR-MV-13", Codes(Cascade(framework: "B8", typeCode: "380")));
        Assert.Contains("BR-FR-BD-13", Codes(Cascade(framework: "S9", typeCode: "380")));

        // 389 is a self-billed invoice: refused under B8, required under S9.
        Assert.Contains("BR-FR-MV-13", Codes(Cascade(framework: "B8", typeCode: "389")));
    }

    [Fact]
    public void A_bidirectional_invoice_carries_exactly_two_root_group_lines()
    {
        Assert.Contains("BR-FR-BD-02", Codes(Cascade(framework: "S9", typeCode: "389")));
    }

    [Fact]
    public void The_cascade_fields_survive_a_round_trip()
    {
        var xml = CiiWriter.Write(Cascade());
        var read = FacturXReader.Read(xml);

        Assert.Equal(3, read.Lines.Count);

        var group = read.Lines[0];
        Assert.Equal("GROUP", group.StatusReason);
        Assert.Equal("SUB-2026-0001", group.ObjectIdentifier);
        Assert.Equal("AFL", group.ObjectIdentifierScheme);
        Assert.Equal("Sous-traitant SAS", group.LineSeller?.Name);
        Assert.Equal("552100554", group.LineSeller?.LegalId);
        Assert.Equal(60m, group.TaxTotal);

        var child = read.Lines[1];
        Assert.Equal("DETAIL", child.StatusReason);
        Assert.Equal("1", child.ParentLineId);
    }

    [Fact]
    public void The_elements_sit_where_the_schema_expects_them()
    {
        var xml = CiiWriter.Write(Cascade());

        // DocumentLineDocument: LineID, ParentLineID, LineStatusReasonCode.
        Assert.True(
            xml.IndexOf("<ram:ParentLineID>1</ram:ParentLineID>", StringComparison.Ordinal)
            < xml.IndexOf("<ram:LineStatusReasonCode>DETAIL</ram:LineStatusReasonCode>", StringComparison.Ordinal));

        // LineTradeAgreement: the net price precedes ItemSellerTradeParty.
        Assert.True(
            xml.IndexOf("NetPriceProductTradePrice", StringComparison.Ordinal)
            < xml.IndexOf("ItemSellerTradeParty", StringComparison.Ordinal));
    }
}
