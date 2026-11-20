using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Internal;

internal sealed class DiagnosticDto
{
    public string? Severity { get; set; }

    public string? Message { get; set; }

    public List<string>? Hints { get; set; }

    public List<string>? Trace { get; set; }

    public string? File { get; set; }

    public int? Line { get; set; }

    public int? Column { get; set; }

    public int? EndLine { get; set; }

    public int? EndColumn { get; set; }

    public bool IsInSource { get; set; }

    public string? SourceLine { get; set; }
}