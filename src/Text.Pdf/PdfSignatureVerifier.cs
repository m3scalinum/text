using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads a PDF's signatures back, and says what can be proven about them.
/// </summary>
/// <remarks>
/// The verifier reports; it does not decide. Every answer is either a fact it checked — the CMS
/// verifies over these bytes, this token's imprint is this signature — or <see langword="null"/>,
/// which says the fact could not be established. The one judgement it makes is the chain, and it
/// makes that only against the store the caller passes: the machine's trust store is never read.
/// </remarks>
public static class PdfSignatureVerifier
{
    /// <summary>Verifies every signature in <paramref name="pdf"/>, without judging the chain.</summary>
    /// <param name="pdf">The document to read.</param>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    public static PdfSignatureVerification Verify(ReadOnlySpan<byte> pdf)
        => Verify(pdf, PdfTrustStore.Empty);

    /// <summary>Verifies every signature in <paramref name="pdf"/> against <paramref name="trustStore"/>.</summary>
    /// <param name="pdf">The document to read.</param>
    /// <param name="trustStore">The anchors to judge the chain by. Null behaves as an empty store.</param>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    public static PdfSignatureVerification Verify(ReadOnlySpan<byte> pdf, PdfTrustStore? trustStore)
        => VerifyDetailed(pdf, trustStore).Verification;

    /// <summary>
    /// Verifies every signature in <paramref name="pdf"/> and names the certificates whose chain did
    /// not reach <paramref name="trustStore"/>.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <param name="trustStore">The anchors to judge the chain by. Null behaves as an empty store.</param>
    /// <returns>
    /// The same <see cref="PdfSignatureVerification"/> the plain overload returns, plus one
    /// <see cref="PdfSignatureChainCertificate"/> per certificate a failed chain could not accept.
    /// </returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    /// <remarks>
    /// This is a reading of the same chain judgement, one element further: the chain's failing
    /// elements are reported with the subject, issuer and serial number a caller needs to find the
    /// certificate and add it to the store. When the chain stops at a certificate nobody supplied, the
    /// entry names the issuer its child states, without inventing a subject or a serial number. A
    /// chain that was not judged names nothing — an empty store is not a missing certificate.
    /// </remarks>
    public static PdfSignatureChainReading VerifyDetailed(ReadOnlySpan<byte> pdf, PdfTrustStore? trustStore)
    {
        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var bytes = pdf.ToArray();
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(bytes));
        var dss = PdfDss.Read(document);

        // The archival chain is judged once for the document: a signature is only B-LTA when the
        // file as a whole is archived — a timestamp over the whole file, whose own chain the store
        // embeds. A signature cannot be more archived than the document that carries it.
        var archived = PdfArchiveChain.Analyze(bytes, document, dss).IsArchived;

        var timestamps = new List<PdfDocumentTimestampInfo>();
        var signatures = new List<(int Offset, PdfSignatureInfo Info)>();
        var pending = new List<(PdfSignatureDictionary Dictionary, byte[] Content)>();
        var rejected = new List<PdfSignatureChainCertificate>();

        foreach (var dictionary in document.SignatureDictionaries())
        {
            if (!TryCovered(bytes, dictionary, out var content, out var rangeError))
            {
                if (dictionary.IsDocumentTimestamp)
                {
                    timestamps.Add(new PdfDocumentTimestampInfo(
                        null, null, null, null, false, false, PdfChainStatus.NotEvaluated, null, rangeError));
                }
                else
                {
                    signatures.Add((dictionary.Offset, new PdfSignatureInfo(
                        null, null, null, PdfSyntax.Date(dictionary.ClaimedTime), null, false, false,
                        false, false, PdfSignatureLevel.None, PdfChainStatus.NotEvaluated, null, rangeError)));
                }

                continue;
            }

            if (dictionary.IsDocumentTimestamp)
            {
                timestamps.Add(VerifyTimestamp(bytes, dictionary, content, trustStore));
            }
            else
            {
                pending.Add((dictionary, content));
            }
        }

        foreach (var (dictionary, content) in pending)
        {
            signatures.Add((dictionary.Offset, VerifySignature(bytes, dictionary, content, trustStore, dss, timestamps, archived, rejected)));
        }

        signatures.Sort((left, right) => left.Offset.CompareTo(right.Offset));

        return new PdfSignatureChainReading(
            new PdfSignatureVerification(
                signatures.Select(entry => entry.Info).ToList(),
                timestamps),
            Deduplicate(rejected));
    }

    /// <summary>
    /// Verifies every signature in <paramref name="pdf"/> against <paramref name="trustStore"/>, and
    /// says which service of <paramref name="trustList"/> covers each signing certificate.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <param name="trustStore">The anchors to judge the chain by. Null behaves as an empty store.</param>
    /// <param name="trustList">The territory's trust list to judge qualification by. Null means no list
    /// was supplied, and the returned coverage is empty.</param>
    /// <returns>The same verification <see cref="Verify(ReadOnlySpan{byte}, PdfTrustStore)"/> returns,
    /// plus one <see cref="PdfSignatureTrustCoverage"/> per signature.</returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    /// <remarks>
    /// The trust list is data the caller read and chose; nothing is downloaded, and a certificate the
    /// list does not know leaves the signature's own verdict untouched. Read the list with
    /// <see cref="PdfLotl.Read(ReadOnlySpan{byte}, string, PdfTrustStore)"/> and its own signature is
    /// checked against the anchors passed there: every coverage entry then reports through
    /// <see cref="PdfSignatureTrustCoverage.Basis"/> whether that check held and whether the list was
    /// current, and a list whose signature did not hold qualifies nothing. A list read without anchors
    /// carries no verdict, and the coverage says the qualification is the list's claim. The caller
    /// keeps the list cached, decides when to refresh it, and checks
    /// <see cref="PdfTrustedList.IsExpiredAt"/>.
    /// </remarks>
    public static PdfTrustListVerification Verify(
        ReadOnlySpan<byte> pdf,
        PdfTrustStore? trustStore,
        PdfTrustedList? trustList)
    {
        var verification = Verify(pdf, trustStore);

        return new PdfTrustListVerification(
            verification,
            trustList is null ? [] : trustList.Evaluate(verification),
            trustList?.Signature);
    }

    /// <summary>
    /// Reads every signature as an eIDAS act: person or seal, timestamped or not, and what a trust
    /// list states about the signing certificate and the timestamp authority.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <returns>One <see cref="PdfEidasSignatureVerdict"/> per signature and one
    /// <see cref="PdfEidasTimestampVerdict"/> per document timestamp, with every qualification
    /// <see cref="PdfEidasQualification.NotEvaluated"/> because no list was supplied.</returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    public static PdfEidasReport ReadEidas(ReadOnlySpan<byte> pdf)
        => PdfEidasReader.Read(pdf, null, null);

    /// <summary>
    /// Reads every signature as an eIDAS act, and says what <paramref name="trustList"/> states
    /// about the signers and the timestamp authorities.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <param name="trustStore">The anchors to judge the chain by. Null behaves as an empty store.</param>
    /// <param name="trustList">The territory's trust list to judge qualification by, or null when the
    /// caller has none — in which case every qualification says so instead of claiming "not qualified".</param>
    /// <returns>The eIDAS reading, with the full verification underneath.</returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    /// <remarks>
    /// The list is data the caller read with <see cref="PdfLotl.Read(ReadOnlySpan{byte}, string)"/>:
    /// nothing is downloaded, and a certificate the list does not know makes the qualification
    /// <see cref="PdfEidasQualification.NotQualified"/> without touching what the verifier proved
    /// about the bytes. Two questions are kept apart: whether the timestamp authority is a qualified
    /// service of the list, and whether a qualified signature or seal service covers the signing
    /// certificate. Neither is answered by the certificate's own text.
    /// </remarks>
    public static PdfEidasReport ReadEidas(
        ReadOnlySpan<byte> pdf,
        PdfTrustStore? trustStore,
        PdfTrustedList? trustList)
        => PdfEidasReader.Read(pdf, trustStore, trustList);

    /// <summary>
    /// Reports, for every certificate the document security store embeds, which revocation data
    /// covers it and when that data was issued.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <returns>One entry per embedded certificate, in the order the store lists them; empty when
    /// the document carries no store.</returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    /// <remarks>
    /// This is the cache side of the revocation seam: a caller that keeps its own answers can see
    /// what the file carries and how old it is, without asking a responder. Nothing is fetched and
    /// nothing is judged — a covering answer about a revoked certificate still covers it.
    /// </remarks>
    public static IReadOnlyList<PdfRevocationCoverage> ReadRevocation(ReadOnlySpan<byte> pdf)
    {
        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        return PdfDss.Read(document).Coverage();
    }

    /// <summary>
    /// Reports, per embedded revocation response, whether its signature proves anything, who signed
    /// it, and which certificate it answers for.
    /// </summary>
    /// <param name="pdf">The document to read.</param>
    /// <returns>One entry per response the store embeds — answers first, then lists — in the order
    /// the store declares them; empty when the document carries no store.</returns>
    /// <exception cref="PdfSignatureException">The data is not a PDF.</exception>
    /// <remarks>
    /// <para>
    /// A response is evidence only when its signature verifies and its signer was entitled to make
    /// it: the certificate's issuer itself, or a responder certificate that issuer issued with
    /// <c>id-kp-OCSPSigning</c>. A CRL is checkable the same way, against the authority it names.
    /// Nothing is fetched and nothing is judged beyond that — a valid answer about a revoked
    /// certificate still verifies, because the question here is who wrote it, not what it says.
    /// </para>
    /// <para>
    /// The status is one of <see cref="PdfRevocationSignatureStatus.Valid"/>,
    /// <see cref="PdfRevocationSignatureStatus.NoSignature"/>,
    /// <see cref="PdfRevocationSignatureStatus.Invalid"/> or
    /// <see cref="PdfRevocationSignatureStatus.UnexpectedSigner"/>, and the entry names the
    /// certificate the response is about and the certificate that signed it, so a caller can report
    /// "the OCSP answer embedded for CN=… was not signed by its issuer" without parsing the store.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PdfRevocationSignatureReading> ReadRevocationSignatures(ReadOnlySpan<byte> pdf)
    {
        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        return PdfDss.Read(document).Signatures();
    }

    private static PdfSignatureInfo VerifySignature(
        byte[] bytes,
        PdfSignatureDictionary dictionary,
        byte[] content,
        PdfTrustStore? trustStore,
        PdfDss dss,
        List<PdfDocumentTimestampInfo> timestamps,
        bool archived,
        List<PdfSignatureChainCertificate> rejected)
    {
        var claimed = PdfSyntax.Date(dictionary.ClaimedTime);
        var coversWhole = CoversWholeFile(bytes, dictionary);

        // "Present" is a fact about the file; "valid" is a fact about the token and its bytes.
        var hasTimestamp = timestamps.Count > 0;
        var timestampValid = timestamps.Any(entry => entry.IsIntact && entry.CoversWholeFile);

        SignedCms cms;
        try
        {
            cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(ReadDer(bytes, dictionary));
        }
        catch (CryptographicException exception)
        {
            return new PdfSignatureInfo(
                null, null, null, claimed, null, false, coversWhole,
                hasTimestamp, timestampValid, PdfSignatureLevel.None,
                PdfChainStatus.NotEvaluated, null, $"the signature is not a CMS structure: {exception.Message}");
        }

        if (cms.SignerInfos.Count == 0)
        {
            return new PdfSignatureInfo(
                null, null, null, claimed, null, false, coversWhole,
                hasTimestamp, timestampValid, PdfSignatureLevel.None,
                PdfChainStatus.NotEvaluated, null, "the CMS carries no signer");
        }

        var signer = cms.SignerInfos[0];

        try
        {
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException exception)
        {
            // The certificate is still worth reporting: it says who the signature claims to be by.
            return new PdfSignatureInfo(
                signer.Certificate?.Subject,
                signer.Certificate?.Issuer,
                signer.Certificate,
                claimed,
                null,
                false,
                coversWhole,
                hasTimestamp,
                timestampValid,
                PdfSignatureLevel.None,
                PdfChainStatus.NotEvaluated,
                null,
                $"the signature does not verify over its byte range: {exception.Message}");
        }

        var timestamped = SignatureTime(cms, signer);
        var chain = EvaluateChain(signer.Certificate, trustStore, dss, timestamped, out var chainIssues);
        rejected.AddRange(chainIssues);

        var level = PdfSignatureLevel.BaselineB;

        if (timestamped is not null)
        {
            level = PdfSignatureLevel.BaselineT;

            if (signer.Certificate is { } certificate && dss.ProvesChain(certificate, out _))
            {
                level = PdfSignatureLevel.BaselineLT;

                if (archived)
                {
                    level = PdfSignatureLevel.BaselineLTA;
                }
            }
        }

        return new PdfSignatureInfo(
            signer.Certificate?.Subject,
            signer.Certificate?.Issuer,
            signer.Certificate,
            claimed,
            timestamped,
            true,
            coversWhole,
            hasTimestamp,
            timestampValid,
            level,
            chain.Status,
            chain.Error,
            null);
    }

    /// <summary>The time the signature's own timestamp token attests, when it carries a valid one.</summary>
    private static DateTimeOffset? SignatureTime(SignedCms cms, SignerInfo signer)
    {
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid?.Value != PdfTimestamp.SignatureTimestampOid)
            {
                continue;
            }

            try
            {
                var token = PdfTimestamp.Parse(attribute.Values[0].RawData);

                if (token.Matches(signer.GetSignature()))
                {
                    return token.GeneratedAt;
                }
            }
            catch (PdfSignatureException)
            {
                // A token that does not parse is not a timestamp; the caller sees null.
            }
        }

        return null;
    }

    private static PdfDocumentTimestampInfo VerifyTimestamp(
        byte[] bytes,
        PdfSignatureDictionary dictionary,
        byte[] content,
        PdfTrustStore? trustStore)
    {
        var coversWhole = CoversWholeFile(bytes, dictionary);

        PdfTimestampToken token;
        try
        {
            token = PdfTimestamp.Parse(ReadDer(bytes, dictionary));
        }
        catch (PdfSignatureException exception)
        {
            return new PdfDocumentTimestampInfo(
                null, null, null, null, false, coversWhole, PdfChainStatus.NotEvaluated, null, exception.Message);
        }

        if (!token.Matches(content))
        {
            return new PdfDocumentTimestampInfo(
                token.GeneratedAt, token.Signer?.Subject, token.Signer?.Issuer, token.Signer, false, coversWhole,
                PdfChainStatus.NotEvaluated, null,
                "the timestamp's imprint does not cover the bytes of its byte range");
        }

        var chain = EvaluateChain(token.Signer, trustStore, PdfDss.Empty, token.GeneratedAt, out _);

        return new PdfDocumentTimestampInfo(
            token.GeneratedAt,
            token.Signer?.Subject,
            token.Signer?.Issuer,
            token.Signer,
            true,
            coversWhole,
            chain.Status,
            chain.Error,
            null);
    }

    private static (PdfChainStatus Status, string? Error) EvaluateChain(
        X509Certificate2? certificate,
        PdfTrustStore? trustStore,
        PdfDss dss,
        DateTimeOffset? at,
        out IReadOnlyList<PdfSignatureChainCertificate> issues)
    {
        issues = [];

        if (certificate is null)
        {
            return (PdfChainStatus.NotEvaluated, null);
        }

        if (trustStore is null || trustStore.Anchors.Count == 0)
        {
            return (PdfChainStatus.NotEvaluated, null);
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

        // A verification that fetched a missing issuer would be a verification that used the network.
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = (at ?? DateTimeOffset.UtcNow).UtcDateTime;

        foreach (var anchor in trustStore.Anchors)
        {
            chain.ChainPolicy.CustomTrustStore.Add(anchor);
        }

        foreach (var intermediate in trustStore.Intermediates)
        {
            chain.ChainPolicy.ExtraStore.Add(intermediate);
        }

        foreach (var embedded in dss.Certificates)
        {
            chain.ChainPolicy.ExtraStore.Add(embedded);
        }

        if (chain.Build(certificate))
        {
            return (PdfChainStatus.Trusted, null);
        }

        issues = Describe(chain, trustStore);

        var reasons = chain.ChainStatus
            .Select(status => status.StatusInformation.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal);

        var joined = string.Join("; ", reasons);
        return (PdfChainStatus.Untrusted, joined.Length > 0 ? joined : "the chain did not build");
    }

    /// <summary>
    /// The certificates to add to the store for a chain that did not build: the topmost certificate
    /// the chain could reach when the store does not anchor it, or the elements a completed path
    /// rejected.
    /// </summary>
    /// <remarks>
    /// The branch is chosen on whether the chain reached an anchor of the store, not on which flags
    /// the platform attached: a chain that stops short is described the same way on every runtime,
    /// while a path that reaches an anchor and is still refused names the elements that failed.
    /// </remarks>
    private static List<PdfSignatureChainCertificate> Describe(X509Chain chain, PdfTrustStore store)
    {
        var rejected = new List<PdfSignatureChainCertificate>();

        if (chain.ChainElements.Count == 0)
        {
            return rejected;
        }

        var last = chain.ChainElements[chain.ChainElements.Count - 1];

        if (!ReachesAnchor(store, last.Certificate))
        {
            // The chain stops before the store. Either the topmost certificate is its own issuer and
            // is the anchor nobody trusts, or the certificate nobody supplied is the issuer it names.
            rejected.Add(string.Equals(last.Certificate.Subject, last.Certificate.Issuer, StringComparison.Ordinal)
                ? new PdfSignatureChainCertificate(
                    last.Certificate.Subject,
                    last.Certificate.Issuer,
                    last.Certificate.SerialNumber,
                    isEmbedded: true,
                    "the chain ends at this certificate, which no anchor of the trust store issued")
                : new PdfSignatureChainCertificate(
                    subject: null,
                    last.Certificate.Issuer,
                    serialNumber: null,
                    isEmbedded: false,
                    "the certificate that issued this one is not in the trust store and the file does not embed it"));

            return rejected;
        }

        foreach (X509ChainElement element in chain.ChainElements)
        {
            var reasons = Reasons(element.ChainElementStatus);

            if (reasons.Count == 0)
            {
                continue;
            }

            var certificate = element.Certificate;

            rejected.Add(new PdfSignatureChainCertificate(
                certificate.Subject,
                certificate.Issuer,
                certificate.SerialNumber,
                isEmbedded: true,
                "the chain rejects this certificate: " + string.Join(", ", reasons)));
        }

        return rejected;
    }

    /// <summary>Whether the chain's topmost certificate is one of the store's anchors.</summary>
    private static bool ReachesAnchor(PdfTrustStore store, X509Certificate2 certificate)
    {
        foreach (var anchor in store.Anchors)
        {
            if (string.Equals(anchor.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Reasons(X509ChainStatus[] status)
    {
        var reasons = new List<string>(status.Length);

        foreach (var entry in status)
        {
            reasons.Add(entry.Status.ToString());
        }

        reasons.Sort(StringComparer.Ordinal);
        return reasons;
    }

    private static List<PdfSignatureChainCertificate> Deduplicate(List<PdfSignatureChainCertificate> rejected)
    {
        if (rejected.Count < 2)
        {
            return rejected;
        }

        var unique = new List<PdfSignatureChainCertificate>(rejected.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var issue in rejected)
        {
            if (seen.Add(issue.Key))
            {
                unique.Add(issue);
            }
        }

        return unique;
    }

    private static bool CoversWholeFile(byte[] bytes, PdfSignatureDictionary dictionary)
        => dictionary.ByteRange[2] + dictionary.ByteRange[3] == bytes.Length;

    /// <summary>
    /// The bytes a signature covers, or the reason they cannot be trusted to name a place in the file.
    /// </summary>
    internal static bool TryCovered(
        byte[] bytes,
        PdfSignatureDictionary dictionary,
        out byte[] content,
        out string? error)
    {
        content = [];
        var range = dictionary.ByteRange;

        if (range[0] != 0)
        {
            error = $"the byte range starts at {range[0]} instead of the first byte";
            return false;
        }

        if (range[1] < 0 || range[3] < 0 || range[0] + range[1] > range[2] || range[2] + range[3] > bytes.Length)
        {
            error = $"the byte range {range[0]} {range[1]} {range[2]} {range[3]} does not fit a file of {bytes.Length} bytes";
            return false;
        }

        content = new byte[range[1] + range[3]];
        Buffer.BlockCopy(bytes, (int)range[0], content, 0, (int)range[1]);
        Buffer.BlockCopy(bytes, (int)range[2], content, (int)range[1], (int)range[3]);

        error = null;
        return true;
    }

    /// <summary>
    /// The DER structure held by <c>/Contents</c>: the hex is padded, so its length comes from the
    /// ASN.1 header rather than from the number of hex digits.
    /// </summary>
    internal static byte[] ReadDer(byte[] bytes, PdfSignatureDictionary dictionary)
    {
        var start = dictionary.ContentsStart;
        var length = dictionary.ContentsLength;

        var text = Encoding.Latin1.GetString(bytes, start, length).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        if (text.Length % 2 != 0)
        {
            text = text.Substring(0, text.Length - 1);
        }

        var raw = Convert.FromHexString(text);
        var total = DerLength(raw);

        if (total > 0 && total <= raw.Length)
        {
            return raw.AsSpan(0, total).ToArray();
        }

        var end = raw.Length;
        while (end > 0 && raw[end - 1] == 0)
        {
            end--;
        }

        return raw.AsSpan(0, end).ToArray();
    }

    /// <summary>The length of the DER structure that starts at byte zero, or -1.</summary>
    private static int DerLength(byte[] raw)
    {
        if (raw.Length < 2 || raw[0] != 0x30)
        {
            return -1;
        }

        var first = raw[1];

        if ((first & 0x80) == 0)
        {
            return 2 + first;
        }

        var octets = first & 0x7F;
        if (octets is 0 or > 4 || raw.Length < 2 + octets)
        {
            return -1;
        }

        var length = 0;
        for (var index = 0; index < octets; index++)
        {
            length = (length << 8) | raw[2 + index];
        }

        return 2 + octets + length;
    }
}
