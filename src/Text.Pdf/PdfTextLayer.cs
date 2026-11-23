using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// One word of an invisible text layer: the characters, the page they belong to, and the box they
/// were read from.
/// </summary>
/// <remarks>
/// The box is in the page's default user space — points, origin at the bottom-left corner of the
/// <c>/MediaBox</c>, the same space <c>/MediaBox</c> is written in — and not in the rotated space a
/// viewer displays. <see cref="PageIndex"/> is zero-based, as everywhere in this package. The words
/// are placed in the order the list holds them, so the caller supplies reading order; within a page
/// the layer keeps that order and never sorts by position.
/// </remarks>
/// <param name="PageIndex">The zero-based page the word belongs to.</param>
/// <param name="Text">The characters of the word, without surrounding whitespace.</param>
/// <param name="Left">The left edge of the box, in points.</param>
/// <param name="Bottom">The bottom edge of the box, in points, which becomes the baseline.</param>
/// <param name="Right">The right edge of the box, in points.</param>
/// <param name="Top">The top edge of the box, in points.</param>
public readonly record struct PdfTextLayerWord(
    int PageIndex,
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top);

/// <summary>
/// Makes a scanned page searchable by writing the text an OCR engine read into the document as an
/// invisible layer positioned over the image.
/// </summary>
/// <remarks>
/// <para>
/// The layer is a content stream appended to each page's <c>/Contents</c> array, drawn with text
/// rendering mode 3 — invisible — at the boxes the caller supplies. It is written as an incremental
/// update, the way <see cref="PdfSigner"/> and <see cref="PdfAttachments"/> work: the bytes already in
/// the file stay where they are, so a signature over them still covers them.
/// </para>
/// <para>
/// The font is a <c>Type0</c> font with an <c>Identity-H</c> encoding, one embedded
/// <c>CIDFontType2</c> and a <c>/ToUnicode</c> CMap, so every character carries its own Unicode
/// value and <see cref="PdfText"/> reads the words back. The embedded program is generated, not
/// subset from a file: the repository ships no font, and the layer is never painted, so the glyphs
/// are empty and only their advances matter. See <c>docs/specifications/18-pdf-text.md</c> for what
/// that costs and what it does not cover.
/// </para>
/// </remarks>
public static class PdfTextLayer
{
    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> carrying <paramref name="words"/> as an invisible text
    /// layer, each word at the box it was read from.
    /// </summary>
    /// <param name="pdf">The document to make searchable, as its whole file.</param>
    /// <param name="words">
    /// The words to place, in reading order. A word's box is in the page's default user space; its
    /// bottom edge becomes the baseline and its height becomes the font size.
    /// </param>
    /// <returns>The document with the layer appended as an incremental update.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="words"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="words"/> is empty, a word has no text, a word carries a control character, or a
    /// word's box has no area.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A word names a page the document does not have.</exception>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a PDF, the document is encrypted, it cannot take an incremental update, a
    /// page is rotated (its boxes would sit at the wrong place), or a page already carries a layer
    /// under this font name.
    /// </exception>
    public static byte[] Add(ReadOnlySpan<byte> pdf, IReadOnlyList<PdfTextLayerWord> words)
        => PdfTextLayerWriter.Add(pdf.ToArray(), words);
}
