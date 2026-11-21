# FactureX — the Peppol CIUS

A Peppol access point does not accept a document because it is valid UBL. It accepts it because the
CIUS says so: it routes on the profile identifiers and the electronic addresses, and it refuses a
document that names none. `FacturXValidator.Validate` covers the EN 16931 rules; `PeppolBisValidator`
adds the Peppol BIS Billing 3.0 rules the document decides about itself.

## The call

```csharp
using M3scalinum.Text.Pdf.FactureX;

// Applied automatically when the document declares the CIUS, in BT-23 or BT-24.
var result = FacturXValidator.Validate(invoice);

// Required even from a document that declares nothing.
var peppol = FacturXValidator.Validate(invoice, PeppolBisValidator.Default);
```

A document that declares the CIUS is built with the two identifiers it names:

```csharp
var invoice = new FacturXInvoice
{
    Number = "FA-2026-0200",
    BusinessProcess = PeppolBisValidator.BusinessProcess,   // BT-23, cbc:ProfileID
    SpecificationId = PeppolBisValidator.SpecificationId,   // BT-24, cbc:CustomizationID
    // …
};
```

`UblWriter` writes both, in the order `InvoiceType` fixes: `CustomizationID`, `ProfileID`, `ID`. Left
empty, `CustomizationID` falls back to the Factur-X guideline URN — which is right for a PDF and
wrong for an access point, and `BR-PEPPOL-03` says so.

## What is checked

| Area | Rule |
| --- | --- |
| Profile | the business process is present and published (`BR-PEPPOL-01`, `02`); the specification identifier names the CIUS (`03`) |
| Routing | the seller and buyer electronic addresses (`04`, `05`) and their EAS scheme codes (`06`) |
| References | a buyer or order reference (`07`), one note at most (`08`) |
| Type codes | the invoice and credit note lists (`09`, `10`), and the German-only and Slovak-only codes (`11`) |
| Credit notes | a `381` or `384` names the invoice it corrects (`12`) |
| Payment | a direct debit carries its mandate (`13`), a credit transfer its account (`14`) |
| VAT | reverse charge and intra-community need the buyer's VAT identifier (`15`, `16`), every line category is in the breakdown (`17`), an exempt line carries no rate (`22`) |
| Parties | the seller carries one of `BT-29`, `BT-30`, `BT-31` (`18`) |
| Allowances and charges | percentage and base amount go together (`19`, `20`), the amount matches the two (`21`) |

## What is not

The official rule set is Schematron — around 165 assertions read by an XSLT 2.0 processor — and it is
the reference. It sees the XML, not the model: the exemption reason *code* (`BT-121`), the cardinality
of `cac:BillingReference`, the tax representative, the `PEPPOL-COMMON` identifier grammars. Those stay
with the artefact, plugged in through `IInvoiceValidator`:

```csharp
public sealed class SchematronValidator : IInvoiceValidator
{
    public IReadOnlyList<FacturXIssue> Validate(FacturXInvoice invoice)
    {
        // Run the official Schematron over UblWriter.Write(invoice) and map its failed
        // assertions to FacturXIssue.
        return [];
    }
}

var result = FacturXValidator.Validate(invoice, new SchematronValidator());
```

The issues of both rule sets are merged; an issue both report appears once.

The test suite runs the OpenPEPPOL publication's own sample invoice, credit note and direct-debit
snippet as the positive cases, and one document built from the model per rule as the negative case.
