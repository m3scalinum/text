namespace M3scalinum.Text.Pdf;

/// <summary>The encoding of a rendered page.</summary>
/// <remarks>
/// Only PNG exists today. The renderer draws premultiplied RGBA8 and its own encoder writes PNG;
/// a JPEG encoder would be a second dependency for a format a caller can produce with
/// <c>System.Drawing</c> or ImageSharp in three lines. An enum with one member is what keeps that a
/// later addition rather than a breaking change.
/// </remarks>
public enum PdfPageImageFormat
{
    /// <summary>A PNG, always 8-bit.</summary>
    Png = 0,
}
