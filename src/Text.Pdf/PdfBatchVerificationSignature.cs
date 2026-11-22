using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// A certificate a chain judge could not accept, or one it never found: what the caller has to add
/// to the trust store before the document's chain can be judged again.
/// </summary>
/// <remarks>
/// <see cref="IsEmbedded"/> says what is actually in hand. <see langword="true"/> means the
/// certificate travelled with the file (or in its store) and the chain rejected it, so its subject,
/// issuer and serial number are read from the certificate itself. <see langword="false"/> means the
/// chain stopped at a certificate nobody supplied: only the name its child states as issuer is
/// known, and <see cref="Subject"/> and <see cref="SerialNumber"/> are <see langword="null"/> rather
/// than guessed.
/// </remarks>
public sealed class PdfSignatureChainCertificate
{
    internal PdfSignatureChainCertificate(
        string? subject,
        string issuer,
        string? serialNumber,
        bool isEmbedded,
        string reason)
    {
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
        IsEmbedded = isEmbedded;
        Reason = reason;
    }

    /// <summary>The certificate's subject, as an X.500 string. Null when only its name is known.</summary>
    public string? Subject { get; }

    /// <summary>The issuer named by the certificate, or by the certificate whose chain stopped.</summary>
    public string Issuer { get; }

    /// <summary>The certificate's serial number as uppercase hexadecimal. Null when it is not in hand.</summary>
    public string? SerialNumber { get; }

    /// <summary>Whether the certificate itself was read; false when only its issuer name was.</summary>
    public bool IsEmbedded { get; }

    /// <summary>Why the chain did not accept this certificate, or that it was not found at all.</summary>
    public string Reason { get; }

    /// <summary>The identity a report deduplicates on: two signatures missing the same root name it once.</summary>
    internal string Key => string.Join(
        '|',
        Subject ?? string.Empty,
        Issuer,
        SerialNumber ?? string.Empty,
        IsEmbedded ? "embedded" : "named");
}

/// <summary>
/// What a verifier established about one document, plus the certificates its chains could not accept.
/// </summary>
/// <remarks>
/// <see cref="Verification"/> is exactly what
/// <see cref="PdfSignatureVerifier.Verify(System.ReadOnlySpan{byte}, PdfTrustStore)"/> returns; the
/// certificates are the same chain judgements read one element further, so the two never disagree.
/// Nothing here strengthens a verdict — a named missing issuer explains an <c>Untrusted</c> status,
/// it does not turn it into anything else.
/// </remarks>
public sealed class PdfSignatureChainReading
{
    internal PdfSignatureChainReading(
        PdfSignatureVerification verification,
        IReadOnlyList<PdfSignatureChainCertificate> certificates)
    {
        Verification = verification;
        Certificates = certificates;
    }

    /// <summary>The per-signature verification, unchanged.</summary>
    public PdfSignatureVerification Verification { get; }

    /// <summary>
    /// The certificates the signatures' chains did not accept, in signature order and then in chain
    /// order, deduplicated: a document signed twice over the same untrusted root names that root once.
    /// Empty when every judged chain reached the store.
    /// </summary>
    public IReadOnlyList<PdfSignatureChainCertificate> Certificates { get; }
}

/// <summary>How a batch is verified: the store to judge chains by, and how many documents at a time.</summary>
public sealed class PdfBatchSignatureOptions
{
    /// <summary>The anchors and intermediates to judge every chain by. Null behaves as an empty store.</summary>
    /// <remarks>
    /// An empty store is meaningful: no chain is judged, every document reads
    /// <see cref="PdfChainStatus.NotEvaluated"/>, and no missing certificate is named, because no
    /// chain was attempted. The machine's trust store is never consulted.
    /// </remarks>
    public PdfTrustStore? TrustStore { get; init; }

    /// <summary>
    /// How many documents are verified at once. Default <c>1</c>: one document at a time, one
    /// document's bytes in flight.
    /// </summary>
    /// <remarks>
    /// The result does not depend on this number: entries are written to their input position, so
    /// the report lists the batch in the order the caller supplied it whatever the degree. Raising
    /// it trades memory for wall-clock time — the peak is roughly this many documents being parsed
    /// at once, on top of the caller's own copies. Values below one are refused.
    /// </remarks>
    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>The default options: no store, one document at a time.</summary>
    internal static PdfBatchSignatureOptions Default { get; } = new();
}
