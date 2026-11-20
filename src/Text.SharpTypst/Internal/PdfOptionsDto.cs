using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Internal;

internal sealed class PdfOptionsDto
{
    public List<string>? Standards { get; set; }

    public string? PageRanges { get; set; }

    public bool? Tagged { get; set; }

    public string? Ident { get; set; }

    public string? Creator { get; set; }

    public bool? Pretty { get; set; }

    public TimestampDto? Timestamp { get; set; }
}