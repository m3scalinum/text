# FactureX — rendering

## The three calls

```csharp
byte[] pdf  = FacturXRenderer.Render(invoice);              // writes the CII and embeds it
byte[] pdf2 = FacturXRenderer.Render(invoice, xmlBytes);    // embeds your XML byte for byte
byte[] pdf3 = FacturXRenderer.ReadAndRender(xml);           // read, check, render
```

The second is how you keep an XML that came from somewhere else: the bytes you pass are the bytes a
receiver will extract, not a re-serialization of the model.

## What comes out

- PDF/A-3b, enforced by the compiler at export — not asserted afterwards.
- The XML attached as `factur-x.xml`, which is the name the standard imposes.
- `fx:` XMP metadata declaring the conformance level, agreeing with the guideline URN inside the XML.
- The invoice date as the document's timestamp, never "now", so rendering the same invoice twice gives
  the same bytes.

That last point is worth stating: a renderer that stamps the current time makes every output
different, which turns every test into a comparison of two different documents.

## Validation, and turning it off

`Render` validates first. An invoice that breaks a rule is refused with `FacturXValidationException`
carrying every issue.

```csharp
var options = new FacturXRenderOptions
{
    Validate = false,          // only when you know why
    FileName = "factur-x.xml", // the default; some receivers insist on it
};
```

A PDF that looks authoritative is worse than no PDF when the data behind it is wrong. A document
produced with `Validate = false` still passes the XSD — schema-valid is not the same as rule-correct.

## What is checked afterwards

| Checked | By |
| --- | --- |
| The document satisfies the EN 16931 rules | `FacturXValidator`, on every render |
| The embedded XML matches the official Factur-X schema | CI, with the FNFE-MPE reference library |
| The PDF is PDF/A-3b | CI, with veraPDF |

The two external tools are what the conformance claim rests on: they found defects the string
assertions could not see.
