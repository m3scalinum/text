using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Signs a PDF with PAdES, as an incremental update.
/// </summary>
/// <remarks>
/// A PAdES signature is a detached CMS structure over the whole file, minus the bytes that hold the
/// signature itself. The file is therefore built first with a fixed-size placeholder, the byte range
/// is computed on the result, the CMS is produced over the two ranges, and the placeholder is
/// overwritten in place. Nothing before the appended block changes, so a document that was PDF/A
/// stays PDF/A and a document that was already signed keeps its earlier signatures valid.
/// <para>
/// An encrypted document is refused: an incremental update to one would append plaintext that no
/// reader would decrypt. Decrypt it with <see cref="PdfEncryption.Open"/> and sign those bytes.
/// </para>
/// </remarks>
public static class PdfSigner
{
    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> carrying a PAdES-B signature made with
    /// <paramref name="certificate"/>.
    /// </summary>
    /// <param name="pdf">The document to sign.</param>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="options">What the signature declares. Optional.</param>
    /// <exception cref="PdfSignatureException">
    /// The data is not a PDF this writer can update, the certificate has no private key, the
    /// appearance cannot be drawn, or the timestamp authority failed.
    /// </exception>
    public static byte[] Sign(
        ReadOnlySpan<byte> pdf,
        X509Certificate2 certificate,
        PdfSignatureOptions? options = null)
        => SignWithReport(pdf, certificate, options).Content;

    /// <summary>
    /// Signs <paramref name="pdf"/> and returns the bytes together with what the revocation seam
    /// contributed to the store.
    /// </summary>
    /// <param name="pdf">The document to sign.</param>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="options">What the signature declares. Optional.</param>
    /// <returns>The signed bytes, the level this call was configured to reach, the evidence it
    /// embedded, and the revocation that was missing.</returns>
    /// <exception cref="PdfSignatureException">
    /// The data is not a PDF this writer can update, the certificate has no private key, the
    /// appearance cannot be drawn, or the timestamp authority failed.
    /// </exception>
    /// <exception cref="PdfRevocationException">
    /// PAdES-B-LT was asked for and a certificate of the chain has neither an OCSP answer nor a CRL.
    /// </exception>
    public static PdfSignatureResult SignWithReport(
        ReadOnlySpan<byte> pdf,
        X509Certificate2 certificate,
        PdfSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        options ??= new PdfSignatureOptions();

        if (!certificate.HasPrivateKey)
        {
            throw new PdfSignatureException("the certificate carries no private key, so it cannot sign");
        }

        if (pdf.IsEmpty)
        {
            throw new PdfSignatureException("the document is empty");
        }

        var appearance = options.Appearance;
        appearance?.Validate();
        ValidateTimestamp(options);
        ValidateLevel(options);

        var material = ResolveDss(options, certificate);
        var dss = material?.Options;

        var text = Encoding.Latin1.GetString(pdf);
        var document = PdfDocument.Parse(text);

        var pageNumber = 0;
        if (appearance is not null)
        {
            var pages = document.PageNumbers();

            if (appearance.PageIndex >= pages.Count)
            {
                throw new PdfSignatureException(
                    $"the document has {pages.Count} page(s) and the appearance asks for page {appearance.PageIndex}; pages are counted from zero");
            }

            pageNumber = pages[appearance.PageIndex];
        }

        var next = document.NextObjectNumber();
        var fieldNumber = next++;
        var signatureNumber = next++;
        var formNumber = appearance is null ? 0 : next++;

        var image = appearance?.Image is { Length: > 0 } imageBytes ? PdfImage.Parse(imageBytes) : null;
        var imageNumber = image is null ? 0 : next++;
        var maskNumber = image?.SoftMask is null ? 0 : next++;

        var append = new PdfAppend(text);

        var catalog = document.Catalog;
        catalog = WithSignatureField(document, catalog, fieldNumber, append);

        var dssCertificates = new List<int>();
        var dssOcsp = new List<int>();
        var dssCrls = new List<int>();

        if (dss is not null)
        {
            catalog = WithDss(document, catalog, append, dss, dssCertificates, dssOcsp, dssCrls, ref next);
        }

        append.Add(document.RootNumber, catalog);

        if (appearance is not null)
        {
            AddAnnotation(document, pageNumber, fieldNumber, append);
        }

        append.Add(fieldNumber, FieldBody(options.FieldName, signatureNumber, appearance, formNumber, pageNumber));

        if (appearance is not null)
        {
            append.Add(formNumber, FormBody(appearance, image is null ? 0 : imageNumber));

            if (image is not null)
            {
                WriteImage(append, imageNumber, image, maskNumber);
            }
        }

        var signatureOffset = append.Add(signatureNumber, SignatureBody(options));

        if (dss is not null)
        {
            WriteDssStreams(append, dss, dssCertificates, dssOcsp, dssCrls);
        }

        var signed = Finish(append, document, next, signatureOffset, certificate, options);

        return new PdfSignatureResult(
            signed,
            Level(options),
            material?.Revocation ?? [],
            material?.Missing ?? []);
    }

    /// <summary>The level this call is configured to reach.</summary>
    private static PdfSignatureLevel Level(PdfSignatureOptions options)
        => options.Level ?? (options.RevocationSource is not null ? PdfSignatureLevel.BaselineLT
            : options.TimestampAuthority is null ? PdfSignatureLevel.BaselineB
            : PdfSignatureLevel.BaselineT);

    /// <summary>Refuses a requested level the writer cannot produce, and a request that means nothing.</summary>
    private static void ValidateLevel(PdfSignatureOptions options)
    {
        if (options.Level is PdfSignatureLevel.None)
        {
            throw new PdfSignatureException("a signature level of None asks for nothing; leave Level unset");
        }

        if (options.Level is PdfSignatureLevel.BaselineLTA)
        {
            throw new PdfSignatureException(
                "PAdES-B-LTA needs a document timestamp over the finished file: sign at BaselineLT and archive the result with PdfArchiveTimestamp.Refresh");
        }
    }

    /// <summary>
    /// Builds the store the signature will carry: the chain the caller supplied, the evidence it
    /// supplied, and the answers the revocation source returned for the certificates nothing covers.
    /// </summary>
    /// <remarks>
    /// This is the single path a store is written through, whether the evidence came from
    /// <see cref="PdfSignatureOptions.Dss"/> or from <see cref="PdfSignatureOptions.RevocationSource"/>,
    /// so a level is judged and a store is written by the same rules. The source's answers are read
    /// far enough to say they name the certificate — the writer does not authenticate them; the
    /// verifier does, and a response that does not verify is not evidence there.
    /// </remarks>
    private static PdfDssPlan? ResolveDss(PdfSignatureOptions options, X509Certificate2 signer)
    {
        var supplied = options.Dss;
        var source = options.RevocationSource;

        if (supplied is null && source is null)
        {
            return null;
        }

        var certificates = new List<X509Certificate2>();

        if (source is not null)
        {
            // A source-driven store must carry the signing certificate itself: it is the one the
            // evidence is about, and a caller that listed only the issuers would lose it.
            AddCertificate(certificates, signer);
        }

        foreach (var certificate in supplied?.Certificates ?? [])
        {
            AddCertificate(certificates, certificate);
        }

        // The caller's own bytes are embedded as they are: they are what it chose to hand over.
        var ocsp = supplied?.OcspResponses.Select(entry => entry.ToArray()).ToList() ?? [];
        var crls = supplied?.Crls.Select(entry => entry.ToArray()).ToList() ?? [];

        var revocation = new List<PdfRevocationUse>();
        var missing = new List<PdfCertificateReference>();

        foreach (var subject in certificates)
        {
            var issuer = IssuerOf(certificates, subject);
            if (issuer is null)
            {
                // An anchor is its own authority: there is no one to ask about it.
                continue;
            }

            var covered = new List<PdfRevocationUse>(2);

            if (FindOcsp(ocsp, subject, issuer) is { } ocspAnswer)
            {
                covered.Add(Use(subject, issuer, PdfRevocationKind.Ocsp, ocspAnswer.ProducedAt));
            }

            if (FindCrl(crls, subject, issuer) is { } crlAnswer)
            {
                covered.Add(Use(subject, issuer, PdfRevocationKind.Crl, crlAnswer.ThisUpdate));
            }

            if (covered.Count == 0 && source is not null)
            {
                var answer = source.GetRevocation(subject, issuer);

                if (answer.OcspResponse is { } ocspBytes
                    && PdfOcspResponse.Parse(ocspBytes.Span) is { } parsed
                    && parsed.Answers(subject, issuer))
                {
                    ocsp.Add(ocspBytes.ToArray());
                    covered.Add(Use(subject, issuer, PdfRevocationKind.Ocsp, parsed.ProducedAt));
                }

                if (answer.Crl is { } crlBytes
                    && PdfCrl.Parse(crlBytes.Span) is { } parsedCrl
                    && parsedCrl.IssuedBy(issuer))
                {
                    crls.Add(crlBytes.ToArray());
                    covered.Add(Use(subject, issuer, PdfRevocationKind.Crl, parsedCrl.ThisUpdate));
                }
            }

            if (covered.Count == 0)
            {
                missing.Add(new PdfCertificateReference(subject.Subject, subject.Issuer, Serial(subject)));
                continue;
            }

            revocation.AddRange(covered);
        }

        if (missing.Count > 0 && Level(options) == PdfSignatureLevel.BaselineLT)
        {
            var first = missing[0];
            throw new PdfRevocationException(
                first.Subject,
                first.Issuer,
                first.SerialNumber,
                $"no revocation data covers \"{first.Subject}\" (issuer \"{first.Issuer}\", serial {first.SerialNumber}): a PAdES-B-LT signature needs an OCSP answer or a CRL for every certificate of its chain, and this library never fetches one. Answer for it from the supplied IRevocationSource, or ask for PdfSignatureLevel.BaselineT to sign without its evidence.");
        }

        return new PdfDssPlan(
            new PdfDssOptions
            {
                Certificates = certificates,
                OcspResponses = ocsp.Select(entry => (ReadOnlyMemory<byte>)entry).ToList(),
                Crls = crls.Select(entry => (ReadOnlyMemory<byte>)entry).ToList(),
            },
            revocation,
            missing);
    }

    /// <summary>The store resolved from the options, and what it does and does not cover.</summary>
    private sealed record PdfDssPlan(
        PdfDssOptions Options,
        IReadOnlyList<PdfRevocationUse> Revocation,
        IReadOnlyList<PdfCertificateReference> Missing);

    private static void AddCertificate(List<X509Certificate2> certificates, X509Certificate2 certificate)
    {
        foreach (var existing in certificates)
        {
            if (existing.RawData.AsSpan().SequenceEqual(certificate.RawData))
            {
                return;
            }
        }

        certificates.Add(certificate);
    }

    /// <summary>The certificate in the chain that issued <paramref name="subject"/>, or null for an anchor.</summary>
    private static X509Certificate2? IssuerOf(List<X509Certificate2> certificates, X509Certificate2 subject)
    {
        foreach (var candidate in certificates)
        {
            if (!candidate.RawData.AsSpan().SequenceEqual(subject.RawData)
                && candidate.SubjectName.RawData.AsSpan().SequenceEqual(subject.IssuerName.RawData))
            {
                return candidate;
            }
        }

        return null;
    }

    private static PdfOcspResponse? FindOcsp(List<byte[]> responses, X509Certificate2 subject, X509Certificate2 issuer)
    {
        foreach (var der in responses)
        {
            if (PdfOcspResponse.Parse(der) is { } parsed && parsed.Answers(subject, issuer))
            {
                return parsed;
            }
        }

        return null;
    }

    private static PdfCrl? FindCrl(List<byte[]> lists, X509Certificate2 subject, X509Certificate2 issuer)
    {
        foreach (var der in lists)
        {
            if (PdfCrl.Parse(der) is { } parsed && parsed.IssuedBy(issuer))
            {
                return parsed;
            }
        }

        return null;
    }

    private static PdfRevocationUse Use(
        X509Certificate2 subject,
        X509Certificate2 issuer,
        PdfRevocationKind kind,
        DateTimeOffset issuedAt)
        => new(subject.Subject, issuer.Subject, Serial(subject), kind, issuedAt);

    /// <summary>The serial number as hexadecimal, in the DER byte order, as reports name it.</summary>
    private static string Serial(X509Certificate2 certificate)
        => Convert.ToHexString(certificate.SerialNumberBytes.Span);

    /// <summary>
    /// Computes the byte range on the built file, signs it, and fills the placeholder in.
    /// </summary>
    private static byte[] Finish(
        PdfAppend append,
        PdfDocument document,
        int size,
        int signatureOffset,
        X509Certificate2 certificate,
        PdfSignatureOptions options)
        => PdfContents.Seal(
            append.Finish(document, size),
            signatureOffset,
            signed => ComputeSignature(signed, certificate, options));

    /// <summary>Fails early when a timestamp is asked for without the means to reach the authority.</summary>
    private static void ValidateTimestamp(PdfSignatureOptions options)
    {
        if (options.TimestampAuthority is not null && options.TimestampClient is null)
        {
            throw new PdfSignatureException(
                "a timestamp authority was named without an HttpClient; the library never opens a connection of its own");
        }

        if (options.TimestampAuthority is null && options.TimestampClient is not null)
        {
            throw new PdfSignatureException(
                "an HttpClient was supplied for timestamps but no authority was named");
        }
    }

    private static byte[] ComputeSignature(byte[] content, X509Certificate2 certificate, PdfSignatureOptions options)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1", "SHA-256"),
        };

        cms.ComputeSignature(signer);

        if (options is { TimestampAuthority: { } authority, TimestampClient: { } client })
        {
            var token = PdfTimestamp.Request(
                authority,
                client,
                PdfTimestamp.BuildRequest(SHA256.HashData(cms.SignerInfos[0].GetSignature())));

            if (!PdfTimestamp.Parse(token).Matches(cms.SignerInfos[0].GetSignature()))
            {
                throw new PdfSignatureException(
                    "the timestamp authority returned a token whose imprint is not this signature; the signature was not written");
            }

            cms.SignerInfos[0].AddUnsignedAttribute(
                new AsnEncodedData(new Oid(PdfTimestamp.SignatureTimestampOid), token));
        }

        return cms.Encode();
    }

    private static string SignatureBody(PdfSignatureOptions options)
    {
        var builder = new StringBuilder("<</Type/Sig/Filter/Adobe.PPKLite/SubFilter/ETSI.CAdES.detached")
            .Append(PdfContents.ByteRangePlaceholder)
            .Append(PdfContents.EmptyContents());

        if (options.Reason is { Length: > 0 } reason)
        {
            builder.Append("/Reason(").Append(EscapeString(reason)).Append(')');
        }

        if (options.Location is { Length: > 0 } location)
        {
            builder.Append("/Location(").Append(EscapeString(location)).Append(')');
        }

        builder.Append("/M(").Append(PdfDate(options.SignedAt ?? DateTimeOffset.UtcNow)).Append(')');
        return builder.Append('>').ToString();
    }

    private static string FieldBody(
        string fieldName,
        int signatureNumber,
        PdfSignatureAppearance? appearance,
        int formNumber,
        int pageNumber)
    {
        var builder = new StringBuilder("<</Type/Annot/Subtype/Widget/FT/Sig/T(")
            .Append(EscapeName(fieldName))
            .Append(")/V ").Append(signatureNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");

        if (appearance is null)
        {
            builder.Append("/Rect[0 0 0 0]");
        }
        else
        {
            builder.Append("/Rect[")
                   .Append(PdfSyntax.Number(appearance.X)).Append(' ')
                   .Append(PdfSyntax.Number(appearance.Y)).Append(' ')
                   .Append(PdfSyntax.Number(appearance.X + appearance.Width)).Append(' ')
                   .Append(PdfSyntax.Number(appearance.Y + appearance.Height)).Append(']')
                   .Append("/P ").Append(pageNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
                   .Append("/AP<</N ").Append(formNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R>>");
        }

        return builder.Append("/F 132>>").ToString();
    }

    private static string FormBody(PdfSignatureAppearance appearance, int imageNumber)
    {
        var width = PdfSyntax.Number(appearance.Width);
        var height = PdfSyntax.Number(appearance.Height);

        string content;
        string resources;

        if (imageNumber == 0)
        {
            content = TextContent(appearance);
            resources = "<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";
        }
        else
        {
            content = string.Create(
                CultureInfo.InvariantCulture,
                $"q\n{width} 0 0 {height} 0 0 cm\n/Im0 Do\nQ");
            resources = "<</XObject<</Im0 " + imageNumber.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>";
        }

        var length = Encoding.Latin1.GetByteCount(content);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"<</Type/XObject/Subtype/Form/FormType 1/BBox[0 0 {width} {height}]/Resources{resources}/Length {length}>>\nstream\n{content}\nendstream");
    }

    /// <summary>The text of an appearance, drawn with Helvetica and WinAnsi.</summary>
    private static string TextContent(PdfSignatureAppearance appearance)
    {
        var leading = appearance.FontSize * 1.2;
        var top = appearance.Height - appearance.Padding - appearance.FontSize;

        var builder = new StringBuilder("BT\n/F1 ")
            .Append(PdfSyntax.Number(appearance.FontSize)).Append(" Tf\n")
            .Append(PdfSyntax.Number(leading)).Append(" TL\n")
            .Append(PdfSyntax.Number(appearance.Padding)).Append(' ')
            .Append(PdfSyntax.Number(Math.Max(top, appearance.Padding))).Append(" Td\n");

        var lines = appearance.TextLines ?? [];

        for (var index = 0; index < lines.Count; index++)
        {
            builder.Append('(').Append(EscapeLatin1(lines[index])).Append(") Tj\n");

            if (index < lines.Count - 1)
            {
                builder.Append("T*\n");
            }
        }

        return builder.Append("ET").ToString();
    }

    private static void WriteImage(PdfAppend append, int imageNumber, PdfImageInfo image, int maskNumber)
    {
        var mask = image.SoftMask is not null
            ? "/SMask " + maskNumber.ToString(CultureInfo.InvariantCulture) + " 0 R"
            : string.Empty;

        append.Add(
            imageNumber,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<</Type/XObject/Subtype/Image/Width {image.Width}/Height {image.Height}/ColorSpace/{image.ColorSpace}/BitsPerComponent 8/Filter/{image.Filter}{mask}/Length {image.Data.Length}>>\nstream\n")
            + Encoding.Latin1.GetString(image.Data.Span)
            + "\nendstream");

        if (image.SoftMask is { } alpha)
        {
            append.Add(
                maskNumber,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"<</Type/XObject/Subtype/Image/Width {alpha.Width}/Height {alpha.Height}/ColorSpace/{alpha.ColorSpace}/BitsPerComponent 8/Filter/{alpha.Filter}/Length {alpha.Data.Length}>>\nstream\n")
                + Encoding.Latin1.GetString(alpha.Data.Span)
                + "\nendstream");
        }
    }

    /// <summary>Adds the signature field to the catalog's <c>/AcroForm</c>, keeping the fields it already has.</summary>
    private static string WithSignatureField(PdfDocument document, string catalog, int fieldNumber, PdfAppend append)
    {
        if (!PdfSyntax.Has(catalog, "AcroForm"))
        {
            return InsertIntoDictionary(catalog, "/AcroForm<</Fields[" + Reference(fieldNumber) + "]/SigFlags 3>>");
        }

        if (PdfSyntax.TryReference(catalog, "AcroForm", out var formNumber))
        {
            append.Add(formNumber, AddField(document.Require(formNumber).Body, fieldNumber));
            return catalog;
        }

        var open = DictionaryStart(catalog, "AcroForm");
        var close = PdfSyntax.CloseDictionary(catalog, open);
        if (close < 0)
        {
            throw new PdfSignatureException("the catalog's /AcroForm is not a dictionary, so no field can be added to it");
        }

        return string.Concat(
            catalog.AsSpan(0, open),
            AddField(catalog.Substring(open, close + 2 - open), fieldNumber),
            catalog.AsSpan(close + 2));
    }

    private static string AddField(string form, int fieldNumber)
    {
        var fields = PdfSyntax.ArrayBody(form, "Fields");
        var withField = fields is null
            ? InsertIntoDictionary(form, "/Fields[" + Reference(fieldNumber) + "]")
            : Regex.Replace(
                form,
                @"(/Fields\s*\[)([^\]]*)\]",
                match => match.Groups[1].Value
                    + match.Groups[2].Value.Trim()
                    + (match.Groups[2].Value.Trim().Length > 0 ? " " : string.Empty)
                    + Reference(fieldNumber) + "]",
                RegexOptions.CultureInvariant);

        return PdfSyntax.Has(withField, "SigFlags")
            ? withField
            : InsertIntoDictionary(withField, "/SigFlags 3");
    }

    /// <summary>
    /// Adds a document security store to the catalog, merging the one a previous revision wrote.
    /// </summary>
    private static string WithDss(
        PdfDocument document,
        string catalog,
        PdfAppend append,
        PdfDssOptions dss,
        List<int> certificates,
        List<int> ocsp,
        List<int> crls,
        ref int next)
    {
        foreach (var _ in dss.Certificates)
        {
            certificates.Add(next++);
        }

        foreach (var _ in dss.OcspResponses)
        {
            ocsp.Add(next++);
        }

        foreach (var _ in dss.Crls)
        {
            crls.Add(next++);
        }

        var existing = ExistingDss(document, catalog);
        var target = existing?.Number ?? next++;

        append.Add(target, MergeDss(existing?.Body, certificates, ocsp, crls));

        return PdfSyntax.Has(catalog, "DSS")
            ? catalog
            : InsertIntoDictionary(catalog, "/DSS " + Reference(target));
    }

    private static (int Number, string Body)? ExistingDss(PdfDocument document, string catalog)
    {
        if (!PdfSyntax.Has(catalog, "DSS"))
        {
            return null;
        }

        if (PdfSyntax.TryReference(catalog, "DSS", out var number))
        {
            return (number, document.Require(number).Body);
        }

        throw new PdfSignatureException(
            "the catalog carries a direct /DSS dictionary; only an indirect store can be extended");
    }

    private static string MergeDss(string? existing, List<int> certificates, List<int> ocsp, List<int> crls)
    {
        var certs = Refs(existing, "Certs").Concat(certificates.Select(Reference)).ToList();
        var ocsps = Refs(existing, "OCSPs").Concat(ocsp.Select(Reference)).ToList();
        var crlRefs = Refs(existing, "CRLs").Concat(crls.Select(Reference)).ToList();

        var builder = new StringBuilder("<</Certs[").Append(string.Join(" ", certs)).Append(']');

        if (ocsps.Count > 0)
        {
            builder.Append("/OCSPs[").Append(string.Join(" ", ocsps)).Append(']');
        }

        if (crlRefs.Count > 0)
        {
            builder.Append("/CRLs[").Append(string.Join(" ", crlRefs)).Append(']');
        }

        return builder.Append('>').ToString();
    }

    private static IEnumerable<string> Refs(string? dss, string key)
    {
        if (dss is null || PdfSyntax.ArrayBody(dss, key) is not { } body)
        {
            return [];
        }

        return Regex.Matches(body, @"\d+\s+0\s+R", RegexOptions.CultureInvariant)
            .Select(match => match.Value.Trim());
    }

    private static void WriteDssStreams(
        PdfAppend append,
        PdfDssOptions dss,
        List<int> certificates,
        List<int> ocsp,
        List<int> crls)
    {
        var index = 0;
        foreach (var certificate in dss.Certificates)
        {
            append.Add(certificates[index++], StreamBody(certificate.RawData));
        }

        index = 0;
        foreach (var response in dss.OcspResponses)
        {
            append.Add(ocsp[index++], StreamBody(response.Span));
        }

        index = 0;
        foreach (var crl in dss.Crls)
        {
            append.Add(crls[index++], StreamBody(crl.Span));
        }
    }

    private static string StreamBody(ReadOnlySpan<byte> data)
        => string.Create(CultureInfo.InvariantCulture, $"<</Length {data.Length}>>\nstream\n")
            + Encoding.Latin1.GetString(data)
            + "\nendstream";

    /// <summary>Rewrites the page so its <c>/Annots</c> carries the widget, keeping the annotations it has.</summary>
    private static void AddAnnotation(PdfDocument document, int pageNumber, int fieldNumber, PdfAppend append)
    {
        var body = document.Require(pageNumber).Body;

        if (PdfSyntax.ArrayBody(body, "Annots") is { } annots)
        {
            var existing = annots.Trim();
            var updated = Regex.Replace(
                body,
                @"(/Annots\s*\[)([^\]]*)\]",
                match => match.Groups[1].Value + existing
                    + (existing.Length > 0 ? " " : string.Empty)
                    + Reference(fieldNumber) + "]",
                RegexOptions.CultureInvariant);

            append.Add(pageNumber, updated);
            return;
        }

        append.Add(pageNumber, InsertIntoDictionary(body, "/Annots[" + Reference(fieldNumber) + "]"));
    }

    /// <summary>Inserts a value just before the closing <c>&gt;&gt;</c> of a dictionary.</summary>
    private static string InsertIntoDictionary(string body, string value)
    {
        var close = PdfSyntax.CloseDictionary(body, body.IndexOf("<<", StringComparison.Ordinal));
        if (close < 0)
        {
            throw new PdfSignatureException("the PDF object is not a dictionary");
        }

        return string.Concat(body.AsSpan(0, close), value, body.AsSpan(close));
    }

    private static int DictionaryStart(string body, string key)
    {
        var match = Regex.Match(
            body,
            "/" + Regex.Escape(key) + @"\s*<<",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        return match.Success ? match.Index + match.Length - 2 : body.IndexOf("<<", StringComparison.Ordinal);
    }

    private static string Reference(int number) => number.ToString(CultureInfo.InvariantCulture) + " 0 R";

    private static string PdfDate(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();

        return "D:" + utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";
    }

    private static string EscapeName(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static string EscapeString(string value) => EscapeName(value);

    /// <summary>Escapes a line of text for a WinAnsi literal string.</summary>
    private static string EscapeLatin1(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is < ' ' or (> '\u007E' and < '\u00A0') or > '\u00FF')
            {
                builder.Append('?');
            }
            else if (character is '(' or ')' or '\\')
            {
                builder.Append('\\').Append(character);
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
