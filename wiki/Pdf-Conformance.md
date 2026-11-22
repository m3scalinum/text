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

## In tests

The conformance suite is built on real files: `tests/Text.Pdf.Tests/fixtures/pdfua` holds PDF/UA-1,
PDF/A-2b and PDF/A-4 documents compiled by Typst 0.15.1, and every negative case is that same file
with one byte-length-preserving property mutated. That keeps the cross-reference valid, so a failing
test names the rule it was written for.
