namespace M3scalinum.Text.Typst.Internal;

/// <summary>Payload sent to <c>tn_document_export</c> (Spec 02 §3.6).</summary>
internal sealed class ExportOptionsDto
{
    public int? Page { get; set; }

    public float? Ppi { get; set; }

    public bool? Pretty { get; set; }

    public PdfOptionsDto? Pdf { get; set; }
}