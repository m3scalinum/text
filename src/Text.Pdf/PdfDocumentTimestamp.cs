using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Adds a document timestamp — the <em>LTA</em> of PAdES — as an incremental update.
/// </summary>
/// <remarks>
/// A document timestamp is an RFC 3161 token over the whole file, stored in a signature dictionary
/// whose <c>/Type</c> is <c>/DocTimeStamp</c> and whose <c>/SubFilter</c> is <c>ETSI.RFC3161</c>. It
/// signs nothing: it attests that the document, with every signature it already carries, existed at
/// the moment the authority issued the token. Because it covers the whole file, it must be added
/// after the last signature — adding one earlier invalidates nothing, but leaves the signatures that
/// follow it outside its protection.
/// </remarks>
public static class PdfDocumentTimestamp
{
    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> carrying a document timestamp from
    /// <paramref name="timestampAuthority"/>.
    /// </summary>
    /// <param name="pdf">The document to stamp, with the signatures it already carries.</param>
    /// <param name="timestampAuthority">The RFC 3161 authority to ask.</param>
    /// <param name="httpClient">The client used to reach it; the caller owns and disposes it.</param>
    /// <exception cref="PdfSignatureException">
    /// The data is not a PDF this writer can update, or the authority failed.
    /// </exception>
    public static byte[] Add(
        ReadOnlySpan<byte> pdf,
        Uri timestampAuthority,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(timestampAuthority);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        var number = document.NextObjectNumber();

        var append = new PdfAppend(document.Text);
        var offset = append.Add(
            number,
            "<</Type/DocTimeStamp/Filter/Adobe.PPKLite/SubFilter/ETSI.RFC3161"
            + PdfContents.ByteRangePlaceholder
            + PdfContents.EmptyContents()
            + string.Create(CultureInfo.InvariantCulture, $"/M({PdfDate(DateTimeOffset.UtcNow)})")
            + ">>");

        return PdfContents.Seal(
            append.Finish(document, number + 1),
            offset,
            covered =>
            {
                var token = PdfTimestamp.Request(
                    timestampAuthority,
                    httpClient,
                    PdfTimestamp.BuildRequest(SHA256.HashData(covered)));

                if (!PdfTimestamp.Parse(token).Matches(covered))
                {
                    throw new PdfSignatureException(
                        "the timestamp authority returned a token whose imprint is not this document; no timestamp was written");
                }

                return token;
            });
    }

    private static string PdfDate(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();

        return "D:" + utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";
    }
}
