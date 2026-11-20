using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>Error payload returned by the native layer (Spec 02 §6).</summary>
internal sealed class DiagnosticPayloadDto
{
    public string? Code { get; set; }

    public string? Message { get; set; }

    public List<DiagnosticDto>? Diagnostics { get; set; }
}