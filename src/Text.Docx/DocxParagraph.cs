using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Docx;

/// <summary>A paragraph: its inline content, and what it is — a heading, a list item, or neither.</summary>
public sealed class DocxParagraph : DocxBlock
{
    internal DocxParagraph(int? headingLevel, DocxListInfo? list, IReadOnlyList<DocxInline> inlines)
    {
        HeadingLevel = headingLevel;
        List = list;
        Inlines = inlines;

        var text = new StringBuilder();

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case DocxTextRun run:
                    text.Append(run.Text);
                    break;
                case DocxLineBreak:
                    text.Append('\n');
                    break;
                default:
                    // An image contributes no characters.
                    break;
            }
        }

        Text = text.ToString();
    }

    /// <summary>
    /// The heading level, 1 to 6, when Word's paragraph style says this is a heading; otherwise
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Word's <c>Heading1</c> and its localised equivalents both reduce to a level, and a paragraph
    /// with an explicit outline level is a heading too. Levels past six are clamped, because Typst
    /// has no seventh heading.
    /// </remarks>
    public int? HeadingLevel { get; }

    /// <summary>Where the paragraph sits in a list, or <see langword="null"/> when it is not one.</summary>
    public DocxListInfo? List { get; }

    /// <summary>The inline content, in document order.</summary>
    public IReadOnlyList<DocxInline> Inlines { get; }

    /// <summary>The paragraph's text: the runs concatenated, each line break as a newline.</summary>
    public string Text { get; }
}
