# FactureX — the XRechnung CIUS

XRechnung is Germany's CIUS of EN 16931, published by KoSIT for UBL and for CII. An invoice to a
public authority has to be one, and since 2025 a German receiver accepts it as the B2B syntax too.
`FacturXValidator.Validate` covers the EN 16931 rules; `XRechnungValidator` adds the German rules the
document decides about itself.

## The call

```csharp
using M3scalinum.Text.Pdf.FactureX;

// Applied automatically when the document declares the CIUS in BT-24.
var result = FacturXValidator.Validate(invoice);

// Required even from a document that declares nothing.
var strict = FacturXValidator.Validate(invoice, XRechnungValidator.Default);
```

A German document is built with the identifier that declares the CIUS and the process the buyer
asks for:

```csharp
var invoice = new FacturXInvoice
{
    Number = "XR-2026-0001",
    BuyerReference = "04011000-1234512345-06",          // BT-10, the Leitweg-ID
    BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0",  // BT-23, cbc:ProfileID
    SpecificationId = XRechnungValidator.SpecificationId,             // BT-24, cbc:CustomizationID
    Seller = new FacturXParty
    {
        Contact = new FacturXContact
        {
            Name = "Erika Musterfrau",                  // BT-41
            Phone = "+49 30 1234567",                   // BT-42
            Email = "rechnung@atelier-glyphe.de",       // BT-43
        },
        // …
    },
    // …
};
```

`BT-24` is where the CIUS is declared, and it is what selects the rule set. `BT-23` is the buyer's
business process: KoSIT names `urn:fdc:peppol.eu:2017:poacc:billing:01:1.0` as its default, so a
Peppol process alone does not make a document an XRechnung one. The identifiers of the extension,
the vehicle CIUS and the 4.0 pre-release are on the same class —
`ExtensionSpecificationId`, `VehicleSpecificationId`, `DraftSpecificationId`.

`UblWriter` writes both identifiers. `CiiWriter` writes the CIUS identifier in `BT-24` when that is
what `SpecificationId` holds; left empty, it falls back to the Factur-X guideline URN, which is right
for a PDF and wrong for a German receiver.

## What is checked

| Area | Rule |
| --- | --- |
| Declaration | the specification identifier is present and published (`BR-XR-01`, `02`), the business process is present (`03`) |
| Leitweg-ID | the buyer reference is present (`04`) |
| Seller contact | the group (`05`), its name (`06`), telephone (`07`, and at least three digits, `08`), e-mail (`09`, `10`) |
| Postal terms | the seller city and post code (`11`, `12`), the buyer city and post code (`13`, `14`), a present deliver-to group (`15`, `16`) |
| Routing | the seller and buyer electronic addresses (`17`, `18`) |
| Payment | the type code list (`19`), the payment instructions (`20`), a credit transfer and its account (`21`), no mandate under a transfer (`22`), no account or mandate under a card (`23`, `24`), a direct debit and its mandate (`25`), no payee account under a direct debit (`26`), the IBAN check digits (`27`) |
| Payment terms | the `SKONTO` lines of `BT-20` (`28`) |
| VAT | the rate of each breakdown (`29`), an identified seller for the VAT categories that need one (`30`) |
| Lines | the price base quantity (`31`), the net price against the gross price (`32`), a line period inside the invoice period (`33`, `34`) |
| Allowances and charges | percentage and base amount go together (`35`, `36`), the amount matches the two (`37`) |
| German terms | a corrected invoice names what it corrects (`38`), the VAT currency differs from the invoice currency (`39`), no sub invoice lines (`40`) |

## What is not

The official rule set is Schematron — the German `BR-DE-nn`, the extension `BR-DEX-nn`, the vehicle
CIUS and the Peppol rules the CIUS incorporated — and it is the reference. It sees the XML, not the
model: whether an element is present but empty, the card information of `BG-18`, the creditor
identifier and debited account of a direct debit (`BT-90`, `BT-91`), the attachment filenames of
`BG-24`, the post-code grammars, the MIME codes of the extension. Those stay with the artefact,
plugged in through `IInvoiceValidator`:

```csharp
public sealed class SchematronValidator : IInvoiceValidator
{
    public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
    {
        // Run the official Schematron over CiiWriter.Write(invoice) or UblWriter.Write(invoice)
        // and map its failed assertions to FacturXIssue.
        return [];
    }
}

var result = FacturXValidator.Validate(invoice, new SchematronValidator());
```

`BR-XR-30` is the one rule the model narrows: `BR-DE-16` also accepts the seller tax number
(`BT-32`) and the tax representative (`BG-11`), and `FacturXParty` carries the VAT identifier only.

The three overlays are mutually exclusive and selected by declaration, XRechnung first: a document
that declares the German CIUS is held to neither the Peppol nor the French rules. One document per
rule, built from the model, and one conformant invoice are the test cases.
