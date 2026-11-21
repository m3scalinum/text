using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Internal;

/// <summary>Payload returned by <c>tn_document_evaluate</c>.</summary>
internal sealed class EvaluationDto
{
    public int PageCount { get; set; }

    public List<OutlineEntryDto>? Outline { get; set; }

    public List<LabelEntryDto>? Labels { get; set; }

    public List<MetadataEntryDto>? Metadata { get; set; }
}