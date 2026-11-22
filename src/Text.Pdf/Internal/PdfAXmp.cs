using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads and rewrites the XMP packet that declares a standard: the <c>pdfaid</c> identifier of
/// PDF/A and the <c>pdfuaid</c> identifier of PDF/UA.
/// </summary>
/// <remarks>
/// <para>
/// The existing packet is kept and edited, not replaced: a document's title, creator, language and
/// history live in the same stream, and a converter that wrote a fresh packet would silently drop
/// them. A <c>pdfaid:part</c> or <c>pdfaid:conformance</c> that is already there is removed before
/// the new value is written, whichever form it took, so the result never declares a standard twice.
/// </para>
/// <para>
/// The packet is a received document's metadata, so it is read under a policy rather than handed to
/// a parser: no document type declaration, no external entity, a bounded byte length, a bounded
/// element depth and a bounded node count. A packet outside the policy is refused by name
/// (<see cref="PdfOperationException"/>, whose message states the rule, the limit and the value
/// observed); a packet that is merely
/// not well-formed XML is replaced, because no reader can take a declaration from it and the
/// converter must not fail a document for metadata that veraPDF also rejects.
/// </para>
/// <para>
/// Every write is deterministic: properties are written as attributes of the first
/// <c>rdf:Description</c>, in one order, with the prefixes named explicitly, so the same input
/// produces the same bytes.
/// </para>
/// </remarks>
internal static class PdfAXmp
{
    /// <summary>The most bytes an XMP packet may decode to.</summary>
    /// <remarks>
    /// A declaration packet is a few kilobytes; a packet that carries a thumbnail is a few hundred.
    /// Four megabytes leaves room for both with an order of magnitude to spare, and keeps the
    /// metadata of a received PDF from being a second document in size.
    /// </remarks>
    internal const int MaxXmpBytes = 4 * 1024 * 1024;

    /// <summary>How deeply XMP elements may nest. RDF reaches six or so; sixty-four is absurd already.</summary>
    internal const int MaxXmpDepth = 64;

    /// <summary>How many nodes one packet may build. A declaration is dozens; a hundred thousand is a generator.</summary>
    internal const int MaxXmpNodes = 100_000;

    private static readonly XNamespace X = "adobe:ns:meta/";

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly XNamespace PdfAId = "http://www.aiim.org/pdfa/ns/id/";

    private static readonly XNamespace PdfUaId = "http://www.aiim.org/pdfua/ns/id/";

    /// <summary>
    /// Returns the XMP packet that declares <paramref name="part"/> and <paramref name="conformance"/>
    /// as PDF/A, and <paramref name="uaPart"/> as PDF/UA, keeping whatever
    /// <paramref name="existing"/> already held.
    /// </summary>
    /// <param name="existing">The document's current XMP, decoded, or <see langword="null"/> when it has none.</param>
    /// <param name="part">The <c>pdfaid:part</c> to declare, or <see langword="null"/> for none.</param>
    /// <param name="conformance">The <c>pdfaid:conformance</c> to declare, or <see langword="null"/>.</param>
    /// <param name="uaPart">The <c>pdfuaid:part</c> to declare, or <see langword="null"/>.</param>
    /// <exception cref="PdfOperationException">
    /// The packet is outside the policy: too large, too deep, too many nodes, or it declares a
    /// document type. The message names the rule and the value observed.
    /// </exception>
    public static byte[] Rewrite(byte[]? existing, int? part, string? conformance, int? uaPart)
    {
        var document = Load(existing) ?? Fresh();
        var rdf = document.Descendants(Rdf + "RDF").First();

        // Remove every existing declaration, in either form: an element or an attribute.
        foreach (var description in rdf.Descendants(Rdf + "Description").ToList())
        {
            Remove(description, PdfAId, "part");
            Remove(description, PdfAId, "conformance");
            Remove(description, PdfUaId, "part");
        }

        var target = rdf.Descendants(Rdf + "Description").FirstOrDefault();
        if (target is null)
        {
            target = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty));
            rdf.Add(target);
        }

        if (part is { } archivePart)
        {
            Declare(target, "pdfaid", PdfAId);
            target.SetAttributeValue(PdfAId + "part", archivePart.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (conformance is { } level)
            {
                target.SetAttributeValue(PdfAId + "conformance", level);
            }
        }

        if (uaPart is { } accessibilityPart)
        {
            Declare(target, "pdfuaid", PdfUaId);
            target.SetAttributeValue(PdfUaId + "part", accessibilityPart.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Serialize(document);
    }

    /// <summary>Removes one property, whether the packet wrote it as an element or as an attribute.</summary>
    private static void Remove(XElement description, XNamespace name, string local)
    {
        description.SetAttributeValue(name + local, null);

        foreach (var element in description.Elements(name + local).ToList())
        {
            element.Remove();
        }

        // A packet that put the identifier in a Description of its own leaves that element behind.
        foreach (var nested in description.Descendants(name + local).ToList())
        {
            nested.Remove();
        }
    }

    /// <summary>Declares a prefix explicitly, so the property is written as <c>pdfaid:part</c>.</summary>
    private static void Declare(XElement element, string prefix, XNamespace name)
    {
        if (element.GetNamespaceOfPrefix(prefix) is { } existing && existing == name)
        {
            return;
        }

        element.SetAttributeValue(XNamespace.Xmlns + prefix, name.NamespaceName);
    }

    /// <summary>
    /// Loads an existing packet under the policy, or returns <see langword="null"/> when it cannot be
    /// edited.
    /// </summary>
    /// <remarks>
    /// Bytes rather than text, so the size ceiling is applied before a string exists. A packet
    /// outside the policy is refused; a packet that is not well-formed is replaced, which is the
    /// difference between "too big to look at" and "nothing a reader could take a declaration from".
    /// </remarks>
    /// <exception cref="PdfOperationException">The packet is outside the policy.</exception>
    private static XDocument? Load(byte[]? existing)
    {
        if (existing is null || existing.Length == 0)
        {
            return null;
        }

        if (existing.Length > MaxXmpBytes)
        {
            throw Refuse(
                $"the XMP packet is {existing.Length} bytes, over the {MaxXmpBytes}-byte XMP limit");
        }

        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(existing);

        // No packet at all is the same case as a packet of whitespace: there is nothing to keep.
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        RefuseDeclaration(text);

        try
        {
            Scan(text);

            var document = Parse(text);

            // A packet without an rdf:RDF is not a packet this code can add a property to.
            return document.Descendants(Rdf + "RDF").Any() ? document : null;
        }
        catch (XmlException)
        {
            // XMP that does not parse is replaced: a document cannot carry a declaration in a
            // packet no reader can parse, and the alternative is failing for a reason veraPDF would
            // also fail on.
            return null;
        }
    }

    /// <summary>The reader settings the policy is: no DTD, no resolver, a bounded document.</summary>
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = MaxXmpBytes,
        CloseInput = true,
    };

    /// <summary>
    /// Walks the packet once, without building a tree, to bound its depth and its node count: a
    /// packet nested ten thousand levels deep is read here as a counter, not as a stack.
    /// </summary>
    /// <exception cref="PdfOperationException">The packet nests too deeply or builds too many nodes.</exception>
    private static void Scan(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), Settings());
        var depth = 0;
        var nodes = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                depth--;
                continue;
            }

            nodes++;

            if (reader.NodeType == XmlNodeType.Element)
            {
                // An empty element is one node with no EndElement behind it: counting it as an
                // opening level would make every self-closing element nest one level deeper than it
                // does, and a packet of a hundred thousand siblings would read as a hundred thousand
                // levels of nesting.
                if (!reader.IsEmptyElement)
                {
                    depth++;
                }

                if (depth > MaxXmpDepth)
                {
                    throw Refuse(
                        $"the XMP packet nests elements {depth} levels deep, over the {MaxXmpDepth}-level XMP limit");
                }
            }

            if (nodes > MaxXmpNodes)
            {
                throw Refuse(
                    $"the XMP packet holds more than {MaxXmpNodes} nodes, over the {MaxXmpNodes}-node XMP limit");
            }
        }
    }

    /// <summary>Parses the packet a second time, into the tree the rewrite edits, under the same settings.</summary>
    private static XDocument Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), Settings());

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// Refuses a packet that declares a document type.
    /// </summary>
    /// <remarks>
    /// A declaration is not part of XMP: the reader is configured to reject one, and this scan runs
    /// first so the refusal names the declaration rather than a parser message. Anything it declared
    /// — an internal entity, an expansion bomb — is refused with it, so nothing in the packet is
    /// expanded before it is read.
    /// </remarks>
    /// <exception cref="PdfOperationException">The packet declares a document type or an external entity.</exception>
    private static void RefuseDeclaration(string text)
    {
        var at = text.IndexOf("<!DOCTYPE", StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }

        var declaration = text[at..];
        var external = declaration.Contains("SYSTEM", StringComparison.Ordinal)
            || declaration.Contains("PUBLIC", StringComparison.Ordinal);

        throw Refuse(
            external
                ? "the XMP packet declares a document type with an external identifier (SYSTEM/PUBLIC); external entities are refused"
                : "the XMP packet declares a document type (DTD); this reader refuses DTDs and expands no entity");
    }

    /// <summary>The typed refusal, as the exception the converter raises.</summary>
    private static PdfOperationException Refuse(string message) => new(message);

    /// <summary>A minimal XMP packet, in the shape a PDF/A producer writes.</summary>
    private static XDocument Fresh()
    {
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty));
        var rdf = new XElement(
            Rdf + "RDF",
            new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
            description);

        var meta = new XElement(
            X + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
            rdf);

        return new XDocument(
            new XProcessingInstruction("xpacket", "begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\""),
            meta,
            new XProcessingInstruction("xpacket", "end=\"w\""));
    }

    /// <summary>Writes the packet as UTF-8, without an XML declaration, as XMP is stored.</summary>
    private static byte[] Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }
}
