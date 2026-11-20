# FactureX — reading

## From XML

```csharp
using M3scalinum.Text.Pdf.FactureX;

var invoice = FacturXReader.Read(File.ReadAllText("invoice.xml"));
var fromStream = FacturXReader.Read(stream);
var fromBytes = FacturXReader.Read(bytes);
```

The syntax is detected from the root element, so CII and UBL take the same path.
`FacturXReader.PeekSyntax(stream)` tells you which one a document is without loading it.

## From a PDF

This is the receiving half of the exchange: a platform, an auditor or a test wants the bytes it was
given, not the model they were turned into.

```csharp
var received = FacturXReader.ReadPdf(File.ReadAllBytes("factur-x.pdf"));

if (FacturXReader.TryExtractXml(bytes, out var name, out var xml))
{
    // `xml` is what the sender attached, byte for byte — not a re-serialization.
}
```

The attachment is found through the catalog's name tree and inflated when compressed.
`factur-x.xml` wins over any other attachment, then `zugferd-invoice.xml`, then
`ZUGFeRD-invoice.xml`, then `cii.xml`.

## What it refuses

`FacturXFormatException` says which of these it hit, rather than returning an empty model:

- the data is not a PDF — no `startxref`, no trailer;
- the catalog has no `/Root` or no `/Metadata`;
- the PDF carries no attachment under a name the standard uses;
- the XML is not well-formed.

A document with no root element reports `well-formed`, because that is what the XML reader sees —
the point is that it is a `FacturXFormatException` and not an `XmlException` escaping from under you.
