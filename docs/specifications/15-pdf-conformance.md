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
    Standards = [M3scalinum.Text.Typst.Export.PdfConformanceStandard.PdfUa1],
});

PdfConformance.Assert(pdf, PdfConformanceStandard.PdfUa1);
```

**Decision — a second enum, named `PdfConformanceStandard`.** The checker could not reuse
`M3scalinum.Text.Typst.Export.PdfStandard` without a reference on the compiler, and it could
not declare a type of the same name: `PublicApiTests.No_two_public_types_share_a_name_across_namespaces`
forbids it, because a caller importing both namespaces would get CS0104 on the bare name. The
members are the same, so a reader maps them at a glance:

| `M3scalinum.Text.Typst.Export.PdfStandard` | `M3scalinum.Text.Pdf.PdfConformanceStandard` |
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
`M3scalinum.Text.Typst` (`fixtures/pdfua/README.md` records the exact source, logo and export
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
  `/LI`, `/Lbl`, `/LBody` correctly. `Check` interprets only `/S /Figure`. `CheckAccessibility`
  (§6) adds the table header, heading level, role mapping and `/Tabs` rules; the rest of the list
  rule and the content-stream reading order remain unexamined.
- **Alternate descriptions on anything but figures**: links (`/Link` and its `/Alt`), annotations,
  form fields and their tooltips, `/ActualText` for ligatures and hyphenation.
- **Document-level accessibility**: `/Suspects`, `/OpenAction`, the order of the outline, and the
  natural language of spans that differ from `/Lang`. `CheckAccessibility` reads
  `/ViewerPreferences /DisplayDocTitle` and `/Tabs`; nothing else on this line is read.
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

## 6. The accessibility checklist, per package

The European Accessibility Act applies to the digital services a business or a public body provides,
so it applies to the documents those services produce. The standard behind it, EN 301 549, points at
WCAG for the web and at PDF/UA for a PDF. PDF/UA-1 structure is therefore necessary, but it is not
the whole of usability: a file can be tagged, identified and titled, and still open in a reader as
an untitled list of "graphic" and "cell" objects. This section is the checklist the suite holds the
pipeline to: **one line per property**, stating what the document model must carry, what the
rendering must turn it into, what Typst 0.15.1 can produce, and how the line is verified.

Each line is exactly one of three things, and says which:

- **checked** — `PdfConformance.Check` or `PdfConformance.CheckAccessibility` reports it from the
  bytes, and a test in `PdfConformanceAccessibilityTests` proves it on a real document.
- **a writing rule** — the model or the author must supply it. The checker only sees the result and
  cannot tell whether it is true; the line names the rule the template must follow.
- **manual** — a person with a real screen reader and a real screen. Nothing in this repository runs
  one.

| # | Line | What the model must carry | What the rendering must do | Typst 0.15.1 | Verified by |
| --- | --- | --- | --- | --- | --- |
| 1 | Alternate text for every image | a description per image, or an explicit "decorative" mark | bind the image to a `/Figure` with a non-empty `/Alt` (or `/ActualText`); a decorative image is an artifact and carries none | yes — `image(..., alt: "...")` | checked: `PDFUA.FIGURE_ALT_MISSING`; a warning `PDFUA.IMAGE_NOT_STRUCTURED` when images are drawn and no figure exists |
| 2 | Table header cells | which row is the header, and whether each header describes its row, its column or both | `/TR` holding `/TH` cells with `/A <</O /Table /Scope /Column>>`; data cells are `/TD` | yes, with the `a11y-extras` feature — `table.header(…)` marks the header row and `pdf.header-cell(scope: …)` writes `/TH` with `/Scope`; the model exposes both, as `TypstTable.HeaderRow` and `TypstTableCell.HeaderScope` | checked: `PDFUA.TABLE_HEADER_MISSING`, `PDFUA.TABLE_HEADER_SCOPE_MISSING`; `ModelAccessibilityPdfTests` asserts that a document built through the model passes. A document that marks no header — the `ua1.pdf` fixture among them, which measures 12 `/TD` and 0 `/TH` — still fails this line |
| 3 | Document language | the document's natural language tag | catalog `/Lang`, plus `/Lang` on any span that changes language | yes — `#set text(lang: "fr")` becomes `/Lang(fr)` | checked: `PDFUA.LANG_MISSING` |
| 4 | Reading order and structure tree | block order as read, not as drawn; the semantic role of each block | `/StructTreeRoot` with `/ParentTree`, every page tagged with `/StructParents` and `/Tabs /S` | yes | checked: `PDFUA.STRUCT_TREE_MISSING`, `PDFUA.STRUCT_TREE_INCONSISTENT`, `PDFUA.TABS_NOT_STRUCTURE`, `PDFUA.ROLE_NOT_MAPPED` |
| 5 | Hierarchical headings | an outline whose levels do not skip a step | `/H1` … `/H6` in document order, never a bare `/H` | yes — `=`, `==` | checked: `PDFUA.HEADING_LEVEL_SKIPPED` (a level that follows one more than one above it) |
| 6 | Contrast and font size | the colour pair and the type scale: WCAG 1.4.3 (4.5:1 for body text) and 1.4.4 (200 % zoom without loss) | nothing — the PDF carries the colour as drawn; the renderer cannot improve a decision the model got wrong | not applicable | **not checked and not checkable from the bytes**: a property of the model and the template, stated here as a writing rule. A test asserts the checker stays silent rather than implying it validates contrast |
| 7 | Document title | the human title of the document | `/Info /Title`, XMP `dc:title`, and `/ViewerPreferences /DisplayDocTitle true` so a reader shows the title instead of the file name | yes — `#set document(title: ...)`; `ua1.pdf` carries both `/Title` and `/DisplayDocTitle true` | checked: `PDFUA.TITLE_MISSING`, `PDFUA.DISPLAY_DOC_TITLE_MISSING` |
| 8 | Structure roles | the semantic role of each block, expressed with standard names | standard structure types, or a custom name mapped by the root's `/RoleMap` | yes — `ua1.pdf` maps `Datetime`, `Terms`, `Title`, `Strong`, `Em` | checked: `PDFUA.ROLE_NOT_MAPPED` |
| 9 | PDF/UA-1 only | nothing to carry: the export target is `ua-1` | declare `pdfuaid:part 1`; a document claiming part 2 while declaring UA-1 is rejected | yes for UA-1, **no** for UA-2 — the native bridge maps the single name `ua-1` | checked: `PDFUA.XMP_STANDARD_MISMATCH` on a part-2 claim, and a test asserts no UA-2 member exists in `PdfConformanceStandard` |
| 10 | Screen-reader usability | nothing: the document itself | nothing further | — | **manual, not executed** — see §7 |

### 6.1 Why the checklist is a second entry point

`PdfConformance.Check` keeps its narrow claim: the declarations of the claimed standard. The
checklist rules that depend on authoring intent, and that the compiler cannot yet satisfy, run under
a separate call:

```csharp
var report = PdfConformance.CheckAccessibility(pdf);
```

`CheckAccessibility` runs the PDF/UA-1 structural checks and, on top, lines 2, 4, 5, 7 and 8 of the
table. It is a separate method because line 2 **fails on the compiler's own PDF/UA-1 output**:
`fixtures/pdfua/ua1.pdf` has a table of twelve `/TD` cells and no `/TH`. Making that an error in
`Check` would report the fixture of §4 as broken while claiming it is the positive case. The two
calls keep both statements true instead of hiding one. The honest reading is:

> A document that passes `Check` has the declarations PDF/UA-1 requires. A document that passes
> `CheckAccessibility` also passes the usability lines a PDF file can state. Neither is a statement
> about what a screen reader will say.

### 6.2 Which test proves which line

| # | Test |
| --- | --- |
| 1 | `Image_Alternate_Text_Is_Reported_When_The_Figure_Loses_It` |
| 2 | `Table_Headers_Are_Reported_Missing_On_The_Compiler_Output`, `A_Table_Header_Without_A_Scope_Is_Reported`, `A_Table_With_A_Header_Row_Is_Not_Reported`, `Tables_Nested_In_A_Head_Wrapper_Are_Still_Read` |
| 3 | `The_Document_Language_Is_Reported_Missing` |
| 4 | `Reading_Order_Is_Reported_When_A_Page_Does_Not_Follow_The_Structure`, `A_Structure_Without_A_Tree_Is_Reported_On_The_Accessibility_Path_Too`, `A_Structure_Role_Outside_The_RoleMap_Is_Reported`, `A_Custom_Role_Mapped_By_The_RoleMap_Is_Accepted` |
| 5 | `A_Skipped_Heading_Level_Is_Reported` |
| 6 | `Contrast_And_Font_Size_Are_Not_Checked_Because_They_Belong_To_The_Model` |
| 7 | `A_Document_That_Does_Not_Display_Its_Title_Is_Reported`, `A_Document_With_No_Title_Anywhere_Is_Reported` |
| 8 | `A_Structure_Role_Outside_The_RoleMap_Is_Reported`, `A_Custom_Role_Mapped_By_The_RoleMap_Is_Accepted` |
| 9 | `A_PDF_UA_2_Claim_Is_Reported_Because_Only_UA_1_Exists` |
| 10 | none — manual |

Every test works on bytes: the compiler's fixture, or the demo document below with one property
changed. None of them asserts a constant; the negative cases are length-preserving mutations of the
fixture, so the cross-reference stays valid and the rule under test is the only thing that changed.

### 6.3 The demo document

The tests build, from hand-written objects, a one-page document that satisfies every automatic line
at once — language, displayed title, `/Tabs /S`, an `H1` then an `H2`, a table with a scoped `/TH`,
a figure with `/Alt` and an image. It is written next to the test assembly by its owning test, so a
reviewer has a file to open:

```bash
dotnet test tests/Text.Pdf.Tests -c Release \
  --filter "FullyQualifiedName~The_Demo_Document_Passes_The_Checklist"
```

The file lands at `tests/Text.Pdf.Tests/bin/Release/net11.0/demo-accessible.pdf`. It is a
demonstration of the checker, not of the compiler: its structure is hand-built, so it proves the
rules, not the pipeline. The compiler's output is `fixtures/pdfua/ua1.pdf`, and it fails line 2.

## 7. What still needs a person, and has not been done

A document that passes the checklist is *not* a document a screen-reader user has confirmed usable.
The sentence "the demo document passes with a real screen reader" cannot be automated here, and this
document does not claim it. No screen reader was run for this change; no NVDA or VoiceOver session,
on any of the fixtures or on the demo file, was recorded. The following are the checks that only a
person can make, and they are **open**:

1. That the reading order *as heard* follows the visual order, including the order of the figure
   relative to the surrounding paragraphs — the checker reads `/Tabs` and the tree, not the
   content stream's marked-content order.
2. That the alternate text says something useful ("Un carré rouge de quatre pixels") rather than
   merely existing.
3. That a table is announced with its header and its scope — the checker reads the `/TH` and
   `/Scope` a marked table carries, not what a reader does with them.
4. That heading navigation (`H` in NVDA, the rotor in VoiceOver) reaches the expected outline.
5. That the contrast ratio and the 200 % zoom of line 6 hold on the rendered page, measured with a
   contrast tool rather than read from the PDF.
6. That links, form fields and annotations — out of scope in §5 — announce their names.
7. That the document title is what the reader shows in its window title, which depends on
   `DisplayDocTitle` *and* on the reader honouring it.

What is delivered instead of that confirmation: one automatic test per line that can be checked,
the demo file for a reviewer to open, and this list. A future change that runs a screen reader in CI
must record the reader, its version and the transcript; until then the honest status of line 10 is
"manual, not executed".
