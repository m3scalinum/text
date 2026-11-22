using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

#pragma warning disable CA5350 // A reference the list was signed with may name SHA-1; the algorithm is read, not chosen.

namespace M3scalinum.Text.Pdf;

/// <summary>What the verifier established about a trust list's own signature.</summary>
/// <remarks>
/// A list is XML signed with XML-DSig, usually inside an XAdES envelope. The status answers one
/// question — did the bytes signed match the bytes the signature covers — and says nothing about
/// whether the signer should be believed: that is <see cref="PdfTrustListSignature.SignerChain"/>,
/// judged against the anchors the caller supplies.
/// </remarks>
public enum PdfTrustListSignatureStatus
{
    /// <summary>The list carries no <c>ds:Signature</c> element at all.</summary>
    NoSignature = 0,

    /// <summary>Every reference digest and the signature value over <c>SignedInfo</c> verified.</summary>
    Valid = 1,

    /// <summary>
    /// A signature is present and does not hold: a reference digest does not match, the signature
    /// value does not verify, or the structure uses an algorithm or transform this reader does not
    /// implement.
    /// </summary>
    Invalid = 2,
}

/// <summary>
/// What a trust list's signature proves: the signer, the references it covers, the moment it was
/// signed, and whether its chain reached the anchors the caller supplied.
/// </summary>
/// <remarks>
/// <para>
/// The verification is XML-DSig over the document as the list carries it: each
/// <c>ds:Reference</c> is resolved (the whole document, or an element by <c>Id</c>), its transforms
/// are applied — the enveloped-signature transform and canonicalisation — and the digest is compared.
/// The signature value is then checked over the canonical form of <c>ds:SignedInfo</c>. One byte
/// changed anywhere the references cover makes them mismatch, which is the point.
/// </para>
/// <para>
/// <b>What it does not do.</b> It does not validate the XAdES properties beyond digesting the ones a
/// reference names (<c>SigningTime</c> is read and reported, never checked against a timestamp
/// token); it does not check revocation of the signer; it does not check the list's sequence number;
/// and it implements the two canonicalisation algorithms XML-DSig deployments actually use, so a list
/// signed under any other transform is refused rather than guessed at. A refusal is never evidence
/// of forgery — it says this reader could not check those bytes.
/// </para>
/// </remarks>
public sealed class PdfTrustListSignature
{
    internal PdfTrustListSignature(
        PdfTrustListSignatureStatus status,
        string? signerSubject,
        string? signerIssuer,
        string? signerSerialNumber,
        DateTimeOffset? signedAt,
        bool coversDocument,
        IReadOnlyList<string> references,
        PdfChainStatus signerChain,
        string? error)
    {
        Status = status;
        SignerSubject = signerSubject;
        SignerIssuer = signerIssuer;
        SignerSerialNumber = signerSerialNumber;
        SignedAt = signedAt;
        CoversDocument = coversDocument;
        References = references;
        SignerChain = signerChain;
        Error = error;
    }

    /// <summary>A verdict for a list that carries no signature.</summary>
    internal static PdfTrustListSignature None(string reason) => new(
        PdfTrustListSignatureStatus.NoSignature, null, null, null, null, false, [], PdfChainStatus.NotEvaluated, reason);

    /// <summary>A verdict for a signature that does not hold.</summary>
    internal static PdfTrustListSignature Refused(string? subject, string? issuer, string? serial, string? reason) => new(
        PdfTrustListSignatureStatus.Invalid,
        subject,
        issuer,
        serial,
        null,
        false,
        [],
        PdfChainStatus.NotEvaluated,
        reason ?? "the signature does not hold");

    /// <summary>What the signature's own bytes proved.</summary>
    public PdfTrustListSignatureStatus Status { get; }

    /// <summary>The subject of the signing certificate, when the signature carries one.</summary>
    public string? SignerSubject { get; }

    /// <summary>The issuer of the signing certificate, when the signature carries one.</summary>
    public string? SignerIssuer { get; }

    /// <summary>The signing certificate's serial number, in uppercase hexadecimal, when it is known.</summary>
    public string? SignerSerialNumber { get; }

    /// <summary>
    /// The moment the list says it signed itself, from <c>xades:SigningTime</c>. Read, never checked:
    /// the value is inside the signed bytes, so it is as trustworthy as the signature — and no more.
    /// </summary>
    public DateTimeOffset? SignedAt { get; }

    /// <summary>
    /// Whether one of the verified references covers the whole document. A signature over a detached
    /// element alone would leave the services of the list outside what it protects.
    /// </summary>
    public bool CoversDocument { get; }

    /// <summary>The <c>URI</c> of every reference whose digest matched, in document order.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Whether the signer's chain reached the anchors the caller supplied.</summary>
    public PdfChainStatus SignerChain { get; }

    /// <summary>Why the signature does not hold, in one sentence naming the element that failed; null when it does.</summary>
    public string? Error { get; }

    /// <summary>
    /// Whether the list's bytes are proven to be the ones this signer signed, by a signer whose chain
    /// reached a supplied anchor. Only this makes a qualified service a checked fact rather than the
    /// list's claim.
    /// </summary>
    public bool IsVerified => Status == PdfTrustListSignatureStatus.Valid
        && CoversDocument
        && SignerChain == PdfChainStatus.Trusted;
}

/// <summary>What a qualification verdict rests on.</summary>
/// <remarks>
/// The three states the caller has to keep apart: a list whose signature was checked and is current,
/// a checked list whose <c>NextUpdate</c> has passed, and a list nothing checked. A fourth says the
/// caller never asked for a check — the qualification is then the list's statement, not a fact.
/// </remarks>
public enum PdfQualificationBasis
{
    /// <summary>The list was read without anchors, so no signature verdict exists. The coverage is the list's claim.</summary>
    NotChecked = 0,

    /// <summary>A verdict was attached and the list's bytes are not proven: nothing may be called qualified.</summary>
    NotVerified = 1,

    /// <summary>The signature verified against a supplied anchor and the list is inside its <c>NextUpdate</c>.</summary>
    Verified = 2,

    /// <summary>The signature verified against a supplied anchor, and the list is past its <c>NextUpdate</c>.</summary>
    VerifiedExpired = 3,
}

/// <summary>
/// XML-DSig over a trust list: canonicalisation, reference digests, and the signature value.
/// </summary>
/// <remarks>
/// The implementation is deliberately narrow. It reads an enveloped signature — <c>ds:SignedInfo</c>
/// next to a <c>ds:SignatureValue</c>, references to the document or to an element by <c>Id</c>,
/// the enveloped-signature transform, and canonical XML 1.0 or exclusive C14N without comments — and
/// refuses anything else by name. Canonicalisation is UTF-8, unformatted, attribute-sorted and
/// namespace-declaration-complete, as Canonical XML 1.0 requires; exclusive C14N emits only the
/// namespaces an element and its attributes visibly use.
/// </remarks>
internal static class PdfXmlSignature
{
    /// <summary>The XML signature namespace.</summary>
    public const string Namespace = "http://www.w3.org/2000/09/xmldsig#";

    private const string InclusiveC14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    private const string InclusiveC14NComments = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315#WithComments";
    private const string ExclusiveC14N = "http://www.w3.org/2001/10/xml-exc-c14n#";
    private const string ExclusiveC14NComments = "http://www.w3.org/2001/10/xml-exc-c14n#WithComments";
    private const string Enveloped = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";

    /// <summary>
    /// Checks the list's own signature. Never fetches: the anchors are the caller's, and the signer's
    /// chain is built with downloads disabled.
    /// </summary>
    public static PdfTrustListSignature Verify(byte[] xml, PdfTrustStore? anchors)
    {
        XDocument document;

        try
        {
            using var stream = new MemoryStream(xml, writable: false);
            using var reader = XmlReader.Create(stream, PdfLotl.SignatureSettings());
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            return PdfTrustListSignature.None($"the list is not well-formed XML: {exception.Message}");
        }

        var root = document.Root;

        if (root is null)
        {
            return PdfTrustListSignature.None("the list has no root element");
        }

        var signature = root.Descendants().FirstOrDefault(element => IsSignature(element));

        if (signature is null)
        {
            return PdfTrustListSignature.None("the list carries no ds:Signature element");
        }

        var signer = ReadCertificate(signature);

        var signedInfo = Child(signature, "SignedInfo");

        if (signedInfo is null)
        {
            return PdfTrustListSignature.Refused(
                signer?.Subject, signer?.Issuer, signer?.SerialNumber, "the signature carries no SignedInfo");
        }

        var canonicalization = Algorithm(Child(signedInfo, "CanonicalizationMethod"));

        if (!TryCanonicalize(signedInfo, canonicalization, out var signedInfoBytes, out var canonicalizationError))
        {
            return PdfTrustListSignature.Refused(
                signer?.Subject, signer?.Issuer, signer?.SerialNumber, canonicalizationError);
        }

        var signatureValue = Base64(Child(signature, "SignatureValue"));

        if (signer is null)
        {
            return PdfTrustListSignature.Refused(
                null, null, null, "the signature carries no signing certificate in its KeyInfo");
        }

        var method = Algorithm(Child(signedInfo, "SignatureMethod"));
        var valueError = VerifyValue(signer, signedInfoBytes, signatureValue, method);

        if (valueError is not null)
        {
            return PdfTrustListSignature.Refused(signer.Subject, signer.Issuer, signer.SerialNumber, valueError);
        }

        var references = new List<string>();
        var coversDocument = false;

        foreach (var reference in Children(signedInfo, "Reference"))
        {
            var uri = reference.Attribute("URI")?.Value;
            var label = uri is null or "" ? "the document" : $"'{uri}'";

            if (!TryReference(document, root, signature, reference, out var digestBytes, out var covers, out var referenceError))
            {
                return PdfTrustListSignature.Refused(signer.Subject, signer.Issuer, signer.SerialNumber, referenceError);
            }

            var expected = Base64(Child(reference, "DigestValue"));
            var algorithm = Algorithm(Child(reference, "DigestMethod"));
            var actual = Digest(algorithm, digestBytes);

            if (actual is null)
            {
                return PdfTrustListSignature.Refused(
                    signer.Subject, signer.Issuer, signer.SerialNumber,
                    $"the reference to {label} uses a digest algorithm this reader does not implement: {algorithm}");
            }

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PdfTrustListSignature.Refused(
                    signer.Subject, signer.Issuer, signer.SerialNumber,
                    $"the digest of the reference to {label} does not match: the bytes it covers were changed after signing");
            }

            references.Add(uri ?? string.Empty);
            coversDocument |= covers;
        }

        if (references.Count == 0)
        {
            return PdfTrustListSignature.Refused(
                signer.Subject, signer.Issuer, signer.SerialNumber,
                "the signature carries no reference, so it covers nothing of the list");
        }

        var chain = Judge(signer, anchors, ReadSignedAt(signature));

        return new PdfTrustListSignature(
            PdfTrustListSignatureStatus.Valid,
            signer.Subject,
            signer.Issuer,
            signer.SerialNumber,
            ReadSignedAt(signature),
            coversDocument,
            references,
            chain,
            null);
    }

    /// <summary>
    /// The canonical form of an element, as Canonical XML 1.0 or exclusive C14N produces it: UTF-8,
    /// no declaration, attributes sorted, namespaces rendered where they change.
    /// </summary>
    public static byte[] Canonicalize(XElement element, bool exclusive)
    {
        var output = new StringBuilder();
        Write(element, exclusive, [], output);
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    /// <summary>Whether the element is a <c>ds:Signature</c>.</summary>
    public static bool IsSignature(XElement element)
        => element.Name.LocalName == "Signature" && element.Name.NamespaceName == Namespace;

    private static void Write(
        XElement element,
        bool exclusive,
        Dictionary<string, string> rendered,
        StringBuilder output)
    {
        var scope = Scope(element);
        var current = new Dictionary<string, string>(rendered, StringComparer.Ordinal);
        var name = Qualified(element.GetPrefixOfNamespace(element.Name.Namespace) ?? string.Empty, element.Name);

        output.Append('<').Append(name);

        foreach (var (prefix, value) in Declarations(element, scope, rendered, exclusive))
        {
            output.Append(' ');

            if (prefix.Length == 0)
            {
                output.Append("xmlns=\"").Append(EscapeAttribute(value)).Append('"');
            }
            else
            {
                output.Append("xmlns:").Append(prefix).Append("=\"").Append(EscapeAttribute(value)).Append('"');
            }

            current[prefix] = value;
        }

        foreach (var attribute in Attributes(element))
        {
            output.Append(' ').Append(Qualified(prefix: element.GetPrefixOfNamespace(attribute.Name.Namespace) ?? string.Empty, attribute.Name)).Append("=\"");
            AppendAttribute(output, attribute.Value);
            output.Append('"');
        }

        output.Append('>');

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    Write(child, exclusive, current, output);
                    break;
                case XText text:
                    AppendText(output, text.Value);
                    break;
                case XProcessingInstruction instruction:
                    output.Append("<?").Append(instruction.Target);
                    if (instruction.Data.Length > 0)
                    {
                        output.Append(' ').Append(instruction.Data);
                    }

                    output.Append("?>");
                    break;
                default:
                    // Comments are outside the without-comments canonical forms, and nothing else
                    // can appear in a parsed element.
                    break;
            }
        }

        output.Append("</").Append(name).Append('>');
    }

    /// <summary>The namespace declarations the element must render, in canonical order.</summary>
    private static IEnumerable<(string Prefix, string Value)> Declarations(
        XElement element,
        Dictionary<string, string> scope,
        Dictionary<string, string> rendered,
        bool exclusive)
    {
        var prefixes = exclusive
            ? UsedPrefixes(element)
            : scope.Keys.ToList();

        foreach (var prefix in prefixes.OrderBy(value => value, StringComparer.Ordinal))
        {
            var value = scope.TryGetValue(prefix, out var found) ? found : string.Empty;

            if (prefix.Length == 0 && value.Length == 0 && !rendered.ContainsKey(string.Empty))
            {
                continue;
            }

            if (!rendered.TryGetValue(prefix, out var already) || already != value)
            {
                yield return (prefix, value);
            }
        }
    }

    /// <summary>The prefixes an element visibly uses: its own, plus those of its namespaced attributes.</summary>
    private static List<string> UsedPrefixes(XElement element)
    {
        var prefixes = new List<string>();

        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace) ?? string.Empty;

        if (element.Name.NamespaceName.Length > 0 || prefix.Length == 0)
        {
            prefixes.Add(prefix);
        }

        foreach (var attribute in Attributes(element))
        {
            var attributePrefix = element.GetPrefixOfNamespace(attribute.Name.Namespace);

            if (attribute.Name.NamespaceName.Length > 0
                && attributePrefix is not null
                && !prefixes.Contains(attributePrefix))
            {
                prefixes.Add(attributePrefix);
            }
        }

        return prefixes;
    }

    /// <summary>Every namespace in scope on the element, inherited ones included, by prefix.</summary>
    /// <remarks>
    /// A parsed document carries a declaration for each namespace it uses, but a tree built in
    /// memory does not: LINQ to XML only materialises the attribute when it serialises. The
    /// element's own name therefore supplies its prefix's namespace too, and an explicit
    /// declaration — including <c>xmlns=""</c> — wins over it.
    /// </remarks>
    private static Dictionary<string, string> Scope(XElement element)
    {
        var scope = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var scopeElement in element.AncestorsAndSelf().Reverse())
        {
            var prefix = scopeElement.GetPrefixOfNamespace(scopeElement.Name.Namespace) ?? string.Empty;

            if (scopeElement.Name.NamespaceName.Length > 0 && !scope.ContainsKey(prefix))
            {
                scope[prefix] = scopeElement.Name.NamespaceName;
            }

            foreach (var attribute in scopeElement.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            {
                var declared = attribute.Name.Namespace == XNamespace.Xmlns
                    ? attribute.Name.LocalName
                    : attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName;

                scope[declared] = attribute.Value;
            }
        }

        return scope;
    }

    private static IEnumerable<XAttribute> Attributes(XElement element)
        => element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal);

    private static string Qualified(string prefix, XName name)
        => name.NamespaceName.Length == 0 || prefix.Length == 0
            ? name.LocalName
            : prefix + ":" + name.LocalName;

    private static void AppendText(StringBuilder output, string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&': output.Append("&amp;"); break;
                case '<': output.Append("&lt;"); break;
                case '>': output.Append("&gt;"); break;
                case '\r': output.Append("&#xD;"); break;
                default: output.Append(character); break;
            }
        }
    }

    private static void AppendAttribute(StringBuilder output, string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&': output.Append("&amp;"); break;
                case '<': output.Append("&lt;"); break;
                case '>': output.Append("&gt;"); break;
                case '"': output.Append("&quot;"); break;
                case '\t': output.Append("&#x9;"); break;
                case '\n': output.Append("&#xA;"); break;
                case '\r': output.Append("&#xD;"); break;
                default: output.Append(character); break;
            }
        }
    }

    private static string EscapeAttribute(string value)
    {
        var output = new StringBuilder(value.Length);
        AppendAttribute(output, value);
        return output.ToString();
    }

    private static bool TryCanonicalize(
        XElement element,
        string algorithm,
        out byte[] bytes,
        out string? error)
    {
        bytes = [];
        error = null;

        if (algorithm == InclusiveC14N)
        {
            bytes = Canonicalize(element, exclusive: false);
            return true;
        }

        if (algorithm == ExclusiveC14N)
        {
            bytes = Canonicalize(element, exclusive: true);
            return true;
        }

        error = algorithm is InclusiveC14NComments or ExclusiveC14NComments
            ? $"the signature canonicalises with comments ({algorithm}), which this reader does not implement"
            : $"the signature canonicalises with {algorithm}, which this reader does not implement";

        return false;
    }

    /// <summary>Resolves one reference, applies its transforms and returns the bytes to digest.</summary>
    private static bool TryReference(
        XDocument document,
        XElement root,
        XElement signature,
        XElement reference,
        out byte[] bytes,
        out bool coversDocument,
        out string? error)
    {
        bytes = [];
        error = null;
        coversDocument = false;

        var uri = reference.Attribute("URI")?.Value;
        var exclusive = false;
        var enveloped = false;

        foreach (var transform in Children(reference, "Transforms"))
        {
            foreach (var step in Children(transform, "Transform"))
            {
                var algorithm = Algorithm(step);

                if (algorithm == Enveloped)
                {
                    enveloped = true;
                }
                else if (algorithm == InclusiveC14N)
                {
                    exclusive = false;
                }
                else if (algorithm == ExclusiveC14N)
                {
                    exclusive = true;
                }
                else
                {
                    error = $"the reference to {(uri is null or "" ? "the document" : uri)} uses the transform"
                            + $" {algorithm}, which this reader does not implement";
                    return false;
                }
            }
        }

        if (uri is null or "")
        {
            coversDocument = true;

            if (!enveloped)
            {
                error = "the reference to the document carries no enveloped-signature transform, so the"
                        + " signature element would have to be inside its own digest";
                return false;
            }

            var copy = new XElement(root);

            foreach (var element in copy.Descendants().Where(IsSignature).ToList())
            {
                element.Remove();
            }

            bytes = Canonicalize(copy, exclusive);
            return true;
        }

        if (!uri.StartsWith('#'))
        {
            error = $"the reference '{uri}' points outside the document, which this reader does not fetch or resolve";
            return false;
        }

        var identifier = uri[1..];
        var target = document.Descendants().FirstOrDefault(element => HasIdentifier(element, identifier));

        if (target is null)
        {
            error = $"the reference '{uri}' names an element the document does not carry";
            return false;
        }

        bytes = Canonicalize(target, exclusive);
        return true;
    }

    private static bool HasIdentifier(XElement element, string identifier)
    {
        foreach (var attribute in element.Attributes())
        {
            if ((attribute.Name.LocalName == "Id" || attribute.Name.LocalName == "ID"
                 || attribute.Name.LocalName == "id")
                && attribute.Value == identifier)
            {
                return true;
            }
        }

        return false;
    }

    private static string? VerifyValue(X509Certificate2 signer, byte[] data, byte[] signature, string method)
    {
        var (hash, isEcdsa) = method switch
        {
            "http://www.w3.org/2000/09/xmldsig#rsa-sha1" => (HashAlgorithmName.SHA1, false),
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256" => (HashAlgorithmName.SHA256, false),
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384" => (HashAlgorithmName.SHA384, false),
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512" => (HashAlgorithmName.SHA512, false),
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256" => (HashAlgorithmName.SHA256, true),
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384" => (HashAlgorithmName.SHA384, true),
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512" => (HashAlgorithmName.SHA512, true),
            _ => (default, false),
        };

        if (hash == default)
        {
            return $"the signature method {method} is outside the set this reader implements";
        }

        if (signature.Length == 0)
        {
            return "the signature carries no SignatureValue";
        }

        try
        {
            if (isEcdsa)
            {
                using var ecdsa = signer.GetECDsaPublicKey();
                return ecdsa is not null && ecdsa.VerifyData(data, signature, hash, DSASignatureFormat.Rfc3279DerSequence)
                    ? null
                    : "the signature value does not verify over SignedInfo with the signing certificate's key";
            }

            using var rsa = signer.GetRSAPublicKey();
            return rsa is not null && rsa.VerifyData(data, signature, hash, RSASignaturePadding.Pkcs1)
                ? null
                : "the signature value does not verify over SignedInfo with the signing certificate's key";
        }
        catch (CryptographicException exception)
        {
            return $"the signature value could not be checked: {exception.Message}";
        }
    }

    private static byte[]? Digest(string algorithm, byte[] data) => algorithm switch
    {
        "http://www.w3.org/2000/09/xmldsig#sha1" => SHA1.HashData(data),
        "http://www.w3.org/2001/04/xmlenc#sha256" => SHA256.HashData(data),
        "http://www.w3.org/2001/04/xmlenc#sha512" => SHA512.HashData(data),
        "http://www.w3.org/2001/04/xmldsig-more#sha384" => SHA384.HashData(data),
        _ => null,
    };

    private static X509Certificate2? ReadCertificate(XElement signature)
    {
        var encoded = signature.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "X509Certificate");

        if (encoded is null)
        {
            return null;
        }

        try
        {
            return PdfAsn.LoadCertificate(Convert.FromBase64String(encoded.Value.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ReadSignedAt(XElement signature)
    {
        var time = signature.Descendants().FirstOrDefault(element => element.Name.LocalName == "SigningTime");

        return time is not null && DateTimeOffset.TryParse(
            time.Value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static PdfChainStatus Judge(X509Certificate2 signer, PdfTrustStore? anchors, DateTimeOffset? at)
    {
        if (anchors is null || anchors.Anchors.Count == 0)
        {
            return PdfChainStatus.NotEvaluated;
        }

        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;

            if (at is { } moment)
            {
                chain.ChainPolicy.VerificationTime = moment.UtcDateTime;
            }

            foreach (var anchor in anchors.Anchors)
            {
                chain.ChainPolicy.CustomTrustStore.Add(anchor);
            }

            foreach (var intermediate in anchors.Intermediates)
            {
                chain.ChainPolicy.ExtraStore.Add(intermediate);
            }

            return chain.Build(signer) ? PdfChainStatus.Trusted : PdfChainStatus.Untrusted;
        }
        catch (CryptographicException)
        {
            return PdfChainStatus.Untrusted;
        }
    }

    private static byte[] Base64(XElement? element)
    {
        var text = element?.Value.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string Algorithm(XElement? element)
        => element?.Attribute("Algorithm")?.Value ?? string.Empty;

    private static XElement? Child(XElement parent, string name)
        => parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement parent, string name)
        => parent.Elements().Where(element => element.Name.LocalName == name);
}
