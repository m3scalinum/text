using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>Turns the native AST into the public tree.</summary>
internal static class DocxModel
{
    internal static DocxDocument Build(DocxAstDto ast, IReadOnlyDictionary<string, byte[]> images)
        => new(Blocks(ast.Blocks), images, Page(ast.Page));

    private static List<DocxBlock> Blocks(List<DocxBlockDto>? blocks)
    {
        var result = new List<DocxBlock>(blocks?.Count ?? 0);

        if (blocks is null)
        {
            return result;
        }

        foreach (var block in blocks)
        {
            switch (block.Type)
            {
                case "paragraph":
                    result.Add(Paragraph(block));
                    break;
                case "table":
                    result.Add(Table(block));
                    break;
                default:
                    // An unknown block type is a newer reader's shape; skipping it keeps a document
                    // readable rather than failing on the part this package does not know.
                    break;
            }
        }

        return result;
    }

    private static DocxParagraph Paragraph(DocxBlockDto block)
    {
        var inlines = new List<DocxInline>(block.Runs?.Count ?? 0);

        foreach (var run in block.Runs ?? [])
        {
            switch (run.Type)
            {
                case "text":
                    inlines.Add(new DocxTextRun(
                        run.Text ?? string.Empty,
                        run.Bold,
                        run.Italic,
                        run.Underline,
                        run.Strike,
                        Script(run.Script),
                        run.Color,
                        run.SizePt));
                    break;
                case "image":
                    inlines.Add(new DocxImageRun(run.Name ?? string.Empty, run.MimeType ?? string.Empty));
                    break;
                case "break":
                    inlines.Add(new DocxLineBreak());
                    break;
                default:
                    break;
            }
        }

        return new DocxParagraph(block.Heading, List(block.List), inlines);
    }

    private static DocxListInfo? List(DocxListDto? list)
        => list is null ? null : new DocxListInfo(list.NumberingId, list.Level, list.Ordered);

    private static DocxScript? Script(string? script) => script switch
    {
        "superscript" => DocxScript.Superscript,
        "subscript" => DocxScript.Subscript,
        _ => null,
    };

    private static DocxTableBlock Table(DocxBlockDto block)
    {
        var rows = new List<DocxTableRow>(block.Rows?.Count ?? 0);

        foreach (var row in block.Rows ?? [])
        {
            var cells = new List<DocxTableCell>(row.Cells.Count);

            foreach (var cell in row.Cells)
            {
                cells.Add(new DocxTableCell(cell.Colspan, cell.Rowspan, Blocks(cell.Blocks)));
            }

            rows.Add(new DocxTableRow(cells));
        }

        return new DocxTableBlock(block.Columns, rows);
    }

    private static DocxPageSetup? Page(DocxPageDto? page) => page is null
        ? null
        : new DocxPageSetup(
            page.WidthPt,
            page.HeightPt,
            page.Landscape,
            page.MarginTopPt,
            page.MarginRightPt,
            page.MarginBottomPt,
            page.MarginLeftPt);
}
