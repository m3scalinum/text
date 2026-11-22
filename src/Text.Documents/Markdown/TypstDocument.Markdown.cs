namespace M3scalinum.Text.Documents;

/// <summary>
/// The Markdown writer of the document model: the inverse of <see cref="TypstDocument.FromMarkdown"/>.
/// </summary>
/// <remarks>
/// The mapping and the fidelity contract are those of
/// <c>docs/specifications/06-markdown-and-text-export.md</c>, applied to the nodes instead of to a
/// compiled HTML tree: a document built in C# is exported without paying for a compilation.
/// </remarks>
public sealed partial class TypstDocument
{
    /// <summary>
    /// Writes the document as Markdown, and reports the constructs that have no Markdown
    /// representation. Never fails: a construct Markdown cannot spell is counted, not thrown.
    /// </summary>
    public TypstMarkdownExportResult ToMarkdown()
    {
        var writer = new TypstMarkdownWriter();
        var markdown = writer.Write(_body, _preamble.Count, _page is not null, _metadata is not null);

        return new TypstMarkdownExportResult(markdown, writer.Snapshot());
    }
}
