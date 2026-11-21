using System.Collections.Generic;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>The AST as the native reader writes it (Spec 22 §4).</summary>
internal sealed class DocxAstDto
{
    public List<DocxBlockDto> Blocks { get; set; } = [];

    public DocxPageDto? Page { get; set; }

    public List<DocxImageDto> Images { get; set; } = [];
}

/// <summary>
/// One block. The native side discriminates on <c>type</c>; the fields of both shapes share this DTO
/// because a block is either a paragraph or a table, never both.
/// </summary>
internal sealed class DocxBlockDto
{
    public string Type { get; set; } = string.Empty;

    public int? Heading { get; set; }

    public DocxListDto? List { get; set; }

    public List<DocxRunDto>? Runs { get; set; }

    public int Columns { get; set; }

    public List<DocxTableRowDto>? Rows { get; set; }
}

/// <summary>Where a paragraph sits in a list.</summary>
internal sealed class DocxListDto
{
    public long NumberingId { get; set; }

    public int Level { get; set; }

    public bool Ordered { get; set; }
}

/// <summary>One inline. Only the fields of its own <c>type</c> are set.</summary>
internal sealed class DocxRunDto
{
    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string? Underline { get; set; }

    public bool Strike { get; set; }

    public string? Script { get; set; }

    public string? Color { get; set; }

    public double? SizePt { get; set; }

    public string? Name { get; set; }

    public string? MimeType { get; set; }

    public ulong? BytesHandle { get; set; }
}

/// <summary>A table row.</summary>
internal sealed class DocxTableRowDto
{
    public List<DocxTableCellDto> Cells { get; set; } = [];
}

/// <summary>A table cell.</summary>
internal sealed class DocxTableCellDto
{
    public int Colspan { get; set; } = 1;

    public int Rowspan { get; set; } = 1;

    public List<DocxBlockDto> Blocks { get; set; } = [];
}

/// <summary>Page geometry, in points.</summary>
internal sealed class DocxPageDto
{
    public double WidthPt { get; set; }

    public double HeightPt { get; set; }

    public bool Landscape { get; set; }

    public double MarginTopPt { get; set; }

    public double MarginRightPt { get; set; }

    public double MarginBottomPt { get; set; }

    public double MarginLeftPt { get; set; }
}

/// <summary>A media part.</summary>
internal sealed class DocxImageDto
{
    public string Name { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public ulong BytesHandle { get; set; }

    public ulong Length { get; set; }
}

/// <summary>The payload of a native error.</summary>
internal sealed class DocxErrorPayloadDto
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}
