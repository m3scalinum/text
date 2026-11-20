# 13 — Factur-X

`M3scalinum.Text.Pdf.FactureX` turns a CII or UBL invoice into a Factur-X PDF: a PDF/A-3 whose embedded XML
is the machine-readable invoice the receiver books, and whose XMP metadata declares it as such.

This document is normative for that package. The main library is described in
[03](03-compilation.md); this one adds the e-invoicing layer on top of it.

## 1. Scope

| In scope | Detail |
| --- | --- |
| Reading | CII (`rsm:CrossIndustryInvoice`) and UBL (`Invoice`) into one model |
| Writing | The CII syntax, which Factur-X mandates, from an invoice built in C# |
| Validation | The EN 16931 rules decidable from the document: mandatory terms and arithmetic |
| Rendering | A4 layout through the document model, PDF/A-3b, XML attached, XMP patched |
| XMP | The `fx:` description and its `pdfaExtension:schemas` entry |

| Out of scope | Why |
| --- | --- |
| Full EN 16931 / Schematron conformance | Roughly 200 rules with code lists and profile cardinalities; the CI job validates the XML against the official XSD instead |
| Signature, transmission, Chorus Pro / Peppol | A separate concern with its own protocol |
| Order-X, EXTENDED German extensions | Different documents; the profile enum exists so they can be added |
| Converting CII back to UBL | Factur-X only accepts CII, so nothing needs it |

## 2. The model

`FacturXInvoice` is a flat description of what the PDF prints and what the XML states. Money is
`decimal`, dates are `DateOnly`, and the conformance level is `FacturXProfile`.

```csharp
var invoice = FacturXReader.Read(xml);          // CII or UBL, sniffed from the root element

var issues = FacturXValidator.Validate(invoice);
var pdf = FacturXRenderer.Render(invoice, xmlBytes);   // embeds the XML it came from
```

**Decision — one model for both syntaxes.** CII and UBL carry the same EN 16931 semantic model; a
second model would double every consumer of it. `FacturXReader` dispatches on the root element and
the two readers fill the same type, so `invoice` above does not tell which syntax produced it.
Rejected: one model per syntax (two validators, two renderers, two sets of tests).

**Decision — matching on local names.** Prefixes differ between producers; the element names are
fixed by the schemas. `Xml.Child("ID")` therefore ignores the namespace, which is what lets one
reader serve both syntaxes and every producer that is not the one in the fixture.

## 3. Reading

| Entry point | Input |
| --- | --- |
| `FacturXReader.Read(string)` | XML text |
| `FacturXReader.Read(Stream)` | XML stream |
| `FacturXReader.Read(ReadOnlySpan<byte>)` | XML bytes |
| `FacturXReader.PeekSyntax(Stream)` | `"CII"` or `"UBL"`, without loading the tree |
| `FacturXReader.ReadPdf(ReadOnlySpan<byte>)` | the invoice embedded in a Factur-X PDF |
| `FacturXReader.TryExtractXml(ReadOnlySpan<byte>, out name, out xml)` | the attachment bytes, unparsed |

The reader refuses a document with a DTD: entity expansion is how a 1 KB invoice becomes gigabytes.
`MaxCharactersInDocument` bounds the rest. An unknown root and malformed XML both surface as
`FacturXFormatException` with the reason in the message.

The subset covers the header, the parties and their tax identifiers, the payment means, the lines,
the VAT breakdown and the totals — the terms a printed invoice needs. Anything else stays out of the
model rather than being guessed at.

**The round trip.** `ReadPdf` is the receiving half of the exchange: it returns the bytes the sender
attached, not a re-serialization of them, so a platform can verify what it was given. The attachment
is located through the catalog's name tree (`/Names` → `/EmbeddedFiles` → `/Filespec` → `/EF`), and
inflated when the writer compressed it. The name tree nests, so the dictionary is read by balancing
`<<` and `>>` rather than by a non-greedy match, which stops at the inner delimiter. `factur-x.xml`
wins over any other attachment; failing that, the first `.xml`. A filter other than `FlateDecode` is
refused by name instead of being read as garbage.

## 4. Validation

`FacturXValidator.Validate` returns every issue at once; `FacturXValidationResult.ThrowIfInvalid`
raises `FacturXValidationException` carrying the full result.

| Code | Rule |
| --- | --- |
| `BR-02` | the invoice number (BT-1) is present |
| `BR-03` | the issue date (BT-2) is present and readable |
| `BR-04` | the document type code (BT-3) is three digits |
| `BR-05` | the currency is an ISO 4217 code (BT-5) |
| `BR-06`, `BR-07` | the seller and buyer names are present |
| `BR-08`, `BR-09` | the seller and buyer postal addresses carry a country |
| `BR-16` | a profile above BASIC WL carries at least one line |
| `BR-18` | a standard-rated invoice carries the seller's VAT identifier |
| `BR-CL-16` | the payment means code is one the standard allows |
| `BR-CL-17` | every VAT category is one of the nine the standard defines |
| `BR-CL-23` | a unit code has the shape of a UN/ECE code |
| `BR-CO-4` | every line carries a VAT category |
| `BR-CO-9` | each breakdown basis is the sum of the lines carrying that category and rate |
| `BR-CO-10` | line amount = quantity × unit price, and the sum matches the line total |
| `BR-CO-13`, `BR-CO-14` | the VAT bases and amounts sum to the totals |
| `BR-CO-15` | tax basis + tax total = grand total |
| `BR-CO-16` | grand total − prepaid = amount due |
| `BR-CO-17` | each VAT breakdown amount = basis × rate |
| `BR-E-10` | an exempt breakdown carries an exemption reason |
| `BR-S-08` | a standard-rated breakdown carries a rate above zero |
| `BR-Z-08` | an exempt category carries no rate |

Amounts compare with a 0.02 tolerance, which absorbs the cent-level rounding the standard allows.

**Decision — closed lists are enforced, open ones are checked for shape.** The nine VAT categories
and the payment means codes are a closed set: a document carrying anything else cannot be booked, so
it is rejected. Units of measure and currencies are thousands of entries; a partial copy would reject
valid documents, which is worse than not checking, so only the shape is verified.

**Decision — the Schematron rule set is not embedded.** EN 16931 publishes its business rules as
Schematron, which needs an XSLT 2.0 processor. The rules above are the ones decidable from the
document alone, reimplemented in C#; the official XSD validation in CI covers the structure, and a
receiver's own Schematron covers the rest.

**Credit notes.** `TypeCode` `381` marks a credit note: `IsCreditNote` reports it, negative amounts
are legitimate, and the rendered document is titled *Avoir* rather than *Facture*. The arithmetic
rules are unchanged — they hold for negative values.

## 5. Writing

`CiiWriter.Write(invoice)` returns CII text, `CiiWriter.WriteBytes(invoice)` the bytes to embed.

**Decision — the serializer writes to a stream, not a `StringBuilder`.** `XmlWriter` over a string
declares `encoding="utf-16"` whatever its `Encoding` setting says; the bytes were UTF-8, and the
official validator refused the file. Writing to a `MemoryStream` makes declaration and bytes agree.

**Decision — the element order is the EN 16931 profile's, not the base CII schema's.** The profile
redefines several sequences, and four of them are checked by the official XSD: `TypeCode` before
`IssueDateTime`; `TypeCode` before `CategoryCode` in a line tax; `PostcodeCode` before `LineOne`,
then `CityName`, then `CountryID`; and `IncludedSupplyChainTradeLineItem` in the `ram:` namespace,
not `rsm:`. Each was found by validating against that schema, and each has a regression test.

## 6. Rendering

`FacturXRenderer.Render` validates (unless `FacturXRenderOptions.Validate` is false), builds the
Typst source, compiles with `PdfStandard.PdfA3b`, and patches the XMP.

```csharp
byte[] pdf = FacturXRenderer.Render(invoice);            // writes the CII, embeds it
byte[] pdf2 = FacturXRenderer.Render(invoice, xmlBytes);  // embeds the caller's XML, byte for byte
byte[] pdf3 = FacturXRenderer.ReadAndRender(xml);         // read, then embed what came in
```

`ReadAndRender` embeds the original bytes only when the source is CII: Factur-X does not accept UBL,
so a UBL source is converted through `CiiWriter` first.

The timestamp written into the PDF is the invoice date at midnight UTC, so two renders of the same
invoice are byte-identical — the `Ident` is the invoice number.

Amounts print with the French convention (`1 204,00`), built from the invariant format. A
`CultureInfo` would be the obvious implementation and the wrong one: a host running with
`InvariantGlobalization` — the default in many containers — has no ICU and `GetCultureInfo("fr-FR")`
throws there.

## 7. The XMP patch

Typst writes its XMP from a fixed set of fields and offers no hook for an extension schema. Factur-X
requires a `fx:` description naming the embedded file, its type, the standard version and the
conformance level, registered through `pdfaExtension:schemas`. Without it the file is a PDF/A-3 with
an attachment, which Chorus Pro and the German receivers reject.

`FacturXPdf.AddMetadata(pdf, fileName, profile)` performs a **PDF incremental update**:

1. Read `startxref`, the trailer, `/Root`, the catalog's `/Metadata` reference and that stream.
2. Insert the schema entry inside the existing `pdfaExtension:schemas` bag, and the four `fx:`
   properties in their own `rdf:Description` before `</rdf:RDF>`.
3. Append the rewritten metadata object under its **own object number**, a one-entry cross-reference
   section, and a trailer with `/Prev` pointing at the previous one.

Nothing before the appended block changes: every other object, the existing XMP content, `/ID` and
the PDF/A marker stay byte-identical, so a patch can never break the conformance Typst enforced. The
patch refuses a compressed `/Metadata` stream and a document without a PDF/A schema bag — it rewrites
what it can read, and says so otherwise.

**Decision — incremental update, not a rewrite.** A full rewrite means re-serializing every object
and every cross-reference table, which is a PDF library. An incremental update is ~60 lines of
string surgery, and the failure mode of getting it wrong is a reader ignoring the new version — the
old one is still intact.

## 9. The French CIUS

France does not apply EN 16931 bare: a national overlay, the CIUS, adds the identifiers a platform
routes on and the rules that reject a document it cannot deliver. A document falls under it because
its issuer says so, and in the data that declaration is `BT-23` — the *cadre de facturation*, `B1`
for a B2B invoice, `S1` for a public-sector one.

**Decision — the French rules are gated on `BT-23`, not on the country.** Gating on the seller's
country demands a SIREN from every invoice a French company issues to a foreign customer, which the
standard does not; `BT-23` is a declaration by the issuer, and a document that claims a French
framework is exactly the document held to the French rules.

| Term | Where it goes | Scheme |
| --- | --- | --- |
| `BT-29` party identifier (SIRET) | `SellerTradeParty/ID` | `0009` |
| `BT-30` legal registration (SIREN) | `SellerTradeParty/SpecifiedLegalOrganization/ID` | `0002` |
| `BT-47` buyer legal registration | `BuyerTradeParty/SpecifiedLegalOrganization/ID` | `0002` |
| `BT-34` / `BT-49` electronic address | `URIUniversalCommunication/URIID` | `0009`, `0002` or `0225` |

The schema sequences these tightly: `ID` before `Name`, `SpecifiedLegalOrganization` after `Name`,
`URIUniversalCommunication` after the postal address and before `SpecifiedTaxRegistration`. A party
written in the wrong order fails the official XSD, which is what the `facturx` job checks.

| Code | Rule |
| --- | --- |
| `BR-FR-09` | when both are given, the SIRET starts with the SIREN |
| `BR-FR-10` | a French invoice carries the seller's SIREN (`BT-30`) |
| `BR-FR-12` | it carries the buyer's electronic address (`BT-49`) |
| `BR-FR-13` | it carries the seller's electronic address (`BT-34`) |
| `BR-FR-32` | a SIREN is nine digits |

### The profile

`FacturXProfile.ExtendedCtcFr` writes the guideline URN
`urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr`. It is a subset of
EXTENDED, so the XMP `fx:ConformanceLevel` stays `EXTENDED` — only the URN tells the two apart, and
the FNFE-MPE validator reads the level back as `extended-ctc-fr` from it.

### The two mentions the profile does not carry

The operation category (goods, services, or both) and the VAT-on-debits option are two of the four
mentions the mandate added, and **neither has a home in the CII of the profile**: the French
Schematron never reads them, and EN 16931 has no such term. `OperationCategory` and `VatOnDebits`
are therefore rendered on the document and left out of the XML. Inventing an element for them would
produce a document no platform expects, which is worse than a document that says nothing.

### The place of delivery

`FacturXInvoice.DeliveryAddress` is `BG-13`, written as
`ApplicableHeaderTradeDelivery/ShipToTradeParty`. It is one of the four mentions the mandate added,
and it is required only when the goods are delivered somewhere other than where they are invoiced —
so the writer emits nothing at all when it is empty, rather than an empty party. The renderer prints
it as a *Livraison* block under the buyer's address.

### Notes with a subject code

`FacturXNote` carries `BT-22` with the `BT-21` code that qualifies it. The code is what turns a
remark into data: `PMT` (recovery costs), `PMD` (late-payment penalties) and `AAB` (early-payment
discount) are the three the mandate requires, and `BR-FR-05` rejects a document missing any of them.
`NoteType` sequences `Content` before `SubjectCode`; the reverse fails the official XSD.

| Code | Rule |
| --- | --- |
| `BR-FR-05` | the three legal mentions are present, each with its subject code |
| `BR-FR-06` | a subject code qualifies one note, not two |

### Cascade invoicing

Under the `S8`, `B8`, `M8`, `S9`, `B9` and `M9` frameworks a document is a cascade invoice: a
`GROUP` line stands for a sub-invoice issued by another seller, and the `DETAIL` lines below it
carry that seller's own lines. `FacturXLine` gains `StatusReason` (`BT-127`), `ParentLineId`,
`LineSeller` (`BG-29`), `PriorInvoiceNumber` / `PriorInvoiceDate` (`BT-25` / `BT-26`), `TaxTotal`
and `ObjectIdentifier` (`BT-128`, scheme `AFL`).

**Decision — the header totals count the root lines only.** A `GROUP` line already contains its
sub-lines, so summing every line double-counts them and `BR-CO-9` / `BR-CO-10` would reject a
correct document. The validator sums the lines without a parent when the framework is a cascade one.

| Code | Rule |
| --- | --- |
| `BR-FR-MV-01` | every line carries a line status reason |
| `BR-FR-MV-02` | at least one root `GROUP` line |
| `BR-FR-MV-03` | a root group line names its sub-invoice, its seller and its VAT |
| `BR-FR-MV-05` | a group line's amount is the sum of its sub-lines |
| `BR-FR-MV-06` | a detail line agrees with its parent on the seller |
| `BR-FR-MV-07` | a detail line agrees with its parent on the sub-invoice number |
| `BR-FR-MV-09` | a group line's VAT is the sum of its sub-lines' |
| `BR-FR-MV-12` | root group lines carry distinct sub-invoice numbers |
| `BR-FR-MV-13` | a cascade invoice is not self-billed |
| `BR-FR-MV-14` | a corrective cascade line names the invoice it corrects |
| `BR-FR-BD-02` | a bidirectional invoice carries exactly two root group lines |
| `BR-FR-BD-13` | a bidirectional invoice is self-billed |
| `BR-FR-BD-14` | a bidirectional line names the prior invoice and its date |

**What is not implemented.** The French Schematron has 58 rules and 171 assertions; the ones above
are those decidable from the document alone. What remains needs the lifecycle data a platform
exchanges — the `BAR` / `TXD` treatments, the `AFL` numbering across sub-invoices, the accounting
currency checks — which a document cannot assert about itself. The reference Schematron ships in the
FNFE-MPE publication and is the authority for what remains.

## 8. Tests and gates

| Level | What it pins |
| --- | --- |
| Unit, `tests/Text.Pdf.FactureX.Tests` | reading both syntaxes, the validation rules, the writer's round trip, its encoding declaration, the four schema orderings, the DTD refusal |
| Integration, `FacturXRenderTests` | PDF/A-3 marker, `/EmbeddedFiles`, the `fx:` block and its values, the profile carried into the XMP, a refused invalid invoice |
| Unit, `PdfExtractionTests` | the attachment is found through the name tree, inflated, and read back; a hex name, an uncompressed stream, a second attachment, and every refusal |
| CI, job `facturx` | renders the samples, extracts the attachment and validates it against the **official XSD** with the FNFE-MPE reference library, then runs **veraPDF** against PDF/A-3B |

The CI checks are the ones that matter. The FNFE-MPE library is the validator the French ecosystem
uses, and it found four defects the string assertions could not see. veraPDF is the reference
implementation of ISO 19005: it confirms that the incremental update which injects the `fx:` block
leaves the file conformant, which is the one thing the patch could plausibly break. The flavour is
passed explicitly (`--flavour 3b`): left to itself, veraPDF believes the file's own claim and
validates against PDF/A-1b.
