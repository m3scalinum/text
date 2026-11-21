using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The closed code lists EN 16931 pins down, and the shape checks for the open ones.
/// </summary>
/// <remarks>
/// A closed list is enforced: a document carrying a category or a payment means code outside it is
/// rejected, because a receiver cannot book it. The open lists — units of measure, currencies — are
/// checked for shape only: the full UN/ECE and ISO 4217 tables are thousands of entries, and a
/// partial copy would reject valid documents, which is worse than not checking.
/// </remarks>
public static class FacturXCodeLists
{
    /// <summary>The VAT category codes of EN 16931 (BT-118, BT-151).</summary>
    public static IReadOnlySet<string> VatCategories { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "S",  // standard rate
        "Z",  // zero rated
        "E",  // exempt
        "AE", // reverse charge
        "K",  // intra-community supply
        "G",  // export outside the EU
        "O",  // not subject to VAT
        "L",  // Canary Islands
        "M",  // Ceuta and Melilla
    };

    /// <summary>The payment means codes EN 16931 allows (BT-81).</summary>
    public static IReadOnlySet<string> PaymentMeansCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14",
        "20", "30", "31", "42", "48", "49", "57", "58", "59", "60", "70", "97",
    };

    /// <summary>The categories that carry no VAT and therefore need an exemption reason (BT-120).</summary>
    public static IReadOnlySet<string> ExemptCategories { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Z", "E", "AE", "K", "G", "O", "L", "M",
    };

    /// <summary>
    /// The VAT categories the French CIUS accepts, <c>BR-FR-15</c>.
    /// </summary>
    /// <remarks>
    /// Stricter than EN 16931: <c>L</c> (Canary Islands) and <c>M</c> (Ceuta and Melilla) are legal
    /// in the standard and rejected by the French rules.
    /// </remarks>
    public static IReadOnlySet<string> FrenchVatCategories { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "S", "E", "AE", "K", "G", "O", "Z",
    };

    /// <summary>The VAT rates the French CIUS accepts, <c>BR-FR-16</c>.</summary>
    public static IReadOnlySet<decimal> FrenchVatRates { get; } = new HashSet<decimal>
    {
        0m, 0.9m, 1.05m, 1.75m, 2.1m, 5.5m, 7m, 8.5m, 9.2m, 9.6m, 10m, 13m, 19.6m, 20m, 20.6m,
    };

    /// <summary>The billing frameworks of the French CIUS, <c>BR-FR-08</c>.</summary>
    public static IReadOnlySet<string> BillingModes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "B1", "S1", "M1", "B2", "S2", "M2", "S3", "B4", "S4", "M4", "S5", "S6",
        "B7", "S7", "B8", "S8", "M8", "B9", "S9", "M9",
    };

    /// <summary>The document type codes the French CIUS accepts, <c>BR-FR-04</c>.</summary>
    public static IReadOnlySet<string> DocumentTypeCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "261", "262", "380", "381", "384", "386", "389", "393", "396",
        "471", "472", "473", "500", "501", "502", "503",
    };

    /// <summary>The treatments a <c>BAR</c> note may declare, <c>BR-FR-31</c>.</summary>
    public static IReadOnlySet<string> BarTreatments { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "B2B", "B2BINT", "B2C", "B2CINT", "OUTOFSCOPE", "ARCHIVEONLY",
    };

    /// <summary>The codes that qualify an attachment, <c>BR-FR-17</c>.</summary>
    public static IReadOnlySet<string> AttachmentCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "RIB", "LISIBLE", "FEUILLE_DE_STYLE", "PJA", "BORDEREAU_SUIVI", "DOCUMENT_ANNEXE",
        "BON_LIVRAISON", "BON_COMMANDE", "BORDEREAU_SUIVI_VALIDATION", "ETAT_ACOMPTE",
        "FACTURE_PAIEMENT_DIRECT", "RECAPITULATIF_COTRAITANCE",
    };

    /// <summary>The billing frameworks under which a document is a cascade invoice.</summary>
    public static IReadOnlySet<string> CascadeFrameworks { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "S8", "B8", "M8", "S9", "B9", "M9",
    };

    /// <summary>The billing frameworks under which a document is bidirectional or self-billed.</summary>
    public static IReadOnlySet<string> BidirectionalFrameworks { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "S9", "B9", "M9",
    };

    /// <summary>The document type codes that mark a self-billed invoice, <c>BR-FR-MV-13</c>.</summary>
    public static IReadOnlySet<string> SelfBilledDocumentTypeCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "389", "261", "501", "500", "502", "471", "473",
    };

    /// <summary>Whether <paramref name="code"/> is a VAT category of the standard.</summary>
    public static bool IsVatCategory(string? code) => code is not null && VatCategories.Contains(code);

    /// <summary>Whether <paramref name="code"/> is a payment means code of the standard.</summary>
    public static bool IsPaymentMeansCode(string? code) => code is not null && PaymentMeansCodes.Contains(code);

    /// <summary>Whether <paramref name="code"/> is an ISO 4217 code, by shape.</summary>
    public static bool IsCurrencyCode(string? code)
        => code is { Length: 3 } && code.All(character => character is >= 'A' and <= 'Z');

    /// <summary>Whether <paramref name="code"/> is a UN/ECE Recommendation 20 unit code, by shape.</summary>
    public static bool IsUnitCode(string? code)
        => code is { Length: >= 2 and <= 3 } && code.All(character => char.IsAsciiLetterOrDigit(character));
}
