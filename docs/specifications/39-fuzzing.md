# 39 — Fuzzing the parsers

## 1. The claim

[29](29-threat-model.md) states every bound the readers enforce, and pins each one with a test that
fails when the bound is removed. A test, though, only asks the question its author thought to ask.
The bound that is missing is, by construction, the one no test names: a length that is read after it
is used, a depth counter that is incremented on one branch and not on its sibling, a `Vec::with_capacity`
fed from a field the caller controls, an index computed from two sizes that disagree.

This document covers the one check in the suite that does not know what it is looking for:
**libFuzzer, driving each parser that accepts bytes from a stranger, on a schedule, under the address
sanitizer.** It is complementary to [29](29-threat-model.md) and does not replace it — a crash is a
hypothesis, and the limit it is missing still has to be written down and pinned by a test as §5 of
that document requires.

The harness lives in `fuzz/`: one cargo package with one binary per format, plus the root
`Cargo.toml` that exists only so `cargo fuzz` has a project to resolve targets from. The four native
crates stay their own workspaces — each keeps its own lock file and its own profile — because that
is what lets a reader move to a new `zip` without moving the compiler.

## 2. The targets

| Target | Entry point | What it exercises | What a crash here means |
| --- | --- | --- | --- |
| `docx` | `sharp_text_docx::fuzz_api::extract` → `parse::extract` | The whole DOCX path: the zip central directory, each part's declared and decoded size, the relationships, the content type table, the run/cell/media counters | The container or a walker takes an input the caps do not refuse, or refuses it with a panic |
| `docx-xml` | `sharp_text_docx::fuzz_api::parse_xml` → `xml::parse` | The XML policy alone: no DTD, no external entity, bounded expansion, byte/depth/node ceilings | `roxmltree`, or the pre-parse scan in front of it, is reachable past the policy |
| `xlsx` | `sharp_text_xlsx::fuzz_api::extract` → `parse::extract` | `calamine` 0.38 over arbitrary bytes, the sheet/used-region/cell-text caps, the AST shaping | The spreadsheet reader allocates, indexes or recurses past what the caps measure |
| `pdf` | `sharp_text_pdf::fuzz_api::render` → `raster::render_page` | `hayro`'s object graph, content streams, fonts and PNG encoding of page 1, at 72 DPI under a four-megapixel ceiling | A malformed PDF panics the renderer, hangs it, or makes it allocate past the pixel cap |
| `barcodes` | `decode::decode_png_all_within` | The PNG decode through `image`, then every symbology's detector on the luma plane, under the pixel and byte ceilings | A hostile PNG or pixel pattern panics or hangs a decoder |

Every target links the parser crate directly, not the C ABI. That is deliberate: `catch_unwind` at
the ABI boundary is a guarantee for the *caller* (P4 of [29](29-threat-model.md)), and a fuzzer that
stopped at it would learn nothing. libFuzzer sees the panic before anything unwinds it, and ASan sees
the out-of-bounds write the panic may never precede.

The `pdf` path is the slow one — a render is milliseconds at best and the corpus holds real pages —
so it measures thousands of inputs rather than tens of thousands. That is acceptable: the crashes
worth finding in a rasteriser are in the parser and the stream interpreter, which a single mutated
page reaches in depth.

`text-ocr` is out of scope. Its two inputs are an image decode (the same `image` crate the barcode
target drives) and a PDF page render (the same `hayro` the `pdf` target drives), and its model
inference is a `.rten` file, not bytes a sender chose — [21](21-ocr.md) makes the model a
caller-supplied asset. The compiler (`sharp-typst`) is out of scope for a different reason, stated
in §6.

## 3. The corpus

A fuzzer that starts from noise spends its window learning the file signature. The seeds are the
repository's own fixtures, `fuzz/corpus/<target>/`:

| Target | Seeds | Where they came from |
| --- | --- | --- |
| `docx` | `report.docx` | `tests/Text.Docx.Tests/fixtures/report.docx` |
| `docx-xml` | the six XML parts of that package | extracted from the same fixture, one file per part |
| `pdf` | eight documents | `tests/Text.Pdf.Tests/fixtures/**/*.pdf` — the PDF/A set, the xref-stream and object-stream documents, a foreign producer's file, an encrypted one |
| `barcodes` | `logo.png` | `tests/Text.Pdf.Tests/fixtures/pdfua/logo.png` — a real PNG, which is what the decoder's front half accepts |
| `xlsx` | `seed.xlsx` | written by `fuzz/scripts/seed-xlsx.py`, not committed |

The XLSX seed is generated because the repository has no `.xlsx` fixture at all: `native/text-xlsx`
builds every test workbook in memory (`src/fixture.rs`, test-only) rather than committing a binary.
The script writes the same seven parts Excel writes — content types, both relationship files, the
workbook, one sheet, a shared string table, a style table — with a fixed zip timestamp, so the seed
is reproducible byte for byte and reviewable as the XML it is. A committed binary a six-line script
reproduces is a binary nobody can read in a diff.

No other corpus entry is synthesised. A seed is a real document, not a guess at what a malformed one
looks like; the malformed ones are libFuzzer's job, and pre-writing them would hide exactly the
inputs it exists to try. The README's `samples/examples` tree holds the second copy of `report.docx`;
one copy is enough.

## 4. The run

`.github/workflows/fuzz.yml` runs one job per target, in a matrix, on `ubuntu-latest`:

- **nightly**, because `-Zsanitizer=address` and `libfuzzer-sys` are nightly-only;
- `cargo-fuzz` pinned to `0.13.2`, so the flags and the runtime move only in a reviewed change;
- `cargo +nightly fuzz run <target> fuzz/corpus/<target> -- -max_total_time=<N> -timeout=25 -rss_limit_mb=4096`.

`-max_total_time` is 90 s by default and settable from 60 to 900 on a manual run. `-timeout=25`
bounds one input: a parser that takes twenty-five seconds on a few kilobytes is a denial of service
even when it returns, and libFuzzer records it as a `timeout-` artefact rather than hanging the job.
`-rss_limit_mb=4096` gives a decompression bomb a ceiling below the runner's memory.

**Why not on every pull request.** The build is the cost: a nightly toolchain, ASan, and four parser
crates with hayro, calamine and rxing compiled under coverage instrumentation — ten to fifteen
minutes before the first input runs, against ninety seconds of fuzzing per target. That buys close to
nothing over `ci-text.yml`'s `cargo test` on a single small change, because a mutation fuzzer finds a
bug in hours of accumulated execution, not in ninety seconds. So the cadence is the one that pays:
a **weekly** run (Monday 03:17 UTC) and `workflow_dispatch`, with an optional `target` input and an
optional `seconds` input so a parser change can be fuzzed on its branch:

```
gh workflow run "fuzz (parsers)" --ref <branch> -f target=docx -f seconds=300
```

**A crash is a red job.** There is no `continue-on-error` on the fuzz step — not as an accident, as
the point of the workflow. A green badge means "this window produced no crash", which is a weaker
claim than the one a required check makes, and the difference has to stay visible. `fail-fast: false`
keeps one parser's crash from cancelling the four others in the same wave.

## 5. What a crash gives

libFuzzer writes the exact input it failed on to `artifacts/<target>/crash-<sha1>` (or `timeout-…`,
`oom-…`), and the job uploads that directory as `fuzz-artifacts-<target>`, retained for thirty days,
whether the run failed or not. The artefact is the reproduction, and replaying it needs no CI:

```
cargo +nightly fuzz run docx fuzz/artifacts/docx/crash-<sha1>
```

What comes back is a stack trace with line numbers in the parser or in the dependency, and the
input's own bytes. Neither is a fix: the fix is a limit or a check, stated in the source beside the
code it bounds, reported as a typed refusal, and pinned by a test that fails when the limit is
removed — the procedure of [29 §3](29-threat-model.md). The crashing input then becomes a corpus
seed (committed under `fuzz/corpus/<target>/`) so the same shape is exercised on every later run, and
a regression test pins the refusal at the managed API.

An issue is filed for a crash that is a real defect against a shipped crate; a crash that is already
refused by a limit but panics before the refusal is a defect too, because P3 of
[29](29-threat-model.md) says the caller receives a code, not a panic.

## 6. What the fuzzing does not cover

Stated plainly, because a fuzzing page that reads as "the parsers are fuzzed" invites the assumption
that everything is:

- **The compiler.** `native/sharp-typst` binds the whole Typst engine — fonts, packages, layout,
  world. Fuzzing it needs Typst's own corpus and hours per input class, and the suite ships it as a
  separate crate with its own workflow; it is not reachable with ninety seconds and a four-crate
  harness. [03](03-compilation.md) is where that boundary is drawn.
- **The network and the file system.** No reader opens a path, resolves a URI or downloads a package
  ([29 §2](29-threat-model.md)); the fuzz inputs are bytes in memory, so those paths have nothing to
  fuzz. The C ABI itself — handle lifetimes, `catch_unwind`, the owned buffers — is covered by the
  managed test suite (`tests/Text.Suite.Tests`), not here.
- **The managed readers.** `PdfFile`, `PdfLexer`, `PdfTextReader`, `PdfStandardSecurity` and
  `PdfAXmp` are C# parsers with their own threat-model rows and their own tests
  ([29 §5.1](29-threat-model.md)); this harness reaches the Rust side only. A fuzzer for the managed
  PDF reader is a separate item, and a separate runtime.
- **The OCR models.** A `.rten` model is a caller-supplied asset, and its decoder is the crate's own
  code; a malformed model is a different attack from a malformed document, and no target here feeds
  one.
- **Correctness.** A fuzzer finds crashes and hangs. A parser that returns the wrong text for a valid
  document, or accepts a document it should refuse, comes back green here; that is what the
  conformance and fixture tests are for.
- **Long-tail crashes.** Ninety seconds a week is a smoke test, not a campaign. A bug behind a
  10 000-byte magic prefix may never be hit. Raising the window is a dispatch input
  (`-f seconds=900`), and a crash found there is worth a longer campaign on the branch.

## 7. The door the fuzzers use

Three crates keep their reader modules private — that privacy is deliberate: [29 P1](29-threat-model.md)
says bytes of a format enter through one documented entry point, and a `pub mod parse` would be a
second door with a weaker contract. The fuzzer still has to reach the parser, so each crate exposes
one `#[doc(hidden)] pub mod fuzz_api` with functions that call exactly the code the ABI calls and
drop the result:

| Crate | `fuzz_api` |
| --- | --- |
| `native/text-docx` | `extract(bytes)` — `parse::extract` with default options; `parse_xml(bytes)` — `xml::parse` |
| `native/text-xlsx` | `extract(bytes)` — `parse::extract` with default options |
| `native/text-pdf` | `render(bytes)` — `raster::render_page(1, …)` at 72 DPI, 4 MP, 32 MiB |
| `native/text-barcodes` | none needed: `decode` is already public, and the target calls `decode_png_all_within` |

`#[doc(hidden)]` keeps the module out of the generated documentation, and nothing in the managed
binding calls it. It adds no bound and changes no behaviour: each function is a two-line call to a
function that already existed, so the crates' own tests and limits are exactly what they were before
the targets existed. The PDF door lowers the ceilings (72 DPI rather than 150, four megapixels rather
than fifty) purely so one iteration stays short; the bounds enforced are the crate's own.

## 8. The first run

Measured on a Linux x86_64 host, nightly 1.102.0, ASan, 31 seconds per target with the corpus of §3.
Recorded here because "it runs" is a claim, and these numbers are the evidence:

| Target | Executions | Coverage (edges) | Result |
| --- | --- | --- | --- |
| `docx` | 57 935 | 3 417 | no crash, no timeout |
| `docx-xml` | 21 500 | — | no crash |
| `xlsx` | 36 170 | 3 986 | no crash |
| `pdf` | 56 | 18 076 | no crash; the render is the slow path, as §2 says |
| `barcodes` | 36 675 | 2 732 | no crash |

No crash was found in this window, and none is claimed. A clean run is the expected outcome for
parsers that already pass the limit tests of [29](29-threat-model.md); the value of the harness is in
the weeks it keeps running, and in the artefact a red job hands back when one of them is not clean.
