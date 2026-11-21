# Templates

A template is a directory with a `template.toml` manifest and a Typst file that **defines** a
`render` function. The host appends `#render((…))` with the bound parameters, so the Typst file stays
a valid document on its own.

```
templates/invoice/
├── template.toml
├── template.typ
└── assets/logo.svg
```

```toml
name = "invoice"
version = "2.0.0"
entry = "template.typ"
description = "Multi-page US invoice: masthead with logo, grouped line items, tax, totals and a legal footer"
authors = ["M3scalinum.Text.SharpTypst"]

[parameters]
title       = { type = "string", required = true }
number      = { type = "string", required = true }
issued      = { type = "date",   required = true }
due         = { type = "date",   required = true }
currency    = { type = "string", default = "USD" }
seller      = { type = "object", required = true }
customer    = { type = "object", required = true }
items       = { type = "array",  of = "object", required = true }
tax_rate    = { type = "number", default = 0.0825 }
amount_paid = { type = "number", default = 0 }
notes       = { type = "string", default = "" }
```

`seller` carries the masthead and the legal footer (`name`, `logo`, `address`, `phone`, `email`,
`website`, `terms`, `remit`, `bank`, `tax_id`, `legal`); each item carries `category`, `sku`,
`description`, `details`, `quantity`, `unit_price` and an optional `taxable` flag.

```typst
#let render(p) = {
  set document(title: p.title, author: p.seller.name)
  set page(paper: "us-letter", margin: 2cm)
  heading(level: 1)[#p.title]
  // …
}
```

## Untyped

```csharp
var template = TypstTemplate.Load("templates/invoice");

var pdf = template.Render(new TypstCompiler(), new Dictionary<string, object?>
{
    ["title"] = "Invoice 2026-0042",
    ["number"] = "INV-2026-0042",
    ["issued"] = new DateOnly(2026, 3, 1),
    ["due"] = new DateOnly(2026, 3, 31),
    ["seller"] = new Dictionary<string, object?> { ["name"] = "Northwind Systems, Inc." },
    ["customer"] = new Dictionary<string, object?> { ["name"] = "Cascade Health Group" },
    ["items"] = Array.Empty<object>(),
});
```

A missing required parameter, an unknown parameter, or a missing `render` function throws
`TypstTemplateException` before anything is compiled.

## Typed, with the source generator

Add the analyzer package and declare a partial record:

```csharp
using M3scalinum.Text.SharpTypst.Templates;

[TypstTemplate("templates/invoice")]
public partial record InvoiceOptions;
```

The generator reads `template.toml` at compile time and writes one property per parameter, typed from
the manifest — `string`, `DateOnly`, `IReadOnlyList<…>`, and so on. A renamed or removed parameter
becomes a **build error**, not a runtime surprise.

```csharp
var template = TypstTemplate<InvoiceOptions>.Load("templates/invoice");

var pdf = template.Render(new TypstCompiler(), new InvoiceOptions
{
    Title = "Invoice 2026-0042",
    Number = "2026-0042",
    Issued = new DateOnly(2026, 3, 1),
    Items = [],
    Total = "0",
});
```

`RenderDocument` returns the `CompilationResult` instead of a PDF, for another export format:

```csharp
using var document = template.RenderDocument(new TypstCompiler(), options);
var png = document.ExportPng(ppi: 300f);
```

The generator emits five diagnostics: `SHT001` (manifest not found), `SHT002` (invalid manifest),
`SHT003` (the record is not `partial`), `SHT004` (the record has a primary constructor), `SHT005`
(unsupported parameter type).

## Embedded and in-memory

```csharp
// From embedded resources: every file under the prefix is loaded.
var embedded = TypstTemplate.LoadEmbedded(typeof(Program).Assembly, "MyApp.templates.invoice");

// From a file set you already hold — a database, a zip, a test fixture.
var inMemory = TypstTemplate.Load(manifestToml, new Dictionary<string, byte[]>
{
    ["template.typ"] = typBytes,
});
```

`LoadEmbedded` matches resource names that start with the prefix and end with the relative path, so
`MyApp.templates.invoice.template.typ` is found for `template.typ`.

## Validation

| Error | When |
| --- | --- |
| Manifest not found | The directory has no `template.toml`. |
| Invalid manifest | A required key is missing, or a type is unknown. |
| Missing parameter | A `required` parameter was not supplied. |
| Unknown parameter | A key was supplied that the manifest does not declare, and `allowExtraParameters` is false. |
| Missing `render` | The entry file does not define `render`. |

## The layouts that ship in the package

`M3scalinum.Text.Templates` embeds seven layouts, each with a generated options record, a label set
in English and French, and an entry in `TemplateCatalog`:

| Id | Document | Record |
| --- | --- | --- |
| `invoice` | invoice (facture) | `InvoiceOptions` |
| `quotation` | quotation (devis) | `QuotationOptions` |
| `statement` | account statement (relevé de compte) | `StatementOptions` |
| `attestation` | attestation | `AttestationOptions` |
| `contract` | service or employment contract (contrat) | `ContractOptions` |
| `payslip` | payslip (bulletin de paie) | `PayslipOptions` |
| `purchase-order` | purchase order (bon de commande) | `PurchaseOrderOptions` |

```csharp
using M3scalinum.Text.SharpTypst.Templates;

var descriptor = TemplateCatalog.Default.Get(TemplateIds.Contract);
var template = descriptor.LoadTyped<ContractOptions>();

var pdf = template.Render(new TypstCompiler(), new ContractOptions
{
    Title = "Contrat de prestation de conseil",
    Number = "CT-2026-0007",
    Issued = new DateOnly(2026, 3, 2),
    Effective = new DateOnly(2026, 4, 1),
    PartyA = new Dictionary<string, object?> { ["name"] = "Atelier Dupont SAS" },
    PartyB = new Dictionary<string, object?> { ["name"] = "Clinique des Tilleuls" },
    Clauses = [ new Dictionary<string, object?> { ["heading"] = "Objet", ["text"] = "…" } ],
    Labels = TemplateLabels.For(TemplateIds.Contract, TemplateLanguage.French),
});
```

The labels are the whole vocabulary of a layout: the three newest ones print `CONTRAT`,
`BULLETIN DE PAIE` and `BON DE COMMANDE` in French and their English counterparts in English
without a second `.typ` file. `TemplateLabels.For` returns exactly the dictionary the `labels`
parameter expects, and a test compares the keys a layout reads with the keys both languages declare.

| Layout | Free-form blocks it reads |
| --- | --- |
| `contract` | `party_a` and `party_b`: `name`, `address` lines, `contact`, `registration`, `representative`; each `clauses` entry: `heading`, `text`, optional `items` bullet list |
| `payslip` | `employer`: `name`, `address`, `establishment`, `registration`, `legal`, `bank`; `employee`: `name`, `position`, `number`, `start_date`, `address`; `earnings` and `contributions` entries: `label`, optional `base`, optional `rate` (a fraction), `amount` |
| `purchase-order` | `buyer` and `supplier`: `name`, `address` lines, `contact`, `vat_id` / `account`; `items` entries: `description`, `reference`, `quantity`, `unit_price`; `delivery`: `name`, `address` lines, `contact`, `date` |
