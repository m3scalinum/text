# Reading

## From XML

```csharp
using M3scalinum.Text.Pdf.FactureX;

var invoice = FacturXReader.Read(File.ReadAllText("invoice.xml"));
var fromStream = FacturXReader.Read(stream);
var fromBytes = FacturXReader.Read(bytes);
```

The syntax is detected from the root element, so CII and UBL take the same path.
`FacturXReader.PeekSyntax(stream)` tells you which one a document is without loading it.

## National syntaxes

Italy and Spain do not apply EN 16931 through a CIUS: FatturaPA and Facturae are syntaxes of their
own, aligned with the same semantic model. Both read into the same `FacturXInvoice`, so nothing
downstream of the reader has to know which one arrived.

```csharp
var italian = FacturXReader.Read(fatturaPaXml);       // <FatturaElettronica>, FatturaPA 1.2.2
var spanish = FacturXReader.Read(facturaeXml);        // <fe:Facturae>, Facturae 3.2.2

var italian2 = FatturaPaReader.Read(fatturaPaXml);    // the same, without the sniffing
var spanish2 = FacturaeReader.Read(facturaeXml);
```

`PeekSyntax` answers `"FatturaPA"` for a `FatturaElettronica` root and `"Facturae"` for a `Facturae`
one. Writing works the same way as CII and UBL:

```csharp
string fatturaPa = FatturaPaWriter.Write(invoice);    // p:FatturaElettronica, versione="FPR12"
string facturae  = FacturaeWriter.Write(invoice);     // 3.2.2 namespace, SchemaVersion 3.2.2

byte[] bytes = FatturaPaWriter.WriteBytes(invoice);   // what you post
```

What the two schemas do not carry stays out of the document rather than being invented: FatturaPA has
no per-line tax amount and no term for a contract, a project or a delivery address; Facturae has no
credit-note class (a `381` is a rectificative `OR` with negative amounts and reads back as a `384`),
no exemption reason and no unit code beyond its own numeric list. `specifications/13-facturx.md`
tabulates both mappings and every gap.

The fixtures under `tests/fixtures/national` are constructed from the published schemas, not official
samples: neither publication's samples are redistributable here. They and the writers' output were
checked against the official FatturaPA 1.2.2 schema and the Facturae 3.2 schema.

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
