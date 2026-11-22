using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using M3scalinum.Text.Docx;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>
/// The XML policy as the managed side sees it: a part that declares a DTD, an external entity or an
/// expansion bomb is refused with <see cref="DocxErrorCode.XmlPolicy"/> and a sentence naming the
/// rule, not with a generic malformed-package error.
/// </summary>
public sealed class DocxXmlPolicyTests
{
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void A_document_with_a_dtd_is_an_xml_policy_refusal()
    {
        var document = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE w:document [<!ELEMENT w:document ANY>]>
            <w:document xmlns:w="{Word}"><w:body/></w:document>
            """;

        var exception = Assert.Throws<DocxException>(() => DocxReader.Read(Package(document)));

        Assert.Equal(DocxErrorCode.XmlPolicy, exception.Code);
        Assert.Contains("DTD", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_an_external_entity_is_an_xml_policy_refusal()
    {
        foreach (var target in new[] { "file:///etc/passwd", "http://attacker.test/leak" })
        {
            var document = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE w:document [<!ENTITY secret SYSTEM "{target}">]>
                <w:document xmlns:w="{Word}"><w:body><w:p><w:r><w:t>&secret;</w:t></w:r></w:p></w:body></w:document>
                """;

            var exception = Assert.Throws<DocxException>(() => DocxReader.Read(Package(document)));

            Assert.Equal(DocxErrorCode.XmlPolicy, exception.Code);
            Assert.Contains("external entity", exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>A package holding exactly the three parts the reader requires, with a chosen body.</summary>
    private static byte[] Package(string documentXml)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", "<Types/>");
            Write(archive, "_rels/.rels", "<Relationships/>");
            Write(archive, "word/document.xml", documentXml);
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string xml)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);

        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }
}
