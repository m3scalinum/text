namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// A free-text note, with the subject code the French CIUS uses to give it meaning.
/// </summary>
/// <remarks>
/// The code turns a remark into data: <c>BAR</c> marks a B2B invoice, <c>TXD</c> a member of a
/// single taxable entity, and <c>PMT</c> / <c>AAB</c> carry the payment and recovery terms the
/// French mandate requires. <c>BR-FR-05</c> reads them, so a note without its code is invisible to
/// a French platform.
/// </remarks>
public sealed class FacturXNote
{
    /// <summary>The text, <c>BT-22</c>.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>The subject code that qualifies it, <c>BT-21</c>.</summary>
    public string? SubjectCode { get; init; }
}