using System;
using System.Net.Http;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>What a refresh needs: an RFC 3161 authority, and the client that reaches it.</summary>
/// <remarks>
/// Both are required, for the same reason as everywhere else in this package: the library never
/// picks a timestamp authority and never opens a connection of its own.
/// </remarks>
public sealed class PdfArchiveTimestampOptions
{
    /// <summary>The RFC 3161 authority that issues the document timestamp.</summary>
    public Uri? TimestampAuthority { get; init; }

    /// <summary>The client used to reach <see cref="TimestampAuthority"/>. The caller owns it.</summary>
    public HttpClient? TimestampClient { get; init; }
}

/// <summary>
/// PAdES-B-LTA: the archival level. A document timestamp over the whole file — the document
/// security store included — that can be renewed before the algorithms and the revocation data it
/// rests on age out.
/// </summary>
/// <remarks>
/// <para>
/// B-LT proves a signature for as long as the revocation data in its store stays usable. B-LTA adds
/// a document timestamp over the file as it stands, so the store itself is attested: the file can be
/// read years later, and the chain can be extended by
/// <see cref="Refresh(byte[], PdfArchiveTimestampOptions)"/> whenever a digest or a key is about to
/// become weak. Each refresh appends one more timestamp over the whole file, which is why a refreshed
/// document carries several and why every one of them must still verify.
/// </para>
/// <para>
/// <see cref="Inspect(byte[])"/> reports the chain entry by entry instead of answering yes or no,
/// because a document is worth what its weakest entry proves.
/// </para>
/// </remarks>
public static class PdfArchiveTimestamp
{
    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> carrying a new document timestamp over the whole
    /// file, extending whatever archival chain it already has.
    /// </summary>
    /// <param name="pdf">The signed document to archive, or to refresh.</param>
    /// <param name="options">The authority and the client that reaches it.</param>
    /// <exception cref="PdfSignatureException">
    /// The data is not a PDF this writer can update, it carries no signature, no authority was named,
    /// or the authority failed.
    /// </exception>
    public static byte[] Refresh(byte[] pdf, PdfArchiveTimestampOptions options)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TimestampAuthority is null)
        {
            throw new PdfSignatureException(
                "no timestamp authority was named, so there is nothing to archive with");
        }

        if (options.TimestampClient is null)
        {
            throw new PdfSignatureException(
                "a timestamp authority was named without an HttpClient; the library never opens a connection of its own");
        }

        if (pdf.Length == 0)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));

        // A timestamp over a file that carries no signature attests that the file existed, which is
        // not an archival level: there is no signature for it to keep alive.
        if (!AnySignature(document))
        {
            throw new PdfSignatureException(
                "the document carries no signature, so there is nothing for a document timestamp to archive");
        }

        return PdfDocumentTimestamp.Add(pdf, options.TimestampAuthority, options.TimestampClient);
    }

    /// <summary>Whether <paramref name="pdf"/> is archived: its chain is intact and covering.</summary>
    /// <param name="pdf">The document to read.</param>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    public static bool IsArchived(byte[] pdf) => Inspect(pdf).IsArchived;

    /// <summary>
    /// Walks every signature and document timestamp of <paramref name="pdf"/>, in file order, and
    /// reports what each one covers and why the chain does not hold when it does not.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <exception cref="PdfSignatureException">The data is empty or is not a PDF.</exception>
    public static PdfArchiveChain Inspect(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (pdf.Length == 0)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));

        return PdfArchiveChain.Analyze(pdf, document, PdfDss.Read(document));
    }

    private static bool AnySignature(PdfDocument document)
    {
        foreach (var dictionary in document.SignatureDictionaries())
        {
            if (!dictionary.IsDocumentTimestamp)
            {
                return true;
            }
        }

        return false;
    }
}
