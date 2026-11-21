using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// The labels each layout prints, in the languages the pack ships. A layout is written with no text
/// of its own: it reads every word, every date pattern and the number separators from the label set
/// the caller passes, so the same file renders in English or French.
/// </summary>
/// <remarks>
/// The French sets use the abbreviations and the fixed phrases French business documents actually
/// carry — <c>N° de facture</c>, <c>Net à payer</c>, <c>Bon pour accord</c>. Punctuation that
/// belongs to a clause lives in the label, so a layout never assembles a sentence out of fragments
/// that only read correctly in one language.
/// </remarks>
public static class TemplateLabels
{
    private static readonly (string Key, string English, string French)[] Invoice =
    [
        ("document", "INVOICE", "FACTURE"),
        ("invoice_number", "Invoice no.", "N° de facture"),
        ("issue_date", "Issue date", "Date d'émission"),
        ("due_date", "Due date", "Échéance"),
        ("currency", "Currency", "Devise"),
        ("bill_to", "Bill to", "Facturé à"),
        ("payment_terms", "Payment terms", "Conditions de règlement"),
        ("description", "Description", "Désignation"),
        ("quantity", "Qty", "Qté"),
        ("unit_price", "Unit price", "Prix unitaire"),
        ("amount", "Amount", "Montant"),
        ("subtotal", "Subtotal", "Sous-total"),
        ("tax", "VAT", "TVA"),
        ("total", "Total", "Total"),
        ("amount_paid", "Amount paid", "Montant réglé"),
        ("balance_due", "Balance due", "Net à payer"),
        ("notes", "Notes", "Remarques"),
        ("remit_to", "Remit to", "Règlement à"),
        ("thank_you", "Thank you for your business.", "Nous vous remercions de votre confiance."),
        ("account", "Account", "Compte"),
        ("po_number", "PO number", "N° de commande"),
        ("tax_id", "Tax ID", "N° de TVA"),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
        ("thousand_separator", ",", " "),
        ("decimal_separator", ".", ","),
    ];

    private static readonly (string Key, string English, string French)[] Quotation =
    [
        ("document", "QUOTATION", "DEVIS"),
        ("quote_number", "Quote no.", "N° de devis"),
        ("issue_date", "Issue date", "Date d'émission"),
        ("valid_until", "Valid until", "Valable jusqu'au"),
        ("currency", "Currency", "Devise"),
        ("prepared_for", "Prepared for", "Destinataire"),
        ("prepared_by", "Prepared by", "Émis par"),
        ("description", "Description", "Désignation"),
        ("quantity", "Qty", "Qté"),
        ("unit_price", "Unit price", "Prix unitaire"),
        ("amount", "Amount", "Montant"),
        ("subtotal", "Subtotal", "Sous-total"),
        ("tax", "VAT", "TVA"),
        ("total", "Total", "Total"),
        ("notes", "Notes", "Remarques"),
        ("validity", "This quotation is valid until the date stated above.", "Ce devis est valable jusqu'à la date indiquée ci-dessus."),
        ("acceptance", "Acceptance", "Bon pour accord"),
        ("signature", "Signature and date", "Signature et date"),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
        ("thousand_separator", ",", " "),
        ("decimal_separator", ".", ","),
    ];

    private static readonly (string Key, string English, string French)[] Statement =
    [
        ("document", "ACCOUNT STATEMENT", "RELEVÉ DE COMPTE"),
        ("account_number", "Account number", "Numéro de compte"),
        ("holder", "Account holder", "Titulaire du compte"),
        ("period", "Period", "Période"),
        ("issued_on", "Issued on", "Édité le"),
        ("date", "Date", "Date"),
        ("description", "Description", "Libellé"),
        ("reference", "Reference", "Référence"),
        ("debit", "Debit", "Débit"),
        ("credit", "Credit", "Crédit"),
        ("balance", "Balance", "Solde"),
        ("opening_balance", "Opening balance", "Solde d'ouverture"),
        ("closing_balance", "Closing balance", "Solde de clôture"),
        ("total_debits", "Total debits", "Total des débits"),
        ("total_credits", "Total credits", "Total des crédits"),
        ("currency", "Currency", "Devise"),
        ("iban", "IBAN", "IBAN"),
        ("bic", "BIC", "BIC"),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
        ("thousand_separator", ",", " "),
        ("decimal_separator", ".", ","),
    ];

    private static readonly (string Key, string English, string French)[] Attestation =
    [
        ("document", "ATTESTATION", "ATTESTATION"),
        ("intro", "The undersigned company", "La société soussignée"),
        ("represented_by", ", represented by", ", représentée par"),
        ("certifies", ", hereby certifies that", ", atteste par la présente que"),
        ("holds_position", " holds the position of", " occupe le poste de"),
        ("employed_since", "The employee has been employed by the company since", "Le salarié est employé par la société depuis le"),
        ("purpose_intro", "This attestation is issued for", "La présente attestation est établie à l'attention de"),
        ("closing", "This attestation is issued to serve and for all legal purposes.", "La présente attestation est délivrée pour servir et valoir ce que de droit."),
        ("signature", "Signature", "Signature"),
        ("stamp", "Company stamp", "Cachet de l'entreprise"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
    ];

    private static readonly (string Key, string English, string French)[] Contract =
    [
        ("document", "CONTRACT", "CONTRAT"),
        ("contract_number", "Contract no.", "N° de contrat"),
        ("issue_date", "Issue date", "Date d'émission"),
        ("effective_date", "Effective date", "Date d'effet"),
        ("party_a", "First party", "Première partie"),
        ("party_b", "Second party", "Seconde partie"),
        ("between", "Between", "Entre"),
        ("registration", "Registration", "Immatriculation"),
        ("representative", "Represented by", "Représentée par"),
        ("clauses", "Clauses", "Clauses"),
        ("article", "Article", "Article"),
        ("signature", "Signature", "Signature"),
        ("signed_at", "Signed at", "Fait à"),
        ("on", ", on", ", le"),
        ("notes", "Notes", "Remarques"),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
    ];

    private static readonly (string Key, string English, string French)[] Payslip =
    [
        ("document", "PAYSLIP", "BULLETIN DE PAIE"),
        ("payslip_number", "Payslip no.", "N° de bulletin"),
        ("issue_date", "Issue date", "Date d'émission"),
        ("period", "Period", "Période"),
        ("employer", "Employer", "Employeur"),
        ("employee", "Employee", "Salarié"),
        ("employee_number", "Employee no.", "Matricule"),
        ("start_date", "Employed since", "Entrée le"),
        ("registration", "Registration", "Immatriculation"),
        ("earnings", "Gross earnings", "Éléments de rémunération"),
        ("contributions", "Contributions", "Cotisations"),
        ("description", "Description", "Libellé"),
        ("base", "Base", "Base"),
        ("rate", "Rate", "Taux"),
        ("amount", "Amount", "Montant"),
        ("gross_pay", "Gross pay", "Salaire brut"),
        ("total_contributions", "Total contributions", "Total des cotisations"),
        ("net_pay", "Net pay", "Net à payer"),
        ("payment", "Payment", "Règlement"),
        ("bank_transfer", "Bank transfer", "Virement bancaire"),
        ("notes", "Notes", "Remarques"),
        ("retain_copy", "Keep this payslip.", "Conservez ce bulletin."),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
        ("thousand_separator", ",", " "),
        ("decimal_separator", ".", ","),
    ];

    private static readonly (string Key, string English, string French)[] PurchaseOrder =
    [
        ("document", "PURCHASE ORDER", "BON DE COMMANDE"),
        ("order_number", "Order no.", "N° de commande"),
        ("issue_date", "Issue date", "Date d'émission"),
        ("currency", "Currency", "Devise"),
        ("buyer", "Buyer", "Acheteur"),
        ("supplier", "Supplier", "Fournisseur"),
        ("delivery_address", "Delivery address", "Adresse de livraison"),
        ("delivery_date", "Required by", "Livraison souhaitée"),
        ("vat_id", "VAT ID", "N° de TVA"),
        ("supplier_account", "Supplier account", "Compte fournisseur"),
        ("description", "Description", "Désignation"),
        ("quantity", "Qty", "Qté"),
        ("unit_price", "Unit price", "Prix unitaire"),
        ("amount", "Amount", "Montant"),
        ("subtotal", "Subtotal", "Sous-total"),
        ("tax", "VAT", "TVA"),
        ("total", "Total", "Total"),
        ("terms", "Terms", "Conditions"),
        ("standard_terms", "Payment on delivery.", "Paiement à réception."),
        ("notes", "Notes", "Remarques"),
        ("no_notes", "None.", "Néant."),
        ("authorised_by", "Authorised by", "Émis par"),
        ("acknowledged_by", "Acknowledged by", "Accepté par"),
        ("page", "Page", "Page"),
        ("of_page", "of", "sur"),
        ("date_format", "[month repr:long] [day], [year]", "[day]/[month]/[year]"),
        ("thousand_separator", ",", " "),
        ("decimal_separator", ".", ","),
    ];

    /// <summary>Returns the labels a layout needs, in one language.</summary>
    /// <param name="templateId">Catalog id, for example <see cref="TemplateIds.Invoice"/>.</param>
    /// <param name="language">Language of the returned labels.</param>
    /// <exception cref="TypstTemplateException">No layout of the pack has that id.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a defined value.</exception>
    public static IReadOnlyDictionary<string, object?> For(string templateId, TemplateLanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var table = Table(templateId);
        var labels = new Dictionary<string, object?>(table.Length, StringComparer.Ordinal);

        foreach (var (key, english, french) in table)
        {
            labels[key] = language switch
            {
                TemplateLanguage.English => english,
                TemplateLanguage.French => french,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(language), language, "unknown template language"),
            };
        }

        return labels;
    }

    private static (string Key, string English, string French)[] Table(string templateId)
    {
        if (string.Equals(templateId, TemplateIds.Invoice, StringComparison.OrdinalIgnoreCase))
        {
            return Invoice;
        }

        if (string.Equals(templateId, TemplateIds.Quotation, StringComparison.OrdinalIgnoreCase))
        {
            return Quotation;
        }

        if (string.Equals(templateId, TemplateIds.Statement, StringComparison.OrdinalIgnoreCase))
        {
            return Statement;
        }

        if (string.Equals(templateId, TemplateIds.Attestation, StringComparison.OrdinalIgnoreCase))
        {
            return Attestation;
        }

        if (string.Equals(templateId, TemplateIds.Contract, StringComparison.OrdinalIgnoreCase))
        {
            return Contract;
        }

        if (string.Equals(templateId, TemplateIds.Payslip, StringComparison.OrdinalIgnoreCase))
        {
            return Payslip;
        }

        if (string.Equals(templateId, TemplateIds.PurchaseOrder, StringComparison.OrdinalIgnoreCase))
        {
            return PurchaseOrder;
        }

        throw new TypstTemplateException($"no label set for template '{templateId}'", templateId);
    }
}
