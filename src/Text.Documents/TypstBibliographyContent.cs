using System;

namespace M3scalinum.Text.Documents;

/// <summary>Places a citation inside a paragraph instead of on a line of its own.</summary>
/// <remarks>
/// <see cref="TypstDocument.Citation"/> appends a citation to the document body, which is a
/// paragraph of its own. A sentence that cites in passing uses this extension instead:
/// <code>
/// var document = TypstDocument.Create();
/// document.Paragraph(c => c.Text("The layout pass is linear ").Cite(document, "chen2022").Text("."));
/// </code>
/// The citation is recorded in the document's citation order at the moment it is built, so
/// numbering is the same whether the citation is inline or in the body.
/// </remarks>
public static class TypstCitationContentExtensions
{
    /// <summary>Appends a citation of <paramref name="key"/> to the content.</summary>
    /// <param name="content">The content to append to.</param>
    /// <param name="document">The document whose bibliography and citation order apply.</param>
    /// <param name="key">The citation key.</param>
    /// <param name="prose">Whether the citation is a grammatical part of the sentence.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank.</exception>
    /// <exception cref="TypstCitationException">The bibliography does not define the key.</exception>
    public static TypstContent Cite(
        this TypstContent content,
        TypstDocument document,
        string key,
        bool prose = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(document);

        document.CitationContent(key, prose).AppendTo(content);
        return content;
    }
}
