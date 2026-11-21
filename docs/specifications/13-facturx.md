# 13 — Factur-X

`M3scalinum.Text.Pdf.FactureX` turns a CII or UBL invoice into a Factur-X PDF: a PDF/A-3 whose embedded XML
is the machine-readable invoice the receiver books, and whose XMP metadata declares it as such.

This document is normative for that package. The main library is described in
[03](03-compilation.md); this one adds the e-invoicing layer on top of it.

## 1. Scope

| In scope | Detail |
| --- | --- |
| Reading | CII (`rsm:CrossIndustryInvoice`), UBL (`Invoice`, `CreditNote`), FatturaPA (`FatturaElettronica`) and Facturae (`Facturae`) into one model |
| Writing | CII, UBL 2.1, FatturaPA 1.2.2 and Facturae 3.2.2, from an invoice built in C# or read from any of them |
| Validation | The EN 16931 rules decidable from the document — mandatory terms and arithmetic — and the rules of the CIUS the document declares: Peppol BIS 3.0, XRechnung or the French profile |
| Rendering | A4 layout through the document model, PDF/A-3b, XML attached, XMP patched |
| XMP | The `fx:` description and its `pdfaExtension:schemas` entry |

| Out of scope | Why |
| --- | --- |
| Full EN 16931 / Schematron conformance | Roughly 200 rules with code lists and profile cardinalities; the CI job validates the XML against the official XSD instead |
| Signature, transmission, Chorus Pro / Peppol | A separate concern with its own protocol |
| National rule sets and CIUS (XRechnung, UBL-TR, Peppol PINT) | N rule sets versioned by their administrations: the caller supplies them through `IEInvoiceProfile` — see [00 §2.3](00-overview.md#23-the-document-test) |
| Country platforms (ZATCA, KSeF, MyInvois, CFDI timbrado, GST, NF-e) | The document only exists once the platform has stamped it: `IInvoiceTransport` |
| Order-X, EXTENDED German extensions | Different documents; the profile enum is frozen — no new national syntax |
| Peppol BIS beyond the document | The SMP/SML lookup, the AS4 transport and the access point's own acceptance are exchange concerns, not invoice terms |

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

**Decision — the model is the EN 16931 core, not the terms one syntax happens to carry.** A model
that stopped at what CII could state would make the UBL path lossy in both directions, and a model
that stopped at what UBL could state would lose the CII price details. The two syntaxes are bound to
the same semantic model, so the type holds the union of what both can express and each writer emits
the part its schema allows:

| Group | Terms |
| --- | --- |
| Adjustments | document-level allowances and charges (`BG-20` / `BG-21`), line-level allowances and charges (`BG-27` / `BG-28`) |
| Periods | invoice period (`BG-14`), line period (`BG-26`) |
| Contacts | seller and buyer contacts (`BG-6` / `BG-9`) |
| References | supporting documents (`BG-24`), contract (`BT-12`), project (`BT-11`), payment reference (`BT-83`) |
| Payment | SEPA mandate (`BG-19`), tax currency (`BT-6`) |
| Price | line base quantity (`BT-149`), gross price (`BT-148`), price discount (`BT-147`), item classification (`BT-158`) |

`FacturXAllowanceCharge`, `FacturXPeriod`, `FacturXContact`, `FacturXSupportingDocument`,
`FacturXClassification` and `FacturXMandate` carry those terms; `FacturXTotals` gains `AllowanceTotal`
(`BT-107`) and `ChargeTotal` (`BT-108`).

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

The subset covers the header, the parties and their tax identifiers, the contacts, the payment
means and the mandate, the invoice period, the lines with their periods, prices and adjustments, the
document-level allowances and charges, the supporting documents, the VAT breakdown and the totals —
the terms a printed invoice needs and a platform books. Anything else stays out of the model rather
than being guessed at.

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
| `BR-CO-9` | each breakdown basis is the sum of the lines carrying that category and rate, adjusted by the document-level allowances and charges assigned to it |
| `BR-CO-10` | line amount = quantity at the price for one base quantity, plus the line charges, less the line allowances; and the lines sum to the line total |
| `BR-CO-11`, `BR-CO-12` | the document allowances and charges sum to `BT-107` and `BT-108` |
| `BR-CO-13` | line total − allowances + charges = the total without VAT (`BT-109`) |
| `BR-CO-14` | the VAT amounts sum to the tax total |
| `BR-CO-15` | tax basis + tax total = grand total |
| `BR-CO-16` | grand total − prepaid = amount due |
| `BR-CO-17` | each VAT breakdown amount = basis × rate |
| `BR-CO-19`, `BR-CO-20` | an invoice period and a line period carry at least one of their two dates |
| `BR-CO-21`, `BR-CO-22` | a document-level and a line-level allowance or charge carries a reason or a reason code |
| `BR-27`, `BR-31` | a document-level allowance or charge amount is positive; the indicator states the direction |
| `BR-32`, `BR-36` | a document-level allowance or charge carries a VAT category |
| `BR-49` | a payment reference or a mandate is accompanied by a payment means code |
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

`CiiWriter.Write(invoice)` and `UblWriter.Write(invoice)` return XML text; the matching
`WriteBytes(invoice)` returns the bytes to embed or post.

```csharp
string cii = CiiWriter.Write(invoice);   // what a Factur-X PDF embeds
string ubl = UblWriter.Write(invoice);   // what a Peppol access point accepts
```

**Decision — a UBL writer, where the earlier decision was to write CII only.** That decision rested
on Factur-X embedding CII: the PDF path needs one syntax, and it is that one. It does not follow that
the package should be unable to produce the syntax its counterparties ask for. The same invoice model
already reads UBL, and a document read as UBL had no way back out; a converter for the EN 16931
semantic model is the same mapping table read in the other direction. `UblWriter` therefore emits an
EN 16931 / Peppol BIS Billing 3.0 compatible UBL 2.1 `Invoice`, or a `CreditNote` when `TypeCode` is
`381`. The PDF path is unchanged: a Factur-X PDF still carries CII, and `ReadAndRender` still converts
a UBL source through `CiiWriter` before embedding it.

**Decision — the serializer writes to a stream, not a `StringBuilder`.** `XmlWriter` over a string
declares `encoding="utf-16"` whatever its `Encoding` setting says; the bytes were UTF-8, and the
official validator refused the file. Writing to a `MemoryStream` makes declaration and bytes agree.
Both writers do it.

**Decision — the element order is the profile's, not the base schema's.** In CII the EN 16931 profile
redefines several sequences, and the official XSD checks them: `TypeCode` before `IssueDateTime`;
`TypeCode` before `CategoryCode` in a line tax; `PostcodeCode` before `LineOne`, then `CityName`, then
`CountryID`; `IncludedSupplyChainTradeLineItem` in the `ram:` namespace, not `rsm:`; and the contact
as `TelephoneUniversalCommunication` / `EmailURIUniversalCommunication`, which is what the profile's
`TradeContactType` declares. UBL 2.1 is a single sequence per type, and the writer follows it
literally: `BuyerReference` after the currency codes and before `InvoicePeriod`; the two adjustment
totals between `TaxInclusiveAmount` and `PrepaidAmount`; `PaymentID` and then `PayerFinancialAccount`,
`PayeeFinancialAccount`, `PaymentMandate`; and in a line, `LineExtensionAmount`, `InvoicePeriod`,
`AllowanceCharge`, `Item`, `Price`. Each ordering has a regression test; each was found by validating
the output against the official schema.

**Decision — the two syntaxes do not carry the same terms, and neither writer invents one.** UBL has
no subject code on a note (`BT-21`), so a note written to UBL keeps its text and loses the code; it
has no business-process element either, so `BT-23` is written in CII only. CII's
`SpecifiedProcuringProject` requires both an identifier and a name, and EN 16931 has one project
reference, so the reference fills both. The line-level `InvoiceReferencedDocument` exists only in the
EXTENDED schema — the writer emits it for `Extended` and `ExtendedCtcFr` and omits it under EN 16931,
where the official XSD would reject it. The alternative would be a document that validates nowhere.

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

The *Total HT* line prints `Totals.TaxBasisTotal` rather than the sum of the lines: a document-level
allowance or charge moves the two apart, and a footer whose own figures do not add up is a defect a
reader sees before any validator does.

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

## 10. The Peppol CIUS

Peppol BIS Billing 3.0 is a CIUS of EN 16931 with its own rule set, and it is the rule set a Peppol
access point enforces. The UBL schema is not what rejects a document: `UblWriter` could produce a
document that validates against the OASIS XSD and is still refused, because a receiver routes on
`BT-23`/`BT-24` and on the electronic addresses, and books on identifiers the XSD does not require.
`PeppolBisValidator` covers the part of the CIUS a document decides about itself.

**Decision — the official Schematron remains the reference, and this is a subset.** The published
rule set is around 165 assertions over an XSLT 2.0 processor, and it reads the XML, not the model:
`BT-121`, the `cac:BillingReference` cardinalities, the tax representative, the language of a note are
all outside what `FacturXInvoice` carries. Reimplementing it in C# would be a second, silently
diverging edition. The rules below are the ones the model states exactly, each one traceable to the
assertion it comes from; everything else is left to the artefact, plugged in through
`IInvoiceValidator`.

### The two identifiers

| Term | UBL | CII | Value |
| --- | --- | --- | --- |
| `BT-23` business process | `cbc:ProfileID` | `BusinessProcessSpecifiedDocumentContextParameter/ID` | `urn:fdc:peppol.eu:2017:poacc:billing:01:1.0` |
| `BT-24` specification identifier | `cbc:CustomizationID` | `GuidelineSpecifiedDocumentContextParameter/ID` | `urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0` |

`FacturXInvoice.BusinessProcess` was already `BT-23`; `FacturXInvoice.SpecificationId` is new and is
`BT-24`. `UblWriter` now writes both — `CustomizationID`, then `ProfileID`, then `ID`, which is the
order `InvoiceType` fixes — and `UblInvoiceReader` reads both back. Before that, a UBL document was
written with the Factur-X guideline URN as its customization identifier and no profile identifier at
all: schema-valid, and rejected by every access point.

In CII, `BT-24` *is* the Factur-X guideline URN, and that is what selects the conformance level, so
`CiiWriter` keeps writing `Profile.ToGuidelineUrn()` and ignores `SpecificationId`. The Peppol CIUS is
a UBL profile: a document that declares it and is written as CII fails `BR-PEPPOL-03`, which is the
truth.

### The rules

| Code | Source | Rule |
| --- | --- | --- |
| `BR-PEPPOL-01` | `PEPPOL-EN16931-R001` | the business process (`BT-23`) is present |
| `BR-PEPPOL-02` | `PEPPOL-EN16931-R007` | it is one the CIUS publishes |
| `BR-PEPPOL-03` | `PEPPOL-EN16931-R004` | the specification identifier (`BT-24`) begins with the CIUS URN and carries no `::` suffix |
| `BR-PEPPOL-04` | `PEPPOL-EN16931-R020` | the seller electronic address (`BT-34`) is present |
| `BR-PEPPOL-05` | `PEPPOL-EN16931-R010` | the buyer electronic address (`BT-49`) is present |
| `BR-PEPPOL-06` | `PEPPOL-EN16931-CL008` | each present address carries a scheme (`BT-34-1` / `BT-49-1`) from the EAS code list |
| `BR-PEPPOL-07` | `PEPPOL-EN16931-R003` | the buyer reference (`BT-10`) or the order reference (`BT-13`) is present |
| `BR-PEPPOL-08` | `PEPPOL-EN16931-R002` | one note at most, unless both parties are German |
| `BR-PEPPOL-09` | `PEPPOL-EN16931-P0101` | a credit note type code on a document that is not a credit note |
| `BR-PEPPOL-10` | `PEPPOL-EN16931-P0100` | the invoice type code is one of the 26 the CIUS accepts |
| `BR-PEPPOL-11` | `PEPPOL-EN16931-P0112` | `326`, `384` and `389` only from a German seller and buyer, `384` also from a Slovak pair |
| `BR-PEPPOL-12` | `DE-R-026`, `NL-R-001` | a `381` or `384` names the invoice it corrects (`BT-25`, `BG-3`) |
| `BR-PEPPOL-13` | `PEPPOL-EN16931-R061` | a direct debit (`49`, `59`) carries the mandate reference (`BT-89`) |
| `BR-PEPPOL-14` | `BR-61` | a credit transfer (`30`, `58`) carries the payment account (`BT-84`) |
| `BR-PEPPOL-15` | `BR-AE-02` | reverse charge carries the buyer's VAT identifier (`BT-48`) |
| `BR-PEPPOL-16` | `BR-IC-02` | intra-community supply carries the buyer's VAT identifier (`BT-48`) |
| `BR-PEPPOL-17` | `BR-CO-04` | every category and rate a line uses appears in the breakdown (`BG-23`) |
| `BR-PEPPOL-18` | `BR-CO-26` | the seller carries `BT-29`, `BT-30` or `BT-31` |
| `BR-PEPPOL-19` | `PEPPOL-EN16931-R041` | a percentage carries its base amount |
| `BR-PEPPOL-20` | `PEPPOL-EN16931-R042` | a base amount carries its percentage |
| `BR-PEPPOL-21` | `PEPPOL-EN16931-R040` | the amount is the base at the percentage |
| `BR-PEPPOL-22` | `BR-E-08`, `BR-Z-08` | an exempt line carries no rate above zero |

`BR-PEPPOL-12` is the one rule the core artefact does not state: Germany requires the reference for
`384` and the Netherlands for `381`, and both are applied. A correction that names no invoice cannot
be matched by any receiver, and the two national rules say the same thing one country at a time.

Certificate and scheme syntax (`PEPPOL-COMMON-R040` to `R057`, the GS1, Norwegian, Danish, Belgian,
Italian, Swedish, Australian and Dutch identifier formats) are not implemented: they are a dozen
per-scheme grammars, a partial copy would reject valid documents, and the artefact already carries
them. The same goes for `R008` (no empty element): the writers never emit one, and the model cannot
see the difference between an absent element and an empty one.

### Using it

```csharp
// The built-in rules alone: applied when the document declares the CIUS, in BT-23 or BT-24.
var result = FacturXValidator.Validate(invoice);

// The CIUS required even from a document that does not declare it, plus a rule set of your own.
var peppol = FacturXValidator.Validate(invoice, PeppolBisValidator.Default);
```

`IInvoiceValidator` is the seam for the official artefact — a Schematron processor wrapped in it, a
partner's rules, a national overlay. `FacturXValidator.Validate(invoice, validator)` runs the built-in
rules, then the supplied one, and merges the two lists; an issue both report is reported once, on
the pair of code and message.

A document that declares the Peppol CIUS is not held to the French one: both gate on `BT-23`, a
document states one business process, and the French rules would demand three legal mentions and a
SIREN that the CIUS does not.

### The fixtures

`tests/fixtures/peppol` holds the OpenPEPPOL publication's own examples — `base-example.xml`,
`base-creditnote-correction.xml` and `direct-debit.xml`, from `rules/examples` and `rules/snippets` of
the `OpenPEPPOL/peppol-bis-invoice-3` repository. They are used unmodified as the positive cases; the
documents that break a single rule are built from the model, because a mutation of the model is what
a caller actually does wrong. The rule codes and the EAS list were read from the Schematron of the
same publication. The fixtures are not validated against it here: that needs an XSLT 2.0 processor.

## 11. The national syntaxes — FatturaPA and Facturae

Italy and Spain do not apply EN 16931 bare, but they differ from France and Peppol in a way that
decides what this package does with them. A CIUS is a rule overlay on the European semantic model: the
document stays CII or UBL, and `BT-23` / `BT-24` say whose rules apply. FatturaPA (`FatturaElettronica`,
Italy, 1.2.2) and Facturae (`Facturae`, Spain, 3.2.2) are **syntaxes**: their own element trees, their
own code lists, their own document type vocabulary, bound to the same semantic model.

**Decision — a reader and a writer for each, and no validator.** A national schema that states the
invoice differently is a mapping problem, not a rule problem: `IInvoiceValidator` is the seam for a
Schematron, and neither of these publishes one that applies to a CII or UBL document. What a caller
needs is to read an Italian or a Spanish document into `FacturXInvoice`, and to produce one from it.

```csharp
var italian  = FatturaPaReader.Read(xml);              // FatturaElettronica
string fatturaPa = FatturaPaWriter.Write(invoice);     // bytes: FatturaPaWriter.WriteBytes

var spanish  = FacturaeReader.Read(xml);               // Facturae
string facturae = FacturaeWriter.Write(invoice);       // bytes: FacturaeWriter.WriteBytes
```

Both are reached from `FacturXReader.Read` as well: the root element decides, so a caller who does not
know which syntax arrived still gets the invoice, and `FacturXReader.PeekSyntax` answers `"FatturaPA"`
or `"Facturae"` without loading the tree. The two are otherwise ordinary EN 16931 terms — the model,
the validator and the renderer see the same `FacturXInvoice` a CII document produces.

### FatturaPA

`FatturaElettronicaHeader` names the transmitter and the two parties; each `FatturaElettronicaBody` is a
complete invoice. The reader takes the first body, which is what a document that is not a batch
carries.

| Model | FatturaPA 1.2.2 |
| --- | --- |
| `Number`, `IssueDate`, `Currency` | `DatiGeneraliDocumento/Numero`, `Data`, `Divisa` |
| `TypeCode` (`BT-3`) | `TipoDocumento`: `TD01` ↔ `380`, `TD04` ↔ `381`, `TD05` ↔ `383`, `TD02`/`TD03` ↔ `386` |
| `Seller` / `Buyer` | `CedentePrestatore` / `CessionarioCommittente`, with `IdFiscaleIVA` as `VatId` (`IT` + number), `CodiceFiscale` as `LegalId` and `Sede` as the address |
| `Buyer.ElectronicAddress` (`BT-49`) | `DatiTrasmissione/CodiceDestinatario`, scheme `0201`, or `PECDestinatario`, scheme `EM` |
| `Lines` | `DettaglioLinee`: `Descrizione`, `Quantita`, `UnitaMisura`, `PrezzoUnitario`, `PrezzoTotale`, `AliquotaIVA`, `Natura` |
| `FacturXLine.SellerAssignedId` / `Classifications` | `CodiceArticolo` with `CodiceTipo` `INT` for the seller's number, and `CPV`, `EER`, `TARIC`, `EAN` or `GTIN` for a classification |
| `TaxBreakdown` (`BG-23`) | `DatiRiepilogo`: `AliquotaIVA`, `Natura`, `ImponibileImporto`, `Imposta`, `RiferimentoNormativo` |
| `AllowancesCharges` (`BG-20` / `BG-21`) | `ScontoMaggiorazione`: `SC` is an allowance, `MG` a charge |
| `Iban`, `Bic`, `PaymentReference`, `DueDate` | `DatiPagamento/DettaglioPagamento`: `IBAN`, `BIC`, `CodicePagamento`, `DataScadenzaPagamento` |
| `PaymentMeansCode` (`BT-81`) | `DettaglioPagamento/ModalitaPagamento`, translated through the table below |

The root is `p:FatturaElettronica` in `http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2`
and its children are unqualified, which is what the published schema says: it declares a
`targetNamespace` and no `elementFormDefault`. `versione` is `FPR12`, the private format; `FPA12`
addresses a public administration, whose recipient code grammar the model does not state, so it is not
produced. Every numeric field is a pattern, not a plain decimal: `22.00` is accepted where `22` is
refused, so the writer states rates, prices and amounts with two decimals and quantities with two to
eight. `Quantita` refuses a negative number, so a credit note carries the absolute quantity and the
direction lives in the document type and the negative amounts.

**What does not map.** FatturaPA states no per-line tax total (the line states a rate and the
`DatiRiepilogo` states the amounts), no document-level due date (only installment dates), no contract,
project, delivery address, mandate or prepaid amount, and no buyer contact. `ImportoTotaleDocumento` is
the only total it states, so the reader sums `LineTotal`, `TaxBasisTotal` and `TaxTotal` from the lines
and the `DatiRiepilogo`. `RegimeFiscale` is required by the schema and has no term in the model: the
writer states `RF01` (the ordinary regime) and the reader ignores it. `NumeroCivico` is its own element
and the model has one address line, so the reader appends it to the street. `UnitaMisura` is free text
where the model wants a UN/ECE code, so it is carried verbatim in both directions.

**The two code bridges.** FatturaPA classifies a zero rate with `Natura` (`N1` to `N7`) where EN 16931
states one of nine categories, and numbers payment means `MP01` to `MP23` where EN 16931 states UNTDID
4461. `FatturaPaNature` and `FatturaPaPaymentMeans` hold both directions:

| `Natura` | Category | Category | `Natura` |
| --- | --- | --- | --- |
| `N1`, `N4`, `N5` | `E` | `E` | `N1` |
| `N2` | `O` | `O` | `N2` |
| `N3.1`, `N3.3`, `N3.4` | `G` | `G` | `N3.1` |
| `N3.2` | `K` | `K` | `N3.2` |
| `N3.5`, `N3.6` | `E` | `AE` | `N6.1` |
| `N6`, `N7` | `AE` | `Z` | `N4` |

`Z` — zero-rated with no exemption reason — has no Italian spelling: the schema refuses a zero rate
without a `Natura`, so it is written as `N4` and reads back as `E`. The payment means table translates
the five modalities both lists name unambiguously (cash, cheque, credit transfer, credit card, direct
debit); a code outside it is carried verbatim rather than turned into a different instruction, and a
SEPA credit transfer (`58`) and a credit transfer (`30`) are the same `MP05`, so `58` reads back as
`30`.

### Facturae

`FileHeader` describes the batch, `Parties` holds the two parties and `Invoices` holds one or more
invoices; the reader takes the first.

| Model | Facturae 3.2.2 |
| --- | --- |
| `Number` | `InvoiceHeader/InvoiceNumber` |
| `TypeCode` (`BT-3`) | `InvoiceClass`: `OO`/`OC` ↔ `380`, `OR`/`CR` with a negative total ↔ `381`, with a positive one ↔ `384` |
| `IssueDate`, `Currency` | `InvoiceIssueData/IssueDate`, `InvoiceCurrencyCode` |
| `Period` (`BG-14`) | `InvoiceIssueData/InvoicingPeriod` |
| `Note` (`BT-22`) | `InvoiceIssueData/InvoiceDescription` |
| `Seller` / `Buyer` | `SellerParty` / `BuyerParty`: `TaxIdentificationNumber` as `VatId` (`ES` + NIF), `CorporateName` as the name, `AddressInSpain` or `OverseasAddress` |
| `Lines` | `Items/InvoiceLine`: `ItemDescription`, `Quantity`, `UnitOfMeasure`, `UnitPriceWithoutTax`, `TotalCost`, `GrossAmount`, `DiscountsAndRebates` / `Charges` |
| `TaxBreakdown` (`BG-23`) | `TaxesOutputs/Tax`: `TaxTypeCode` `01`, `TaxRate`, `TaxableBase`, `TaxAmount` |
| `AllowancesCharges` (`BG-20` / `BG-21`) | `InvoiceTotals/GeneralDiscounts` and `GeneralSurcharges` |
| `Iban`, `Bic`, `PaymentReference`, `DueDate` | `PaymentDetails/Installment`: `AccountToBeCredited/IBAN` and `BIC`, `PaymentReconciliationReference`, `InstallmentDueDate` |

The `FileHeader` is derived: the batch identifier is the seller's tax number and the invoice number,
and the three batch amounts are the invoice total, what is still due and what is executable.

**What does not map.** Facturae has no credit-note class: a correction is a rectificative invoice
(`InvoiceClass` `OR`) with negative amounts that names the invoice it corrects, so a model credit note
(`381`) is written that way and reads back as `384` — the same document under the name Spanish
receivers use. It has no term for an exemption reason, a contract, a project, a supporting document, a
mandate, a delivery address, a second note or a due date per installment. Its `ExchangeRateDetails`
exists for a tax currency that is not the invoice currency, and the model carries the code but not the
rate, so the writer states the invoice currency for both rather than inventing one. `UnitOfMeasure` is a
two-digit numeric list of its own where the model wants a UN/ECE code: the unit is written only when it
already is one of those codes. `Province` has no term in the model and the writer states the town there,
which is what a Spanish receiver expects of a domestic address in the absence of anything else.

### The round trip and the fixtures

Each writer's output is read back by the same reader, and the semantic values — number, dates, parties
and their identifiers, lines, rates, adjustments, breakdown and totals — survive. The tests in
`FatturaPaTests` and `FacturaeTests` assert that, plus a document with several VAT rates, a credit note
under each schema, a document with a general allowance, the schema element order, and the dispatch from
`FacturXReader` and `PeekSyntax`.

**The fixtures are constructed, not official.** `tests/fixtures/national/fattura-pa.xml` and
`facturae.xml` were written against the published schemas — the sequence of every element, the
cardinalities, the totals — because no sample from the Agenzia delle Entrate or the Facturae
publication is redistributable here. They, and the documents the two writers emit, were checked with a
schema-validating XML reader against the official FatturaPA 1.2.2 schema (from `fatturapa.gov.it`) and
the Facturae 3.2 schema: a valid document, a credit note, a document with several rates and a Natura
code, a general allowance, a PEC recipient and a foreign party all validate. The Facturae 3.2.2 schema
file itself was not obtainable, so what 3.2.2 adds over 3.2 is unchecked. The schema files are not
vendored and no test validates against them, so the element orders are pinned by tests instead, the
same way the UBL writer's are.
## 12. The XRechnung CIUS

XRechnung is Germany's CIUS of EN 16931, published by KoSIT for both UBL and CII. An invoice to a
public authority has to be one; since 2025 a German receiver also accepts it as the B2B syntax.
`XRechnungValidator` covers the part of the CIUS a document decides about itself — the identifiers,
the mandatory contact and postal terms, the payment instructions, the tax identifiers and the
arithmetic the CIUS adopted from Peppol when the two were harmonised.

**Decision — the official Schematron remains the reference, and this is a subset.** KoSIT publishes
the rules as Schematron: the German business rules `BR-DE-nn`, the extension rules `BR-DEX-nn`, the
vehicle-document CIUS, and the Peppol rules the CIUS incorporated. It reads the XML through an XSLT
2.0 processor and states things the model cannot see — whether an element is present but empty, the
card information of `BG-18`, the creditor identifier of a direct debit. Reimplementing it in C# would
be a second, silently diverging edition; the rules below are the ones `FacturXInvoice` states
exactly, each traceable to the assertion it comes from, and the artefact plugs in through
`IInvoiceValidator`.

### The identifier placement

`BT-24` is how a document declares the CIUS, and `BT-23` is 1..1 under XRechnung since 3.0.

| Term | UBL | CII | Value |
| --- | --- | --- | --- |
| `BT-23` business process | `cbc:ProfileID` | `BusinessProcessSpecifiedDocumentContextParameter/ID` | the buyer's process; `urn:fdc:peppol.eu:2017:poacc:billing:01:1.0` is the documented default |
| `BT-24` specification identifier | `cbc:CustomizationID` | `GuidelineSpecifiedDocumentContextParameter/ID` | `urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0` |

`XRechnungValidator.SpecificationId` holds the identifier of 3.0, `ExtensionSpecificationId` the one
of the extension (`…#conformant#urn:xeinkauf.de:kosit:extension:xrechnung_3.0`),
`VehicleSpecificationId` the one of the vehicle CIUS, and `DraftSpecificationId`
(`urn:cen.eu:en16931:2026#compliant#urn:xeinkauf.de:kosit:xrechnung_4.0`) the pre-release KoSIT
published in September 2026 for EN 16931-1:2026 — the year segment moves with the revision of the
European standard, so the two are separate values rather than variants of one spelling.

**Decision — `CiiWriter` writes the CIUS identifier when it is given one.** For Factur-X, `BT-24` is
the guideline URN and that is what selects the conformance level; XRechnung publishes CII as a
syntax of its own, and a German receiver reads `BT-24` there as the CIUS. A CII document built with
`SpecificationId = XRechnungValidator.SpecificationId` therefore carries that value and not the
guideline URN, which would make it a plain EN 16931 document. Every other invoice keeps the previous
behaviour.

### The rules

| Code | Source | Rule |
| --- | --- | --- |
| `BR-XR-01` | `BR-DE-21` | the specification identifier (`BT-24`) is present |
| `BR-XR-02` | `BR-DE-21` | it is one KoSIT publishes — the CIUS, its extension, the vehicle CIUS, or 4.0 |
| `BR-XR-03` | `PEPPOL-EN16931-R001` | the business process (`BT-23`) is present |
| `BR-XR-04` | `BR-DE-15` | the buyer reference (`BT-10`) is present — the Leitweg-ID a public authority routes on |
| `BR-XR-05` | `BR-DE-2` | the seller contact group (`BG-6`) is present |
| `BR-XR-06` | `BR-DE-5` | its name (`BT-41`) |
| `BR-XR-07` | `BR-DE-6` | its telephone number (`BT-42`) |
| `BR-XR-08` | `BR-DE-27` | the number carries at least three digits |
| `BR-XR-09` | `BR-DE-7` | its e-mail address (`BT-43`) |
| `BR-XR-10` | `BR-DE-28` | the address has the shape of an e-mail |
| `BR-XR-11`, `BR-XR-12` | `BR-DE-3`, `BR-DE-4` | the seller city (`BT-37`) and post code (`BT-38`) |
| `BR-XR-13`, `BR-XR-14` | `BR-DE-8`, `BR-DE-9` | the buyer city (`BT-52`) and post code (`BT-53`) |
| `BR-XR-15`, `BR-XR-16` | `BR-DE-10`, `BR-DE-11` | a deliver-to group that is present carries its city (`BT-77`) and post code (`BT-78`) |
| `BR-XR-17` | `PEPPOL-EN16931-R020` | the seller electronic address (`BT-34`) is present |
| `BR-XR-18` | `PEPPOL-EN16931-R010` | the buyer electronic address (`BT-49`) is present |
| `BR-XR-19` | `BR-DE-17` | the type code (`BT-3`) is one of the eight the CIUS names |
| `BR-XR-20` | `BR-DE-1` | the payment instructions (`BG-16`) are present |
| `BR-XR-21` | `BR-DE-23-a` | a credit transfer (`30`, `58`) carries the payment account (`BT-84`) |
| `BR-XR-22` | `BR-DE-23-b` | a credit transfer carries no direct debit mandate |
| `BR-XR-23`, `BR-XR-24` | `BR-DE-24-b` | a card payment (`48`, `54`, `55`) carries neither an account nor a mandate |
| `BR-XR-25` | `BR-DE-25-a` | a direct debit (`59`) carries its mandate reference (`BT-89`) |
| `BR-XR-26` | `BR-DE-25-b` | a direct debit carries no payee account |
| `BR-XR-27` | `BR-DE-19`, `BR-DE-20` | the account is a valid IBAN, check digits included |
| `BR-XR-28` | `BR-DE-18` | a `SKONTO` line of the payment terms (`BT-20`) is in the spelling the CIUS fixes |
| `BR-XR-29` | `BR-DE-14` | each VAT breakdown carries a rate (`BT-119`) |
| `BR-XR-30` | `BR-DE-16` | a VAT category of `S`, `Z`, `E`, `AE`, `K`, `G`, `L` or `M` requires the seller to be identified (`BT-31`) |
| `BR-XR-31` | `PEPPOL-EN16931-R121` | the price base quantity (`BT-149`) is above zero |
| `BR-XR-32` | `PEPPOL-EN16931-R046` | the net price is the gross price less the discount (`BT-146`, `BT-147`, `BT-148`) |
| `BR-XR-33`, `BR-XR-34` | `PEPPOL-EN16931-R110`, `R111` | a line period (`BG-26`) sits inside the invoice period (`BG-14`) |
| `BR-XR-35`, `BR-XR-36` | `PEPPOL-EN16931-R041`, `R042` | the base amount and the percentage of an allowance or charge go together |
| `BR-XR-37` | `PEPPOL-EN16931-R040` | the amount is the base at the percentage |
| `BR-XR-38` | `BR-DE-26` | a corrected invoice (`384`) names the invoice it corrects (`BG-3`) |
| `BR-XR-39` | `PEPPOL-EN16931-R005` | the VAT accounting currency differs from the invoice currency (`BT-6`, `BT-5`) |
| `BR-XR-40` | `BR-DEX-15` | the document carries no sub invoice lines (`BT-127`), which XRechnung does not support |

`BR-DE-17`, `BR-DE-27` and `BR-DE-28` are *warning* assertions in the Schematron: the code list of
UNTDID 1001 is open, and a telephone number or an address is a shape rather than a term. They are
reported as issues all the same — a document that fails them is one a German receiver complains
about, and the library has one severity.

`BR-XR-30` is the one rule narrowed by the model. `BR-DE-16` accepts `BT-31`, `BT-32` (the tax
number) or `BG-11` (the tax representative); the model carries `BT-31` only, so the rule reports the
seller as unidentified when no VAT identifier is there. A document that carries `BT-32` alone is read
with an empty `VatId` and is flagged, which is a false positive the model cannot avoid — and adding
`BT-32` to `FacturXParty` is the change that would remove it.

### What is left to the official Schematron

- `BR-DE-12` and its deprecated sibling `BR-DE-13`: the post code of a German supplier has a shape,
  which is a per-country grammar.
- `BR-DE-24-a`: a card payment has to carry `BG-18`, the card information — the model has no such
  group, and inventing one would put a card number in an invoice model.
- `BR-DE-30` and `BR-DE-31`: a direct debit carries the creditor identifier (`BT-90`) and the debited
  account (`BT-91`); neither is modelled.
- `BR-DE-22`: attachment filenames are unique — the model keeps the document identifier and the
  location of `BG-24`, not the filename of the attached binary.
- `BR-DEX-01` to `BR-DEX-14`: the extension's MIME codes, sub-invoice totals and prepaid payments.
- `BR-DE-CVD-01` to `BR-DE-CVD-06`: the vehicle CIUS, whose classification terms the model does not
  carry.
- `PEPPOL-EN16931-R007` (the process identifier has a format): `BT-23` is specified by the buyer, and
  the format holds inside the Peppol network rather than for every German receiver.
- `PEPPOL-EN16931-R008` (no empty element): the writers never emit one, and the model cannot tell an
  absent element from an empty one.

### Using it

```csharp
// Applied when the document declares XRechnung in BT-24 — read before the Peppol and French rules.
var result = FacturXValidator.Validate(invoice);

// Required even from a document that does not declare it, plus a rule set of your own.
var strict = FacturXValidator.Validate(invoice, XRechnungValidator.Default);
```

The three overlays are mutually exclusive and are selected by declaration, XRechnung first. That
order is not cosmetic: a German document carries the Peppol billing process in `BT-23` — KoSIT names
it as the default — while declaring the German CIUS in `BT-24`, so a gate that read `BT-23` alone
would hold every Peppol invoice to the German rules. A document that declares XRechnung is therefore
held to neither the Peppol nor the French rules.

## 8. Tests and gates

| Level | What it pins |
| --- | --- |
| Unit, `tests/Text.Pdf.FactureX.Tests` | reading all four syntaxes, the validation rules including the allowance arithmetic, every writer's round trip (CII→CII, UBL→UBL, CII→UBL, UBL→CII, FatturaPA→FatturaPA, Facturae→Facturae, FatturaPA→Facturae), the encoding declaration, the schema orderings, the DTD refusal, and the Peppol CIUS: one document per rule, the conformant model, and the OpenPEPPOL samples |
| Unit, `tests/Text.Pdf.FactureX.Tests` | reading both syntaxes, the validation rules including the allowance arithmetic, both writers' round trips (CII→CII, UBL→UBL, CII→UBL, UBL→CII), the encoding declaration, the schema orderings, the DTD refusal, the Peppol CIUS (one document per rule, the conformant model, the OpenPEPPOL samples), and the XRechnung CIUS (one document per rule, the conformant model, and the selection of each overlay by declaration) |
| Integration, `FacturXRenderTests` | PDF/A-3 marker, `/EmbeddedFiles`, the `fx:` block and its values, the profile carried into the XMP, a refused invalid invoice |
| Unit, `PdfExtractionTests` | the attachment is found through the name tree, inflated, and read back; a hex name, an uncompressed stream, a second attachment, and every refusal |
| CI, job `facturx` | renders the samples, extracts the attachment and validates it against the **official XSD** with the FNFE-MPE reference library, then runs **veraPDF** against PDF/A-3B |

The CI checks are the ones that matter. The FNFE-MPE library is the validator the French ecosystem
uses, and it found four defects the string assertions could not see. veraPDF is the reference
implementation of ISO 19005: it confirms that the incremental update which injects the `fx:` block
leaves the file conformant, which is the one thing the patch could plausibly break. The flavour is
passed explicitly (`--flavour 3b`): left to itself, veraPDF believes the file's own claim and
validates against PDF/A-1b.
