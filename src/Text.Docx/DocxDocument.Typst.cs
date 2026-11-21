using System;
using System.Collections.Generic;
using M3scalinum.Text.SharpTypst.Documents;

namespace M3scalinum.Text.Docx;

/// <summary>Turns a read document into the document model of <c>M3scalinum.Text.SharpTypst</c>.</summary>
public sealed partial class DocxDocument
{
    /// <summary>
    /// Builds the Typst document this Word file describes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is a <see cref="TypstDocument"/> of the suite's own model — the same one every
    /// other producer builds — so it compiles through
    /// <see cref="M3scalinum.Text.SharpTypst.Compilation.TypstCompiler"/> and exports like any other
    /// document.
    /// </para>
    /// <para>
    /// Images become <c>image("…")</c> calls, and an image is addressed by the part name in
    /// <see cref="Images"/>. Register each one as an asset of the world, or the compile fails with a
    /// missing file:
    /// </para>
    /// <code>
    /// var builder = new TypstWorldBuilder().AddSource("main.typ", document.ToTypstDocument().ToMarkup());
    ///
    /// foreach (var (name, bytes) in document.Images)
    /// {
    ///     builder.AddAsset(name, bytes);
    /// }
    /// </code>
    /// <para>
    /// An inline image becomes a block-level figure: Word keeps a picture in the run flow and Typst
    /// has no inline image, so the picture is emitted before the paragraph that held it.
    /// </para>
    /// </remarks>
    public TypstDocument ToTypstDocument()
    {
        var document = TypstDocument.Create();

        if (Page is { } page)
        {
            document.Page(setup => setup
                .Size(TypstLength.Pt(page.WidthPoints), TypstLength.Pt(page.HeightPoints))
                .Margin(
                    TypstLength.Pt(page.MarginTopPoints),
                    TypstLength.Pt(page.MarginRightPoints),
                    TypstLength.Pt(page.MarginBottomPoints),
                    TypstLength.Pt(page.MarginLeftPoints)));
        }

        AppendBlocks(document, Blocks);

        return document;
    }

    private static void AppendBlocks(TypstDocument document, IReadOnlyList<DocxBlock> blocks)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            switch (blocks[index])
            {
                case DocxParagraph { List: { } list }:
                    index = AppendList(document, blocks, index, list.NumberingId, list.Ordered);
                    break;

                case DocxParagraph paragraph:
                    AppendParagraph(document, paragraph);
                    break;

                case DocxTableBlock table:
                    AppendTable(document, table);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Emits the run of consecutive paragraphs that share one numbering as a single list, and
    /// returns the index of the last paragraph it consumed.
    /// </summary>
    private static int AppendList(
        TypstDocument document,
        IReadOnlyList<DocxBlock> blocks,
        int start,
        long numberingId,
        bool ordered)
    {
        var items = new List<TypstContent>();

        var index = start;

        while (index < blocks.Count
            && blocks[index] is DocxParagraph { List: { } list } paragraph
            && list.NumberingId == numberingId)
        {
            // Word nests with a level; Typst nests with an indented sublist, which the model's list
            // builder does not express, so a deeper item stays in the same list.
            items.Add(Content(paragraph.Inlines));
            index++;
        }

        if (items.Count > 0)
        {
            if (ordered)
            {
                document.NumberedList([.. items]);
            }
            else
            {
                document.BulletList([.. items]);
            }
        }

        return index - 1;
    }

    private static void AppendParagraph(TypstDocument document, DocxParagraph paragraph)
    {
        EmitFigures(document, paragraph.Inlines);

        var content = Content(paragraph.Inlines);

        if (!HasText(paragraph.Inlines))
        {
            return;
        }

        if (paragraph.HeadingLevel is { } level)
        {
            document.Heading(level, content);
            return;
        }

        document.Paragraph(content);
    }

    private static void EmitFigures(TypstDocument document, IReadOnlyList<DocxInline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is DocxImageRun image)
            {
                document.Figure(figure => figure.Image(image.Name));
            }
        }
    }

    private static void AppendTable(TypstDocument document, DocxTableBlock table)
    {
        var columns = Math.Max(table.Columns, WidestRow(table));

        if (columns <= 0)
        {
            return;
        }

        document.Table(builder =>
        {
            var widths = new TypstLength[columns];

            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = TypstLength.Fr(1);
            }

            builder.Columns(widths);

            foreach (var row in table.Rows)
            {
                var cells = new TypstTableCell[row.Cells.Count];

                for (var index = 0; index < row.Cells.Count; index++)
                {
                    var cell = row.Cells[index];

                    cells[index] = new TypstTableCell(
                        CellContent(cell), Math.Max(cell.Colspan, 1), Math.Max(cell.Rowspan, 1));
                }

                builder.Row(cells);
            }
        });
    }

    private static int WidestRow(DocxTableBlock table)
    {
        var widest = 0;

        foreach (var row in table.Rows)
        {
            var columns = 0;

            foreach (var cell in row.Cells)
            {
                columns += Math.Max(cell.Colspan, 1);
            }

            widest = Math.Max(widest, columns);
        }

        return widest;
    }

    /// <summary>
    /// A cell's body. Typst's table cell holds content, not blocks, so a cell's paragraphs are joined
    /// by line breaks and a nested table — which Typst would need a block for — is dropped.
    /// </summary>
    private static TypstContent CellContent(DocxTableCell cell)
    {
        var content = new TypstContent();
        var first = true;

        foreach (var block in cell.Blocks)
        {
            if (block is not DocxParagraph paragraph || !HasText(paragraph.Inlines))
            {
                continue;
            }

            if (!first)
            {
                content.LineBreak();
            }

            AppendInlines(content, paragraph.Inlines);
            first = false;
        }

        return content;
    }

    private static TypstContent Content(IReadOnlyList<DocxInline> inlines)
    {
        var content = new TypstContent();
        AppendInlines(content, inlines);
        return content;
    }

    private static void AppendInlines(TypstContent content, IReadOnlyList<DocxInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case DocxTextRun run:
                    AppendRun(content, run, 0);
                    break;

                case DocxLineBreak:
                    content.LineBreak();
                    break;

                default:
                    // An image is a block-level figure, never a run of content.
                    break;
            }
        }
    }

    /// <summary>
    /// Adds one run to <paramref name="sink"/>, one decoration per layer, outermost first.
    /// </summary>
    /// <remarks>
    /// The model has no "append this content as it stands" method, so each layer is a call on the
    /// sink itself: bold wraps everything inside it, italic wraps what is inside that, and the
    /// innermost layer writes the characters with their colour and size. That also fixes the
    /// nesting order — Word lists the properties in no order and Typst nests strictly.
    /// </remarks>
    private static void AppendRun(TypstContent sink, DocxTextRun run, int layer)
    {
        switch (layer)
        {
            case 0 when run.Bold:
                sink.Bold(Child(run, 1));
                return;
            case 0:
                AppendRun(sink, run, 1);
                return;

            case 1 when run.Italic:
                sink.Emphasis(Child(run, 2));
                return;
            case 1:
                AppendRun(sink, run, 2);
                return;

            case 2 when run.Underline is not null:
                sink.Underline(Child(run, 3));
                return;
            case 2:
                AppendRun(sink, run, 3);
                return;

            case 3 when run.Strike:
                sink.Strikethrough(Child(run, 4));
                return;
            case 3:
                AppendRun(sink, run, 4);
                return;

            case 4 when run.Script == DocxScript.Superscript:
                sink.Superscript(Child(run, 5));
                return;
            case 4 when run.Script == DocxScript.Subscript:
                sink.Subscript(Child(run, 5));
                return;
            case 4:
                AppendRun(sink, run, 5);
                return;

            default:
                WriteCharacters(sink, run);
                return;
        }
    }

    private static TypstContent Child(DocxTextRun run, int layer)
    {
        var content = new TypstContent();
        AppendRun(content, run, layer);
        return content;
    }

    private static void WriteCharacters(TypstContent sink, DocxTextRun run)
    {
        if (!IsHexColour(run.Color) && run.SizePoints is null)
        {
            sink.Text(run.Text);
            return;
        }

        var style = new TypstTextStyle();

        if (IsHexColour(run.Color))
        {
            style.Fill(TypstColor.Hex(run.Color!));
        }

        if (run.SizePoints is { } size)
        {
            style.Size(TypstLength.Pt(size));
        }

        sink.Styled(run.Text, style);
    }

    /// <summary>Word writes <c>auto</c> and theme colours too, which are not hex; they are ignored.</summary>
    private static bool IsHexColour(string? colour)
    {
        if (colour is not { Length: 6 })
        {
            return false;
        }

        foreach (var character in colour)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasText(IReadOnlyList<DocxInline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is DocxTextRun or DocxLineBreak)
            {
                return true;
            }
        }

        return false;
    }
}
