namespace M3scalinum.Text.Documents;

/// <summary>
/// A language whose date and number conventions <see cref="TypstFormatting"/> can print.
/// </summary>
/// <remarks>
/// The set mirrors the languages the template pack ships labels in, because a document is written in
/// one language and its words and its figures must agree. It is a separate enum from
/// <c>M3scalinum.Text.Templates.TemplateLanguage</c> on purpose: the document model must not depend
/// on the pack that consumes it.
/// </remarks>
public enum TypstLanguage
{
    /// <summary>English: <c>1,234.56</c>, <c>03/01/2026</c>, the symbol before the amount.</summary>
    English,

    /// <summary>French: <c>1&#160;234,56</c>, <c>01/03/2026</c>, the symbol after the amount.</summary>
    French,
}
