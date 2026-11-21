# 26 — Converting a document to PDF/A

## 1. Scope

The suite produces PDF/A when the compiler exports one. This is the other direction: a PDF written
by a word processor, a scanner, an invoicing tool or another library already exists, and an archive
demands PDF/A-2b for retention. `M3scalinum.Text.Pdf` gains one entry point for that:

```csharp
PdfAConversionResult result = PdfAConverter.Convert(source, new PdfAConversionOptions
{
    Target = PdfConformanceStandard.PdfA3b,
    IccProfile = profile,
});
```

**In scope.** What can be established from the file and written back into it: the PDF version, the
XMP identifier of the target, an output intent with the caller's ICC profile, `/MarkInfo`, a
`/ToUnicode` CMap for a font whose encoding makes one exact, and the removal of the actions the
standard forbids.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Inventing a structure tree | A structure tree records what each mark on a page means. The page description says where a glyph is drawn, not that it is a table cell or a heading; a tree inferred from coordinates is a guess presented as a fact. Refused, one issue per page. |
| Embedding a font program | Outlines are not recoverable from the codes the page draws. Refused by name. |
| Generating a `/ToUnicode` CMap for a non-WinAnsi font | The mapping of a symbolic or identity-encoded font lives in the font program, not in a published table. Guessing it would produce text that extracts to the wrong characters. |
| Interpreting colour, transparency, glyph coverage, reading order, contrast | The rules ISO 19005 states about rendering need a renderer. This is a rewrite of declarations, not a validator. |
| Re-rendering the document | That is a producer's job: compile the source again, or run it through a third-party tool that renders. |
| Decrypting | `PdfEncryption.Open` owns it. The conversion refuses an encrypted file before writing anything, because a rewrite would put plaintext into a file whose trailer still says `/Encrypt`. |
| Keeping signatures valid | A signature covers bytes that no longer exist after a rewrite. Sign after converting, never before. |

## 2. The result

`PdfAConversionResult` carries four things, and the distinction between the last two is the point:

| Member | Meaning |
| --- | --- |
| `Bytes` | The converted document, or an empty array when the conversion refused. |
| `Report` | `PdfConformance.Check(Bytes, Target)` — the same report a caller would obtain by checking the output themselves. `null` when nothing was written. |
| `IsConformant` | `Report.IsConformant`. True only when the checker found no error in the produced bytes. |
| `Changes` | What the converter rewrote, each with a stable code from `PdfAConversionCodes`. |
| `Refusals` | What it could not rewrite. Non-empty means `Bytes` is empty: the conversion stops rather than returning a file that claims a standard it does not meet. |

`EnsureConformant()` throws the `PdfConformanceException` the package already uses, so a caller that
wants an exception rather than a result does not have to build one.

```csharp
var result = PdfAConverter.Convert(pdf, new PdfAConversionOptions
{
    Target = PdfConformanceStandard.PdfA2b,
    IccProfile = icc,
});

if (!result.IsConformant)
{
    foreach (var issue in result.Errors)
    {
        Console.WriteLine($"{issue.Code} at {issue.Target}: {issue.Message}");
    }
}
```

**Decision — two lists, not one.** A change and a refusal are different statements. "I added the
XMP identifier" is true whenever it was added; "the document is untagged" is a reason no identifier
helps. Folding them into one list would let a caller read a refusal as progress.

**Decision — refusals produce no bytes.** Returning a file that carries `pdfaid:part 3` while its
pages have no structure would be worse than returning nothing: the archive would store it, and the
fraud would only surface when someone ran veraPDF. A refusal says the file cannot be converted and
why, which is a fact the caller can act on — by re-rendering, by asking for a tagged export, or by
choosing a basic conformance level that does not need tagging.

**Decision — one refusal per page.** When tagging is the blocker, the reason is per page: a page
either carries structure elements or it does not. A single document-level message would leave the
caller guessing which of fifty scanned pages to replace.

## 3. What a conversion does

The order is fixed, and every step records what it did.

| # | Step | Code reported |
| --- | --- | --- |
| 1 | Read the file; refuse bytes that are not a PDF | `PDFA.NOT_A_PDF` |
| 2 | Refuse an encrypted trailer before touching an object | `PDFA.ENCRYPTED` |
| 3 | Resolve the catalog and the page tree | `PDFA.CATALOG_MISSING`, `PDFA.NO_PAGES` |
| 4 | Refuse a file whose version is above the one the target fixes | `PDFA.VERSION_ABOVE_TARGET` |
| 5 | Refuse an untagged document for the `a` levels and PDF/UA-1, page by page | `PDFA.NOT_TAGGED` |
| 6 | Refuse a font with no embedded program | `PDFA.FONT_NOT_EMBEDDED` |
| 7 | Refuse a font with no `/ToUnicode` and no encoding that determines one | `PDFA.FONT_WITHOUT_TO_UNICODE` |
| 8 | Remove the forbidden actions and name trees from the source graph | `PDFA.JAVASCRIPT_REMOVED`, `PDFA.ACTION_REMOVED` |
| 9 | Copy the catalog into a fresh object numbering, dropping `/Metadata`, `/Version` and — when the caller supplied a profile — `/OutputIntents` | — |
| 10 | Attach the generated `/ToUnicode` CMaps | `PDFA.TO_UNICODE_ADDED` |
| 11 | Write the XMP packet, keeping what it already held | `PDFA.METADATA_ADDED`, `PDFA.METADATA_REWRITTEN` |
| 12 | Write the output intent and the ICC profile stream | `PDFA.OUTPUT_INTENT_ADDED` |
| 13 | Add `/MarkInfo << /Marked true >>` when the tree is there and the entry is not | `PDFA.MARK_INFO_ADDED` |
| 14 | Set `/Lang` from the options when the document declares none | `PDFA.LANGUAGE_SET` |
| 15 | Set the header to the version the target fixes | `PDFA.VERSION_SET` |
| 16 | Write the file and check it | — |

### 3.1 The version

| Target | PDF version |
| --- | --- |
| PDF/A-1a, PDF/A-1b | 1.4 |
| PDF/A-2a, -2b, -2u | 1.7 |
| PDF/A-3a, -3b, -3u | 1.7 |
| PDF/A-4, -4f, -4e | 2.0 |
| PDF/UA-1 | 1.7 |

A source below the target is raised. A source **above** it is refused, not lowered: a PDF 2.0 file
may hold object streams, a transparency group or a 256-bit AES crypt filter, and none of those can
be removed by rewriting the objects that surround them. Claiming PDF/A-2b for such a file would be a
declaration the bytes do not support, and veraPDF would say so. The message names both versions and
tells the caller which flavour would accept the file.

### 3.2 The XMP identifier

The packet is edited, not replaced. A document's title, creator, language, history and extension
schemas live in the same stream; a converter that wrote a fresh packet would silently drop them, and
a PDF/A file without `dc:title` fails the rules that need one.

Both forms are handled: `pdfaid:part` written as an element and as an attribute is removed before
the new value is written as an attribute of the first `rdf:Description`, with the `pdfaid` prefix
declared explicitly. A packet that already declared PDF/A-2 is therefore updated to PDF/A-3 with one
`pdfaid:part`, never two. A PDF/UA-1 target writes `pdfuaid:part 1` the same way.

A packet that does not parse as XML is replaced by a minimal one. That is deliberate: a declaration
inside a packet no reader can parse is not a declaration, and the alternative — failing for a reason
the archive cannot fix — helps nobody.

### 3.3 The output intent and the ICC profile

`PdfAConversionOptions.IccProfile` is **required** for PDF/A-1, PDF/A-2 and PDF/A-3, and optional
for PDF/A-4 and PDF/UA-1, which do not demand an output intent. Missing where required is a
`ArgumentException`, not a refusal: it is a caller mistake, available before any byte is read.

The profile is written unchanged as the `/DestOutputProfile` of a `/GTS_PDFA1` output intent. Its
data colour space is read from the ICC signature at byte 16 — `RGB `, `CMYK` or `GRAY` — and becomes
`/N 3`, `4` or `1`; a profile whose signature says anything else is refused with
`ArgumentException` rather than written with a channel count that would misdescribe it.
`OutputConditionIdentifier` defaults to `Custom`.

### 3.4 Fonts

Embedding is checked the way `PdfConformance` checks it, including through `/DescendantFonts`. A
font with no program is refused: the bytes of a font are not in the page.

A missing `/ToUnicode` is generated when — and only when — the mapping is a published table:

- the font is `/Type1`, `/TrueType` or `/MMType1` (a composite font's mapping is in its CMap, not in
  a one-byte table);
- its `/Encoding` is `WinAnsiEncoding`, directly or as the `/BaseEncoding` of a dictionary;
- that dictionary holds no `/Differences`, which would replace codes with glyphs the table does not
  describe.

The generated CMap is the usual `bfchar` form, one block per hundred codes, codespace `<00> <ff>`.
Everything else is refused with `PDFA.FONT_WITHOUT_TO_UNICODE`: stating a mapping the file does not
have would change what the document means.

### 3.5 Tagging

`/StructTreeRoot` must already be there for the `a` levels and PDF/UA-1. `/MarkInfo` is added when
the tree exists and the entry does not, because that entry is a single boolean about the document
rather than structure inferred from a page.

The structure tree is copied along with the catalog, so `/StructParents`, the parent tree and every
element's `/P` keep pointing at the same objects: the copy renumbers objects, it does not restructure
them. That is what makes a converted tagged document still coherent.

### 3.6 Actions, and `/Encrypt`

Removed wherever they are reachable from the catalog: the `/Names /JavaScript` name tree, and any
`/A`, `/AA`, `/OpenAction`, `/Next` or `/PA` entry carrying an action of type `Launch`, `Sound`,
`Movie`, `ResetForm`, `ImportData`, `JavaScript`, `SubmitForm`, `Rendition`, `Trans`, `GoTo3DView` or
`SetOCGState`.

For PDF/A-1a, PDF/A-1b and PDF/UA-1, additionally any `/OpenAction` that is not a `/GoTo`: an open
action that hands control to a location outside the file is not something an archive can vouch for.
A link annotation's own `/URI` action is **not** touched — a hyperlink inside an archived document is
permitted, and removing every URI would break every link in the file.

The removal happens on the **source** graph, before the copy, so a JavaScript payload is not in the
result at all, not merely unreferenced from it. A payload an archive can never run is still a
payload it carries, and still something a reader may extract.

`/Encrypt` needs no stripping step: an encrypted file is refused at step 2, and the trailer this
converter writes carries no such entry.

### 3.7 Determinism

Same input, same options, same bytes. No timestamp, no random identifier, no `/ID` is invented, all
dictionaries are written in ordinal key order by `PdfObjectWriter`, the object numbering follows the
copy order, and the XMP is serialised with a fixed writer. A caller can therefore hash the output and
compare archives; a test asserts it.

## 4. What still fails conformance, honestly

`IsConformant` is the checker's verdict, and the checker establishes a subset of what the standard
says. A converted file can be:

- **Refused** — no bytes at all: untagged for an `a` level or PDF/UA-1, unembedded font,
  un-mappable font, encrypted, version above target, not a PDF.
- **Produced but not conformant** — bytes written, `Report` naming the residual errors: a missing
  `/Lang`, a `/Figure` with no `/Alt`, a page whose `/StructParents` the parent tree does not index,
  a structure element whose `/P` disagrees, an image the tree does not describe. These are properties
  of the source that a rewrite of the catalog cannot fix, and `Changes` shows none of them was
  touched.
- **Produced and conformant by the checker, and still not PDF/A by veraPDF** — the cases the
  checker does not look at. Colour spaces that must be declared in an output intent, transparency,
  font programs that violate the embedding rules in ways a `/FontFile` key does not reveal, glyph
  coverage of the embedded subset, the exact form of the XMP schemas, the reading order of the
  structure tree. The specification of the checker, [15](15-pdf-conformance.md) §5, lists them.

**veraPDF remains the reference.** This converter produces a file that is *plausibly* PDF/A, with
every declaration an archive checks for, and reports exactly what it changed. It does not certify
anything, and the honest use is to run veraPDF on the result before depositing it.

## 5. Tests

| Test | What it establishes |
| --- | --- |
| `A_compiler_PDF_A_2b_becomes_a_conformant_PDF_A_3b` | A real Typst PDF/A-2b converts to PDF/A-3b, the checker passes it, the structure tree survives, and the page text still extracts. |
| `The_identifier_of_an_existing_XMP_packet_is_replaced_not_duplicated` | Exactly one `pdfaid:part`, value 3, and `dc:title` still in the packet. |
| `An_untagged_document_is_refused_page_by_page_for_PDF_A_2a` | The refusal, one per page; the same bytes convert to PDF/A-2b, which does not need tagging. |
| `A_document_with_an_unembedded_font_is_refused` | `PDFA.FONT_NOT_EMBEDDED` naming the font's object. |
| `An_encrypted_document_is_refused_before_anything_is_written` | `PDFA.ENCRYPTED`, no changes, and the route through `PdfEncryption.Open` then converts. |
| `The_same_input_and_options_produce_the_same_bytes` | Determinism. |
| `A_document_that_still_breaks_a_rule_reports_the_error_instead_of_claiming_success` | PDF/UA-1 without `/Lang`: bytes produced, `IsConformant` false, `PDFUA.LANG_MISSING` named; supplying `Language` closes it. |
| `A_PDF_UA_1_target_adds_the_pdfuaid_identifier_and_keeps_the_structure` | `pdfuaid:part 1` written once, the tagged tree kept. |
| `A_PDF_A_target_without_an_ICC_profile_is_a_caller_mistake` | `ArgumentException` for PDF/A-2b, allowed for PDF/A-4. |
| `A_document_above_the_version_the_target_fixes_is_refused` | PDF 2.0 to PDF/A-2b refused, both versions named. |
| `A_simple_font_whose_encoding_is_WinAnsi_gains_a_ToUnicode_CMap` | The generated CMap, and the result passes the checker. |
| `A_JavaScript_action_is_removed_from_the_document_it_is_reachable_from` | The payload is absent from the bytes, not merely unreferenced. |
| `A_document_level_URI_open_action_is_removed_for_PDF_A_1_and_kept_otherwise` | The strict-flavour rule, and PDF/A-2 leaving it alone. |
| `A_conversion_that_cannot_reach_the_target_says_so_through_EnsureConformant` | The exception path carries the refusals as a report. |

The untagged fixture is the compiler's PDF/A-2b with `/StructTreeRoot` and `/MarkInfo` removed and
`/StructParents` dropped from the pages: that is what the same producer writes when it is asked for
no standard, and the test project has no reference on the compiler to ask it again.
