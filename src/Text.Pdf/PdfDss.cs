using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// A document security store, read from the catalog: the certificates it embeds, and the revocation
/// data that goes with them.
/// </summary>
/// <remarks>
/// A store is what makes a signature verifiable after its certificate expired and its OCSP responder
/// disappeared. The verifier treats the material as evidence only when it holds together: the chain
/// has to reach a root inside the store, and every certificate on the way needs a revocation entry
/// that was signed by its issuer.
/// </remarks>
internal sealed class PdfDss
{
    private PdfDss(
        IReadOnlyList<X509Certificate2> certificates,
        IReadOnlyList<PdfCrl> crls,
        IReadOnlyList<PdfOcspResponse> ocsp,
        int endOffset)
    {
        Certificates = certificates;
        Crls = crls;
        Ocsp = ocsp;
        EndOffset = endOffset;
    }

    /// <summary>An empty store, for a document that carries none.</summary>
    public static PdfDss Empty { get; } = new([], [], [], 0);

    /// <summary>The certificates the store embeds.</summary>
    public IReadOnlyList<X509Certificate2> Certificates { get; }

    /// <summary>The revocation lists the store embeds, parsed.</summary>
    public IReadOnlyList<PdfCrl> Crls { get; }

    /// <summary>The OCSP answers the store embeds, parsed.</summary>
    public IReadOnlyList<PdfOcspResponse> Ocsp { get; }

    /// <summary>
    /// The offset just past the last byte of the store and of the streams it points at, or zero
    /// when the document carries none. A document timestamp that covers the store must reach it.
    /// </summary>
    public int EndOffset { get; }

    /// <summary>Reads the store the catalog points at, or an empty store when there is none.</summary>
    public static PdfDss Read(PdfDocument document)
    {
        var catalog = document.Catalog;

        string? body = null;
        var end = 0;

        if (PdfSyntax.TryReference(catalog, "DSS", out var number) && document.Objects.ContainsKey(number))
        {
            var store = document.Require(number);
            body = store.Body;
            end = Math.Max(end, EndOf(store));
        }

        if (body is null)
        {
            return Empty;
        }

        var certificates = new List<X509Certificate2>();
        foreach (var entry in Streams(document, body, "Certs"))
        {
            try
            {
                #if NET9_0_OR_GREATER
                certificates.Add(X509CertificateLoader.LoadCertificate(entry));
#else
                certificates.Add(new X509Certificate2(entry));
#endif
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // A stream that is not a certificate is not evidence; the chain check will say so.
            }
        }

        var crls = new List<PdfCrl>();
        foreach (var entry in Streams(document, body, "CRLs"))
        {
            if (PdfCrl.Parse(entry) is { } crl)
            {
                crls.Add(crl);
            }
        }

        var ocsp = new List<PdfOcspResponse>();
        foreach (var entry in Streams(document, body, "OCSPs"))
        {
            if (PdfOcspResponse.Parse(entry) is { } response)
            {
                ocsp.Add(response);
            }
        }

        foreach (var stream in StreamNumbers(body))
        {
            if (document.Objects.TryGetValue(stream, out var entry))
            {
                end = Math.Max(end, EndOf(entry));
            }
        }

        return new PdfDss(certificates, crls, ocsp, end);
    }

    /// <summary>The offset just past the <c>endobj</c> that closes an object.</summary>
    private static int EndOf(PdfObject entry) => entry.BodyEnd + "endobj".Length;

    private static IEnumerable<int> StreamNumbers(string dss)
    {
        foreach (var key in (string[])["Certs", "OCSPs", "CRLs"])
        {
            if (PdfSyntax.ArrayBody(dss, key) is not { } array)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(array, @"(\d+)\s+0\s+R"))
            {
                yield return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Whether the store carries the whole chain from <paramref name="signer"/> to a root, and a
    /// usable revocation entry for every certificate on it.
    /// </summary>
    public bool ProvesChain(X509Certificate2 signer, out string? error)
    {
        var path = PathToRoot(signer);
        if (path is null)
        {
            error = "the security store does not carry the chain from the signing certificate to a root";
            return false;
        }

        for (var index = 0; index + 1 < path.Count; index++)
        {
            var subject = path[index];
            var issuer = path[index + 1];

            if (!HasGoodStanding(subject, issuer))
            {
                error = $"the security store carries no usable revocation entry for {subject.Subject}";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Whether any embedded certificate is the same one.</summary>
    public bool Contains(X509Certificate2 certificate)
        => Certificates.Any(entry => entry.RawData.AsSpan().SequenceEqual(certificate.RawData));

    /// <summary>
    /// For every certificate the store embeds, which revocation data covers it — a CRL signed by
    /// its issuer, or an OCSP answer that names its serial and issuer — and when that data was
    /// issued.
    /// </summary>
    /// <remarks>
    /// The same rules the level proof applies, reported rather than judged: an entry that would not
    /// count towards PAdES-B-LT is not "covering" here either. An anchor has no issuer inside the
    /// store and is reported as uncovered, which is expected — a root is its own authority.
    /// </remarks>
    public IReadOnlyList<PdfRevocationCoverage> Coverage()
    {
        var coverage = new List<PdfRevocationCoverage>(Certificates.Count);

        foreach (var certificate in Certificates)
        {
            var issuer = IssuerOf(certificate);

            DateTimeOffset? ocspIssuedAt = null;
            DateTimeOffset? crlIssuedAt = null;

            if (issuer is not null)
            {
                foreach (var response in Ocsp)
                {
                    if (response.Answers(certificate, issuer) && response.SignatureVerifies(issuer))
                    {
                        ocspIssuedAt = response.ProducedAt;
                    }
                }

                foreach (var crl in Crls)
                {
                    if (crl.IssuedBy(issuer) && crl.SignatureVerifies(issuer))
                    {
                        crlIssuedAt = crl.ThisUpdate;
                    }
                }
            }

            coverage.Add(new PdfRevocationCoverage(
                certificate.Subject,
                certificate.Issuer,
                Convert.ToHexString(certificate.SerialNumberBytes.Span),
                ocspIssuedAt is not null,
                ocspIssuedAt,
                crlIssuedAt is not null,
                crlIssuedAt));
        }

        return coverage;
    }

    /// <summary>The embedded certificate that issued <paramref name="certificate"/>, or null for an anchor.</summary>
    private X509Certificate2? IssuerOf(X509Certificate2 certificate)
    {
        foreach (var candidate in Certificates)
        {
            if (!candidate.RawData.AsSpan().SequenceEqual(certificate.RawData)
                && candidate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool HasGoodStanding(X509Certificate2 subject, X509Certificate2 issuer)
    {
        var serial = subject.SerialNumberBytes.Span;

        foreach (var crl in Crls)
        {
            if (crl.IssuedBy(issuer) && crl.SignatureVerifies(issuer) && !crl.Lists(serial))
            {
                return true;
            }
        }

        foreach (var response in Ocsp)
        {
            if (response.GivesGoodAnswer(subject, issuer) && response.SignatureVerifies(issuer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The chain inside the store, from the signer to the root, built with the store as its only
    /// source: a certificate that is not embedded is not part of the path.
    /// </summary>
    private List<X509Certificate2>? PathToRoot(X509Certificate2 signer)
    {
        var anchors = Certificates.Where(IsSelfSigned).ToList();
        if (anchors.Count == 0)
        {
            return null;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Nothing here is allowed to reach the network: a chain that needs a download does not build.
        chain.ChainPolicy.DisableCertificateDownloads = true;

        foreach (var anchor in anchors)
        {
            chain.ChainPolicy.CustomTrustStore.Add(anchor);
        }

        foreach (var certificate in Certificates)
        {
            if (!IsSelfSigned(certificate))
            {
                chain.ChainPolicy.ExtraStore.Add(certificate);
            }
        }

        if (!chain.Build(signer))
        {
            return null;
        }

        return chain.ChainElements.Select(element => element.Certificate).ToList();
    }

    private static bool IsSelfSigned(X509Certificate2 certificate)
        => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

    private static IEnumerable<byte[]> Streams(PdfDocument document, string dss, string key)
    {
        if (PdfSyntax.ArrayBody(dss, key) is not { } array)
        {
            yield break;
        }

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(array, @"(\d+)\s+0\s+R"))
        {
            var number = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (document.Objects.TryGetValue(number, out var entry))
            {
                var bytes = PdfDocument.StreamBytes(entry).ToArray();
                if (bytes.Length > 0)
                {
                    yield return bytes;
                }
            }
        }
    }
}
