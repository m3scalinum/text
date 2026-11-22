using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads a document as eIDAS acts rather than as CMS structures: who acted, whether the act is
/// timestamped, what a trust list states about the services involved, and the level proven.
/// </summary>
/// <remarks>
/// The reader adds two facts to <see cref="PdfSignatureVerifier.Verify(ReadOnlySpan{byte}, PdfTrustStore)"/>:
/// the kind of each signature, read from the signing certificate, and the qualification a supplied
/// <see cref="PdfTrustedList"/> states. It judges nothing else and fetches nothing: with no list,
/// every qualification is <see cref="PdfEidasQualification.NotEvaluated"/>, never a quiet "no".
/// </remarks>
internal static class PdfEidasReader
{
    /// <summary>Reads every signature and document timestamp, and what the list says about them.</summary>
    public static PdfEidasReport Read(ReadOnlySpan<byte> pdf, PdfTrustStore? trustStore, PdfTrustedList? trustList)
    {
        var verification = PdfSignatureVerifier.Verify(pdf, trustStore);
        var bytes = pdf.ToArray();
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(bytes));

        var facts = new List<SignatureFacts>();
        foreach (var dictionary in document.SignatureDictionaries())
        {
            if (!dictionary.IsDocumentTimestamp)
            {
                facts.Add(Inspect(bytes, dictionary));
            }
        }

        facts.Sort((left, right) => left.Offset.CompareTo(right.Offset));

        var verdicts = new List<PdfEidasSignatureVerdict>(verification.Signatures.Count);
        for (var index = 0; index < verification.Signatures.Count; index++)
        {
            var read = index < facts.Count ? facts[index] : SignatureFacts.None;
            verdicts.Add(Verdict(index, verification.Signatures[index], read, trustList));
        }

        var timestamps = new List<PdfEidasTimestampVerdict>(verification.DocumentTimestamps.Count);
        for (var index = 0; index < verification.DocumentTimestamps.Count; index++)
        {
            var timestamp = verification.DocumentTimestamps[index];
            var (qualification, explanation) = QualifyTimestampAuthority(
                timestamp.SignerCertificate, timestamp.TimestampedTime, trustList);

            timestamps.Add(new PdfEidasTimestampVerdict(
                index,
                timestamp.TimestampedTime,
                timestamp.SignerSubject,
                timestamp.IsIntact,
                qualification,
                explanation));
        }

        return new PdfEidasReport(verification, verdicts, timestamps, trustList is not null);
    }

    /// <summary>Builds one signature's verdict from what the verifier proved and what the CMS carries.</summary>
    private static PdfEidasSignatureVerdict Verdict(
        int index,
        PdfSignatureInfo info,
        SignatureFacts facts,
        PdfTrustedList? trustList)
    {
        var kind = info.SignerCertificate is { } certificate
            ? PdfEidasCertificate.KindOf(certificate)
            : facts.Kind;

        var hasTimestamp = info.TimestampedSigningTime is not null;
        var timestamped = info.TimestampedSigningTime ?? facts.TimestampedTime;

        PdfEidasQualification timestampQualification;
        string timestampExplanation;

        if (hasTimestamp)
        {
            (timestampQualification, timestampExplanation) =
                QualifyTimestampAuthority(facts.TimestampAuthority, timestamped, trustList);
        }
        else
        {
            timestampQualification = PdfEidasQualification.NotEvaluated;
            timestampExplanation = facts.TimestampError is { } error
                ? $"not evaluated: no timestamp could be read from the signature: {error}"
                : "not evaluated: the signature carries no timestamp, so there is no authority to judge";
        }

        var (signerQualification, signerExplanation) = QualifySigner(info, kind, trustList);

        var summary = Summary(kind, hasTimestamp, timestampQualification);

        return new PdfEidasSignatureVerdict(
            index,
            kind,
            info.SignerSubject,
            facts.SigningTime is not null,
            facts.SigningTime,
            hasTimestamp,
            timestamped,
            facts.TimestampAuthority?.Subject,
            timestampQualification,
            timestampExplanation,
            signerQualification,
            signerExplanation,
            info.Level,
            summary);
    }

    /// <summary>
    /// The one-sentence verdict: the act, then what is known about the timestamp — and, when no
    /// list was supplied, that the authority's status was not evaluated rather than not qualified.
    /// </summary>
    private static string Summary(
        PdfSignatureKind kind,
        bool hasTimestamp,
        PdfEidasQualification qualification)
    {
        var act = kind == PdfSignatureKind.Seal ? "seal" : "person";

        var timestamp = !hasTimestamp
            ? "not timestamped"
            : qualification switch
            {
                PdfEidasQualification.Qualified => "timestamped by a qualified service",
                PdfEidasQualification.NotQualified => "timestamped by a service the list does not mark qualified",
                _ => "timestamped; the authority's qualification was not evaluated",
            };

        return act + ", " + timestamp;
    }

    /// <summary>What a supplied list states about the signing certificate, at the signature's proven time.</summary>
    private static (PdfEidasQualification Qualification, string Explanation) QualifySigner(
        PdfSignatureInfo info,
        PdfSignatureKind kind,
        PdfTrustedList? trustList)
    {
        if (trustList is null)
        {
            return (PdfEidasQualification.NotEvaluated,
                "not evaluated: no trust list was supplied, so nothing is claimed about the signing certificate's qualification");
        }

        if (info.SignerCertificate is not { } certificate)
        {
            return (PdfEidasQualification.NotQualified,
                "not qualified: the signature carries no signing certificate, so no service can cover it");
        }

        var at = info.TimestampedSigningTime ?? info.ClaimedSigningTime;
        if (at is null)
        {
            return (PdfEidasQualification.NotEvaluated,
                "not evaluated: the signature carries no time this reader can judge a service period against");
        }

        var evidenced = info.TimestampedSigningTime is not null
            ? " (timestamped time)"
            : " (claimed time, not evidence)";

        var services = trustList.FindServices(certificate, at.Value);
        if (services.Count == 0)
        {
            return (PdfEidasQualification.NotQualified,
                $"not qualified: {trustList.Territory} lists no granted service covering the signing certificate at {at.Value:u}{evidenced}");
        }

        var expected = kind == PdfSignatureKind.Seal ? PdfTrustServiceType.Seal : PdfTrustServiceType.Signature;
        PdfTrustService? sameKind = null;

        foreach (var service in services)
        {
            if (service.Type != expected)
            {
                continue;
            }

            sameKind ??= service;

            if (service.IsQualified)
            {
                return (PdfEidasQualification.Qualified,
                    $"qualified: {trustList.Territory} lists {service.ProviderName} as granting {service.TypeIdentifier}"
                    + $" covering the signing certificate at {at.Value:u}{evidenced}");
            }
        }

        if (sameKind is not null)
        {
            return (PdfEidasQualification.NotQualified,
                $"not qualified: {trustList.Territory} covers the signing certificate with a service of type {sameKind.TypeIdentifier},"
                + " which the list does not mark qualified");
        }

        var other = services[0];
        return (PdfEidasQualification.NotQualified,
            $"not qualified: {trustList.Territory} covers the signing certificate with a service of type {other.TypeIdentifier},"
            + (kind == PdfSignatureKind.Seal ? " which is not a seal service" : " which is not a signature service"));
    }

    /// <summary>What a supplied list states about a timestamp authority, at the moment it attested.</summary>
    private static (PdfEidasQualification Qualification, string Explanation) QualifyTimestampAuthority(
        X509Certificate2? authority,
        DateTimeOffset? at,
        PdfTrustedList? trustList)
    {
        if (trustList is null)
        {
            return (PdfEidasQualification.NotEvaluated,
                "not evaluated: no trust list was supplied, so nothing is claimed about the timestamp authority's qualification");
        }

        if (authority is null)
        {
            return (PdfEidasQualification.NotEvaluated,
                "not evaluated: the timestamp token carries no authority certificate this reader can identify");
        }

        if (at is null)
        {
            return (PdfEidasQualification.NotEvaluated,
                "not evaluated: the token carries no time to judge a service period against");
        }

        var services = trustList.FindServices(authority, at.Value);
        if (services.Count == 0)
        {
            return (PdfEidasQualification.NotQualified,
                $"not qualified: {trustList.Territory} lists no granted timestamp service covering the authority certificate at {at.Value:u}");
        }

        PdfTrustService? timestampService = null;

        foreach (var service in services)
        {
            if (service.Type != PdfTrustServiceType.Timestamp)
            {
                continue;
            }

            timestampService ??= service;

            if (service.IsQualified)
            {
                return (PdfEidasQualification.Qualified,
                    $"qualified: {trustList.Territory} lists {service.ProviderName} as granting {service.TypeIdentifier}"
                    + $", a qualified timestamp service covering the authority at {at.Value:u}");
            }
        }

        if (timestampService is not null)
        {
            return (PdfEidasQualification.NotQualified,
                $"not qualified: {trustList.Territory} covers the authority with a timestamp service of type {timestampService.TypeIdentifier},"
                + " which the list does not mark qualified");
        }

        return (PdfEidasQualification.NotQualified,
            $"not qualified: {trustList.Territory} covers the authority with a service of type {services[0].TypeIdentifier},"
            + " which is not a timestamp service");
    }

    /// <summary>Reads the CMS facts the verifier does not report: the kind and the claimed signing time.</summary>
    private static SignatureFacts Inspect(byte[] bytes, PdfSignatureDictionary dictionary)
    {
        if (!PdfSignatureVerifier.TryCovered(bytes, dictionary, out var content, out var rangeError))
        {
            return SignatureFacts.Failed(dictionary.Offset, rangeError ?? "the byte range could not be read");
        }

        SignedCms cms;
        try
        {
            cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(PdfSignatureVerifier.ReadDer(bytes, dictionary));
        }
        catch (CryptographicException exception)
        {
            return SignatureFacts.Failed(dictionary.Offset, $"the signature is not a CMS structure: {exception.Message}");
        }

        if (cms.SignerInfos.Count == 0)
        {
            return SignatureFacts.Failed(dictionary.Offset, "the CMS carries no signer");
        }

        var signer = cms.SignerInfos[0];
        var kind = signer.Certificate is { } certificate
            ? PdfEidasCertificate.KindOf(certificate)
            : PdfSignatureKind.Person;

        var signingTime = SigningTimeOf(signer);

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
                    return new SignatureFacts(dictionary.Offset, kind, signingTime, token.GeneratedAt, token.Signer, null);
                }
            }
            catch (PdfSignatureException exception)
            {
                return new SignatureFacts(dictionary.Offset, kind, signingTime, null, null, exception.Message);
            }
            catch (AsnContentException exception)
            {
                return new SignatureFacts(dictionary.Offset, kind, signingTime, null, null, exception.Message);
            }
        }

        return new SignatureFacts(dictionary.Offset, kind, signingTime, null, null, null);
    }

    /// <summary>The value of the CAdES <c>signing-time</c> signed attribute, when it is present and readable.</summary>
    private static DateTimeOffset? SigningTimeOf(SignerInfo signer)
    {
        foreach (var attribute in signer.SignedAttributes)
        {
            if (attribute.Oid?.Value != PdfEidasCertificate.SigningTimeOid || attribute.Values.Count == 0)
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(attribute.Values[0].RawData, AsnEncodingRules.BER);
                var tag = reader.PeekTag();

                return tag.TagValue == (byte)UniversalTagNumber.UtcTime
                    ? reader.ReadUtcTime()
                    : reader.ReadGeneralizedTime();
            }
            catch (AsnContentException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>What one signature dictionary says about itself, before any judgement is made.</summary>
    private sealed record SignatureFacts(
        int Offset,
        PdfSignatureKind Kind,
        DateTimeOffset? SigningTime,
        DateTimeOffset? TimestampedTime,
        X509Certificate2? TimestampAuthority,
        string? TimestampError)
    {
        /// <summary>The facts of a dictionary this reader could not decode at all.</summary>
        public static SignatureFacts None { get; } =
            new(0, PdfSignatureKind.Person, null, null, null, null);

        /// <summary>The facts of a dictionary whose bytes could not be read, and why.</summary>
        public static SignatureFacts Failed(int offset, string error) =>
            new(offset, PdfSignatureKind.Person, null, null, null, error);
    }
}
