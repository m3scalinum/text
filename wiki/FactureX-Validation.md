# FactureX — validation

## The call

```csharp
using M3scalinum.Text.Pdf.FactureX;

var result = FacturXValidator.Validate(invoice);

if (!result.IsValid)
{
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"{issue.Code}: {issue.Message}");
    }
}

result.ThrowIfInvalid();   // or throw once, with every issue in the message
```

The codes are the standard's — `BR-02`, `BR-CO-15`, `BR-S-08` — so they match whatever your accountant
or your customer's platform reads.

## What the rules decide

Only what can be decided from the document itself:

- **Mandatory terms.** A number, a date, both parties, a currency, at least one line (except in the
  profiles that allow none).
- **Line arithmetic.** Quantity × unit price against the line net amount, and the lines against the
  header totals. In cascade invoicing the header counts root lines only.
- **VAT.** The breakdown must match the lines that carry it, category by category and rate by rate,
  and the exemption categories must state a reason.
- **Closed code lists.** The nine VAT categories, the payment means codes, the document type codes, the
  currencies and the units, checked for shape.
- **Dates.** A due date before the issue date is refused.

## Credit notes

A credit note is the same document with the opposite sign: `TypeCode` `381`, `IsCreditNote` reports it,
negative amounts are legitimate, and the rendered PDF is titled *Avoir*.

## What is not decided

The full Schematron rule set, the transmission to a platform (Chorus Pro, Peppol) and the business
rules that depend on data a platform holds rather than on the document. Those are a different job.

The gap between "valid" and "accepted" is real: passing here means the document is internally
consistent and well-formed, not that a given platform will accept it.

## What catches the rest

| Layer | Tool |
| --- | --- |
| EN 16931 rules | `FacturXValidator`, on every render |
| Official XML schema | the FNFE-MPE reference library, in CI |
| PDF/A-3b conformance | veraPDF, in CI |

The two external tools are deliberately part of the pipeline: the schema validator found ordering
defects and a wrong namespace that the string assertions could not see, and veraPDF found the XMP
problems.
