using System.Collections.Generic;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// A rule set a caller adds to the built-in ones.
/// </summary>
/// <remarks>
/// It is the seam for the official artefacts: OpenPEPPOL publishes the Peppol BIS Billing 3.0 rules
/// as Schematron, and a national CIUS or a trading partner publishes its own. Such a rule set needs
/// an XSLT 2.0 processor and the XML the document was written from, which is why it stays outside
/// this library and is plugged in here instead.
/// <para>
/// An implementation returns the issues it found and does not throw for an invalid document; an
/// exception is a defect in the validator, not a verdict on the invoice.
/// </para>
/// </remarks>
public interface IInvoiceValidator
{
    /// <summary>Validates an invoice and returns every rule it breaks.</summary>
    /// <param name="invoice">The invoice to check.</param>
    /// <returns>The issues, empty when the invoice satisfies the rule set.</returns>
    IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice);
}
