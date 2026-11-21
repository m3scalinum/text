using System;
using System.Collections.Generic;
using System.Globalization;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>
/// The generated options records: one per layout, one property per manifest parameter, with the
/// manifest's defaults. No native library is involved.
/// </summary>
public sealed class TemplateOptionsTests
{
    public static TheoryData<string> Layouts => new()
    {
        TemplateIds.Invoice,
        TemplateIds.Quotation,
        TemplateIds.Statement,
        TemplateIds.Attestation,
    };

    [Fact]
    public void The_invoice_record_carries_the_manifest_parameters_and_defaults()
    {
        var options = new InvoiceOptions
        {
            Title = "Facture 2026-0042",
            Number = "FA-2026-0042",
            Issued = new DateOnly(2026, 3, 1),
            Due = new DateOnly(2026, 3, 31),
            Seller = new Dictionary<string, object?> { ["name"] = "Atelier Dupont SAS" },
            Customer = new Dictionary<string, object?> { ["name"] = "Clinique des Tilleuls" },
            Items = [],
            Labels = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French),
        };

        var parameters = options.ToParameters();

        Assert.Equal("EUR", parameters["currency"]);
        Assert.Equal(0.2, parameters["tax_rate"]);
        Assert.Equal(0.0, parameters["amount_paid"]);
        Assert.Equal(string.Empty, parameters["notes"]);
        Assert.Same(options.Labels, parameters["labels"]);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_manifest_parameter_is_a_property_of_the_record(string id)
    {
        var manifest = TemplateCatalog.Default.Get(id).Manifest;
        var parameters = Parameters(id);

        Assert.Equal(manifest.Parameters.Count, parameters.Count);

        foreach (var parameter in manifest.Parameters.Values)
        {
            Assert.True(
                parameters.ContainsKey(parameter.Name),
                $"the record for '{id}' does not map '{parameter.Name}'");

            if (!parameter.Required)
            {
                var actual = parameters[parameter.Name];

                // The manifest reader yields an integer for `0` and the record holds a double: a
                // default is compared by value, not by the CLR type the two parsers happened to pick.
                if (parameter.Default is long or double && actual is double real)
                {
                    Assert.Equal(Convert.ToDouble(parameter.Default, CultureInfo.InvariantCulture), real);
                }
                else
                {
                    Assert.Equal(parameter.Default, actual);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_typed_wrapper_loads_the_embedded_layout(string id)
    {
        var descriptor = TemplateCatalog.Default.Get(id);

        var manifest = id switch
        {
            TemplateIds.Invoice => Load<InvoiceOptions>(descriptor),
            TemplateIds.Quotation => Load<QuotationOptions>(descriptor),
            TemplateIds.Statement => Load<StatementOptions>(descriptor),
            _ => Load<AttestationOptions>(descriptor),
        };

        Assert.Equal(id, manifest.Name);
        Assert.Equal(descriptor.Version, manifest.Version);
    }

    private static TypstTemplateManifest Load<TOptions>(TemplateDescriptor descriptor)
        where TOptions : ITypstTemplateOptions
        => descriptor.LoadTyped<TOptions>().Manifest;

    private static IReadOnlyDictionary<string, object?> Parameters(string id) => id switch
    {
        TemplateIds.Invoice => new InvoiceOptions
        {
            Title = "Facture 2026-0042",
            Number = "FA-2026-0042",
            Issued = new DateOnly(2026, 3, 1),
            Due = new DateOnly(2026, 3, 31),
            Seller = new Dictionary<string, object?>(StringComparer.Ordinal),
            Customer = new Dictionary<string, object?>(StringComparer.Ordinal),
            Items = [],
            Labels = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French),
        }.ToParameters(),
        TemplateIds.Quotation => new QuotationOptions
        {
            Title = "Devis 2026-0117",
            Number = "DE-2026-0117",
            Issued = new DateOnly(2026, 2, 12),
            ValidUntil = new DateOnly(2026, 3, 14),
            Seller = new Dictionary<string, object?>(StringComparer.Ordinal),
            Customer = new Dictionary<string, object?>(StringComparer.Ordinal),
            Items = [],
            Labels = TemplateLabels.For(TemplateIds.Quotation, TemplateLanguage.French),
        }.ToParameters(),
        TemplateIds.Statement => new StatementOptions
        {
            Title = "Relevé de compte 03/2026",
            Number = "RC-2026-0031",
            Issued = new DateOnly(2026, 4, 1),
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Account = new Dictionary<string, object?>(StringComparer.Ordinal),
            OpeningBalance = 12500.75,
            Transactions = [],
            Labels = TemplateLabels.For(TemplateIds.Statement, TemplateLanguage.French),
        }.ToParameters(),
        _ => new AttestationOptions
        {
            Title = "Attestation de travail",
            Issued = new DateOnly(2026, 3, 20),
            Employer = new Dictionary<string, object?>(StringComparer.Ordinal),
            Employee = new Dictionary<string, object?>(StringComparer.Ordinal),
            Purpose = "la banque du salarié",
            Labels = TemplateLabels.For(TemplateIds.Attestation, TemplateLanguage.French),
        }.ToParameters(),
    };
}
