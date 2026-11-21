using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>What an entry of an archival chain is: a signature, or a document timestamp.</summary>
public enum PdfArchiveEntryKind
{
    /// <summary>A signature over the document.</summary>
    Signature = 0,

    /// <summary>An RFC 3161 document timestamp over the whole file.</summary>
    DocumentTimestamp = 1,
}

/// <summary>What one entry of an archival chain covers, and whether it still holds.</summary>
/// <remarks>
/// Reported in the order the entries appear in the file, which is the order the revisions were
/// written. <see cref="CoveredEnd"/> is what the entry actually protects: every byte before it,
/// except the gap that holds its own <c>/Contents</c>.
/// </remarks>
public sealed class PdfArchiveEntry
{
    internal PdfArchiveEntry(
        PdfArchiveEntryKind kind,
        int offset,
        long coveredStart,
        long coveredEnd,
        long revisionEnd,
        bool isIntact,
        bool coversWholeFile,
        bool coversDocumentSecurityStore,
        DateTimeOffset? attestedTime,
        string? signerSubject,
        string? error)
    {
        Kind = kind;
        Offset = offset;
        CoveredStart = coveredStart;
        CoveredEnd = coveredEnd;
        RevisionEnd = revisionEnd;
        IsIntact = isIntact;
        CoversWholeFile = coversWholeFile;
        CoversDocumentSecurityStore = coversDocumentSecurityStore;
        AttestedTime = attestedTime;
        SignerSubject = signerSubject;
        Error = error;
    }

    /// <summary>Whether this is a signature or a document timestamp.</summary>
    public PdfArchiveEntryKind Kind { get; }

    /// <summary>Where the dictionary starts in the file.</summary>
    public int Offset { get; }

    /// <summary>The first byte the entry claims to cover. Always zero for a usable entry.</summary>
    public long CoveredStart { get; }

    /// <summary>The byte just past the last one the entry covers.</summary>
    public long CoveredEnd { get; }

    /// <summary>
    /// The byte just past the <c>%%EOF</c> of the revision the entry was written into. What the entry
    /// must cover to still protect the revision it attested.
    /// </summary>
    public long RevisionEnd { get; }

    /// <summary>Whether the CMS verifies — or the token's imprint matches — over the bytes it covers.</summary>
    public bool IsIntact { get; }

    /// <summary>Whether the entry reaches the last byte of the file.</summary>
    public bool CoversWholeFile { get; }

    /// <summary>Whether the entry covers the document security store and the streams it points at.</summary>
    public bool CoversDocumentSecurityStore { get; }

    /// <summary>Whether the entry still covers the whole revision it was written into.</summary>
    public bool CoversItsRevision => Error is null && RevisionEnd > 0 && CoveredStart == 0 && CoveredEnd >= RevisionEnd;

    /// <summary>The time a timestamp authority attests, for a document timestamp.</summary>
    public DateTimeOffset? AttestedTime { get; }

    /// <summary>The subject of the signer, or of the authority that issued the timestamp.</summary>
    public string? SignerSubject { get; }

    /// <summary>Why the entry is not intact, when it is not.</summary>
    public string? Error { get; }
}

/// <summary>
/// The archival chain of a document: every signature and document timestamp, in file order, and the
/// reasons the chain does not hold — an empty <see cref="Breaks"/> is what
/// <see cref="IsArchived"/> means.
/// </summary>
/// <remarks>
/// The chain is verified, not assumed. Each entry is re-checked over the bytes it covers, and each
/// one has to reach the end of the revision it was written into: a document timestamp that protects
/// a shorter file than the revision it sits in says so, and the document is not archived.
/// </remarks>
public sealed class PdfArchiveChain
{
    internal PdfArchiveChain(IReadOnlyList<PdfArchiveEntry> entries, IReadOnlyList<string> breaks)
    {
        Entries = entries;
        Breaks = breaks;
    }

    /// <summary>The entries, in the order they appear in the file.</summary>
    public IReadOnlyList<PdfArchiveEntry> Entries { get; }

    /// <summary>Why the document is not archived, one sentence per reason. Empty when it is.</summary>
    public IReadOnlyList<string> Breaks { get; }

    /// <summary>
    /// Whether the chain is intact and covering: every entry verifies over the bytes it covers and
    /// still reaches the end of its revision, the newest entry is a document timestamp over the
    /// whole file, and the store carries the chain of every authority that issued one.
    /// </summary>
    public bool IsArchived => Breaks.Count == 0;

    /// <summary>
    /// Walks every signature and document timestamp in <paramref name="document"/> and reports what
    /// each one covers.
    /// </summary>
    internal static PdfArchiveChain Analyze(byte[] bytes, PdfDocument document, PdfDss dss)
    {
        var breaks = new List<string>();
        var entries = new List<PdfArchiveEntry>();
        var signers = new List<X509Certificate2?>();

        foreach (var dictionary in document.SignatureDictionaries())
        {
            var entry = AnalyzeEntry(bytes, document, dss, dictionary, out var signer);
            entries.Add(entry);
            signers.Add(signer);
        }

        if (entries.Count == 0)
        {
            breaks.Add("the document carries neither a signature nor a document timestamp");
            return new PdfArchiveChain(entries, breaks);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var name = Name(entry.Kind);

            if (!entry.IsIntact)
            {
                breaks.Add(
                    $"the {name} at byte {entry.Offset} does not verify over the bytes it covers: {entry.Error}");
            }

            if (!entry.CoversItsRevision)
            {
                breaks.Add(
                    $"the {name} at byte {entry.Offset} stops covering at byte {entry.CoveredEnd}, "
                    + $"before the end of its revision at byte {entry.RevisionEnd}: it no longer covers the revision it was written into");
            }

            if (index > 0 && entry.CoveredEnd < entries[index - 1].CoveredEnd)
            {
                breaks.Add(
                    $"the {name} at byte {entry.Offset} covers {entry.CoveredEnd} bytes, fewer than the "
                    + $"{Name(entries[index - 1].Kind)} before it covers ({entries[index - 1].CoveredEnd})");
            }

            // The archival level rests on the long-term level: a store that does not prove the
            // signature's chain is a store the timestamp cannot keep alive.
            if (entry.Kind == PdfArchiveEntryKind.Signature && entry.IsIntact)
            {
                if (signers[index] is not { } signer)
                {
                    breaks.Add($"the signature at byte {entry.Offset} carries no certificate, so no chain can be proven for it");
                }
                else if (!dss.ProvesChain(signer, out var chainError))
                {
                    breaks.Add(
                        $"the security store does not prove the chain of the signature at byte {entry.Offset}: {chainError}");
                }
            }
        }

        var last = entries[entries.Count - 1];
        if (last.Kind != PdfArchiveEntryKind.DocumentTimestamp)
        {
            breaks.Add(
                "the newest revision ends with a signature, not a document timestamp: nothing attests that it existed at a given time");
        }
        else if (!last.CoversWholeFile)
        {
            breaks.Add(
                $"the last document timestamp stops at byte {last.CoveredEnd} of a file of {bytes.Length} bytes");
        }

        var covering = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Kind != PdfArchiveEntryKind.DocumentTimestamp
                || !entries[index].IsIntact
                || !entries[index].CoversDocumentSecurityStore)
            {
                continue;
            }

            covering++;

            if (signers[index] is not { } certificate)
            {
                breaks.Add(
                    $"the document timestamp at byte {entries[index].Offset} carries no certificate for its authority");
            }
            else if (!dss.Contains(certificate))
            {
                breaks.Add(
                    $"the security store does not carry the certificate of {certificate.Subject}, which issued the document timestamp at byte {entries[index].Offset}");
            }
            else if (!dss.ProvesChain(certificate, out var chainError))
            {
                breaks.Add(
                    $"the security store does not carry the chain of the document timestamp at byte {entries[index].Offset}: {chainError}");
            }
        }

        if (covering == 0)
        {
            breaks.Add(
                dss.EndOffset > 0
                    ? "no document timestamp covers the document security store"
                    : "the document carries no document security store for a document timestamp to cover");
        }

        return new PdfArchiveChain(entries, breaks);
    }

    private static PdfArchiveEntry AnalyzeEntry(
        byte[] bytes,
        PdfDocument document,
        PdfDss dss,
        PdfSignatureDictionary dictionary,
        out X509Certificate2? signer)
    {
        signer = null;

        var kind = dictionary.IsDocumentTimestamp
            ? PdfArchiveEntryKind.DocumentTimestamp
            : PdfArchiveEntryKind.Signature;

        var range = dictionary.ByteRange;
        var coveredEnd = range[2] + range[3];
        var revisionEnd = RevisionEnd(document.Text, dictionary.Offset);

        if (!PdfSignatureVerifier.TryCovered(bytes, dictionary, out var content, out var rangeError))
        {
            return new PdfArchiveEntry(
                kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, false, false, false, null, null, rangeError);
        }

        var coversWhole = coveredEnd == bytes.Length;
        var coversStore = dss.EndOffset > 0 && coveredEnd >= dss.EndOffset;

        try
        {
            if (kind == PdfArchiveEntryKind.DocumentTimestamp)
            {
                var token = PdfTimestamp.Parse(PdfSignatureVerifier.ReadDer(bytes, dictionary));
                signer = token.Signer;

                if (!token.Matches(content))
                {
                    return new PdfArchiveEntry(
                        kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, false, coversWhole, coversStore,
                        token.GeneratedAt, token.Signer?.Subject,
                        "the timestamp's imprint does not cover the bytes of its byte range");
                }

                return new PdfArchiveEntry(
                    kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, true, coversWhole, coversStore,
                    token.GeneratedAt, token.Signer?.Subject, null);
            }

            var cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(PdfSignatureVerifier.ReadDer(bytes, dictionary));

            if (cms.SignerInfos.Count == 0)
            {
                return new PdfArchiveEntry(
                    kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, false, coversWhole, coversStore,
                    null, null, "the CMS carries no signer");
            }

            signer = cms.SignerInfos[0].Certificate;
            cms.CheckSignature(verifySignatureOnly: true);

            return new PdfArchiveEntry(
                kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, true, coversWhole, coversStore,
                null, signer?.Subject, null);
        }
        catch (Exception exception) when (exception is CryptographicException or PdfSignatureException or FormatException or ArgumentException)
        {
            return new PdfArchiveEntry(
                kind, dictionary.Offset, range[0], coveredEnd, revisionEnd, false, coversWhole, coversStore,
                null, signer?.Subject, exception.Message);
        }
    }

    /// <summary>
    /// The offset just past the <c>%%EOF</c> of the revision that starts before
    /// <paramref name="offset"/>, or zero when the file has none.
    /// </summary>
    private static long RevisionEnd(string text, int offset)
    {
        var end = text.IndexOf("%%EOF", offset, StringComparison.Ordinal);
        if (end < 0)
        {
            return 0;
        }

        var after = end + "%%EOF".Length;
        while (after < text.Length && text[after] is '\r' or '\n')
        {
            after++;
        }

        return after;
    }

    private static string Name(PdfArchiveEntryKind kind)
        => kind == PdfArchiveEntryKind.DocumentTimestamp ? "document timestamp" : "signature";
}
