using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>Which eIDAS act a signature performs.</summary>
/// <remarks>
/// The kind is read from the signing certificate's extended key usage, not from anything the PDF
/// says about itself: a document can carry a seal or a signature, and the certificate is what
/// distinguishes one from the other.
/// </remarks>
public enum PdfSignatureKind
{
    /// <summary>An electronic signature: a natural person signs.</summary>
    Person = 0,

    /// <summary>
    /// An electronic seal: a legal person seals, with a certificate carrying
    /// <c>id-kp-documentSigning</c> (1.3.6.1.5.5.7.3.36).
    /// </summary>
    Seal = 1,
}

/// <summary>What a trust list lets this reader say about a service, when one was supplied.</summary>
/// <remarks>
/// <see cref="NotEvaluated"/> is not a negative finding: it means no list was supplied, so the
/// question was not asked. A verdict never turns it into "not qualified".
/// </remarks>
public enum PdfEidasQualification
{
    /// <summary>No trust list was supplied, so nothing was judged. Never read as "not qualified".</summary>
    NotEvaluated = 0,

    /// <summary>A granted, qualified service of the supplied list covers the certificate.</summary>
    Qualified = 1,

    /// <summary>
    /// The supplied list was read, and it does not state that a qualified service covers the
    /// certificate at the moment judged.
    /// </summary>
    NotQualified = 2,
}

/// <summary>
/// The eIDAS reading of one signature: which act it performs, whether it is timestamped, what the
/// trust list says about the timestamp authority and the signer, and the PAdES level proven.
/// </summary>
/// <remarks>
/// Every qualification is <see cref="PdfEidasQualification.NotEvaluated"/> unless the caller passed
/// a <see cref="PdfTrustedList"/>; a signature this package produced and verified by itself proves
/// bytes and time, never qualification. The wording of <see cref="Summary"/> keeps the two apart.
/// </remarks>
public sealed class PdfEidasSignatureVerdict
{
    internal PdfEidasSignatureVerdict(
        int signatureIndex,
        PdfSignatureKind kind,
        string? signerSubject,
        bool hasSigningTimeAttribute,
        DateTimeOffset? signingTime,
        bool hasTimestamp,
        DateTimeOffset? timestampedTime,
        string? timestampAuthoritySubject,
        PdfEidasQualification timestampAuthorityQualification,
        string timestampAuthorityExplanation,
        PdfEidasQualification signerQualification,
        string signerExplanation,
        PdfSignatureLevel level,
        string summary)
    {
        SignatureIndex = signatureIndex;
        Kind = kind;
        SignerSubject = signerSubject;
        HasSigningTimeAttribute = hasSigningTimeAttribute;
        SigningTime = signingTime;
        HasTimestamp = hasTimestamp;
        TimestampedTime = timestampedTime;
        TimestampAuthoritySubject = timestampAuthoritySubject;
        TimestampAuthorityQualification = timestampAuthorityQualification;
        TimestampAuthorityExplanation = timestampAuthorityExplanation;
        SignerQualification = signerQualification;
        SignerExplanation = signerExplanation;
        Level = level;
        Summary = summary;
    }

    /// <summary>The position of the signature in <see cref="PdfSignatureVerification.Signatures"/>.</summary>
    public int SignatureIndex { get; }

    /// <summary>Whether the signer is a person or a legal person sealing the document.</summary>
    public PdfSignatureKind Kind { get; }

    /// <summary>The subject of the signing certificate, as an X.500 string.</summary>
    public string? SignerSubject { get; }

    /// <summary>
    /// Whether the CMS carries the CAdES <c>signing-time</c> signed attribute — the claimed signing
    /// time PAdES expects of a seal, and the value a validator calls <c>claimedSigningTime</c>.
    /// </summary>
    public bool HasSigningTimeAttribute { get; }

    /// <summary>The time the signing-time attribute carries, when it is present and readable.</summary>
    public DateTimeOffset? SigningTime { get; }

    /// <summary>Whether the signature carries a signature timestamp token that verifies.</summary>
    public bool HasTimestamp { get; }

    /// <summary>The moment the timestamp authority attests, when the signature carries a token.</summary>
    public DateTimeOffset? TimestampedTime { get; }

    /// <summary>The subject of the timestamp authority's certificate, when the token carries it.</summary>
    public string? TimestampAuthoritySubject { get; }

    /// <summary>What the supplied trust list says about the timestamp authority.</summary>
    public PdfEidasQualification TimestampAuthorityQualification { get; }

    /// <summary>Why the timestamp authority is, is not, or was not judged qualified, with the instant used.</summary>
    public string TimestampAuthorityExplanation { get; }

    /// <summary>What the supplied trust list says about the signing certificate.</summary>
    public PdfEidasQualification SignerQualification { get; }

    /// <summary>Why the signing certificate is, is not, or was not judged qualified, with the instant used.</summary>
    public string SignerExplanation { get; }

    /// <summary>The highest PAdES baseline level the verifier could establish.</summary>
    public PdfSignatureLevel Level { get; }

    /// <summary>
    /// One sentence naming the act and the timestamp standing, for example
    /// <c>seal, timestamped by a qualified service</c> or <c>person, not timestamped</c>.
    /// </summary>
    public string Summary { get; }
}

/// <summary>The eIDAS reading of one document timestamp, and what the trust list says about it.</summary>
public sealed class PdfEidasTimestampVerdict
{
    internal PdfEidasTimestampVerdict(
        int index,
        DateTimeOffset? timestampedTime,
        string? authoritySubject,
        bool isIntact,
        PdfEidasQualification qualification,
        string explanation)
    {
        Index = index;
        TimestampedTime = timestampedTime;
        AuthoritySubject = authoritySubject;
        IsIntact = isIntact;
        Qualification = qualification;
        Explanation = explanation;
    }

    /// <summary>The position of the timestamp in <see cref="PdfSignatureVerification.DocumentTimestamps"/>.</summary>
    public int Index { get; }

    /// <summary>The moment the authority attests.</summary>
    public DateTimeOffset? TimestampedTime { get; }

    /// <summary>The subject of the authority's certificate, when the token carries it.</summary>
    public string? AuthoritySubject { get; }

    /// <summary>Whether the token verifies over the bytes it covers.</summary>
    public bool IsIntact { get; }

    /// <summary>What the supplied trust list says about the authority.</summary>
    public PdfEidasQualification Qualification { get; }

    /// <summary>Why the authority is, is not, or was not judged qualified.</summary>
    public string Explanation { get; }
}

/// <summary>
/// A document's signatures read as eIDAS acts: person or seal, timestamped or not, and qualified or
/// not as far as the supplied trust list states.
/// </summary>
public sealed class PdfEidasReport
{
    internal PdfEidasReport(
        PdfSignatureVerification verification,
        IReadOnlyList<PdfEidasSignatureVerdict> signatures,
        IReadOnlyList<PdfEidasTimestampVerdict> documentTimestamps,
        bool trustListSupplied)
    {
        Verification = verification;
        Signatures = signatures;
        DocumentTimestamps = documentTimestamps;
        TrustListSupplied = trustListSupplied;
    }

    /// <summary>What the verifier established about the bytes, unchanged.</summary>
    public PdfSignatureVerification Verification { get; }

    /// <summary>One verdict per signature, in signature order.</summary>
    public IReadOnlyList<PdfEidasSignatureVerdict> Signatures { get; }

    /// <summary>One verdict per document timestamp, in timestamp order.</summary>
    public IReadOnlyList<PdfEidasTimestampVerdict> DocumentTimestamps { get; }

    /// <summary>
    /// Whether a trust list was supplied. When false, every qualification in this report is
    /// <see cref="PdfEidasQualification.NotEvaluated"/>.
    /// </summary>
    public bool TrustListSupplied { get; }
}
