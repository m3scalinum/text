using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Docx;
using M3scalinum.Text.Docx.Internal;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>A Word table with merged cells, followed from the reader into the model and back.</summary>
/// <remarks>
/// The first test feeds the model the AST the native reader writes for a merged table, which needs no
/// native library and runs everywhere; the second reads a package built from raw OOXML, so the whole
/// path — <c>w:gridSpan</c>, <c>w:vMerge</c>, the reader and the conversion — is pinned in CI.
/// </remarks>
public sealed class DocxTableSpanTests
{
    [Fact]
    public void A_merged_table_from_the_reader_keeps_its_spans_in_the_model()
    {
        var table = Assert.IsType<DocxTableBlock>(Assert.Single(Build(MergedAst).Blocks));

        Assert.Equal(3, table.Columns);
        Assert.Equal(3, table.Rows.Count);

        Assert.Equal(2, table.Rows[0].Cells[0].Colspan);
        Assert.Equal(2, table.Rows[0].Cells[0].Rowspan);
        Assert.Equal("Merged", table.Rows[0].Cells[0].Blocks.OfType<DocxParagraph>().Single().Text);

        // The row the merge covers completely holds no cell: Word's continuations were dropped.
        Assert.Empty(table.Rows[1].Cells);

        Assert.Equal(3, table.Rows[2].Cells.Count);
    }

    [Fact]
    public void A_merged_table_from_the_reader_becomes_markup_with_the_same_grid()
    {
        var markup = Build(MergedAst).ToTypstDocument().ToMarkup();

        Assert.Equal(
            "#table(\n" +
            "  columns: (1fr, 1fr, 1fr),\n" +
            "  table.cell(colspan: 2, rowspan: 2)[Merged], [Third],\n" +
            "  [Bottom a], [Bottom b], [Bottom c],\n" +
            ")\n",
            markup);
    }

    [Fact]
    public void A_merged_table_from_the_reader_exports_the_same_grid_as_html()
    {
        var html = Build(MergedAst).ToTypstDocument().ToHtml().Html;

        Assert.Contains("<td colspan=\"2\" rowspan=\"2\">Merged</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>Bottom c</td>", html, StringComparison.Ordinal);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public void A_docx_with_a_merged_table_reads_back_with_the_same_grid()
    {
        var table = Assert.IsType<DocxTableBlock>(
            Assert.Single(DocxReader.Read(Package(MergedDocumentXml)).Blocks));

        Assert.Equal(3, table.Columns);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells[0].Colspan);
        Assert.Equal(2, table.Rows[0].Cells[0].Rowspan);
        Assert.Empty(table.Rows[1].Cells);
        Assert.Equal(3, table.Rows[2].Cells.Count);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public void A_docx_with_a_merged_table_converts_to_the_same_grid()
    {
        var document = DocxReader.Read(Package(MergedDocumentXml));
        var markup = document.ToTypstDocument().ToMarkup();

        Assert.Contains(
            "table.cell(colspan: 2, rowspan: 2)[Merged]", markup, StringComparison.Ordinal);
        Assert.Contains("[Third],\n", markup, StringComparison.Ordinal);
        Assert.Contains("[Bottom a], [Bottom b], [Bottom c],\n", markup, StringComparison.Ordinal);
    }

    /// <summary>The AST the native reader writes for the table in <see cref="MergedDocumentXml"/>.</summary>
    private const string MergedAst =
        """
        {"blocks":[{"type":"table","columns":3,"rows":[
          {"cells":[{"colspan":2,"rowspan":2,"blocks":[
            {"type":"paragraph","runs":[{"type":"text","text":"Merged"}]}]},
            {"colspan":1,"rowspan":1,"blocks":[
            {"type":"paragraph","runs":[{"type":"text","text":"Third"}]}]}]},
          {"cells":[]},
          {"cells":[
            {"colspan":1,"rowspan":1,"blocks":[
              {"type":"paragraph","runs":[{"type":"text","text":"Bottom a"}]}]},
            {"colspan":1,"rowspan":1,"blocks":[
              {"type":"paragraph","runs":[{"type":"text","text":"Bottom b"}]}]},
            {"colspan":1,"rowspan":1,"blocks":[
              {"type":"paragraph","runs":[{"type":"text","text":"Bottom c"}]}]}]}]}]}
        """;

    /// <summary>
    /// A three-column table whose first cell spans two columns and two rows, so the row below it
    /// holds nothing and the last row holds three cells.
    /// </summary>
    private const string MergedDocumentXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:tbl>
              <w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/></w:tblGrid>
              <w:tr>
                <w:tc>
                  <w:tcPr><w:gridSpan w:val="2"/><w:vMerge w:val="restart"/></w:tcPr>
                  <w:p><w:r><w:t>Merged</w:t></w:r></w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>Third</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge/></w:tcPr><w:p/></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Bottom a</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Bottom b</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Bottom c</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
          </w:body>
        </w:document>
        """;

    private static DocxDocument Build(string json)
    {
        var ast = JsonSerializer.Deserialize(json, DocxJsonContext.Default.DocxAstDto)!;

        return DocxModel.Build(ast, new System.Collections.Generic.Dictionary<string, byte[]>());
    }

    /// <summary>The fixture's parts, with the document part replaced by <paramref name="documentXml"/>.</summary>
    private static byte[] Package(string documentXml)
    {
        var parts = DocxFixture.Parts()
            .Select(part => part.Name == "word/document.xml"
                ? (part.Name, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(documentXml))
                : part)
            .ToList();

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }
}
