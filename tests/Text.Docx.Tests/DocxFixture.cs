using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>
/// Builds the `.docx` the tests read, from raw OOXML parts.
/// </summary>
/// <remarks>
/// The fixture is written by hand rather than by Word or by a writer library: every byte the reader
/// reacts to is then named in this file, and a test that fails points at the property that broke
/// rather than at a producer's opinion. <c>fixtures/README.md</c> records how the committed binary
/// was made and how to regenerate it.
/// </remarks>
internal static class DocxFixture
{
    internal const string HeadingOne = "Quarterly report";

    internal const string HeadingTwo = "Revenue";

    internal const string BodyText = "Revenue grew twelve percent while cost stayed flat.";

    internal const string StyledText = "much higher";

    internal const string BulletOne = "First finding";

    internal const string BulletTwo = "Second finding";

    internal const string MergedCellText = "Merged";

    internal const string ImagePart = "word/media/image1.png";

    /// <summary>The committed fixture, as a file name inside <c>fixtures/</c>.</summary>
    internal const string FileName = "report.docx";

    /// <summary>The parts the fixture is made of, in the order they are written.</summary>
    internal static IReadOnlyList<(string Name, byte[] Content)> Parts()
    {
        return
        [
            ("[Content_Types].xml", Utf8(ContentTypes)),
            ("_rels/.rels", Utf8(PackageRelationships)),
            ("word/document.xml", Utf8(Document)),
            ("word/_rels/document.xml.rels", Utf8(DocumentRelationships)),
            ("word/styles.xml", Utf8(Styles)),
            ("word/numbering.xml", Utf8(Numbering)),
            (ImagePart, Png),
        ];
    }

    /// <summary>The fixture: a zip of <see cref="Parts"/>.</summary>
    internal static byte[] Build() => Pack(Parts());

    /// <summary>Bytes that are not a package at all.</summary>
    internal static byte[] NotAPackage() => Encoding.UTF8.GetBytes("this is not a zip package");

    /// <summary>A package that is a zip, but has no <c>word/document.xml</c>.</summary>
    internal static byte[] WithoutDocument()
    {
        var parts = new List<(string Name, byte[] Content)>();

        foreach (var part in Parts())
        {
            if (!string.Equals(part.Name, "word/document.xml", StringComparison.Ordinal))
            {
                parts.Add(part);
            }
        }

        return Pack(parts);
    }

    /// <summary>The bytes of a 2x2 PNG, the picture the fixture embeds.</summary>
    internal static byte[] Png =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02,
        0x08, 0x02, 0x00, 0x00, 0x00, 0xFD, 0xD4, 0x9A,
        0x73, 0x00, 0x00, 0x00, 0x11, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x50, 0x70, 0x48, 0x50,
        0x70, 0x48, 0x60, 0x80, 0x50, 0x00, 0x14, 0x0E,
        0x03, 0x01, 0x20, 0x3A, 0x33, 0x9B, 0x00, 0x00,
        0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42,
        0x60, 0x82,
    ];

    /// <summary>
    /// Zips the parts with a fixed timestamp, so two builds of the same parts are the same bytes.
    /// </summary>
    private static byte[] Pack(IReadOnlyList<(string Name, byte[] Content)> parts)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        .GetBytes(text);

    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string PictureNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private const string WordDrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static readonly string ContentTypes = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="{ContentTypesNamespace}">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
        </Types>
        """;

    private static readonly string PackageRelationships = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="{PackageRelationshipsNamespace}">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private static readonly string DocumentRelationships = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="{PackageRelationshipsNamespace}">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
        </Relationships>
        """;

    /// <summary>
    /// Two heading styles, a body style, and the run formatting the fixture exercises: bold, italic,
    /// underline, colour, size, superscript and a strike.
    /// </summary>
    private static readonly string Styles = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="{WordNamespace}">
          <w:docDefaults>
            <w:rPrDefault>
              <w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr>
            </w:rPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:styleId="Heading1">
            <w:name w:val="heading 1"/>
            <w:basedOn w:val="Normal"/>
            <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
            <w:rPr><w:b/><w:sz w:val="36"/></w:rPr>
          </w:style>
          <w:style w:type="paragraph" w:styleId="Heading2">
            <w:name w:val="heading 2"/>
            <w:basedOn w:val="Normal"/>
            <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
            <w:rPr><w:b/><w:sz w:val="28"/></w:rPr>
          </w:style>
          <w:style w:type="paragraph" w:styleId="Normal">
            <w:name w:val="Normal"/>
          </w:style>
        </w:styles>
        """;

    /// <summary><c>numId</c> 1 is a bulleted list; 2 would be a numbered one.</summary>
    private static readonly string Numbering = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="{WordNamespace}">
          <w:abstractNum w:abstractNumId="0">
            <w:multiLevelType w:val="hybridMultilevel"/>
            <w:lvl w:ilvl="0">
              <w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#61623;"/><w:lvlJc w:val="left"/>
            </w:lvl>
            <w:lvl w:ilvl="1">
              <w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="o"/><w:lvlJc w:val="left"/>
            </w:lvl>
          </w:abstractNum>
          <w:abstractNum w:abstractNumId="1">
            <w:multiLevelType w:val="hybridMultilevel"/>
            <w:lvl w:ilvl="0">
              <w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/>
            </w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
          <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
        </w:numbering>
        """;

    private static readonly string Drawing = $"""
        <w:drawing>
          <wp:inline distT="0" distB="0" distL="0" distR="0">
            <wp:extent cx="190500" cy="190500"/>
            <wp:docPr id="1" name="Picture 1"/>
            <a:graphic xmlns:a="{DrawingNamespace}">
              <a:graphicData uri="{PictureNamespace}">
                <pic:pic xmlns:pic="{PictureNamespace}">
                  <pic:nvPicPr><pic:cNvPr id="0" name="image1.png"/><pic:cNvPicPr/></pic:nvPicPr>
                  <pic:blipFill>
                    <a:blip r:embed="rId3"/>
                    <a:stretch><a:fillRect/></a:stretch>
                  </pic:blipFill>
                  <pic:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="190500" cy="190500"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </pic:spPr>
                </pic:pic>
              </a:graphicData>
            </a:graphic>
          </wp:inline>
        </w:drawing>
        """;

    /// <summary>
    /// The body: two headings, a body paragraph, a formatted paragraph, a two-item bulleted list, a
    /// table with a horizontal span and a vertical merge, a picture, and an A4 page with its own
    /// margins.
    /// </summary>
    private static readonly string Document = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="{WordNamespace}" xmlns:r="{RelationshipsNamespace}" xmlns:wp="{WordDrawingNamespace}">
          <w:body>
            <w:p>
              <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
              <w:r><w:t>{HeadingOne}</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
              <w:r><w:t>{HeadingTwo}</w:t></w:r>
            </w:p>
            <w:p>
              <w:r><w:t xml:space="preserve">Revenue grew </w:t></w:r>
              <w:r><w:rPr><w:b/></w:rPr><w:t>twelve percent</w:t></w:r>
              <w:r><w:t xml:space="preserve"> while cost </w:t></w:r>
              <w:r><w:rPr><w:i/></w:rPr><w:t>stayed flat</w:t></w:r>
              <w:r><w:t>.</w:t></w:r>
            </w:p>
            <w:p>
              <w:r>
                <w:rPr><w:u w:val="single"/><w:color w:val="C00000"/><w:sz w:val="28"/><w:vertAlign w:val="superscript"/></w:rPr>
                <w:t>{StyledText}</w:t>
              </w:r>
              <w:r><w:rPr><w:strike/></w:rPr><w:t xml:space="preserve"> than </w:t></w:r>
              <w:r><w:t>budget</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
              <w:r><w:t>{BulletOne}</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
              <w:r><w:t>{BulletTwo}</w:t></w:r>
            </w:p>
            <w:tbl>
              <w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr>
              <w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>
              <w:tr>
                <w:tc>
                  <w:tcPr><w:gridSpan w:val="2"/></w:tcPr>
                  <w:p><w:r><w:t>{MergedCellText}</w:t></w:r></w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>Third</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc>
                  <w:tcPr><w:vMerge w:val="restart"/></w:tcPr>
                  <w:p><w:r><w:t>Left</w:t></w:r></w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>Middle</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Right</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc>
                  <w:tcPr><w:vMerge/></w:tcPr>
                  <w:p><w:r><w:t/></w:r></w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>Middle below</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Right below</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            <w:p><w:r>{Drawing}</w:r></w:p>
            <w:sectPr>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1701" w:bottom="1440" w:left="1701" w:header="851" w:footer="992" w:gutter="0"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """;
}
