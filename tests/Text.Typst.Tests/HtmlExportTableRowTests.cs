using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// Fails when the HTML exporter writes a row that the markup does not emit.
/// </summary>
/// <remarks>
/// A row entirely absorbed by a vertical merge carries no cell: the table builder drops it and the
/// Typst markup never writes it. The HTML writer used to emit an empty <c>tr</c> in its place, which
/// is valid HTML and a phantom row to every reader of the table.
/// </remarks>
public sealed class HtmlExportTableRowTests
{
    [Fact]
    public void ToHtml_With_A_Row_Absorbed_By_A_Vertical_Merge_Writes_No_Empty_Row()
    {
        var document = TypstDocument.Create()
            .Table(table => table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("tall", rowspan: 2))
                .Row()
                .Row("below", "cells"));

        var result = document.ToHtml();

        Assert.DoesNotContain("<tr></tr>", result.Html, System.StringComparison.Ordinal);
        Assert.Equal(2, CountOf(result.Html, "<tr>"));
    }

    [Fact]
    public void ToHtml_With_A_Rowspan_Writes_The_Attribute()
    {
        var document = TypstDocument.Create()
            .Table(table => table
                .Columns(TypstLength.Fr(1), TypstLength.Fr(1))
                .Row(new TypstTableCell("tall", rowspan: 2))
                .Row("right"));

        var result = document.ToHtml();

        Assert.Contains("rowspan=\"2\"", result.Html, System.StringComparison.Ordinal);
        Assert.Equal(2, CountOf(result.Html, "<tr>"));
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        var index = text.IndexOf(token, System.StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, System.StringComparison.Ordinal);
        }

        return count;
    }
}
