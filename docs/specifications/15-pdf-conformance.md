# 15 — PDF conformance

## 1. Scope

`M3scalinum.Text.Pdf` can now say whether a PDF is structurally what it claims to be. The
`M3scalinum.Text.Pdf` package gains one checker, `PdfConformance`, which reads a document's bytes and
returns a `PdfConformanceReport`. It has no reference on the compiler and the compiler has none on
it: a caller compiles (or receives) a file, and passes the bytes.

**In scope.** What the file *declares*, established from its object graph: cross-reference and
catalog integrity, tagged structure, language, title, alternate text, embedded fonts and Unicode
maps, encryption, the PDF version, and the XMP identifier of the claimed standard.

**Out of scope.** Anything that needs the page description interpreted, and everything that is not
a declaration. See §5 for the exact list. **veraPDF is the reference validator**; this checker is a
fast, dependency-free pre-flight that catches the mistakes a producer actually makes, not a
replacement for ISO 14289 validation.

### Why there is no PDF/UA-2 flavour

PDF/UA-2 (ISO 14289-2) applies to PDF 2.0 and would be the natural target for the European
Accessibility Act. The embedded compiler does not support it: `typst-pdf 0.15.1` declares its own
`PdfStandard` enum with a single accessibility variant, `Ua_1`, and the native bridge
(`native/sharp-typst/src/export.rs`, `parse_standard`) maps exactly one name, `"ua-1"`, rejecting
anything else with `invalid_argument`. No UA-2 flavour exists to be selected.

A PDF/UA-2 member added to the managed enum would therefore fail at export time, not fall back
silently — but it would still be a value that cannot work. It is not added. PDF/UA-1 remains the
accessible flavour the suite can produce and check, and this document says so rather than implying
more.

## 2. The API

```csharp
using M3scalinum.Text.Pdf;

var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1);

if (!report.IsConformant)
{
    foreach (var issue in report.Errors)
    {
        Console.WriteLine($"{issue.Code} at {issue.Target}: {issue.Message}");
    }
}
```

`Check` never throws on malformed input: bytes that are not a PDF come back as a report with
`PDF.NOT_A_PDF`. `Assert` is the one-call form for a caller that wants an exception:

```csharp
PdfConformance.Assert(pdf, PdfConformanceStandard.PdfUa1);
```

**Decision — `Assert`, not a compile-and-check helper.** A method that both compiles and asserts
would live in a third assembly: `M3scalinum.Text.Pdf` must not reference the compiler, and the
compiler does not reference it back. Rather than introduce a package for two lines, `Assert` closes
the loop on the bytes:

```csharp
var pdf = compiled.ExportPdf(new PdfExportOptions
{
    Standards = [M3scalinum.Text.SharpTypst.Export.PdfConformanceStandard.PdfUa1],
});

PdfConformance.Assert(pdf, PdfConformanceStandard.PdfUa1);
```

**Decision — a second enum, named `PdfConformanceStandard`.** The checker could not reuse
`M3scalinum.Text.SharpTypst.Export.PdfStandard` without a reference on the compiler, and it could
not declare a type of the same name: `PublicApiTests.No_two_public_types_share_a_name_across_namespaces`
forbids it, because a caller importing both namespaces would get CS0104 on the bare name. The
members are the same, so a reader maps them at a glance:

| `M3scalinum.Text.SharpTypst.Export.PdfStandard` | `M3scalinum.Text.Pdf.PdfConformanceStandard` |
| --- | --- |
| `Pdf14` … `Pdf20` | same members |
| `PdfA1b`, `PdfA1a` | same members |
| `PdfA2b`, `PdfA2u`, `PdfA2a` | same members |
| `PdfA3b`, `PdfA3u`, `PdfA3a` | same members |
| `PdfA4`, `PdfA4f`, `PdfA4e` | same members |
| `PdfUa1` | `PdfUa1` |
| — (no UA-2 in Typst 0.15.1) | — |

## 3. What is checked

Every rule below is a `PdfConformanceIssue` with a **stable code** (in `PdfConformanceCodes`), a
severity, a `Target` naming the object concerned, and one sentence. `IsConformant` is true when no
issue has severity `Error`.

### 3.1 The file itself

| Code | Severity | Rule | How the bytes are read |
| --- | --- | --- | --- |
| `PDF.NOT_A_PDF` | Error | A `%PDF-` header in the first KiB | header scan |
| `PDF.XREF_INVALID` | Error | `startxref` resolves; each section is a table or an `/Type /XRef` stream; every in-use entry points at `N G obj`; an entry in an object stream names a declared stream | cross-reference tables and streams, `/Prev` chain |
| `PDF.TRAILER_INVALID` | Error | `/Size` present, numeric, and greater than the highest object number | merged trailer |
| `PDF.CATALOG_MISSING` | Error | `/Root` resolves to a dictionary | trailer |
| `PDF.CATALOG_INVALID` | Error | the catalog is `/Type /Catalog` | catalog |
| `PDF.PAGES_INVALID` | Error | `/Pages` resolves to `/Type /Pages`, holds at least one page, and `/Count` agrees with the tree | page tree walk |
| `PDF.ENCRYPTED` | Error | the trailer names an `/Encrypt` entry | trailer; the check stops, because the content cannot be decrypted |

### 3.2 Standard identity

| Code | Severity | Rule |
| --- | --- | --- |
| `PDF.VERSION_MISMATCH` | Error | the header version (raised by a catalog `/Version` if present) is within the range the standard allows |
| `PDF.METADATA_MISSING` | Error | the catalog names an XMP `/Metadata` stream — required for PDF/A and PDF/UA |
| `PDF.METADATA_UNREADABLE` | Warning | the XMP stream uses a filter this reader does not decode |
| `PDF.XMP_STANDARD_MISMATCH` | Error | the XMP declares the claimed standard: `pdfaid:part` and `pdfaid:conformance` for PDF/A, `pdfuaid:part` = 1 for PDF/UA-1 |

Version ranges: PDF 1.4/1.5/1.6/1.7/2.0 exactly; PDF/A-1 → 1.4; PDF/A-2 and PDF/A-3 → 1.7;
PDF/A-4 → 2.0; PDF/UA-1 → 1.4 to 1.7, because ISO 14289-1 is built on ISO 32000-1.

### 3.3 Fonts

| Code | Severity | Rule |
| --- | --- | --- |
| `PDF.FONT_NOT_EMBEDDED` | Error | every font reachable from a page's `/Resources` has a `/FontDescriptor` with `/FontFile`, `/FontFile2` or `/FontFile3`; a `/Type0` font is satisfied through its `/DescendantFonts`; a `/Type3` font is embedded by construction |
| `PDF.FONT_TO_UNICODE_MISSING` | Error | every font that is not `/Type3` has a `/ToUnicode` entry |

Run for PDF/A and PDF/UA claims, where embedding and Unicode mapping are requirements.

**Decision — `/ToUnicode` is required from every font.** ISO 19005 accepts a font whose encoding
already maps to Unicode without a `/ToUnicode` CMap. The check is deliberately stricter: it reports
a missing CMap rather than proving the encoding sufficient. A producer that embeds and maps (Typst
does both) passes; a hand-built PDF can be flagged where veraPDF would pass it. The report says
which rule fired, so the difference is visible rather than silent.

### 3.4 Tagged structure, language, title, alternate text

Applied to PDF/UA-1 and the `a` levels of PDF/A.

| Code | Severity | Rule |
| --- | --- | --- |
| `PDFUA.MARK_INFO_MISSING` | Error | the catalog holds `/MarkInfo` with `/Marked true` |
| `PDFUA.STRUCT_TREE_MISSING` | Error | `/StructTreeRoot` resolves to a dictionary |
| `PDFUA.STRUCT_TREE_INCONSISTENT` | Error | the root is `/Type /StructTreeRoot`; every structure element's `/P` names the object whose `/K` holds it; no element is reachable twice; the root has a `/ParentTree` with `/Nums` (or `/Kids`); every page's `/StructParents` is a key the parent tree indexes |
| `PDFUA.LANG_MISSING` | Error | the catalog's `/Lang` is a non-empty string |
| `PDFUA.TITLE_MISSING` | Error | `/Info /Title` or the XMP `dc:title` is non-empty (PDF/UA-1 only) |
| `PDFUA.FIGURE_ALT_MISSING` | Error | every structure element with `/S /Figure` carries a non-empty `/Alt` or `/ActualText` |
| `PDFUA.IMAGE_NOT_STRUCTURED` | Warning | the pages reference image XObjects but the structure tree holds no `/Figure` at all: the images are decorative or unexplained, and the bytes do not say which |

The figure check walks the structure tree and the page resources together: the image XObjects are
enumerated (including those inside form XObjects) so the warning can compare their count with the
figures, and the alternate text is read where the standard puts it — on the `/Figure` element.
Typst binds an image to its figure through marked content (`/Figure <</MCID n>> BDC`) and the
parent tree, so an image is described by its figure's `/Alt`; an `/OBJR`-bound image is described by
the figure that owns the object reference.

## 4. Verification

The tests are the deliverable, and they use **real output**: `fixtures/pdfua/ua1.pdf`,
`a2b.pdf` and `a4.pdf` are committed bytes produced by Typst 0.15.1 through
`M3scalinum.Text.SharpTypst` (`fixtures/pdfua/README.md` records the exact source, logo and export
options). A hand-written minimal PDF would not exercise the parser against object streams,
compressed XMP, two embedded fonts, an outline and a two-page structure tree.

Each rule has a positive and a negative case:

| Rule | Positive | Negative (one mutated property of the real file) |
| --- | --- | --- |
| `PDF.NOT_A_PDF` | `ua1.pdf` parses | empty bytes; ASCII text |
| `PDF.XREF_INVALID` | `ua1.pdf` | `startxref` +1; first in-use entry offset +1 |
| `PDF.TRAILER_INVALID` | `ua1.pdf` | `/Size 62` → `/Size 10` |
| `PDF.CATALOG_MISSING` | `ua1.pdf` | `/Root` key renamed |
| `PDF.CATALOG_INVALID` | `ua1.pdf` | `/Type/Catalog` → `/Type/Catalox` |
| `PDF.PAGES_INVALID` | `ua1.pdf` | `/Count 2` → `/Count 0` |
| `PDF.ENCRYPTED` | `ua1.pdf` | `/Info 59 0 R` → `/Encrypt /Ey` |
| `PDF.VERSION_MISMATCH` | `ua1.pdf` at 1.7; `a4.pdf` at 2.0 | `%PDF-1.7` → `%PDF-1.3`; `a2b.pdf` claimed as PDF/A-1b |
| `PDF.XMP_STANDARD_MISMATCH` | `a2b.pdf` (`2`/`B`), `ua1.pdf` (`pdfuaid:part` 1) | `pdfuaid:part` 2; `pdfaid:conformance` U; `pdfaid:part` key renamed |
| `PDF.METADATA_MISSING` | `ua1.pdf` | `/Metadata 60 0 R` removed |
| `PDF.FONT_NOT_EMBEDDED` | `ua1.pdf` | both `/FontFile3` keys renamed |
| `PDF.FONT_TO_UNICODE_MISSING` | `ua1.pdf` | both `/ToUnicode` keys renamed |
| `PDFUA.MARK_INFO_MISSING` | `ua1.pdf` | `/MarkInfo` key renamed |
| `PDFUA.STRUCT_TREE_MISSING` | `ua1.pdf` | `/StructTreeRoot 5 0 R` blanked |
| `PDFUA.STRUCT_TREE_INCONSISTENT` | `ua1.pdf` | `/P 5 0 R` → `/P 6 0 R` on the Document element; a page's `/StructParents 1` → `3` |
| `PDFUA.LANG_MISSING` | `ua1.pdf` (`/Lang(fr)`) | `/Lang` key renamed |
| `PDFUA.TITLE_MISSING` | `ua1.pdf` | `/Info /Title` and `<dc:title>` renamed together |
| `PDFUA.FIGURE_ALT_MISSING` | `ua1.pdf` (figure `/Alt`) | `/Alt` key renamed on the figure |
| `PDFUA.IMAGE_NOT_STRUCTURED` | `ua1.pdf` | `/S/Figure` → `/S/Divvvv`: warning only, still conformant |

The mutations are **byte-for-byte length preserving** and asserted unique in the fixture, so the
cross-reference stays valid and a test fails for the rule it names, not because the file moved by a
byte. `PdfConformance.Check` is also run on `ua1.pdf` **after** `PdfSigner.Sign`, which appends an
incremental update with a second trailer and a `/Prev` chain: the reader must merge both sections
and still see a conformant document.

`PdfConformance.Assert` is tested in both directions (throws with the report, returns quietly).

Two further fixtures, `xref-stream.pdf` and `objstm.pdf`, are hand-written minimal PDF 2.0 files
whose objects live in a cross-reference stream and in an object stream respectively. They are not
conformance fixtures — a minimal PDF is not PDF/UA, and a test asserts exactly that — but they cover
the two reader paths a PDF 2.0 producer may take. The conformance positives are the compiler's
output.

Around those document-level cases, `PdfReaderTests`, `PdfStandardRequirementsTests` and
`PdfValueTests` drive the pieces the report cannot reach: every standard's version range, XMP
identifier and flags; each cross-reference error the reader can state (a bad `startxref`, a broken
entry, a malformed subsection, an xref stream whose `/W`, `/Index` or filter is wrong); object
streams, ASCII hex and Flate filters, TIFF and PNG predictors; the escaped, hexadecimal and UTF-16
string forms; and the syntax errors that must be reported as `PDF.OBJECT_UNREADABLE` rather than
thrown. Those tests see internal types through `InternalsVisibleTo`, because the alternative is to
assert a report that hides which parse step failed.

## 5. What is not checked, and what veraPDF still catches

A structural checker reads declarations. It does not implement ISO 14289-1, and it is not a
substitute for veraPDF. In particular this checker does **not** establish:

- **Colour and graphics state**: `/OutputIntent`, DeviceRGB without an ICC profile, transparency
  groups, overprint, image resolution. PDF/A-specific, and entirely unexamined here.
- **Glyph coverage**: that the embedded font program actually contains every glyph the content
  stream draws, or that the `/ToUnicode` CMap maps the codes correctly. Presence is checked, not
  correctness.
- **Content-stream semantics**: that marked-content sequences nest correctly, that every `/MCID`
  is used exactly once, that the parent tree's leaf arrays map every marked-content identifier, or
  that text inside a figure is tagged as such.
- **Reading order and structure roles**: that `/RoleMap` maps every custom role to a standard one,
  that headings nest, that tables carry `/THead`, `/TBody`, `/TH` scope, or that lists use `/L`,
  `/LI`, `/Lbl`, `/LBody` correctly. Only `/S /Figure` is interpreted.
- **Alternate descriptions on anything but figures**: links (`/Link` and its `/Alt`), annotations,
  form fields and their tooltips, `/ActualText` for ligatures and hyphenation.
- **Document-level accessibility**: `/ViewerPreferences /DisplayDocTitle`, `/Suspects`, `/Tabs`,
  `/OpenAction`, the order of the outline, the natural language of spans that differ from `/Lang`.
- **XMP schema conformance**: three properties are read by name (`pdfaid:part`,
  `pdfaid:conformance`, `pdfuaid:part`, plus `dc:title`); the packet is not validated against ISO
  16684, and a well-formed-but-wrong packet with the right values passes.
- **PDF/A**: no PDF/A rule beyond version, identifier, font embedding and (for the `a` levels)
  tagged structure. veraPDF reports dozens more per file.
- **Encryption internals**, linearization, digital-signature validity, embedded-file relationships.
- **Anything visual.** No rendering, no page geometry, no WCAG contrast.

The claim in this document is narrow and testable: a document that passes has the declarations a
tagged, embedded, identified PDF needs, and one that fails names the declaration that is wrong.
Conformance to ISO 14289-1 or ISO 19005 is only ever established by running veraPDF (or another
ISO 32000-based validator) on the produced file.
