using System;
using M3scalinum.Text.Xlsx;
using M3scalinum.Text.Xlsx.Internal;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>How the native tree becomes the public one, including the shapes this build does not know.</summary>
public sealed class XlsxModelTests
{
    [Fact]
    public void A_text_entry_becomes_a_text_value()
    {
        var value = Value("text", cell => cell.Text = "Client");

        Assert.Equal("Client", Assert.IsType<XlsxTextValue>(value).Text);
    }

    [Fact]
    public void A_text_entry_with_no_characters_is_an_empty_text()
    {
        Assert.Equal(string.Empty, Assert.IsType<XlsxTextValue>(Value("text")).Text);
    }

    [Fact]
    public void A_number_entry_becomes_a_number_value()
    {
        Assert.Equal(1200, Assert.IsType<XlsxNumberValue>(Value("number", cell => cell.Number = 1200)).Number);
    }

    [Fact]
    public void A_number_entry_with_no_value_is_zero()
    {
        Assert.Equal(0, Assert.IsType<XlsxNumberValue>(Value("number")).Number);
    }

    [Fact]
    public void A_boolean_entry_becomes_a_boolean_value()
    {
        Assert.True(Assert.IsType<XlsxBooleanValue>(Value("boolean", cell => cell.Value = true)).Value);
    }

    [Fact]
    public void A_boolean_entry_with_no_value_is_false()
    {
        Assert.False(Assert.IsType<XlsxBooleanValue>(Value("boolean")).Value);
    }

    [Fact]
    public void A_date_entry_becomes_a_date_value()
    {
        var value = Assert.IsType<XlsxDateValue>(
            Value("date", cell => { cell.Iso = "2026-01-15"; cell.DateOnly = true; }));

        Assert.Equal(new DateTime(2026, 1, 15), value.Value);
        Assert.True(value.IsDateOnly);
    }

    [Fact]
    public void A_date_entry_this_package_cannot_parse_keeps_its_text()
    {
        var value = Assert.IsType<XlsxTextValue>(Value("date", cell => cell.Iso = "not a date"));

        Assert.Equal("not a date", value.Text);
    }

    [Fact]
    public void A_date_entry_with_no_text_is_an_empty_text()
    {
        Assert.Equal(string.Empty, Assert.IsType<XlsxTextValue>(Value("date")).Text);
    }

    [Fact]
    public void A_duration_entry_becomes_a_duration_value()
    {
        var value = Assert.IsType<XlsxDurationValue>(Value("duration", cell => cell.Seconds = 5400));

        Assert.Equal(TimeSpan.FromMinutes(90), value.Value);
    }

    [Fact]
    public void A_duration_entry_with_no_value_is_zero()
    {
        Assert.Equal(TimeSpan.Zero, Assert.IsType<XlsxDurationValue>(Value("duration")).Value);
    }

    [Fact]
    public void An_error_entry_becomes_an_error_value()
    {
        Assert.Equal("#DIV/0!", Assert.IsType<XlsxErrorValue>(Value("error", cell => cell.Error = "#DIV/0!")).Error);
    }

    [Fact]
    public void An_error_entry_with_no_text_is_the_unavailable_error()
    {
        Assert.Equal("#N/A", Assert.IsType<XlsxErrorValue>(Value("error")).Error);
    }

    [Fact]
    public void A_type_this_build_does_not_know_is_a_blank_cell()
    {
        Assert.Same(XlsxBlankValue.Instance, Value("chart"));
    }

    [Fact]
    public void A_sheet_keeps_its_region_and_its_offsets()
    {
        var workbook = XlsxModel.Build(new XlsxWorkbookDto
        {
            Sheets =
            [
                new XlsxSheetDto
                {
                    Name = "Facture",
                    Rows = 2,
                    Columns = 3,
                    RowOffset = 4,
                    ColumnOffset = 1,
                    Cells =
                    [
                        new XlsxCellDto { Row = 0, Column = 2, Type = "text", Text = "Total" },
                    ],
                },
            ],
        });

        var sheet = Assert.Single(workbook.Sheets);

        Assert.Equal("Facture", sheet.Name);
        Assert.Equal(2, sheet.RowCount);
        Assert.Equal(3, sheet.ColumnCount);
        Assert.Equal(4, sheet.FirstRow);
        Assert.Equal(1, sheet.FirstColumn);
        Assert.Equal("D5", sheet.Cells[0].Reference(sheet));
    }

    private static XlsxCellValue Value(string type, Action<XlsxCellDto>? configure = null)
    {
        var cell = new XlsxCellDto { Row = 0, Column = 0, Type = type };
        configure?.Invoke(cell);

        var workbook = XlsxModel.Build(new XlsxWorkbookDto
        {
            Sheets = [new XlsxSheetDto { Name = "Feuille", Cells = [cell] }],
        });

        return workbook.Sheets[0].Cells[0].Value;
    }
}
