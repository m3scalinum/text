using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace M3scalinum.Text.Pdf;

/// <summary>What was established about one document of a batch.</summary>
/// <remarks>
/// A document is read once and keeps no bytes: the entry carries the verdicts, the chain status the
/// document was filed under, and the certificates its chain could not accept. <see cref="IsRead"/>
/// separates a document the verifier could not parse — whose <see cref="Error"/> says why — from one
/// that parsed and carried no signature, which is a fact about the document, not a failure.
/// </remarks>
public sealed class PdfBatchSignatureEntry
{
    internal PdfBatchSignatureEntry(
        int index,
        bool isRead,
        string? error,
        PdfChainStatus chainStatus,
        int signatures,
        IReadOnlyList<PdfSignatureChainCertificate> missing)
    {
        Index = index;
        IsRead = isRead;
        Error = error;
        ChainStatus = chainStatus;
        Signatures = signatures;
        MissingCertificates = missing;
    }

    /// <summary>The document's position in the batch the caller supplied, counting from zero.</summary>
    public int Index { get; }

    /// <summary>Whether the document parsed and its signatures were read.</summary>
    public bool IsRead { get; }

    /// <summary>Why the document could not be read, or <see langword="null"/> when it was.</summary>
    public string? Error { get; }

    /// <summary>
    /// The status the document was filed under: the first signature that is not
    /// <see cref="PdfChainStatus.Trusted"/>, or <see cref="PdfChainStatus.Trusted"/> when every
    /// signature is, or <see cref="PdfChainStatus.NotEvaluated"/> when nothing was judged — no
    /// signature at all, no trust store supplied, or the document could not be read.
    /// </summary>
    public PdfChainStatus ChainStatus { get; }

    /// <summary>How many signatures the document carries. Zero for an unreadable document.</summary>
    public int Signatures { get; }

    /// <summary>
    /// The certificates the document's chains did not accept, named. Empty when every judged chain
    /// reached the store, and empty when no chain was judged at all.
    /// </summary>
    public IReadOnlyList<PdfSignatureChainCertificate> MissingCertificates { get; }
}

/// <summary>
/// The aggregate view of a batch: how many documents were read, one count per
/// <see cref="PdfChainStatus"/>, the documents behind each count, and the certificates missing from
/// every chain that did not build.
/// </summary>
/// <remarks>
/// <para>
/// The report keeps verdicts, never documents. A caller that archives a thousand invoices hands over
/// a thousand byte arrays and keeps one entry per invoice afterwards: an index, a status, a signature
/// count and the names of the certificates to add to the store. The bytes are free to go as soon as
/// the pass ends, which is what makes a batch of a thousand documents affordable.
/// </para>
/// <para>
/// The report aggregates verdicts, it does not reinforce them: a document counted
/// <see cref="PdfChainStatus.Trusted"/> is one whose signatures all chained to the caller's store,
/// exactly as <see cref="PdfSignatureVerifier.Verify(System.ReadOnlySpan{byte}, PdfTrustStore)"/>
/// said one at a time. Nothing here decides whether a document is acceptable, and a batch with no
/// unreadable document is not a batch whose documents are true.
/// </para>
/// </remarks>
public sealed class PdfBatchSignatureReport
{
    /// <summary>The first line of the canonical text form: the format's name and its version.</summary>
    internal const string Header = "M3SCALINUM-BATCH-SIGNATURE-REPORT 1";

    /// <summary>The tag an unreadable document carries in the status column, where a chain status would be.</summary>
    internal const string UnreadableTag = "Unreadable";

    private const char Separator = '\t';

    internal PdfBatchSignatureReport(IReadOnlyList<PdfBatchSignatureEntry> entries)
    {
        Entries = entries;

        var counts = new SortedDictionary<PdfChainStatus, int>();
        var documents = new SortedDictionary<PdfChainStatus, List<int>>();

        foreach (var status in Enum.GetValues<PdfChainStatus>())
        {
            counts[status] = 0;
            documents[status] = [];
        }

        var read = 0;
        var signed = 0;

        foreach (var entry in entries)
        {
            if (!entry.IsRead)
            {
                continue;
            }

            read++;
            counts[entry.ChainStatus]++;
            documents[entry.ChainStatus].Add(entry.Index);

            if (entry.Signatures > 0)
            {
                signed++;
            }
        }

        var lists = new SortedDictionary<PdfChainStatus, IReadOnlyList<int>>();

        foreach (var (status, indices) in documents)
        {
            lists[status] = indices;
        }

        CountsByStatus = counts;
        DocumentsByStatus = lists;
        Documents = entries.Count;
        Read = read;
        Unreadable = entries.Count - read;
        SignedDocuments = signed;
    }

    /// <summary>The entries, in the order the caller supplied the documents.</summary>
    public IReadOnlyList<PdfBatchSignatureEntry> Entries { get; }

    /// <summary>How many documents the batch carried.</summary>
    public int Documents { get; }

    /// <summary>How many documents were read: <see cref="Documents"/> minus <see cref="Unreadable"/>.</summary>
    public int Read { get; }

    /// <summary>How many documents could not be read at all. Each carries its <see cref="PdfBatchSignatureEntry.Error"/>.</summary>
    public int Unreadable { get; }

    /// <summary>How many read documents carry at least one signature.</summary>
    public int SignedDocuments { get; }

    /// <summary>
    /// One count per <see cref="PdfChainStatus"/> value, every defined value present even at zero, so
    /// a reader never has to tell "no document there" from "a status the report forgot". Unreadable
    /// documents are counted in <see cref="Unreadable"/>, not under a chain status.
    /// </summary>
    public IReadOnlyDictionary<PdfChainStatus, int> CountsByStatus { get; }

    /// <summary>
    /// The read documents behind each count, in batch order. A document appears under exactly one
    /// status, so the lists partition <see cref="Read"/>.
    /// </summary>
    public IReadOnlyDictionary<PdfChainStatus, IReadOnlyList<int>> DocumentsByStatus { get; }

    /// <summary>
    /// Renders the report as its canonical text: UTF-8, LF line endings, tab-separated fields, every
    /// chain status present, documents in batch order. The same batch verified twice renders the same
    /// bytes, whatever the degree of parallelism.
    /// </summary>
    /// <returns>The report as text, ready to be stored next to the batch.</returns>
    /// <remarks>
    /// The first line is the format's name and version. Then one <c>documents</c>, <c>read</c>,
    /// <c>unreadable</c> and <c>signed</c> line, one <c>status</c> line per chain status in ascending
    /// order, and one block per document: a <c>document</c> line with its index, status, signature
    /// count and missing-certificate count; a <c>missing</c> line per certificate the chain could not
    /// accept, carrying its subject, issuer, serial number and whether it was read from the file or
    /// only named; and, for a document that could not be read, an <c>error</c> line. Tabs, line feeds,
    /// carriage returns and percent signs inside a name are escaped as <c>%09</c>, <c>%0A</c>,
    /// <c>%0D</c> and <c>%25</c>. The sentence explaining a rejection stays on
    /// <see cref="PdfSignatureChainCertificate.Reason"/> and out of the text: which flags a chain
    /// reports is the platform's judgement, and the canonical form must be the same everywhere.
    /// </remarks>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');

        Line(text, "documents", null, Documents);
        Line(text, "read", null, Read);
        Line(text, "unreadable", null, Unreadable);
        Line(text, "signed", null, SignedDocuments);

        foreach (var (status, count) in CountsByStatus)
        {
            Line(text, "status", status.ToString(), count);
        }

        foreach (var entry in Entries)
        {
            text.Append("document").Append(Separator)
                .Append(Number(entry.Index)).Append(Separator)
                .Append(entry.IsRead ? entry.ChainStatus.ToString() : UnreadableTag).Append(Separator)
                .Append(Number(entry.Signatures)).Append(Separator)
                .Append(Number(entry.MissingCertificates.Count)).Append('\n');

            foreach (var certificate in entry.MissingCertificates)
            {
                text.Append("missing").Append(Separator)
                    .Append(Number(entry.Index)).Append(Separator)
                    .Append(Escape(certificate.Subject ?? string.Empty)).Append(Separator)
                    .Append(Escape(certificate.Issuer)).Append(Separator)
                    .Append(Escape(certificate.SerialNumber ?? string.Empty)).Append(Separator)
                    .Append(certificate.IsEmbedded ? "embedded" : "named").Append('\n');
            }

            if (!entry.IsRead)
            {
                text.Append("error").Append(Separator)
                    .Append(Number(entry.Index)).Append(Separator)
                    .Append(Escape(entry.Error ?? string.Empty)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The canonical text as bytes: UTF-8 without a byte-order mark, LF line endings.</summary>
    /// <returns>The bytes <see cref="ToText"/> describes.</returns>
    public byte[] ToBytes() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(ToText());

    private static void Line(StringBuilder text, string tag, string? name, int count)
    {
        text.Append(tag).Append(Separator);

        if (name is not null)
        {
            text.Append(name).Append(Separator);
        }

        text.Append(Number(count)).Append('\n');
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Escapes the four characters that would break a tab-separated line, and the escape character itself.</summary>
    private static string Escape(string value)
        => value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\t", "%09", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal);
}

/// <summary>
/// Verifies a batch of documents, one verdict per document, and aggregates them by chain status with
/// the missing certificates named.
/// </summary>
/// <remarks>
/// <para>
/// This is the loop an archivist would otherwise write around
/// <see cref="PdfSignatureVerifier.Verify(System.ReadOnlySpan{byte}, PdfTrustStore)"/>: read each
/// document, keep its verdict, count the statuses, and list what has to be added to the trust store
/// before the untrusted ones can be judged again. A document that cannot be parsed becomes an entry
/// with its error instead of failing the batch, because one corrupt invoice out of a thousand is a
/// line in the report, not a reason to abandon the other nine hundred and ninety-nine.
/// </para>
/// <para>
/// The report keeps no document bytes. The caller's arrays are the batch; the verifier parses each
/// one, writes an entry into that document's position, and lets the bytes go. Peak memory is
/// therefore the caller's own copies plus
/// <see cref="PdfBatchSignatureOptions.MaxDegreeOfParallelism"/> documents being parsed at once.
/// </para>
/// </remarks>
public static class PdfBatchSignatureVerifier
{
    /// <summary>Verifies every document of <paramref name="documents"/> and returns the aggregate report.</summary>
    /// <param name="documents">The batch, in the order the report must list it.</param>
    /// <param name="options">The store to judge chains by and the degree of parallelism; null for the defaults.</param>
    /// <returns>One entry per document, the counts by chain status, and the certificates to add to the store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="documents"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="PdfBatchSignatureOptions.MaxDegreeOfParallelism"/> is below one.
    /// </exception>
    public static PdfBatchSignatureReport Verify(
        IEnumerable<byte[]> documents,
        PdfBatchSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var settings = options ?? PdfBatchSignatureOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxDegreeOfParallelism, 1);

        // The batch is walked once. A lazy sequence is materialised as the caller's references, never
        // as copies: the entries are what this method keeps, and the report keeps no bytes at all.
        var batch = documents as IReadOnlyList<byte[]> ?? [.. documents];
        var entries = new PdfBatchSignatureEntry[batch.Count];

        if (batch.Count > 0)
        {
            var loop = new ParallelOptions { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism };
            Parallel.For(0, batch.Count, loop, index => entries[index] = VerifyOne(index, batch[index], settings));
        }

        return new PdfBatchSignatureReport(entries);
    }

    private static PdfBatchSignatureEntry VerifyOne(int index, byte[]? document, PdfBatchSignatureOptions settings)
    {
        if (document is null)
        {
            return new PdfBatchSignatureEntry(
                index, isRead: false, "the batch carries no document at this position", PdfChainStatus.NotEvaluated, 0, []);
        }

        try
        {
            var reading = PdfSignatureVerifier.VerifyDetailed(document, settings.TrustStore);

            return new PdfBatchSignatureEntry(
                index,
                isRead: true,
                error: null,
                StatusOf(reading.Verification),
                reading.Verification.Signatures.Count,
                reading.Certificates);
        }
        catch (Exception exception)
        {
            // One unreadable document is one entry, not a failed batch: the error travels with the
            // entry so the caller can name the file behind the index it supplied.
            return new PdfBatchSignatureEntry(
                index, isRead: false, exception.Message, PdfChainStatus.NotEvaluated, 0, []);
        }
    }

    /// <summary>
    /// The status a document is filed under: the first signature that is not trusted names the
    /// document's doubt, and a document with nothing judged is not evaluated rather than trusted.
    /// </summary>
    private static PdfChainStatus StatusOf(PdfSignatureVerification verification)
    {
        if (verification.Signatures.Count == 0)
        {
            return PdfChainStatus.NotEvaluated;
        }

        foreach (var signature in verification.Signatures)
        {
            if (signature.ChainStatus != PdfChainStatus.Trusted)
            {
                return signature.ChainStatus;
            }
        }

        return PdfChainStatus.Trusted;
    }
}
