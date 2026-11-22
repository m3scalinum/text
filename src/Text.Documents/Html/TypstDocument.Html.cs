using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>HTML export: the model written as semantic HTML5.</summary>
public sealed partial class TypstDocument
{
    /// <summary>The block nodes, in document order, for the HTML writer.</summary>
    internal IReadOnlyList<TypstNode> HtmlBody => _body;

    /// <summary>
    /// Writes the document as an HTML5 body fragment.
    /// </summary>
    /// <returns>
    /// The fragment and the nodes that have no HTML equivalent. An empty document returns an empty
    /// string and no losses.
    /// </returns>
    /// <remarks>
    /// The mapping is semantic, not visual: headings become <c>h1</c>–<c>h6</c>, paragraphs
    /// <c>p</c>, lists <c>ul</c>/<c>ol</c>, tables <c>table</c> with <c>th</c> and <c>td</c>,
    /// strong and emphasis <c>strong</c>/<c>em</c>, links <c>a href</c>, figures
    /// <c>figure</c>/<c>figcaption</c>, images <c>img src</c>. Page setup, metadata and the Typst
    /// preamble do not reach a fragment: they describe a page, not content.
    /// <para>
    /// Nodes that carry Typst code or layout instead of content — raw markup, <c>#place</c> and the
    /// other calls, a bibliography, an outline — cannot be represented. They are listed in
    /// <see cref="TypstHtmlExportResult.Unrepresentable"/> with their name and count, never
    /// silently transformed. The output is deterministic and uses <c>\n</c> line endings.
    /// </para>
    /// </remarks>
    public TypstHtmlExportResult ToHtml() => TypstHtmlWriter.Write(this);
}

/// <summary>HTML export of inline content: the nodes of a run, for the HTML writer.</summary>
public sealed partial class TypstContent
{
    /// <summary>The nodes of this run, in order, for the HTML writer.</summary>
    internal IReadOnlyList<TypstNode> HtmlNodes => _nodes;
}
