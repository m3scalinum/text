namespace M3scalinum.Text.Typst.Internal;

/// <summary>
/// The JSON object <c>tn_document_page_fingerprints</c> returns: one digest per page, in page
/// order.
/// </summary>
/// <remarks>
/// A fingerprint is opaque: it is only meaningful against the fingerprint of the same page in
/// another compilation of the same document (Spec 03 §15). The array is empty for a document
/// without pages — an HTML or bundle compilation.
/// </remarks>
internal sealed class PageFingerprintDto
{
    /// <summary>One digest per page, in page order.</summary>
    public ulong[] Pages { get; set; } = [];
}
