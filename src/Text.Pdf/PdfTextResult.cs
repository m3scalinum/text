using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>The text of every page of a document, in page order.</summary>
/// <remarks>
/// A document with no page has an empty <see cref="Pages"/> list and a <see cref="PageCount"/> of
/// zero; that is a document, not an error. What is an error — bytes that are not a PDF, an encrypted
/// document, an unreadable catalog — is refused by <see cref="PdfText.Extract"/> with a
/// <see cref="PdfTextException"/> naming the reason.
/// </remarks>
public sealed class PdfTextResult
{
    internal PdfTextResult(IReadOnlyList<PdfPageText> pages) => Pages = pages;

    /// <summary>The page count the document declares in its page tree and the reader walked.</summary>
    public int PageCount => Pages.Count;

    /// <summary>One entry per page, in page order.</summary>
    public IReadOnlyList<PdfPageText> Pages { get; }

    /// <summary>The text of every page, joined with a newline between pages.</summary>
    /// <remarks>
    /// The whole document as one string, for a caller that only wants to index it. A page break is
    /// not a paragraph break and a page that holds no text contributes nothing, so the join is the
    /// page order the document declared and nothing more.
    /// </remarks>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();

            foreach (var page in Pages)
            {
                builder.Append(page.Text);
            }

            return builder.ToString();
        }
    }
}
