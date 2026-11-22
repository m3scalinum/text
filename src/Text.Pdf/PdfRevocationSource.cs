using System;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The revocation evidence a signing operation may embed, asked from the caller one certificate at
/// a time.
/// </summary>
/// <remarks>
/// A document security store is only evidence when it carries, for every certificate of the chain,
/// an OCSP answer or a CRL that its issuer signed. Collecting those answers means naming a responder
/// the certificate chose and opening a socket to it — which this library does not do, not once, for
/// any document. The caller supplies the answers it already holds, or fetches them where its own
/// policy allows, and keeps them in whatever cache it trusts.
/// <para>
/// The implementation is called once per certificate that needs an entry, in the order the chain is
/// walked, and it may block: the writer calls it synchronously. A source that throws fails the
/// signing operation; a source that returns <see cref="PdfRevocationAnswer.None"/> is refused for
/// PAdES-B-LT, and reported when the caller asked for B-T.
/// </para>
/// </remarks>
public interface IRevocationSource
{
    /// <summary>
    /// Returns the revocation data the caller holds for <paramref name="certificate"/>, whose issuer
    /// is <paramref name="issuer"/>.
    /// </summary>
    /// <param name="certificate">The certificate the entry must cover.</param>
    /// <param name="issuer">The authority that issued it, as found among the supplied chain.</param>
    /// <returns>The OCSP answer, the CRL, both, or <see cref="PdfRevocationAnswer.None"/>.</returns>
    PdfRevocationAnswer GetRevocation(X509Certificate2 certificate, X509Certificate2 issuer);
}

/// <summary>What a <see cref="IRevocationSource"/> holds for one certificate.</summary>
/// <remarks>
/// The bytes are the structures as the responder or the issuer returned them, DER-encoded: the
/// writer embeds them in the store verbatim, so a reader can verify them later. Neither member is
/// required; a source that has one but not the other answers with what it has.
/// </remarks>
public sealed class PdfRevocationAnswer
{
    /// <summary>An answer holding nothing, for a certificate the caller has no data about.</summary>
    public static PdfRevocationAnswer None { get; } = new();

    /// <summary>The <c>OCSPResponse</c> structure, when the caller has one.</summary>
    public ReadOnlyMemory<byte>? OcspResponse { get; init; }

    /// <summary>The <c>CertificateList</c> structure, when the caller has one.</summary>
    public ReadOnlyMemory<byte>? Crl { get; init; }
}

/// <summary>
/// The signature cannot carry the revocation evidence its level requires.
/// </summary>
/// <remarks>
/// The named certificate is the one the evidence is missing for: its subject, its issuer and its
/// serial number say what the source should have answered about. The signature is not written when
/// this is thrown — a PAdES-B-LT that silently fell back to B-T would claim more than the file holds.
/// </remarks>
public sealed class PdfRevocationException : PdfSignatureException
{
    /// <summary>Initialises the exception for one certificate.</summary>
    /// <param name="subject">The subject of the certificate, as an X.500 string.</param>
    /// <param name="issuer">The issuer of the certificate, as an X.500 string.</param>
    /// <param name="serialNumber">The certificate's serial number, in hexadecimal.</param>
    /// <param name="message">What was missing, and what to do about it.</param>
    public PdfRevocationException(string subject, string issuer, string serialNumber, string message)
        : base(message)
    {
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
    }

    /// <summary>The subject of the certificate whose revocation evidence is missing.</summary>
    public string Subject { get; }

    /// <summary>The issuer of that certificate, as an X.500 string.</summary>
    public string Issuer { get; }

    /// <summary>The serial number of that certificate, in hexadecimal.</summary>
    public string SerialNumber { get; }
}
