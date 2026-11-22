using System;
using System.IO;
using System.Linq;
using M3scalinum.Text.Xlsx;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>What the reader makes of a workbook the fixture writes.</summary>
/// <remarks>
/// The bytes are a real package — content types, relationships, shared strings, styles and the sheet
/// XML Excel writes — so what these tests exercise is the whole path: zip, XML, typed cells.
/// </remarks>
public sealed class XlsxReaderTests
{
    internal const string InvoiceName = "Facture";
    internal const string AnnexName = "Annexe";
    internal const string Title = "Facture 2026-014";
    internal const string ClientName = "ACME";
    internal const double TotalBeforeTax = 1000;
    internal const double Tax = 200;
    internal const double TotalWithTax = 1200;

    [Fact]
    public void A_text_cell_is_text()
    {
        var sheet = Invoice();

        var cell = Assert.IsType<XlsxTextValue>(Find(sheet, 0, 0).Value);

        Assert.Equal(Title, cell.Text);
    }

    [Fact]
    public void A_number_cell_is_a_number()
    {
        var cell = Assert.IsType<XlsxNumberValue>(Find(Invoice(), 2, 1).Value);

        Assert.Equal(TotalBeforeTax, cell.Number);
    }

    [Fact]
    public void A_boolean_cell_is_a_boolean()
    {
        var cell = Assert.IsType<XlsxBooleanValue>(Find(Invoice(), 2, 2).Value);

        Assert.True(cell.Value);
    }

    [Fact]
    public void A_date_cell_is_a_date()
    {
        var cell = Assert.IsType<XlsxDateValue>(Find(Invoice(), 0, 2).Value);

        Assert.Equal(new DateTime(2026, 1, 1), cell.Value);
        Assert.True(cell.IsDateOnly);
    }

    [Fact]
    public void A_duration_cell_is_a_duration()
    {
        var cell = Assert.IsType<XlsxDurationValue>(Find(Invoice(), 0, 4).Value);

        Assert.Equal(TimeSpan.FromHours(36), cell.Value);
    }

    [Fact]
    public void An_error_cell_is_an_error()
    {
        var cell = Assert.IsType<XlsxErrorValue>(Find(Invoice(), 3, 3).Value);

        Assert.Equal("#DIV/0!", cell.Error);
    }

    [Fact]
    public void A_formula_cell_carries_its_cached_value_and_its_formula()
    {
        var cell = Find(Invoice(), 4, 1);

        Assert.Equal("=B3+B4", cell.Formula);
        Assert.Equal(TotalWithTax, Assert.IsType<XlsxNumberValue>(cell.Value).Number);
    }

    [Fact]
    public void A_formula_without_a_cached_value_displays_its_formula()
    {
        var cell = Find(Invoice(), 5, 1);

        Assert.IsType<XlsxBlankValue>(cell.Value);
        Assert.Equal("=B3*0.2", cell.Formula);
        Assert.Equal("=B3*0.2", cell.DisplayText);
    }

    [Fact]
    public void The_used_region_is_reported()
    {
        var sheet = Invoice();

        Assert.Equal(7, sheet.RowCount);
        Assert.Equal(5, sheet.ColumnCount);
        Assert.Equal(0, sheet.FirstRow);
        Assert.Equal(0, sheet.FirstColumn);
    }

    [Fact]
    public void A_region_that_starts_further_down_reports_where_it_starts()
    {
        var workbook = Read(XlsxFixture.Build(InvoiceName, new TestText(4, 2, ClientName)));

        var sheet = workbook.Sheets[0];

        Assert.Equal(1, sheet.RowCount);
        Assert.Equal(1, sheet.ColumnCount);
        Assert.Equal(4, sheet.FirstRow);
        Assert.Equal(2, sheet.FirstColumn);
        Assert.Equal("C5", sheet.Cells[0].Reference(sheet));
    }

    [Fact]
    public void Every_sheet_is_read_in_workbook_order()
    {
        var workbook = Read(Annexed());

        Assert.Equal(2, workbook.Sheets.Count);
        Assert.Equal(InvoiceName, workbook.Sheets[0].Name);
        Assert.Equal(AnnexName, workbook.Sheets[1].Name);
        Assert.Equal(2.0, Assert.IsType<XlsxNumberValue>(Find(workbook.Sheets[1], 1, 1).Value).Number);
    }

    [Fact]
    public void A_workbook_finds_a_sheet_by_name()
    {
        var workbook = Read(Annexed());

        Assert.NotNull(workbook.Find(AnnexName));
        Assert.Null(workbook.Find("Compte de résultat"));
    }

    [Fact]
    public void An_empty_sheet_reads_as_a_sheet_without_cells()
    {
        var workbook = Read(new XlsxFixture().EmptySheet("Vide").Build());

        var sheet = Assert.Single(workbook.Sheets);

        Assert.Equal(0, sheet.RowCount);
        Assert.Equal(0, sheet.ColumnCount);
        Assert.Empty(sheet.Cells);
    }

    [Fact]
    public void A_cell_is_found_by_position_and_missing_ones_are_null()
    {
        var sheet = Invoice();

        Assert.NotNull(sheet.Find(1, 0));
        Assert.Null(sheet.Find(6, 4));
        Assert.Null(sheet.Find(-1, 0));
    }

    [Fact]
    public void A_cell_reference_is_its_excel_name()
    {
        Assert.Equal("A1", XlsxSheet.Reference(0, 0));
        Assert.Equal("B7", XlsxSheet.Reference(6, 1));
        Assert.Equal("AA1", XlsxSheet.Reference(0, 26));
        Assert.Equal("AB100", XlsxSheet.Reference(99, 27));
    }

    [Fact]
    public void A_reference_refuses_a_negative_coordinate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XlsxSheet.Reference(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => XlsxSheet.Reference(0, -1));
    }

    [Fact]
    public void A_reference_needs_the_sheet_it_belongs_to()
    {
        var sheet = Invoice();

        Assert.Throws<ArgumentNullException>(() => sheet.Cells[0].Reference(null!));
    }

    [Fact]
    public void A_null_sheet_name_is_an_argument_error()
    {
        Assert.Throws<ArgumentNullException>(() => Read(Annexed()).Find(null!));
    }

    /// <summary>The workbook every test above describes.</summary>
    internal static byte[] Annexed()
        => new XlsxFixture()
            .Sheet(
                InvoiceName,
                new TestText(0, 0, Title),
                new TestDate(0, 2, 46023),
                new TestDuration(0, 4, 1.5),
                new TestText(1, 0, "Client"),
                new TestText(1, 1, ClientName),
                new TestNumber(2, 1, TotalBeforeTax),
                new TestBoolean(2, 2, true),
                new TestNumber(3, 1, Tax),
                new TestError(3, 3, "#DIV/0!"),
                new TestFormula(4, 1, "B3+B4", TotalWithTax),
                new TestFormula(5, 1, "B3*0.2", null),
                new TestText(6, 0, "Total TTC"))
            .Sheet(AnnexName, new TestText(0, 0, "Détail"), new TestNumber(1, 1, 2.0))
            .Build();

    /// <summary>The invoice sheet alone.</summary>
    internal static XlsxSheet Invoice() => Read(Annexed()).Sheets[0];

    internal static XlsxWorkbook Read(byte[] workbook)
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);
        return XlsxReader.Read(workbook);
    }

    internal static XlsxCell Find(XlsxSheet sheet, int row, int column)
        => Assert.IsType<XlsxCell>(sheet.Find(row, column));
}
