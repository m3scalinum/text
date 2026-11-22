using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The certificate-level half of eIDAS: which act a signing certificate is meant to perform, and
/// whether it is allowed to perform it.
/// </summary>
/// <remarks>
/// eIDAS distinguishes the electronic signature — a natural person signs — from the electronic seal,
/// which a legal person applies. The only fact a certificate states about the difference is its
/// extended key usage: an eSeal certificate must carry <c>id-kp-documentSigning</c>
/// (<c>1.3.6.1.5.5.7.3.36</c>), the usage ETSI EN 319 411-1 requires for a document signing
/// certificate. This type reads that fact and refuses a seal asked for with a certificate that does
/// not carry it; it does not read the certificate's policy, and says nothing about qualification —
/// that is the trust list's statement, not the certificate's.
/// </remarks>
internal static class PdfEidasCertificate
{
    /// <summary>The extended key usage an eSeal certificate carries: <c>id-kp-documentSigning</c>.</summary>
    public const string DocumentSigningOid = "1.3.6.1.5.5.7.3.36";

    /// <summary>The extended key usage extension: <c>id-ce-extKeyUsage</c>.</summary>
    private const string ExtendedKeyUsageOid = "2.5.29.37";

    /// <summary>The CAdES signed attribute that carries the signer's claimed signing time.</summary>
    public const string SigningTimeOid = "1.2.840.113549.1.9.5";

    /// <summary>
    /// Which act a certificate is meant to perform: <see cref="PdfSignatureKind.Seal"/> when it
    /// carries <c>id-kp-documentSigning</c>, <see cref="PdfSignatureKind.Person"/> otherwise.
    /// </summary>
    public static PdfSignatureKind KindOf(X509Certificate2 certificate)
        => HasDocumentSigning(certificate) ? PdfSignatureKind.Seal : PdfSignatureKind.Person;

    /// <summary>Whether the certificate's extended key usage names <c>id-kp-documentSigning</c>.</summary>
    public static bool HasDocumentSigning(X509Certificate2 certificate)
    {
        foreach (var oid in EnhancedKeyUsages(certificate))
        {
            if (oid.Value == DocumentSigningOid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses a certificate that cannot seal, naming the extension it is missing.
    /// </summary>
    /// <exception cref="PdfSignatureException">The certificate carries no document signing usage.</exception>
    public static void RequireSealUsage(X509Certificate2 certificate)
    {
        if (HasDocumentSigning(certificate))
        {
            return;
        }

        var stated = false;
        foreach (var _ in EnhancedKeyUsages(certificate))
        {
            stated = true;
            break;
        }

        throw new PdfSignatureException(
            $"a PdfSignatureKind.Seal signature needs a certificate whose extended key usage (2.5.29.37) carries {DocumentSigningOid} (id-kp-documentSigning); \"{certificate.Subject}\" "
            + (stated
                ? "carries an extended key usage extension without that usage"
                : "carries no extended key usage extension at all")
            + ". Sign the document as a PdfSignatureKind.Person with a signature certificate, or seal it with a certificate issued for document signing.");
    }

    /// <summary>The extended key usages the certificate declares, in certificate order.</summary>
    private static IEnumerable<System.Security.Cryptography.Oid> EnhancedKeyUsages(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != ExtendedKeyUsageOid)
            {
                continue;
            }

            var usage = extension as X509EnhancedKeyUsageExtension
                ?? new X509EnhancedKeyUsageExtension(extension, extension.Critical);

            foreach (var oid in usage.EnhancedKeyUsages)
            {
                if (oid is not null)
                {
                    yield return oid;
                }
            }
        }
    }
}
