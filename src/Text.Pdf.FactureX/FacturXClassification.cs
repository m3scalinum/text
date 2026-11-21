namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// A classification code of the item, <c>BT-158</c> — a CPV code for a public contract, a UNSPSC
/// code for goods, or whatever code list the two parties agreed on.
/// </summary>
public sealed class FacturXClassification
{
    /// <summary>The code itself, <c>BT-158</c>.</summary>
    public string? Code { get; init; }

    /// <summary>The code list the code belongs to, <c>BT-158-1</c>.</summary>
    public string? Scheme { get; init; }
}
