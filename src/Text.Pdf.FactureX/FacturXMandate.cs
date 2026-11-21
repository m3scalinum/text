namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The SEPA direct debit the invoice is collected under, <c>BG-19</c>.
/// </summary>
/// <remarks>
/// A direct debit needs the mandate the debtor signed and the account it is drawn on. The bank's own
/// creditor identifier (<c>BT-90</c>) is not carried: the two syntaxes disagree on where it belongs,
/// and a document that guesses would be read differently by each receiver.
/// </remarks>
public sealed class FacturXMandate
{
    /// <summary>The mandate reference the debtor's bank knows, <c>BT-89</c>.</summary>
    public string? Reference { get; init; }

    /// <summary>The account debited, <c>BT-91</c> — an IBAN.</summary>
    public string? DebitedAccount { get; init; }
}
