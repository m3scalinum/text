# Checking conformance

## What it answers

A PDF can claim PDF/UA-1 or PDF/A. `PdfConformance` reads the bytes and tells you whether the
declarations that claim rests on are there — tagged structure, a language, a title, alternate text
on figures, embedded fonts with Unicode maps, the version, and the XMP identifier.

```csharp
using M3scalinum.Text.Pdf;

var pdf = compiled.ExportPdf(new PdfExportOptions
{
    Standards = [M3scalinum.Text.Typst.Export.PdfConformanceStandard.PdfUa1],
});

var report = PdfConformance.Check(pdf, PdfConformanceStandard.PdfUa1);

if (!report.IsConformant)
{
    foreach (var issue in report.Errors)
    {
        Console.WriteLine($"{issue.Code} at {issue.Target}: {issue.Message}");
    }
}
```

`PdfConformance.Assert(pdf, PdfConformanceStandard.PdfUa1)` is the same check when you would rather get a
`PdfConformanceException`; its `Report` carries every issue.

The checker takes bytes and returns a report. It has no reference on the compiler, so it works on a
PDF that came from anywhere: an email attachment, an invoice a third party rendered, a file another
tool produced.

## What it does not do

It is a structural checker, not a validator. It reads declarations; it does not interpret the page
description. Colour spaces, output intents, glyph coverage, reading order, link alternate
descriptions, XMP schema validity and the several hundred other rules of ISO 14289-1 are out of
scope. Table headers, heading levels, tab order and role mapping are checked only by
`CheckAccessibility`, which is opt-in.

**veraPDF remains the reference.** Run it on the produced file before you ship a document that
claims a standard; `PdfConformance` catches the producer's own mistakes early, in a unit test, with
no Java and no validator install.

## The accessibility checklist

`PdfConformance.CheckAccessibility(pdf)` is `Check` against PDF/UA-1 plus the usability findings the
structural rules do not cover:

```csharp
var report = PdfConformance.CheckAccessibility(pdf);

foreach (var issue in report.Issues)
{
    // PDFUA.TABLE_HEADER_MISSING, PDFUA.TABLE_HEADER_SCOPE_MISSING, PDFUA.HEADING_LEVEL_SKIPPED,
    // PDFUA.TABS_NOT_STRUCTURE, PDFUA.DISPLAY_DOC_TITLE_MISSING, PDFUA.ROLE_NOT_MAPPED, …
}
```

It answers whether every table declares its header cells and their scope, whether heading levels skip
(a level 3 after a level 1), whether the tab order is the structure order, whether the viewer is asked
to show the document title, and whether a structure role is left unmapped. These are opt-in and not
part of `Check`: a producer can be conformant by the structural rules and still fail one of them, and
the specification states for each line whether it is a checker rule, a writing rule or a manual
verification.

## Reading the report

```csharp
report.IsConformant;   // true when no issue has severity Error
report.Errors;         // the rules that were broken
report.Warnings;       // what could not be established from the bytes
report.Has("PDFUA.FIGURE_ALT_MISSING");
```

Every issue has a stable code from `PdfConformanceCodes`, a severity, a target (`catalog`,
`trailer`, `obj 12`, `page 47`, `xmp`) and one sentence. The codes are listed, with the rule each
one applies, in `docs/specifications/15-pdf-conformance.md`; they do not change once published, so a
test can filter on them.

## Which standards

`PdfConformanceStandard` mirrors the compiler's `PdfStandard` members: PDF 1.4 to 2.0, PDF/A-1b/1a,
PDF/A-2b/2u/2a, PDF/A-3b/3u/3a, PDF/A-4/4f/4e, and PDF/UA-1. It is a separate type with a different
name because the checker cannot reference the compiler, and the suite forbids two public types of
the same name in different namespaces.

PDF/UA-2 is not among them. Typst 0.15.1 has no PDF/UA-2 flavour — the native bridge accepts
`ua-1` and nothing else — so the suite cannot produce one and the checker will not pretend to
check one. If you need PDF/UA-2 output today, it has to come from another producer; the checker can
still be pointed at it as `PdfConformanceStandard.Pdf20`, which confirms the PDF 2.0 structure but not the
accessibility rules of ISO 14289-2.

## What the pipeline can produce

The enum mirrors the producer, and the producer reaches these levels and no others:

| Target | Produced | Checked with |
| --- | --- | --- |
| PDF 1.4, 1.5, 1.6, 1.7, 2.0 | yes | `PdfConformanceStandard.Pdf14` … `Pdf20` |
| PDF/A-1b/1a, 2b/2u/2a, 3b/3u/3a, 4/4f/4e | yes | the matching `PdfA…` member |
| PDF/UA-1 on a 1.4 to 1.7 file | yes | `PdfConformanceStandard.PdfUa1`, and `CheckAccessibility` for the usability lines |
| PDF/UA-1 on a PDF 2.0 file | **no** | `Check(a4.pdf, PdfUa1)` reports `PDF.VERSION_MISMATCH`: “PDF/UA-1 requires PDF 1.4 to 1.7, but the file declares 2.0” |
| PDF/UA-2 | **no** | no member in `PdfConformanceStandard`, no rule in `PdfConformanceCodes` |

The PDF/UA-1 ceiling is the standard's, not a choice of this suite. ISO 14289-1 is built on
ISO 32000-1, and the embedded `krilla 0.8.2` fixes `Accessibility::UA1` to PDF 1.7. A configuration
that asks `typst-pdf 0.15.1` for a 2.0 version with the UA-1 validator fails inside the producer, so
the export cannot be made and the checker would report the mismatch if a file claimed it anyway.
PDF/UA-1 and PDF/A-4 cannot be combined for the same reason: the two validators' version ranges do
not overlap.

PDF/UA-2 needs a producer before it can need a checker. `typst-pdf 0.15.1` declares a single
accessibility variant (`Ua_1`), `krilla 0.8.2` a single `Accessibility::UA1`, and the native bridge
maps exactly one name, `ua-1`. Adding a `PdfUa2` member first would be a value the export rejects
with `invalid_argument` — a value that cannot work. It is not added; the `PdfUa2Tests` guard fails
if one is added without a producer.

## In tests

The conformance suite is built on real files: `tests/Text.Pdf.Tests/fixtures/pdfua` holds PDF/UA-1,
PDF/A-2b and PDF/A-4 documents compiled by Typst 0.15.1, and every negative case is that same file
with one byte-length-preserving property mutated. That keeps the cross-reference valid, so a failing
test names the rule it was written for.
