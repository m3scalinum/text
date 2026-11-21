# 05 — Templates

A template is a Typst file with declared parameters, shipped as data, filled from C#. It is the
answer to "the layout is designed by someone who writes Typst, the data comes from the
application".

## 1. Template package layout

```
templates/invoice/
├── template.toml        manifest: identity, parameters, entry point
├── template.typ         the layout, defines `#let render(params)`
├── assets/
│   └── logo.svg
└── README.md            optional, human documentation
```

`template.toml`:

```toml
name = "invoice"
version = "2.0.0"
entry = "template.typ"
description = "Multi-page US invoice: masthead with logo, grouped line items, tax, totals and a legal footer"
authors = ["Finance <finance@example.com>"]

[parameters]
title       = { type = "string",  required = true }
number      = { type = "string",  required = true }
issued      = { type = "date",    required = true }
due         = { type = "date",    required = true }
currency    = { type = "string",  default = "USD" }
seller      = { type = "object",  required = true }
customer    = { type = "object",  required = true }
items       = { type = "array",   of = "object", required = true }
tax_rate    = { type = "number",  default = 0.0825 }
amount_paid = { type = "number",  default = 0 }
notes       = { type = "string",  default = "" }
```

`seller`, `customer` and every entry of `items` are free-form dictionaries: `seller` carries the
masthead and the legal footer (name, `logo` asset path, `address` lines, `phone`, `email`,
`website`, `terms`, `remit`, `bank`, `tax_id`, `legal`), each item carries `category`, `sku`,
`description`, `details`, `quantity`, `unit_price` and an optional `taxable` flag. The layout
groups the items by category, computes the amounts, subtotals and the balance due.

Supported parameter types: `string`, `number`, `boolean`, `date`, `asset`, `array` (with `of`:
`string` | `number` | `object`), `object` (free-form dictionary).

The manifest is parsed by a TOML reader that supports exactly what a manifest needs: top-level
keys, one level of `[table]` headers, strings with escapes, booleans, integers, reals, single-line
arrays and inline tables. Dates, multi-line strings, dotted keys and arrays of tables are rejected
with a message naming the line, rather than half-supported. A full TOML library would be a
dependency for a file format this library also writes by hand.

## 2. The Typst side

`template.typ` MUST define a function named `render` taking a single dictionary:

```typ
#let render(p) = {
  set document(title: p.title, author: p.seller.name)
  set page(paper: "us-letter", margin: 2cm, numbering: "1 / 1")
  set text(size: 10pt)

  grid(
    columns: (1fr, auto),
    [#text(size: 18pt, weight: "bold")[#p.title]],
    image(p.seller.logo, width: 3cm),
  )

  v(1em)
  table(
    columns: (1fr, auto, auto, auto),
    table.header([Description], [Qty], [Unit price], [Amount]),
    ..p.items.map(it => (
      it.description,
      str(it.quantity),
      [#it.unit_price],
      [#it.quantity * it.unit_price],
    )).flatten(),
  )

  v(1em)
  align(right)[*Total: #p.items.fold(0.0, (sum, it) => sum + it.quantity * it.unit_price) #p.currency*]
  if p.notes != "" { parbreak(); text(size: 8pt, fill: gray)[#p.notes] }
}
```

The file defines `render` and **does not call it**. The host appends `#render((…))` with the bound
parameters, so the template stays a valid Typst document that can be previewed with any Typst
editor — it simply renders nothing until it is given parameters.

`Load` checks that the entry file contains `#let render`. That is a textual check, not a parse: the
managed layer has no Typst parser, and compiling at load time would make every `Load` pay for a full
layout. A template that defines `render` under another name fails at render time with a compiler
error pointing at the appended call.

## 3. Binding from C#

### 3.1 Untyped

```csharp
using M3scalinum.Text.SharpTypst.Templates;

var template = TypstTemplate.Load("templates/invoice");   // directory
using var compiler = new TypstCompiler();

byte[] pdf = template.Render(compiler, new Dictionary<string, object?>
{
    ["title"]    = "Invoice 2026-0042",
    ["number"]   = "INV-2026-0042",
    ["issued"]   = new DateOnly(2026, 3, 1),
    ["due"]      = new DateOnly(2026, 3, 31),
    ["seller"]   = new Dictionary<string, object?>
    {
        ["name"] = "Northwind Systems, Inc.",
        ["logo"] = "assets/logo.svg",
    },
    ["customer"] = new Dictionary<string, object?> { ["name"] = "Cascade Health Group" },
    ["items"]    = new[]
    {
        new Dictionary<string, object?>
        {
            ["description"] = "Edge gateway",
            ["quantity"]    = 4,
            ["unit_price"]  = 1249.00m,
        },
    },
});
```

### 3.2 Typed

The options type is generated from the manifest, so the declaration is one line:

```csharp
[TypstTemplate("templates/invoice")]
public partial record InvoiceOptions;

var template = TypstTemplate<InvoiceOptions>.Load("templates/invoice");
byte[] pdf = template.Render(compiler, new InvoiceOptions
{
    Title    = "Invoice 2026-0042",
    Number   = "INV-2026-0042",
    Issued   = new DateOnly(2026, 3, 1),
    Due      = new DateOnly(2026, 3, 31),
    Seller   = new Dictionary<string, object?> { ["name"] = "Northwind Systems, Inc." },
    Customer = new Dictionary<string, object?> { ["name"] = "Cascade Health Group" },
    Items    = [ /* … */ ],
});
```

The generator emits, for each declared parameter, a property named in `PascalCase` from the
manifest key (`line_count` → `LineCount`), typed from the manifest, `required` when the manifest
says so, and initialised to the declared default otherwise. It also emits `ToParameters()`, which
maps the properties back to the manifest names.

**Decision — no reflection-based binding.** Reflection would break trimming and AOT, and would
make a typo in a property name a runtime surprise. The generic template requires the options type
to implement `ITypstTemplateOptions`, and the generator writes that implementation. Rejected:
`System.Text.Json`-style reflection binding.

### 3.3 The generator

`M3scalinum.Text.SharpTypst.Templates.Generator` is a Roslyn incremental generator, shipped as an analyzer. Its
package adds every `template.toml` in the project to `AdditionalFiles`, so a consumer wires nothing
up beyond the package reference.

| Manifest type | Generated C# |
| --- | --- |
| `string` | `string` |
| `number` | `double` |
| `boolean` | `bool` |
| `date` | `DateOnly` |
| `asset` | `string` (a virtual path) |
| `object` | `IReadOnlyDictionary<string, object?>` |
| `array` of *T* | `IReadOnlyList<T>` |

| Diagnostic | Severity | When |
| --- | --- | --- |
| `SHT001` | Error | No `template.toml` matches the attribute's path |
| `SHT002` | Error | A `[parameters]` entry is malformed |
| `SHT003` | Error | The annotated type is not `partial` |
| `SHT004` | Error | The annotated type declares a primary constructor |
| `SHT005` | Warning | A parameter declares an unknown type; it is generated as `object?` |

The generator reads only the `[parameters]` table, with its own small reader rather than the
runtime's TOML parser: an analyzer targets `netstandard2.0`, and the runtime parser uses APIs that
do not exist there. The two are kept apart deliberately — the generator needs a dozen lines of
parsing, not a general TOML implementation.

## 4. Parameter conversion

| Manifest type | C# accepted | Emitted Typst |
| --- | --- | --- |
| `string` | `string` | `"escaped"` |
| `number` | `int`, `long`, `double`, `decimal` | invariant literal (`3`, `1.5`) |
| `boolean` | `bool` | `true` / `false` |
| `date` | `DateOnly`, `DateTimeOffset` | `datetime(year: …, month: …, day: …)` |
| `asset` | `string` (virtual path) or `byte[]` + name | `"path"`; bytes are registered in the world |
| `array` | `IEnumerable<T>` | `(a, b, c)` |
| `object` | `IReadOnlyDictionary<string, object?>` or a record | `(key: value, …)` |

Rules:

- Strings are escaped with the same writer as the document model, then quoted with Typst string
  escaping (`\` and `"`).
- `decimal` is formatted with the invariant culture and no exponent.
- `null` for a parameter with a default omits the key, so the template's default applies.
- `null` for a required parameter throws `TypstTemplateException` naming the parameter, before any
  compilation happens.
- An unknown key (not in the manifest) throws, unless the template declares
  `allow_extra_parameters = true`.

## 5. Rendering pipeline

```
TypstTemplate.Load(dir)
   ├─ parse template.toml            → TypstTemplateManifest
   ├─ read template.typ              → source "template.typ"
   ├─ enumerate assets/              → world assets
   └─ validate: entry exists, render defined, parameters well-formed

Render(compiler, parameters)
   ├─ validate parameters against manifest
   ├─ build world: template.typ + assets + caller assets
   ├─ append:  #render((title: "…", items: (…), …))
   ├─ compile (paged by default)
   └─ return byte[] / CompilationResult
```

`Render` returns `byte[]` for the common case and `RenderResult` (document + diagnostics) when the
caller wants warnings:

```csharp
using var result = template.RenderDocument(compiler, options);
foreach (var d in result.Diagnostics) logger.LogWarning("{Message} at {Location}", d.Message, d.Location);
byte[] pdf = result.ExportPdf();
```

## 6. Embedded templates

Templates can ship inside an assembly:

```csharp
var template = TypstTemplate.LoadEmbedded(typeof(InvoiceTemplate).Assembly, "Templates.Invoice");
```

The resource naming convention is `<RootNamespace>.<Folder>.<File>`, with `/` replaced by `.`.
`LoadEmbedded` reads the manifest and every file under the prefix into memory; nothing touches the
disk. This is the recommended deployment for libraries that ship their own templates.

A template can also be built from a file set the host already holds — a database row, a zip, an
HTTP response:

```csharp
var template = TypstTemplate.Load(manifestToml, new Dictionary<string, byte[]>
{
    ["template.typ"] = entryBytes,
    ["assets/logo.svg"] = logoBytes,
});
```

Keys are paths relative to the template root, with `/` separators. The entry file must be present;
every other file becomes an asset the template can reference.

## 7. Validation and errors

| Situation | Exception | When |
| --- | --- | --- |
| Manifest missing or unparsable | `TypstTemplateException` | `Load` |
| `entry` file absent | `TypstTemplateException` | `Load` |
| `render` not defined in the entry file | `TypstTemplateException` | `Load` (checked by parsing the syntax tree, not by compiling) |
| Required parameter missing | `TypstTemplateException` | `Render`, before compilation |
| Unknown parameter | `TypstTemplateException` | `Render` |
| Type mismatch | `TypstTemplateException` | `Render` |
| Typst error inside the template | `TypstCompilationException` | `Render`, with positions in `template.typ` |

`TypstTemplateException` carries the template name, the parameter name and the manifest path, so a
misconfigured deployment is diagnosable from the log line alone.

## 8. Versioning a template

- `version` in the manifest is the template's own version, independent of the package.
- A template MAY declare `min_typst = "0.15.0"`; `Load` fails if the bundled compiler is older.
- Changing a parameter's type or removing it is a major version bump of the template. Adding an
  optional parameter is a minor bump.

## 9. What a template is not

- Not a theme system: it does not compose with other templates.
- Not a sandbox: a template is Typst code and can read any asset registered in the world. Templates
  are trusted input, like a Razor view.
- Not a data source: it never fetches anything. Everything it reads is registered by the caller.

## 10. The shipped layouts

`M3scalinum.Text.Templates` ships seven layouts as embedded resources, each with its manifest:

| Id | Document | Parameters beyond `title`, `number`, `issued` and `labels` |
| --- | --- | --- |
| `invoice` | invoice (facture) | `due`, `currency`, `seller`, `customer`, `items`, `tax_rate`, `amount_paid`, `notes` |
| `quotation` | quotation (devis) | `valid_until`, `currency`, `seller`, `customer`, `items`, `tax_rate`, `notes` |
| `statement` | account statement (relevé de compte) | `period_start`, `period_end`, `account`, `opening_balance`, `transactions`, `currency`, `notes` |
| `attestation` | attestation | `employer`, `employee`, `purpose`, `place` |
| `contract` | service or employment contract (contrat) | `effective`, `party_a`, `party_b`, `clauses`, `place`, `notes` |
| `payslip` | payslip (bulletin de paie) | `period_start`, `period_end`, `employer`, `employee`, `earnings`, `contributions`, `currency`, `notes` |
| `purchase-order` | purchase order (bon de commande) | `buyer`, `supplier`, `items`, `delivery`, `currency`, `tax_rate`, `terms`, `notes` |

Their typed records are `InvoiceOptions`, `QuotationOptions`, `StatementOptions`,
`AttestationOptions`, `ContractOptions`, `PayslipOptions` and `PurchaseOrderOptions`, and
`TemplateCatalog` resolves a layout by id and version.

The three newest layouts follow the same shape as the four they join: every free-form block is a
dictionary the caller shapes, and the layout reads only the keys it declares as optional.

- `contract` prints two parties (each with `name`, optional `address` lines, `contact`,
  `registration` and `representative`), then one numbered article per entry of `clauses`, each a
  `heading` and a `text` with an optional `items` list rendered as bullets, then the signature block
  with one line per party and the place and date above it.
- `payslip` prints an employer and an employee block, the period, the `earnings` lines and the
  `contributions` lines — each a `label`, an optional `base`, an optional `rate` as a fraction and an
  `amount` — and computes the gross, the withheld total and the net. Nothing is recomputed from a
  rate: the amounts are the caller's, because a payslip is a declaration, not a calculator.
- `purchase-order` prints the buyer, the supplier and the `delivery` block (`name`, `address`
  lines, `contact`, `date`), the ordered `items` with their quantity, unit price and amount, the
  subtotal, the tax at `tax_rate` and the total, then the `terms` and the two signature lines.

```csharp
var catalog = TemplateCatalog.Default;
var descriptor = catalog.Get("contract");

var embedded = descriptor.Template;                     // resources of the package
var onDisk = TypstTemplate.Load("templates/contract");   // the same files, from a directory
```

Labels are data, not layout text: `TemplateLabels.For("invoice", TemplateLanguage.English)` returns
the dictionary the `labels` parameter expects, and a layout reads every word, date pattern and
number separator from it. A test compares the keys a layout reads with the keys both languages
declare, so a label cannot be forgotten on one side, and the render tests compile each layout in
both languages.
