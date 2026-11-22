using System;

namespace M3scalinum.Text.Pdf;

/// <summary>What the verifier established about the signature on one embedded revocation response.</summary>
/// <remarks>
/// An answer that names the right certificate is not evidence until its signature is checked: a
/// response anybody could have written names whatever serial it likes. The status separates the
/// three ways that check can end apart from success — nothing to check, a signature that verifies
/// with no key the file carries, and a signature made by a certificate the expected authority did
/// not authorise.
/// </remarks>
public enum PdfRevocationSignatureStatus
{
    /// <summary>
    /// The signature verifies over the signed bytes, and its signer is the certificate's issuer or a
    /// responder certificate that issuer issued with <c>id-kp-OCSPSigning</c>.
    /// </summary>
    Valid = 0,

    /// <summary>
    /// The response carries no signature this reader can check: an empty signature value, no signed
    /// bytes, or a signature algorithm outside the set the package implements.
    /// </summary>
    NoSignature = 1,

    /// <summary>
    /// A signature is present and verifies with no key the file carries. The bytes were changed after
    /// signing, or the signature was made by a key the file does not embed; a reader cannot tell the
    /// two apart, and neither is evidence.
    /// </summary>
    Invalid = 2,

    /// <summary>
    /// The signature verifies, but its signer is not the authority the response claims to answer
    /// for: a delegated responder without <c>id-kp-OCSPSigning</c>, a responder issued by another
    /// authority, or a CRL that names an authority no embedded certificate is.
    /// </summary>
    UnexpectedSigner = 3,
}

/// <summary>
/// One embedded revocation response, and what its signature proved: who signed it, which
/// certificate it answers for, and why it was refused when it was.
/// </summary>
/// <remarks>
/// The entry says nothing about what the answer states — good, revoked or unknown — and nothing
/// about whether the responder is one the caller's policy accepts, beyond the issuer's own
/// authorisation. It is the answer to one question: did the certificate this response names sign it?
/// </remarks>
public sealed class PdfRevocationSignatureReading
{
    internal PdfRevocationSignatureReading(
        PdfRevocationKind kind,
        PdfRevocationSignatureStatus status,
        string? subject,
        string? issuer,
        string? serialNumber,
        string? signerSubject,
        string? signerSerialNumber,
        string? error)
    {
        Kind = kind;
        Status = status;
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
        SignerSubject = signerSubject;
        SignerSerialNumber = signerSerialNumber;
        Error = error;
    }

    /// <summary>Whether the response is an OCSP answer or a CRL.</summary>
    public PdfRevocationKind Kind { get; }

    /// <summary>What the response's signature proved.</summary>
    public PdfRevocationSignatureStatus Status { get; }

    /// <summary>
    /// The subject of the certificate the response answers for, as an X.500 string — resolved
    /// against the certificates the store embeds. Null for a CRL, which answers for every
    /// certificate its authority issued, and null when the store does not carry that certificate.
    /// </summary>
    public string? Subject { get; }

    /// <summary>
    /// The issuer of the certificate the response answers for, as an X.500 string; null when the
    /// store does not carry that certificate.
    /// </summary>
    public string? Issuer { get; }

    /// <summary>
    /// The serial number of the certificate the response answers for, in uppercase hexadecimal —
    /// the value the response itself names. Null for a CRL and when the store does not carry the
    /// certificate.
    /// </summary>
    public string? SerialNumber { get; }

    /// <summary>
    /// The subject of the certificate whose key verified the signature, when one did. This is the
    /// issuer itself for an answer it signed, or the delegated responder's certificate otherwise.
    /// </summary>
    public string? SignerSubject { get; }

    /// <summary>The serial number of that certificate, in uppercase hexadecimal. Null when unknown.</summary>
    public string? SignerSerialNumber { get; }

    /// <summary>Why the signature is not valid, in one sentence naming the facts used; null when it is.</summary>
    public string? Error { get; }

    /// <summary>Whether the response counts as evidence: its signature verified and its signer was authorised.</summary>
    public bool IsValid => Status == PdfRevocationSignatureStatus.Valid;
}
