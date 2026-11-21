using System;
using System.Collections.Generic;
using System.IO;
using M3scalinum.Text.SharpTypst.Templates;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>Paths and datasets the whole test project shares.</summary>
internal static class Samples
{
    /// <summary>Directory holding the layouts, copied beside the test binary.</summary>
    internal static string Templates => Path.Combine(AppContext.BaseDirectory, "templates");

    /// <summary>Directory of one layout on disk.</summary>
    internal static string Directory(string id) => Path.Combine(Templates, id);

    /// <summary>Source of one layout, read from disk.</summary>
    internal static string Source(string id) => File.ReadAllText(Path.Combine(Directory(id), "template.typ"));

    internal static Dictionary<string, object?> MinimalInvoice() => new(StringComparer.Ordinal)
    {
        ["title"] = "Facture 2026-0042",
        ["number"] = "FA-2026-0042",
        ["issued"] = new DateOnly(2026, 3, 1),
        ["due"] = new DateOnly(2026, 3, 31),
        ["seller"] = Seller(),
        ["customer"] = Customer(),
        ["items"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["description"] = "Audit technique",
                ["quantity"] = 1,
                ["unit_price"] = 950.00m,
            },
        },
        ["labels"] = Labels(TemplateIds.Invoice, TemplateLanguage.French),
    };

    internal static Dictionary<string, object?> MinimalQuotation() => new(StringComparer.Ordinal)
    {
        ["title"] = "Devis 2026-0117",
        ["number"] = "DE-2026-0117",
        ["issued"] = new DateOnly(2026, 2, 12),
        ["valid_until"] = new DateOnly(2026, 3, 14),
        ["seller"] = Seller(),
        ["customer"] = Customer(),
        ["items"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["description"] = "Mise en service",
                ["quantity"] = 1,
                ["unit_price"] = 1200.00m,
            },
        },
        ["labels"] = Labels(TemplateIds.Quotation, TemplateLanguage.French),
    };

    internal static Dictionary<string, object?> MinimalStatement() => new(StringComparer.Ordinal)
    {
        ["title"] = "Relevé de compte 03/2026",
        ["number"] = "RC-2026-0031",
        ["issued"] = new DateOnly(2026, 4, 1),
        ["period_start"] = new DateOnly(2026, 3, 1),
        ["period_end"] = new DateOnly(2026, 3, 31),
        ["account"] = Account(),
        ["opening_balance"] = 12500.75m,
        ["transactions"] = Transactions(3),
        ["labels"] = Labels(TemplateIds.Statement, TemplateLanguage.French),
    };

    internal static Dictionary<string, object?> MinimalAttestation() => new(StringComparer.Ordinal)
    {
        ["title"] = "Attestation de travail",
        ["issued"] = new DateOnly(2026, 3, 20),
        ["employer"] = Employer(),
        ["employee"] = Employee(),
        ["purpose"] = "la banque du salarié",
        ["labels"] = Labels(TemplateIds.Attestation, TemplateLanguage.French),
    };

    /// <summary>An invoice with enough lines to break the table across pages.</summary>
    internal static Dictionary<string, object?> RealisticInvoice()
    {
        var data = MinimalInvoice();
        data["seller"] = Seller(withContacts: true);
        data["customer"] = Customer(withAccount: true);
        data["items"] = Items(34);
        data["amount_paid"] = 5000.00m;
        data["notes"] = "Règlement par virement bancaire à 30 jours.";
        return data;
    }

    /// <summary>A quotation with a realistic number of lines.</summary>
    internal static Dictionary<string, object?> RealisticQuotation()
    {
        var data = MinimalQuotation();
        data["seller"] = Seller(withContacts: true);
        data["items"] = Items(12);
        data["notes"] = "Délai de livraison : trois semaines à compter de la commande.";
        return data;
    }

    /// <summary>A statement with enough movements to break the table across pages.</summary>
    internal static Dictionary<string, object?> RealisticStatement()
    {
        var data = MinimalStatement();
        data["transactions"] = Transactions(46);
        data["notes"] = "Document produit automatiquement, sans valeur de justification.";
        return data;
    }

    /// <summary>An attestation naming both parties in full.</summary>
    internal static Dictionary<string, object?> RealisticAttestation()
    {
        var data = MinimalAttestation();
        data["place"] = "Paris";
        data["purpose"] = "le dépôt d'un dossier de location";
        return data;
    }

    internal static IReadOnlyDictionary<string, object?> Labels(string id, TemplateLanguage language)
        => TemplateLabels.For(id, language);

    internal static Dictionary<string, object?> Seller(bool withContacts = false) => new(StringComparer.Ordinal)
    {
        ["name"] = "Atelier Dupont SAS",
        ["address"] = new[] { "12 rue des Lilas", "75011 Paris" },
        ["phone"] = withContacts ? "+33 1 55 00 12 34" : string.Empty,
        ["email"] = withContacts ? "facturation@atelier-dupont.example" : string.Empty,
        ["legal"] = "SAS au capital de 50 000 € · RCS Paris 812 345 678",
        ["terms"] = "Paiement à 30 jours",
        ["remit"] = "Atelier Dupont SAS — Service facturation",
        ["bank"] = "IBAN FR76 3000 4000 0300 0000 1234 567",
        ["tax_id"] = "FR 32 812345678",
    };

    internal static Dictionary<string, object?> Customer(bool withAccount = false) => new(StringComparer.Ordinal)
    {
        ["name"] = "Clinique des Tilleuls",
        ["address"] = new[] { "4 avenue de la Gare", "69002 Lyon" },
        ["email"] = "comptabilite@tilleuls.example",
        ["account"] = withAccount ? "CT-4471" : string.Empty,
        ["po"] = withAccount ? "PO-2026-0117" : string.Empty,
    };

    internal static Dictionary<string, object?> Account() => new(StringComparer.Ordinal)
    {
        ["holder"] = "Clinique des Tilleuls",
        ["number"] = "00012345678",
        ["iban"] = "FR76 3000 4000 0300 0001 2345 678",
        ["bic"] = "BNPAFRPPXXX",
        ["branch"] = "Agence Lyon Part-Dieu",
        ["legal"] = "Banque Exemple SA · 1 place de la Bourse, 75002 Paris",
    };

    internal static Dictionary<string, object?> Employer() => new(StringComparer.Ordinal)
    {
        ["name"] = "Atelier Dupont SAS",
        ["address"] = "12 rue des Lilas, 75011 Paris",
        ["contact"] = "contact@atelier-dupont.example",
        ["legal_representative"] = "Camille Dupont, Présidente",
        ["legal"] = "SAS au capital de 50 000 € · RCS Paris 812 345 678",
    };

    internal static Dictionary<string, object?> Employee() => new(StringComparer.Ordinal)
    {
        ["name"] = "Samir Benali",
        ["position"] = "Ingénieur logiciel",
        ["start_date"] = new DateOnly(2021, 9, 1),
        ["contract"] = "CDI",
    };

    private static List<Dictionary<string, object?>> Items(int count)
    {
        var items = new List<Dictionary<string, object?>>(count);

        for (var index = 1; index <= count; index++)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["description"] = $"Prestation de conseil {index}",
                ["details"] = "Journée d'intervention, déplacement inclus",
                ["quantity"] = index,
                ["unit_price"] = 125.50m * index,
            });
        }

        return items;
    }

    private static List<Dictionary<string, object?>> Transactions(int count)
    {
        var movements = new List<Dictionary<string, object?>>(count);
        var date = new DateOnly(2026, 3, 1);

        for (var index = 1; index <= count; index++)
        {
            var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["date"] = date.AddDays(index % 28),
                ["description"] = index % 3 == 0 ? "Prélèvement fournisseur" : "Virement reçu",
                ["reference"] = "MVT-" + index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture),
            };

            if (index % 3 == 0)
            {
                entry["debit"] = 120.25m * index;
            }
            else
            {
                entry["credit"] = 240.50m * index;
            }

            movements.Add(entry);
        }

        return movements;
    }
}
