namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// Identifiers of the layouts shipped in <c>M3scalinum.Text.Templates</c>. They are the ids the
/// catalog is keyed by, the names of the embedded template directories, and the keys of
/// <see cref="TemplateLabels.For"/>.
/// </summary>
public static class TemplateIds
{
    /// <summary>An invoice: what was sold, what it costs, what is due.</summary>
    public const string Invoice = "invoice";

    /// <summary>A quotation: what is offered and until when.</summary>
    public const string Quotation = "quotation";

    /// <summary>An account statement: movements between two dates and the balance after each.</summary>
    public const string Statement = "statement";

    /// <summary>An attestation: an employer certifying a fact about an employee.</summary>
    public const string Attestation = "attestation";
}
