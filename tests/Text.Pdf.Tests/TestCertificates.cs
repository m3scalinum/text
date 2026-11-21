using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>Certificates the tests sign with, and the chains they form.</summary>
internal static class TestCertificates
{
    /// <summary>Loads a certificate with its private key in a form the CMS builder accepts everywhere.</summary>
    public static X509Certificate2 WithKey(X509Certificate2 certificate)
        => X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);

    /// <summary>The certificate a PDF is signed with: self-signed, valid for ten years.</summary>
    public static X509Certificate2 Signer(string subject = "CN=M3scalinum.Text.Pdf Test, O=M3scalinum.Text.Pdf, C=FR")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return WithKey(created);
    }

    /// <summary>A root, to chain a signer to and to trust in a store.</summary>
    public static X509Certificate2 Root(string subject = "CN=M3scalinum Test Root, O=M3scalinum.Text.Pdf, C=FR")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        using var created = request.CreateSelfSigned(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return WithKey(created);
    }

    /// <summary>A certificate issued by <paramref name="issuer"/>, with its private key.</summary>
    public static X509Certificate2 Issued(string subject, X509Certificate2 issuer, int serial = 12)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

        using var created = request.Create(
            issuer,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            [1, 2, 3, 4, 5, 6, 7, (byte)serial]);

        return created.CopyWithPrivateKey(key);
    }

    /// <summary>
    /// A timestamp authority certificate: the critical <c>id-kp-timeStamping</c> extended key usage
    /// RFC 3161 requires, issued by <paramref name="issuer"/> so it chains to a root.
    /// </summary>
    public static X509Certificate2 TimestampAuthority(
        X509Certificate2 issuer,
        string subject = "CN=M3scalinum Sample TSA, O=M3scalinum, C=FR")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8", "timeStamping") },
                critical: true));

        var serial = RandomNumberGenerator.GetBytes(9);
        serial[0] = 0x01;

        using var created = request.Create(
            issuer,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            serial);

        return created.CopyWithPrivateKey(key);
    }

    /// <summary>The certificate without its private key, as a receiver would have it.</summary>
    public static X509Certificate2 Public(X509Certificate2 certificate)
        => X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
}
