using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>Why a trust list was refused.</summary>
public enum PdfTrustListError
{
    /// <summary>The bytes are not an ETSI TS 119 612 trust list this reader can read.</summary>
    Malformed = 0,

    /// <summary>The document declares a DTD or an entity: a trust list never does, and the parser refuses it.</summary>
    XmlPolicy = 1,

    /// <summary>The document passes a bound: bytes, nesting, elements, services, pointers or certificates.</summary>
    Limit = 2,
}

/// <summary>A trust list could not be read.</summary>
/// <remarks>
/// The error class says whether the caller is looking at a broken document, a hostile one, or one
/// this reader will not accept. <see cref="PdfTrustListError.XmlPolicy"/> is never reported for a
/// list a software producer wrote in good faith: an ETSI list has no document type declaration.
/// </remarks>
public sealed class PdfTrustListException : PdfSignatureException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="error">Which class of refusal this is.</param>
    /// <param name="message">What was refused, naming the rule and the value that passed it.</param>
    public PdfTrustListException(PdfTrustListError error, string message)
        : base(message)
        => Error = error;

    /// <summary>Which class of refusal this is.</summary>
    public PdfTrustListError Error { get; }
}

/// <summary>
/// Reads a European trust list — the LOTL of ETSI TS 119 612 and the national lists it points at —
/// into <see cref="PdfTrustedList"/>.
/// </summary>
/// <remarks>
/// <para>
/// The format is XML written by a third party, so it is read under the same policy the suite applies
/// to every XML part it parses ([22 §10](../docs/specifications/22-docx.md#10-the-xml-policy)): no
/// document type declaration, no external entity, an explicit byte cap, an explicit nesting cap and
/// an explicit element count. A refusal is typed — <see cref="PdfTrustListError.XmlPolicy"/> or
/// <see cref="PdfTrustListError.Limit"/> — and never a parser message the caller has to interpret.
/// </para>
/// <para>
/// Nothing here fetches. A list-of-lists is parsed into <see cref="PdfTrustedList.NationalLists"/>
/// and the caller decides whether to download a national list, how to cache it, and when to replace
/// it: this reader never opens a socket, and loading the national lists is not a cascade it performs.
/// </para>
/// </remarks>
public static class PdfLotl
{
    /// <summary>The largest trust list read, in bytes. A national list is hundreds of kilobytes; this is a ceiling, not a budget.</summary>
    public const int MaxXmlBytes = 8 * 1024 * 1024;

    /// <summary>The deepest element nesting accepted. A trust list nests eight levels; this leaves room and stops a stack.</summary>
    public const int MaxXmlDepth = 64;

    /// <summary>The most elements one list may contain.</summary>
    public const int MaxXmlElements = 1_000_000;

    /// <summary>The most trust services one list may declare.</summary>
    public const int MaxServices = 100_000;

    /// <summary>The most pointers to other lists one list may declare.</summary>
    public const int MaxPointers = 4_096;

    /// <summary>The most certificates one service or pointer may name.</summary>
    public const int MaxCertificates = 64;

    /// <summary>Reads a trust list from its XML bytes.</summary>
    /// <param name="xml">The document, as published.</param>
    /// <param name="location">Where the caller obtained the bytes; recorded, never fetched.</param>
    /// <returns>The list, its services and its pointers to national lists.</returns>
    /// <exception cref="PdfTrustListException">The bytes are not a readable trust list, declare a
    /// DTD or an entity, or pass a bound.</exception>
    public static PdfTrustedList Read(ReadOnlySpan<byte> xml, string? location = null)
    {
        if (xml.IsEmpty)
        {
            throw new PdfTrustListException(PdfTrustListError.Malformed, "the trust list is empty");
        }

        if (xml.Length > MaxXmlBytes)
        {
            throw new PdfTrustListException(
                PdfTrustListError.Limit,
                $"the trust list is {xml.Length} bytes, over the {MaxXmlBytes}-byte cap");
        }

        var bytes = xml.ToArray();

        Guard(bytes);

        XDocument document;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, Settings());
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw Refusal(exception);
        }

        return Build(document, location);
    }

    /// <summary>Reads a trust list from a stream, without reading past <see cref="MaxXmlBytes"/>.</summary>
    /// <param name="stream">The document, as published. Left open.</param>
    /// <param name="location">Where the caller obtained the bytes; recorded, never fetched.</param>
    /// <returns>The list, its services and its pointers to national lists.</returns>
    /// <exception cref="PdfTrustListException">The bytes are not a readable trust list, declare a
    /// DTD or an entity, or pass a bound.</exception>
    public static PdfTrustedList Read(Stream stream, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxXmlBytes)
            {
                throw new PdfTrustListException(
                    PdfTrustListError.Limit,
                    $"the trust list is over the {MaxXmlBytes}-byte cap");
            }

            buffer.Write(chunk, 0, read);
        }

        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), location);
    }

    /// <summary>
    /// The XML reader settings this file — and only this file — creates. DTD processing is
    /// prohibited, every resolver is removed, and the entity expansion is bounded should the policy
    /// ever change.
    /// </summary>
    private static XmlReaderSettings Settings() => new()
    {
        ConformanceLevel = ConformanceLevel.Document,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
        MaxCharactersInDocument = MaxXmlBytes,
        MaxCharactersFromEntities = 1024 * 1024,
    };

    /// <summary>Refuses a document type declaration before any parser sees one, and bounds the walk.</summary>
    private static void Guard(byte[] bytes)
    {
        // A trust list is a producer's XML: it has no DTD and no entity declaration. Refusing the
        // string outright is coarser than classifying the declaration, and it is deliberately so —
        // a document that only mentions the syntax is still refused, which costs nothing because a
        // service name never contains it.
        var text = Encoding.Latin1.GetString(bytes);

        if (text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfTrustListException(
                PdfTrustListError.XmlPolicy,
                "the trust list declares a document type or an entity; an ETSI TS 119 612 list has neither,"
                + " and both are refused");
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, Settings());
            var elements = 0;

            while (reader.Read())
            {
                if (reader.Depth > MaxXmlDepth)
                {
                    throw new PdfTrustListException(
                        PdfTrustListError.Limit,
                        $"the trust list nests past {MaxXmlDepth} levels");
                }

                if (reader.NodeType == XmlNodeType.Element)
                {
                    elements++;

                    if (elements > MaxXmlElements)
                    {
                        throw new PdfTrustListException(
                            PdfTrustListError.Limit,
                            $"the trust list holds more than {MaxXmlElements} elements");
                    }
                }
            }
        }
        catch (XmlException exception)
        {
            // The backstop: a declaration this scan could not see (another encoding) is refused by
            // the reader's own DTD prohibition, and keeps the policy's class.
            throw Refusal(exception);
        }
    }

    private static PdfTrustListException Refusal(XmlException exception)
        => exception.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
            ? new PdfTrustListException(
                PdfTrustListError.XmlPolicy,
                $"the trust list declares a document type the reader prohibits: {exception.Message}")
            : new PdfTrustListException(
                PdfTrustListError.Malformed,
                $"the trust list is not well-formed XML: {exception.Message}");

    private static PdfTrustedList Build(XDocument document, string? location)
    {
        var root = document.Root
            ?? throw Malformed("the trust list has no root element");

        if (root.Name.LocalName != "TrustServiceStatusList")
        {
            throw Malformed($"the root element is {root.Name.LocalName}, not TrustServiceStatusList");
        }

        var scheme = Child(root, "SchemeInformation")
            ?? throw Malformed("the list carries no SchemeInformation");

        var territory = Text(Child(scheme, "SchemeTerritory"));

        if (string.IsNullOrEmpty(territory))
        {
            throw Malformed("the list states no SchemeTerritory");
        }

        var issue = Date(Text(Child(scheme, "ListIssueDateTime")))
            ?? throw Malformed("the list states no readable ListIssueDateTime");

        var nextUpdate = Date(Child(scheme, "NextUpdate") is { } next ? Text(Child(next, "dateTime")) : null);

        return new PdfTrustedList(
            territory,
            (int)(Integer(Text(Child(scheme, "TSLVersionIdentifier"))) ?? 0),
            Integer(Text(Child(scheme, "TSLSequenceNumber"))) ?? 0,
            issue,
            nextUpdate,
            Text(Child(scheme, "TSLType")),
            location,
            ReadServices(root),
            ReadPointers(scheme));
    }

    private static List<PdfTrustService> ReadServices(XElement root)
    {
        var services = new List<PdfTrustService>();
        var list = Child(root, "TrustServiceProviderList");

        if (list is null)
        {
            return services;
        }

        foreach (var provider in Children(list, "TrustServiceProvider"))
        {
            var providerName = Name(Child(Child(provider, "TSPInformation"), "TSPName")) ?? string.Empty;
            var entries = Child(provider, "TSPServices");

            if (entries is null)
            {
                continue;
            }

            foreach (var entry in Children(entries, "TSPService"))
            {
                if (services.Count >= MaxServices)
                {
                    throw new PdfTrustListException(
                        PdfTrustListError.Limit,
                        $"the trust list declares more than {MaxServices} services");
                }

                services.Add(ReadService(entry, providerName, services.Count));
            }
        }

        return services;
    }

    private static PdfTrustService ReadService(XElement entry, string providerName, int index)
    {
        var information = Child(entry, "ServiceInformation")
            ?? throw Malformed($"the trust service {index} carries no ServiceInformation");

        var typeIdentifier = Text(Child(information, "ServiceTypeIdentifier"))
            ?? throw Malformed($"the trust service {index} states no ServiceTypeIdentifier");

        var statusIdentifier = Text(Child(information, "ServiceStatus"))
            ?? throw Malformed($"the trust service {index} states no ServiceStatus");

        var started = Date(Text(Child(information, "StatusStartingTime")))
            ?? throw Malformed($"the trust service {index} states no readable StatusStartingTime");

        var periods = new List<(PdfTrustServiceStatus Status, DateTimeOffset From)>();

        foreach (var past in Descendants(entry, "ServiceHistoryInstance"))
        {
            var pastStatus = StatusOf(Text(Child(past, "ServiceStatus")));
            var pastFrom = Date(Text(Child(past, "StatusStartingTime")));

            if (pastFrom is { } from)
            {
                periods.Add((pastStatus, from));
            }
        }

        periods.Add((StatusOf(statusIdentifier), started));
        periods.Sort((left, right) => left.From.CompareTo(right.From));

        var windows = new List<PdfTrustServicePeriod>(periods.Count);
        for (var position = 0; position < periods.Count; position++)
        {
            windows.Add(new PdfTrustServicePeriod(
                periods[position].Status,
                periods[position].From,
                position + 1 < periods.Count ? periods[position + 1].From : null));
        }

        return new PdfTrustService(
            providerName,
            Name(Child(information, "ServiceName")) ?? string.Empty,
            TypeOf(typeIdentifier),
            typeIdentifier,
            StatusOf(statusIdentifier),
            statusIdentifier,
            IsQualified(typeIdentifier, information),
            ReadCertificates(Child(information, "ServiceDigitalIdentity"), $"the trust service {index}"),
            windows);
    }

    private static List<PdfTrustListPointer> ReadPointers(XElement scheme)
    {
        var pointers = new List<PdfTrustListPointer>();
        var container = Child(scheme, "PointersToOtherTSL");

        if (container is null)
        {
            return pointers;
        }

        foreach (var pointer in Children(container, "OtherTSLPointer"))
        {
            if (pointers.Count >= MaxPointers)
            {
                throw new PdfTrustListException(
                    PdfTrustListError.Limit,
                    $"the trust list declares more than {MaxPointers} pointers to other lists");
            }

            var location = Text(Child(pointer, "TSLLocation"));

            if (string.IsNullOrEmpty(location))
            {
                throw Malformed("a pointer to another trust list states no TSLLocation");
            }

            var information = Descendants(pointer, "OtherInformation").FirstOrDefault();

            pointers.Add(new PdfTrustListPointer(
                location,
                Text(Child(information, "SchemeTerritory")),
                Name(Child(information, "SchemeOperatorName")),
                ReadCertificates(Child(pointer, "ServiceDigitalIdentities"), $"the pointer to {location}")));
        }

        return pointers;
    }

    /// <summary>
    /// The certificates a digital identity names. A certificate that does not decode is dropped —
    /// the service then covers nothing by it — rather than failing a list over one broken entry.
    /// </summary>
    private static List<X509Certificate2> ReadCertificates(XElement? identity, string owner)
    {
        var certificates = new List<X509Certificate2>();

        if (identity is null)
        {
            return certificates;
        }

        foreach (var element in Descendants(identity, "X509Certificate"))
        {
            if (certificates.Count >= MaxCertificates)
            {
                throw new PdfTrustListException(
                    PdfTrustListError.Limit,
                    $"{owner} names more than {MaxCertificates} certificates");
            }

            if (TryCertificate(element.Value, out var certificate))
            {
                certificates.Add(certificate);
            }
        }

        return certificates;
    }

    private static bool TryCertificate(string text, out X509Certificate2 certificate)
    {
        certificate = null!;

        var base64 = text.Trim();

        if (base64.Length == 0)
        {
            return false;
        }

        try
        {
            var raw = Convert.FromBase64String(base64);

#if NET9_0_OR_GREATER
            certificate = X509CertificateLoader.LoadCertificate(raw);
#else
            certificate = new X509Certificate2(raw);
#endif
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static PdfTrustServiceType TypeOf(string identifier)
    {
        if (identifier.EndsWith("/CA/QC", StringComparison.Ordinal))
        {
            return PdfTrustServiceType.QualifiedCertificateAuthority;
        }

        if (identifier.EndsWith("/CA", StringComparison.Ordinal))
        {
            return PdfTrustServiceType.CertificateAuthority;
        }

        if (identifier.EndsWith("AdESigQC", StringComparison.Ordinal)
            || identifier.EndsWith("/AdESig", StringComparison.Ordinal))
        {
            return PdfTrustServiceType.Signature;
        }

        if (identifier.EndsWith("AdESealQC", StringComparison.Ordinal)
            || identifier.EndsWith("/AdESeal", StringComparison.Ordinal))
        {
            return PdfTrustServiceType.Seal;
        }

        if (identifier.EndsWith("/TSA", StringComparison.Ordinal)
            || identifier.EndsWith("/TSA/QI", StringComparison.Ordinal)
            || identifier.EndsWith("/TSA/QC", StringComparison.Ordinal))
        {
            return PdfTrustServiceType.Timestamp;
        }

        return PdfTrustServiceType.Other;
    }

    private static PdfTrustServiceStatus StatusOf(string? identifier)
    {
        if (identifier is null)
        {
            return PdfTrustServiceStatus.Unknown;
        }

        if (identifier.EndsWith("/granted", StringComparison.Ordinal)
            || identifier.EndsWith("/accredited", StringComparison.Ordinal))
        {
            return PdfTrustServiceStatus.Granted;
        }

        if (identifier.EndsWith("/withdrawn", StringComparison.Ordinal))
        {
            return PdfTrustServiceStatus.Withdrawn;
        }

        if (identifier.EndsWith("/deprecated", StringComparison.Ordinal))
        {
            return PdfTrustServiceStatus.Deprecated;
        }

        return PdfTrustServiceStatus.Unknown;
    }

    /// <summary>
    /// Whether the entry is marked qualified: a service type whose last segment ends in <c>QC</c>,
    /// or a qualifier URI whose last segment starts with it.
    /// </summary>
    private static bool IsQualified(string typeIdentifier, XElement information)
    {
        if (typeIdentifier.EndsWith("QC", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var qualifier in Descendants(information, "Qualifier"))
        {
            var uri = qualifier.Attribute("URI")?.Value;

            if (uri is not null && LastSegment(uri).StartsWith("QC", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string LastSegment(string uri)
    {
        var slash = uri.LastIndexOf('/');
        return slash >= 0 ? uri[(slash + 1)..] : uri;
    }

    private static IEnumerable<XElement> Children(XElement parent, string name)
        => parent.Elements().Where(element => element.Name.LocalName == name);

    private static IEnumerable<XElement> Descendants(XElement parent, string name)
        => parent.Descendants().Where(element => element.Name.LocalName == name);

    private static XElement? Child(XElement? parent, string name)
        => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static string? Text(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>The name in <paramref name="container"/>, preferring English, then the first written.</summary>
    private static string? Name(XElement? container)
    {
        if (container is null)
        {
            return null;
        }

        var names = Children(container, "Name").ToList();

        foreach (var name in names)
        {
            var language = name.Attribute(XNamespace.Xml + "lang")?.Value;

            if (language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return Text(name);
            }
        }

        return names.Count > 0 ? Text(names[0]) : Text(container);
    }

    private static DateTimeOffset? Date(string? text)
        => DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;

    private static long? Integer(string? text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static PdfTrustListException Malformed(string message)
        => new(PdfTrustListError.Malformed, message);
}
