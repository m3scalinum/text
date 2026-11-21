# Pdf — converting a document to PDF/A

## What it answers

You have a PDF someone else made — a word processor, a scanner, an invoicing tool — and an archive
asks for PDF/A-2b or PDF/A-3b. `PdfAConverter` rewrites what can be established from the file and
tells you, in the same breath, what it changed and what it could not do.

```csharp
using M3scalinum.Text.Pdf;

var result = PdfAConverter.Convert(pdf, new PdfAConversionOptions
{
    Target = PdfConformanceStandard.PdfA3b,
    IccProfile = profile,
    Language = "fr",
});

if (result.IsConformant)
{
    File.WriteAllBytes("invoice-a3b.pdf", result.Bytes);
}
else
{
    foreach (var issue in result.Errors)
    {
        Console.WriteLine($"{issue.Code} at {issue.Target}: {issue.Message}");
    }
}
```

`IsConformant` is not the converter's opinion of its own work: it is `PdfConformance.Check` run on
the bytes it produced. A file that was rewritten but still breaks a rule comes back with the errors
named, and `EnsureConformant()` throws the report instead when you would rather fail fast.

## What it changes

- the PDF version, to the one the target fixes (1.4 for PDF/A-1, 1.7 for PDF/A-2 and -3, 2.0 for
  PDF/A-4);
- the XMP packet — `pdfaid:part` and `pdfaid:conformance`, or `pdfuaid:part` for PDF/UA — updated in
  place, keeping the title, creator and language it already declared;
- an `/OutputIntent` with the ICC profile you pass;
- `/MarkInfo << /Marked true >>` when the document is tagged but never said so;
- a `/ToUnicode` CMap for a font whose encoding is `WinAnsiEncoding`, where the mapping is a
  published table and not a guess;
- `/Lang`, when you pass `Language` and the document declares none;
- removal of the actions PDF/A forbids: a `/JavaScript` name tree, an open action or additional
  action that launches, plays or submits something, and — for PDF/A-1 and PDF/UA-1 — a document-level
  open action that leaves the file. Link annotations keep their URIs.

## What it refuses, and why

| Refusal | Why |
| --- | --- |
| The document is untagged and the target needs tagging (`a` levels, PDF/UA-1) | A structure tree says what each mark on the page *means*. The page description only says where a glyph was drawn. Inventing the tree would invent a reading order, a table and a heading level, and every one of them could be wrong. One issue per page. |
| A font has no embedded program | The outlines are not in the file. Nothing can recover them from the codes the page draws. |
| A font has no `/ToUnicode` and its encoding is not `WinAnsiEncoding` | The mapping of a symbolic or identity-encoded font lives in the font program. Stating one would change what the document says. |
| The file declares a version above the target | A PDF 2.0 file may hold constructs PDF 1.7 forbids, and a rewrite cannot remove them. Choose a flavour that accepts the file, or re-render. |
| The file is encrypted | An encrypted document has to be decrypted first: `PdfEncryption.Open`, then convert the bytes it returned. Nothing is written before the refusal. |

A refusal returns no bytes. Handing back a file that declares PDF/A while its pages have no
structure would be worse than returning nothing: the archive stores it, and the problem surfaces
later, at the auditor.

## The ICC profile is yours, not ours

PDF/A-1, PDF/A-2 and PDF/A-3 require an output intent, and this package ships no ICC profile:
redistributing one is a licence question, and an sRGB profile is not the library's to hand out. Pass
the profile's bytes in `IccProfile`, and answer for them. The converter reads the profile's colour
space from the ICC signature and writes `/N 3`, `4` or `1` to match. PDF/A-4 and PDF/UA-1 take a
profile when you give one and do not require one.

## veraPDF is still the reference

The conversion establishes declarations, not conformance. Colour, transparency, glyph coverage,
reading order and the several hundred other rules of ISO 19005 need a renderer and a validator. Run
veraPDF on the result before you deposit it; this converter gives a document that is *plausibly*
PDF/A and an exact account of what it touched.

Everything below is out of reach of a rewrite, and none of it is claimed:

- a page whose text is an image, so there is text nobody can extract;
- transparency a PDF/A-1 file cannot hold;
- a font whose embedded subset lacks the glyphs the page draws;
- XMP schemas an archive's profile requires beyond `pdfaid`.

## Where to read more

`docs/specifications/26-pdfa-conversion.md` states every step, every code, and the tests.
`Pdf-Conformance.md` covers the checker that decides the verdict.
