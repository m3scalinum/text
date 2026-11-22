using System;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads the text a PDF draws, page by page, from the bytes of the document alone.
/// </summary>
/// <remarks>
/// This is the receiving half of the suite: the compiler can tell a caller what it laid out, and this
/// reads what some other producer wrote. It walks each page's content streams and reports the glyphs
/// in the order the stream yields them, with a space where the layout leaves a horizontal gap and a
/// newline between lines. It is not a renderer: it does not reconstruct columns, paragraphs or
/// reading order, it does not recognise a table, and it cannot see an image at all — a scanned page
/// has no text to recover and comes back empty.
/// <para>
/// Bytes in, bytes out: the package has no dependency on the compiler, and nothing here touches the
/// file system, the network or a font engine. An encrypted document is decrypted with the password
/// the caller supplies, through <see cref="PdfEncryption.Open"/>, and then read like any other; with
/// no password it is refused rather than read incorrectly.
/// </para>
/// </remarks>
public static class PdfText
{
    /// <summary>Reads the text of every page, decrypting an encrypted document first.</summary>
    /// <param name="pdf">The document, as its whole file.</param>
    /// <param name="password">
    /// The user or owner password of an encrypted document. An unencrypted document ignores it; when
    /// it is <see langword="null"/> an encrypted document is refused instead of decrypted.
    /// </param>
    /// <returns>One text per page, in page order, with the document's page count.</returns>
    /// <exception cref="PdfTextException">
    /// The bytes are not a PDF, the document is encrypted and the password is <see langword="null"/>
    /// or does not open it, its handler is not one this package decrypts, or its catalog cannot be
    /// read. A wrong password is <see cref="PdfTextError.WrongPassword"/>.
    /// </exception>
    public static PdfTextResult Extract(ReadOnlySpan<byte> pdf, string? password = null)
        => PdfTextReader.Extract(pdf, password);

    /// <summary>Reads the text of one page, decrypting an encrypted document first.</summary>
    /// <param name="pdf">The document, as its whole file.</param>
    /// <param name="page">Zero-based page index, as the compiler's page export uses.</param>
    /// <param name="password">
    /// The user or owner password of an encrypted document. An unencrypted document ignores it; when
    /// it is <see langword="null"/> an encrypted document is refused instead of decrypted.
    /// </param>
    /// <returns>The page's text, and what the reader had to fall back on to produce it.</returns>
    /// <exception cref="PdfTextException">
    /// The bytes are not a PDF, the document is encrypted and the password is <see langword="null"/>
    /// or does not open it, its handler is not one this package decrypts, or its catalog cannot be
    /// read. A wrong password is <see cref="PdfTextError.WrongPassword"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> names a page the document does not have.</exception>
    public static PdfPageText ExtractPage(ReadOnlySpan<byte> pdf, int page, string? password = null)
        => PdfTextReader.ReadPage(pdf, page, password);
}
