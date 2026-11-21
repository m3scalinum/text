using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>
/// Imports the supported subset of Markdown into the document model. See
/// <c>docs/specifications/04-document-model.md</c> for the fidelity contract: which constructs are
/// supported, which are dropped and counted, and what the import deliberately loses.
/// </summary>
/// <remarks>
/// The parser is hand-written and has no dependency of its own: a line-based block pass, a
/// two-pass definition collection for reference links and footnotes, and a recursive inline
/// pass. It never fetches a network resource and never reads a file. Every piece of text enters
/// the model as a node, so the markup writer escapes it; nothing is concatenated into Typst
/// source.
/// </remarks>
public sealed class MarkdownImporter
{
    /// <summary>Creates an importer, with default options when none are given.</summary>
    public MarkdownImporter(TypstMarkdownImportOptions? options = null)
        => Options = options ?? new TypstMarkdownImportOptions();

    /// <summary>The options this importer was created with.</summary>
    public TypstMarkdownImportOptions Options { get; }

    /// <summary>Imports a Markdown document or fragment, reporting what was dropped.</summary>
    /// <exception cref="TypstMarkdownImportException">
    /// An image destination does not resolve to a registered asset, or raw HTML was met with
    /// <see cref="TypstMarkdownRawHtmlPolicy.Throw"/>.
    /// </exception>
    public TypstMarkdownImportResult Import(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return new MarkdownDocumentConverter(Options).Convert(markdown);
    }
}
