using System;
using System.Globalization;
using System.Text;
using M3scalinum.Text.Typst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// Adds the Factur-X XMP extension to a PDF/A-3 that already carries the invoice XML.
/// </summary>
/// <remarks>
/// Typst writes its own XMP from a fixed set of fields and offers no hook for an extension schema,
/// while Factur-X (and ZUGFeRD 2.x) require a <c>fx:</c> description naming the embedded file, its
/// type, the standard version and the conformance level — registered through
/// <c>pdfaExtension:schemas</c>. Without it the file is a PDF/A-3 with an attachment, which Chorus
/// Pro and the German receivers reject.
///
/// The edit is a PDF incremental update: the metadata stream is rewritten under its own object
/// number, a one-entry cross-reference section is appended, and the new trailer points at the
/// previous one with <c>/Prev</c>. The original bytes, every other object and the existing XMP
/// content stay untouched, so nothing that made the file PDF/A can be broken by the patch.
/// </remarks>
public static class FacturXPdf
{
    private const string FxNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";

    /// <summary>
    /// Returns a copy of <paramref name="pdf"/> whose XMP metadata declares it as a Factur-X
    /// invoice carrying <paramref name="fileName"/> at <paramref name="profile"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The PDF has no metadata stream, or that stream is compressed — the patch only rewrites a
    /// stream it can read.
    /// </exception>
    public static byte[] AddMetadata(ReadOnlySpan<byte> pdf, string fileName, FacturXProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var text = Encoding.Latin1.GetString(pdf);
        var document = ParsedPdf.Parse(text);
        var patched = Inject(document.Xmp, fileName, profile.ToConformanceLevel());

        var builder = new StringBuilder(text);
        if (!text.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        var objectOffset = builder.Length;
        builder.Append(document.MetadataNumber).Append(" 0 obj\n")
               .Append("<</Length ").Append(patched.Length.ToString(CultureInfo.InvariantCulture))
               .Append("/Type/Metadata/Subtype/XML>>\n")
               .Append("stream\n").Append(patched).Append("\nendstream\nendobj\n");

        var xrefOffset = builder.Length;
        var size = Math.Max(document.Size, document.MetadataNumber + 1);

        builder.Append("xref\n")
               .Append(document.MetadataNumber).Append(" 1\n")
               .Append(objectOffset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n")
               .Append("trailer\n<</Size ").Append(size.ToString(CultureInfo.InvariantCulture))
               .Append("/Root ").Append(document.RootNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R")
               .Append("/Prev ").Append(document.PreviousXref.ToString(CultureInfo.InvariantCulture));

        if (document.Id is { } id)
        {
            builder.Append("/ID ").Append(id);
        }

        builder.Append(">>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static string Inject(string xmp, string fileName, string conformanceLevel)
    {
        // The four properties the standard defines, in their own description.
        var description =
            "<rdf:Description rdf:about=\"\" xmlns:fx=\"" + FxNamespace + "\">" +
            "<fx:DocumentType>INVOICE</fx:DocumentType>" +
            "<fx:DocumentFileName>" + Escape(fileName) + "</fx:DocumentFileName>" +
            "<fx:Version>1.0</fx:Version>" +
            "<fx:ConformanceLevel>" + Escape(conformanceLevel) + "</fx:ConformanceLevel>" +
            "</rdf:Description>";

        // …and the schema entry that makes them legal PDF/A metadata.
        var schema =
            "<rdf:li rdf:parseType=\"Resource\">" +
            "<pdfaSchema:namespaceURI>" + FxNamespace + "</pdfaSchema:namespaceURI>" +
            "<pdfaSchema:prefix>fx</pdfaSchema:prefix>" +
            "<pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>" +
            "<pdfaSchema:property><rdf:Seq>" +
            Property("DocumentFileName", "The name of the embedded XML invoice file") +
            Property("DocumentType", "INVOICE") +
            Property("Version", "The version of the ZUGFeRD / Factur-X standard") +
            Property("ConformanceLevel", "The conformance level of the embedded XML") +
            "</rdf:Seq></pdfaSchema:property>" +
            "</rdf:li>";

        var schemas = xmp.IndexOf("<pdfaExtension:schemas>", StringComparison.Ordinal);
        if (schemas < 0)
        {
            throw new InvalidOperationException(
                "the PDF metadata has no pdfaExtension:schemas block, so it is not a PDF/A document");
        }

        var bagEnd = xmp.IndexOf("</rdf:Bag>", schemas, StringComparison.Ordinal);
        if (bagEnd < 0)
        {
            throw new InvalidOperationException("the pdfaExtension:schemas block is not closed");
        }

        var rdfEnd = xmp.LastIndexOf("</rdf:RDF>", StringComparison.Ordinal);
        if (rdfEnd < 0)
        {
            throw new InvalidOperationException("the XMP metadata has no closing rdf:RDF");
        }

        return xmp.Insert(bagEnd, schema).Insert(rdfEnd + schema.Length, description);
    }

    private static string Property(string name, string description) =>
        "<rdf:li rdf:parseType=\"Resource\">" +
        "<pdfaProperty:name>" + name + "</pdfaProperty:name>" +
        "<pdfaProperty:valueType>Text</pdfaProperty:valueType>" +
        "<pdfaProperty:category>external</pdfaProperty:category>" +
        "<pdfaProperty:description>" + description + "</pdfaProperty:description>" +
        "</rdf:li>";

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
