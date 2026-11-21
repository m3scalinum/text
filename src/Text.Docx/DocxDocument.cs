using System.Collections.Generic;

namespace M3scalinum.Text.Docx;

/// <summary>A Word document, read into a typed tree.</summary>
/// <remarks>
/// The tree mirrors the body of <c>word/document.xml</c>: blocks in document order, each paragraph
/// with its inline content, each table with its merged cells. What Word stores beyond that — styles
/// other than the run properties, footnotes, tracked changes, fields — is not represented; see
/// <c>docs/specifications/22-docx.md</c> for the list and the reasons.
/// </remarks>
public sealed partial class DocxDocument
{
    internal DocxDocument(
        IReadOnlyList<DocxBlock> blocks,
        IReadOnlyDictionary<string, byte[]> images,
        DocxPageSetup? page)
    {
        Blocks = blocks;
        Images = images;
        Page = page;
    }

    /// <summary>The body, in document order.</summary>
    public IReadOnlyList<DocxBlock> Blocks { get; }

    /// <summary>
    /// The embedded images, keyed by part name (<c>word/media/image1.png</c>). Empty when
    /// <see cref="DocxOptions.ExtractImages"/> was off, or when the document embeds none.
    /// </summary>
    /// <remarks>
    /// The bytes are the original part, byte for byte: this reader never decodes or re-encodes an
    /// image, so an EMF stays an EMF and a PNG keeps its compression.
    /// </remarks>
    public IReadOnlyDictionary<string, byte[]> Images { get; }

    /// <summary>
    /// The page Word laid the document out on, or <see langword="null"/> when the document states no
    /// page setup of its own.
    /// </summary>
    public DocxPageSetup? Page { get; }
}
