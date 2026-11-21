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
/// Every write is deterministic: properties are written as attributes of the first
/// <c>rdf:Description</c>, in one order, with the prefixes named explicitly, so the same input
/// produces the same bytes.
/// </para>
/// </remarks>
internal static class PdfAXmp
{
    private static readonly XNamespace X = "adobe:ns:meta/";

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly XNamespace PdfAId = "http://www.aiim.org/pdfa/ns/id/";

    private static readonly XNamespace PdfUaId = "http://www.aiim.org/pdfua/ns/id/";

    /// <summary>
    /// Returns the XMP packet that declares <paramref name="part"/> and <paramref name="conformance"/>
    /// as PDF/A, and <paramref name="uaPart"/> as PDF/UA, keeping whatever
    /// <paramref name="existing"/> already held.
    /// </summary>
    /// <param name="existing">The document's current XMP, or <see langword="null"/> when it has none.</param>
    /// <param name="part">The <c>pdfaid:part</c> to declare, or <see langword="null"/> for none.</param>
    /// <param name="conformance">The <c>pdfaid:conformance</c> to declare, or <see langword="null"/>.</param>
    /// <param name="uaPart">The <c>pdfuaid:part</c> to declare, or <see langword="null"/>.</param>
    public static byte[] Rewrite(string? existing, int? part, string? conformance, int? uaPart)
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

    /// <summary>Loads an existing packet, or returns <see langword="null"/> when it cannot be edited.</summary>
    private static XDocument? Load(string? existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(existing, LoadOptions.PreserveWhitespace);

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
