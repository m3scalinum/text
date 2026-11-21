using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A block of a Markdown document, before it is converted into document nodes.</summary>
internal abstract class MarkdownBlock;

/// <summary>A run of inline text, with the source line breaks still in it.</summary>
internal sealed class MarkdownParagraph(string text) : MarkdownBlock
{
    public string Text { get; } = text;
}

/// <summary>An ATX or setext heading.</summary>
internal sealed class MarkdownHeading(int level, string text) : MarkdownBlock
{
    public int Level { get; } = level;

    public string Text { get; } = text;
}

/// <summary>A fenced or indented code block, with the info string's first word as the language.</summary>
internal sealed class MarkdownCodeBlock(string language, string code) : MarkdownBlock
{
    public string Language { get; } = language;

    public string Code { get; } = code;
}

/// <summary>A thematic break: <c>---</c>, <c>***</c> or <c>___</c>.</summary>
internal sealed class MarkdownThematicBreak : MarkdownBlock;

/// <summary>A block quote, already parsed into the blocks it contains.</summary>
internal sealed class MarkdownQuote(IReadOnlyList<MarkdownBlock> blocks) : MarkdownBlock
{
    public IReadOnlyList<MarkdownBlock> Blocks { get; } = blocks;
}

/// <summary>One item of a list, with everything inside it parsed.</summary>
internal sealed class MarkdownListItem(IReadOnlyList<MarkdownBlock> blocks)
{
    public IReadOnlyList<MarkdownBlock> Blocks { get; } = blocks;
}

/// <summary>An ordered or unordered list.</summary>
internal sealed class MarkdownList(bool ordered, bool tight, IReadOnlyList<MarkdownListItem> items) : MarkdownBlock
{
    public bool Ordered { get; } = ordered;

    public bool Tight { get; } = tight;

    public IReadOnlyList<MarkdownListItem> Items { get; } = items;
}

/// <summary>A GFM table: the header, the alignment of each column, and the body rows.</summary>
internal sealed class MarkdownTable(
    IReadOnlyList<TypstAlignment?> alignments,
    IReadOnlyList<string> header,
    IReadOnlyList<IReadOnlyList<string>> rows) : MarkdownBlock
{
    public IReadOnlyList<TypstAlignment?> Alignments { get; } = alignments;

    public IReadOnlyList<string> Header { get; } = header;

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; } = rows;
}

/// <summary>A raw HTML block. It is counted and never imported.</summary>
internal sealed class MarkdownHtmlBlock(string html) : MarkdownBlock
{
    public string Html { get; } = html;
}
