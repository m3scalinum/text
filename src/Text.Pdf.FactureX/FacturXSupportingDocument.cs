namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// A supporting document the invoice refers to, <c>BG-24</c>.
/// </summary>
/// <remarks>
/// It is how an invoice points at the contract, the timesheet or the despatch note a receiver may
/// ask for. The identifier is what the document is known by at the issuer; the scheme says what
/// kind of identifier it is, and the location is where a platform can fetch it.
/// </remarks>
public sealed class FacturXSupportingDocument
{
    /// <summary>The identifier of the document, <c>BT-122</c>.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// The scheme the identifier belongs to, <c>BT-18-1</c> — the same code list the identifier of
    /// an invoiced object uses.
    /// </summary>
    public string? Scheme { get; init; }

    /// <summary>A description of the document, <c>BT-123</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Where the document can be retrieved, <c>BT-124</c>.</summary>
    public string? Uri { get; init; }
}
