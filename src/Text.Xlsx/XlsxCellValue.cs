using System;
using System.Globalization;

namespace M3scalinum.Text.Xlsx;

/// <summary>What a cell holds, as the workbook typed it.</summary>
/// <remarks>
/// The hierarchy is closed so a switch over the shapes is exhaustive, as <c>DocxInline</c> is in
/// <c>M3scalinum.Text.Docx</c>. A cell holds exactly one of these; a cell with a formula holds its
/// cached result, which is the value Excel last computed and wrote into the package.
/// </remarks>
public abstract class XlsxCellValue
{
    private protected XlsxCellValue()
    {
    }

    /// <summary>The cell as a person reads it, in the form a table prints.</summary>
    public abstract string DisplayText { get; }
}

/// <summary>A string.</summary>
public sealed class XlsxTextValue : XlsxCellValue
{
    internal XlsxTextValue(string text) => Text = text;

    /// <summary>The characters.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string DisplayText => Text;
}

/// <summary>A number. Excel stores every number as a double.</summary>
public sealed class XlsxNumberValue : XlsxCellValue
{
    internal XlsxNumberValue(double number) => Number = number;

    /// <summary>The value.</summary>
    public double Number { get; }

    /// <inheritdoc />
    public override string DisplayText => Number.ToString("G15", CultureInfo.InvariantCulture);
}

/// <summary>A boolean.</summary>
public sealed class XlsxBooleanValue : XlsxCellValue
{
    internal XlsxBooleanValue(bool value) => Value = value;

    /// <summary>The value.</summary>
    public bool Value { get; }

    /// <inheritdoc />
    public override string DisplayText => Value ? "TRUE" : "FALSE";
}

/// <summary>A date, a time of day, or both.</summary>
public sealed class XlsxDateValue : XlsxCellValue
{
    internal XlsxDateValue(DateTime value, bool isDateOnly)
    {
        Value = value;
        IsDateOnly = isDateOnly;
    }

    /// <summary>The instant the cell holds, with <see cref="DateTimeKind.Unspecified"/>: Excel stores no zone.</summary>
    public DateTime Value { get; }

    /// <summary>True when the time part is midnight, as a date-only cell is.</summary>
    public bool IsDateOnly { get; }

    /// <inheritdoc />
    public override string DisplayText => Value.ToString(
        IsDateOnly ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>An elapsed time, from a format such as <c>[h]:mm:ss</c>.</summary>
public sealed class XlsxDurationValue : XlsxCellValue
{
    internal XlsxDurationValue(TimeSpan value) => Value = value;

    /// <summary>The length.</summary>
    public TimeSpan Value { get; }

    /// <inheritdoc />
    public override string DisplayText =>
        $"{(int)Value.TotalHours}:{Value.Minutes:D2}:{Value.Seconds:D2}";
}

/// <summary>An error value such as <c>#DIV/0!</c>.</summary>
public sealed class XlsxErrorValue : XlsxCellValue
{
    internal XlsxErrorValue(string error) => Error = error;

    /// <summary>The error as Excel prints it, including the <c>#</c>.</summary>
    public string Error { get; }

    /// <inheritdoc />
    public override string DisplayText => Error;
}

/// <summary>
/// No value. A cell only holds this and appears at all when it carries a formula whose result the
/// workbook never cached.
/// </summary>
public sealed class XlsxBlankValue : XlsxCellValue
{
    internal static XlsxBlankValue Instance { get; } = new();

    private XlsxBlankValue()
    {
    }

    /// <inheritdoc />
    public override string DisplayText => string.Empty;
}
