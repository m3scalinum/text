namespace M3scalinum.Text.Pdf;

/// <summary>The encoding of a rendered page.</summary>
/// <remarks>
/// <para>
/// PNG is lossless and is what <see cref="PdfRaster"/> produces when the caller names no format: it
/// is byte-exact for a page a recogniser will read, and it is the only encoding that survives a page
/// of flat vector art without artefacts.
/// </para>
/// <para>
/// JPEG is lossy and irreversible, and it exists for the preview: a scanned A4 page at 150 DPI is
/// about 3 MB as a PNG and under 400 kB as a JPEG at the default quality. It carries no alpha
/// channel and no more than 8 bits per channel, and its artefacts are permanent — a page that has to
/// be read rather than shown should stay PNG.
/// </para>
/// </remarks>
public enum PdfPageImageFormat
{
    /// <summary>A PNG, always 8-bit, lossless.</summary>
    Png = 0,

    /// <summary>
    /// A baseline JPEG, 8-bit RGB, lossy. Its size is set by
    /// <see cref="PdfRasterOptions.Quality"/>.
    /// </summary>
    Jpeg = 1,
}
