using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Reads a Factur-X invoice from CII or UBL XML.</summary>
public static class FacturXReader
{
    private const long MaxDocumentBytes = 16 * 1024 * 1024;

    /// <summary>Reads an invoice from its XML text.</summary>
    /// <exception cref="FacturXFormatException">The document is not CII or UBL, or lacks a required part.</exception>
    public static FacturXInvoice Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var root = Parse(XmlReader.Create(new StringReader(xml), Settings()));
        return FromRoot(root);
    }

    /// <summary>Reads an invoice from a stream of XML.</summary>
    /// <exception cref="FacturXFormatException">The document is not CII or UBL, or lacks a required part.</exception>
    public static FacturXInvoice Read(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = XmlReader.Create(xml, Settings());
        return FromRoot(Parse(reader));
    }

    /// <summary>Reads an invoice from its bytes.</summary>
    /// <exception cref="FacturXFormatException">The document is not CII or UBL, or lacks a required part.</exception>
    public static FacturXInvoice Read(ReadOnlySpan<byte> xml)
    {
        using var stream = new MemoryStream(xml.ToArray(), writable: false);
        return Read(stream);
    }

    /// <summary>
    /// Reads the invoice embedded in a Factur-X PDF.
    /// </summary>
    /// <remarks>
    /// This is the receiving half of the exchange: the bytes the sender attached, not a
    /// re-serialization of them. The attachment is located through the catalog's name tree and
    /// inflated if the writer compressed it.
    /// </remarks>
    /// <exception cref="FacturXFormatException">
    /// The PDF carries no embedded invoice, or the one it carries cannot be read.
    /// </exception>
    public static FacturXInvoice ReadPdf(ReadOnlySpan<byte> pdf)
    {
        if (!TryExtractXml(pdf, out var fileName, out var xml))
        {
            throw new FacturXFormatException(
                "the PDF carries no embedded invoice; expected a /Names tree with a factur-x.xml attachment");
        }

        try
        {
            return Read(xml);
        }
        catch (FacturXFormatException exception)
        {
            throw new FacturXFormatException($"the embedded `{fileName}` is not a readable invoice: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Extracts the embedded invoice XML from a PDF without parsing it.
    /// </summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <param name="fileName">The name the attachment carries, for example <c>factur-x.xml</c>.</param>
    /// <param name="xml">The bytes, exactly as they were embedded.</param>
    /// <returns><see langword="true"/> when an attachment was found.</returns>
    public static bool TryExtractXml(ReadOnlySpan<byte> pdf, out string fileName, out byte[] xml)
    {
        if (pdf.IsEmpty)
        {
            fileName = string.Empty;
            xml = [];
            return false;
        }

        return PdfAttachments.TryExtract(Encoding.Latin1.GetString(pdf), out fileName, out xml);
    }

    /// <summary>The root element name of a document, without parsing the whole tree.</summary>
    /// <exception cref="FacturXFormatException">The document is not well-formed XML.</exception>
    public static string PeekSyntax(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = XmlReader.Create(xml, Settings());

        try
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    return SyntaxOf(reader.LocalName);
                }
            }
        }
        catch (XmlException exception)
        {
            throw new FacturXFormatException($"the document is not well-formed XML: {exception.Message}", exception);
        }

        throw new FacturXFormatException("the document has no root element");
    }

    private static XElement Parse(XmlReader reader)
    {
        try
        {
            return XElement.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new FacturXFormatException($"the document is not well-formed XML: {exception.Message}", exception);
        }
    }

    private static FacturXInvoice FromRoot(XElement root) => root.Name.LocalName switch
    {
        "CrossIndustryInvoice" => CiiInvoiceReader.Read(root),
        "Invoice" or "CreditNote" => UblInvoiceReader.Read(root),
        var other => throw new FacturXFormatException(
            $"unrecognized document root `{other}`; expected a CII `CrossIndustryInvoice` or a UBL `Invoice`"),
    };

    private static string SyntaxOf(string rootName) => rootName switch
    {
        "CrossIndustryInvoice" => "CII",
        "Invoice" or "CreditNote" => "UBL",
        var other => throw new FacturXFormatException($"unrecognized document root `{other}`"),
    };

    private static XmlReaderSettings Settings() => new()
    {
        // A DTD would let a hostile document expand entities; nothing legitimate needs one.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        MaxCharactersInDocument = MaxDocumentBytes,
        CloseInput = false,
    };
}