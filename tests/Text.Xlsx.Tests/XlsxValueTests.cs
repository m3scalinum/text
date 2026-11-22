using System;
using M3scalinum.Text.Xlsx;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>How each value prints, which is what a rendered table shows.</summary>
public sealed class XlsxValueTests
{
    [Fact]
    public void A_text_prints_itself()
    {
        Assert.Equal("Total TTC", new XlsxTextValue("Total TTC").DisplayText);
    }

    [Fact]
    public void A_number_prints_without_a_trailing_zero()
    {
        Assert.Equal("1000", new XlsxNumberValue(1000).DisplayText);
        Assert.Equal("1234.5", new XlsxNumberValue(1234.5).DisplayText);
        Assert.Equal("0.1", new XlsxNumberValue(0.1).DisplayText);
    }

    [Fact]
    public void A_boolean_prints_as_excel_prints_it()
    {
        Assert.Equal("TRUE", new XlsxBooleanValue(true).DisplayText);
        Assert.Equal("FALSE", new XlsxBooleanValue(false).DisplayText);
    }

    [Fact]
    public void A_date_only_prints_the_date()
    {
        var value = new XlsxDateValue(new DateTime(2026, 1, 15), isDateOnly: true);

        Assert.Equal("2026-01-15", value.DisplayText);
    }

    [Fact]
    public void A_date_with_a_time_prints_the_time()
    {
        var value = new XlsxDateValue(new DateTime(2026, 1, 15, 10, 30, 0), isDateOnly: false);

        Assert.Equal("2026-01-15 10:30:00", value.DisplayText);
    }

    [Fact]
    public void A_duration_prints_as_hours_and_minutes()
    {
        Assert.Equal("36:00:00", new XlsxDurationValue(TimeSpan.FromHours(36)).DisplayText);
        Assert.Equal("1:30:15", new XlsxDurationValue(new TimeSpan(1, 30, 15)).DisplayText);
    }

    [Fact]
    public void An_error_prints_as_excel_prints_it()
    {
        Assert.Equal("#N/A", new XlsxErrorValue("#N/A").DisplayText);
    }

    [Fact]
    public void A_blank_prints_nothing()
    {
        Assert.Equal(string.Empty, XlsxBlankValue.Instance.DisplayText);
    }

    [Fact]
    public void A_cell_with_a_cached_value_prints_the_value()
    {
        var cell = new XlsxCell(0, 0, new XlsxNumberValue(1200), "=B3+B4");

        Assert.Equal("1200", cell.DisplayText);
    }

    [Fact]
    public void A_cell_holds_its_position_and_its_value()
    {
        var value = new XlsxTextValue("Client");
        var cell = new XlsxCell(1, 2, value, null);

        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
        Assert.Same(value, cell.Value);
        Assert.Null(cell.Formula);
    }
}
