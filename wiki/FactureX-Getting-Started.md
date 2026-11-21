# FactureX — getting started

`M3scalinum.Text.Pdf.FactureX` produces the e-invoice the French and German ecosystems exchange: a
PDF/A-3 with the invoice XML attached (`factur-x.xml`) and the XMP metadata the standard requires.
It also takes one apart: the XML out of a PDF, byte for byte, so a receiver can verify what it was
given.

```bash
dotnet add package M3scalinum.Text.Pdf.FactureX
```

It renders through `M3scalinum.Text.SharpTypst`, which it takes from nuget.org; the rest of the work —
the model, the writer, the validator, the reader — is plain managed code.

## End to end

```csharp
using M3scalinum.Text.Pdf.FactureX;

var invoice = new FacturXInvoice
{
    Number = "FA-2026-0042",
    Profile = FacturXProfile.En16931,
    Currency = "EUR",
    IssueDate = new DateOnly(2026, 2, 1),
    Seller = new FacturXParty { Name = "Atelier Typographique", VatId = "FR40123456789" },
    Buyer = new FacturXParty { Name = "Société Exemple", VatId = "FR55987654321" },
    Lines =
    [
        new FacturXLine
        {
            Id = "1",
            Name = "Journée de conseil",
            Quantity = 2,
            UnitPrice = 150.00m,
            LineNetAmount = 300.00m,
            TaxCategory = "S",
            TaxRate = 20m,
        },
    ],
    TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = 300.00m, Amount = 60.00m }],
    Totals = new FacturXTotals
    {
        LineTotal = 300.00m,
        TaxBasisTotal = 300.00m,
        TaxTotal = 60.00m,
        GrandTotal = 360.00m,
        Due = 360.00m,
    },
};

byte[] pdf = FacturXRenderer.Render(invoice);
File.WriteAllBytes("factur-x.pdf", pdf);

// And on the receiving side:
var received = FacturXReader.ReadPdf(File.ReadAllBytes("factur-x.pdf"));
Console.WriteLine($"{received.Number} {received.Totals.GrandTotal} {received.Currency}");
```

`Render` validates first and refuses an invoice that breaks a rule, so the line above cannot quietly
produce a document that looks authoritative and is wrong.

## The other syntax

Factur-X PDFs carry CII, and that is what `Render` embeds. A counterparty that asks for UBL gets the
same invoice from `UblWriter`: an EN 16931 / Peppol BIS Billing 3.0 compatible UBL 2.1 `Invoice`, or a
`CreditNote` when the type code is `381`.

```csharp
string ubl = UblWriter.Write(invoice);
byte[] posted = UblWriter.WriteBytes(invoice);
```

A document read from UBL can go back out the same way, and a document read from CII can be written as
UBL — the model in the middle is the EN 16931 semantic model, which is what both syntaxes bind to.

## Where to go

- **[The invoice model](FactureX-Model)** — every field, and what the standard expects of it.
- **[Rendering](FactureX-Rendering)** — the PDF/A-3, the XMP, determinism.
- **[Reading](FactureX-Reading)** — from XML or from a PDF, and what the reader refuses.
- **[Validation](FactureX-Validation)** — the rules, the codes, what is checked and what is not.
- **[The French profile](FactureX-French-Profile)** — `EXTENDED-CTC-FR`, SIREN, the mandatory notes,
  cascade invoicing.
