using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Xlsx.Internal;

/// <summary>The workbook as the native reader writes it (Spec 28 §4).</summary>
internal sealed class XlsxWorkbookDto
{
    public List<XlsxSheetDto> Sheets { get; set; } = [];
}

/// <summary>One sheet and its cells.</summary>
internal sealed class XlsxSheetDto
{
    public string Name { get; set; } = string.Empty;

    public int Rows { get; set; }

    public int Columns { get; set; }

    public int RowOffset { get; set; }

    public int ColumnOffset { get; set; }

    public List<XlsxCellDto> Cells { get; set; } = [];
}

/// <summary>
/// One cell. The native side discriminates on <c>type</c> and flattens the value's fields into this
/// object, so a single DTO carries every shape.
/// </summary>
internal sealed class XlsxCellDto
{
    public int Row { get; set; }

    public int Column { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public double? Number { get; set; }

    public bool? Value { get; set; }

    public string? Iso { get; set; }

    public bool DateOnly { get; set; }

    public double? Seconds { get; set; }

    public string? Error { get; set; }

    public string? Formula { get; set; }
}

/// <summary>The payload of a native error.</summary>
internal sealed class XlsxErrorPayloadDto
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}
