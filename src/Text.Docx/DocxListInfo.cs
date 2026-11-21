using System.Collections.Generic;

namespace M3scalinum.Text.Docx;

/// <summary>Where a paragraph sits in a list: which numbering, at which level, counting or not.</summary>
public sealed class DocxListInfo
{
    internal DocxListInfo(long numberingId, int level, bool ordered)
    {
        NumberingId = numberingId;
        Level = level;
        Ordered = ordered;
    }

    /// <summary>
    /// The `numId` the paragraph points at. Two paragraphs in the same list share it; consecutive
    /// paragraphs with the same id and level are one list.
    /// </summary>
    public long NumberingId { get; }

    /// <summary>Nesting level, zero-based: 0 is the outermost list.</summary>
    public int Level { get; }

    /// <summary>
    /// Whether the level counts. A bulleted list is not ordered, and a numbered one is, which is
    /// what decides between a bullet list and a numbered list in
    /// <see cref="DocxDocument.ToTypstDocument"/>.
    /// </summary>
    public bool Ordered { get; }
}
