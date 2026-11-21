using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Imports the supported subset of HTML into the document model. See
/// <c>docs/specifications/04-document-model.md</c> for the fidelity contract: which elements are
/// supported, which are dropped and counted, and what the import deliberately loses.
/// </summary>
/// <remarks>
/// The parser is hand-written and has no dependency of its own. It repairs the omissions HTML
/// allows — void elements, unclosed <c>p</c>, <c>li</c> and <c>td</c>, attributes with single,
/// double or no quotes, stray end tags — and decodes the character references listed in the
/// specification. It never fetches a network resource and never reads a file.
/// </remarks>
public sealed class TypstHtmlImporter
{
    /// <summary>Creates an importer, with default options when none are given.</summary>
    public TypstHtmlImporter(TypstHtmlImportOptions? options = null)
        => Options = options ?? new TypstHtmlImportOptions();

    /// <summary>The options this importer was created with.</summary>
    public TypstHtmlImportOptions Options { get; }

    /// <summary>Imports an HTML document or fragment, reporting what was dropped.</summary>
    /// <exception cref="TypstHtmlImportException">
    /// An <c>img</c> source does not resolve to a registered asset, or an unsupported element was
    /// met with <see cref="TypstHtmlUnknownElementPolicy.Throw"/>.
    /// </exception>
    public TypstHtmlImportResult Import(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var root = HtmlTreeBuilder.Parse(html);
        return new HtmlDocumentConverter(Options).Convert(root);
    }
}
