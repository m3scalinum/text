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
            signatures.Add((dictionary.Offset, VerifySignature(bytes, dictionary, content, trustStore, dss, timestamps, archived)));
        }

        signatures.Sort((left, right) => left.Offset.CompareTo(right.Offset));

        return new PdfSignatureVerification(
            signatures.Select(entry => entry.Info).ToList(),
            timestamps);
    }

    private static PdfSignatureInfo VerifySignature(
        byte[] bytes,
        PdfSignatureDictionary dictionary,
        byte[] content,
        PdfTrustStore? trustStore,
        PdfDss dss,
        List<PdfDocumentTimestampInfo> timestamps,
        bool archived)
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
        var chain = EvaluateChain(signer.Certificate, trustStore, dss, timestamped);

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

        var chain = EvaluateChain(token.Signer, trustStore, PdfDss.Empty, token.GeneratedAt);

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
        DateTimeOffset? at)
    {
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

        var reasons = chain.ChainStatus
            .Select(status => status.StatusInformation.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal);

        var joined = string.Join("; ", reasons);
        return (PdfChainStatus.Untrusted, joined.Length > 0 ? joined : "the chain did not build");
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
