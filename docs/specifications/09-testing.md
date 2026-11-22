# 09 — Testing

## 1. The suites

| Level | Project | Needs natives | Tests | Runtime |
| --- | --- | --- | --- | --- |
| Unit (compiler) | `tests/Text.Typst.Tests` | No — fake native | 459 | ~2 s |
| Unit (signing, conformance, pages, forms, text, encryption) | `tests/Text.Pdf.Tests` | No | 345 | ~10 s |
| Unit (mcp) | `tests/Text.Mcp.Tests` | No | 95 | ~3 s |
| Unit (barcodes) | `tests/Text.Barcodes.Tests` | **Yes** — `sharp_text_barcodes` | 105 | ~5 s |
| Unit (docx) | `tests/Text.Docx.Tests` | **Yes** — `sharp_text_docx` | 46 | ~5 s |
| Unit (xlsx) | `tests/Text.Xlsx.Tests` | **Yes** — `sharp_text_xlsx` | 68 | ~3 s |
| Unit (ocr) | `tests/Text.Ocr.Tests` | No — fake native | 99 | ~5 s |
| Unit (generator) | `tests/Text.Templates.Generator.Tests` | No | 14 | < 5 s |
| Layouts | `tests/Text.Templates.Tests` | Yes, for the render tests | 111 | ~15 s |
| Suite (surface and docs) | `tests/Text.Suite.Tests` | No | 7 | < 2 s |
| Integration | `tests/Text.Typst.Integration.Tests` | **Yes** — `SHARPTYPST_NATIVE_PATH` | 241 | < 3 min |
| ABI | `native/sharp-typst` (`cargo test`) | — | 9 | < 30 s |
| Unit (native xlsx) | `native/text-xlsx` (`cargo test --lib`) | — | 36 | < 5 s |
| Benchmark | `benchmarks/Text.Typst.Benchmarks` | Yes | 8 scenarios | manual / nightly |

That is 1 590 managed tests in eleven suites, plus the nine Rust ABI tests of `native/sharp-typst`, the
thirty-six of `native/text-xlsx`, and the benchmark scenarios.

A suite marked *fake native* exercises the layer above the ABI through an `internal` seam, which is
why it runs on a machine with no Rust toolchain; a suite marked **Yes** needs the real cdylib from
`native/artifacts` (or, for the compiler, the path in `SHARPTYPST_NATIVE_PATH`). `Text.Barcodes.Tests`
and `Text.Docx.Tests` fail with `DllNotFoundException` on a checkout whose `native/artifacts` carries
no `win-x64` build; `Text.Xlsx.Tests` needs the same kind of library but skips the reader tests when
it cannot load one, so a documentation-only change still goes green without a Rust toolchain.

The split is deliberate: everything that can be tested without loading a native library is, so
that `dotnet test` on a machine without a Rust toolchain still covers the writer, the template
binder, the diagnostic mapper and the option validation — and, since the suite packages contain no
compiler at all, their two suites too.

`tests/Text.Suite.Tests` is the one project that references every package. It holds the checks that
only make sense over the whole surface: no two public types share a name across namespaces, every
public type lives in a `M3scalinum.Text.*` namespace, and every type or method named in
`docs/specifications`, `wiki/` and `README.md` exists. It lives on its own so that a change to
`M3scalinum.Text.Pdf` does not pull the compiler through six native builds to run it, and so that a
change to the compiler cannot skip it: `ci-text.yml` runs it.

## 2. Unit tests (no natives)

| Area | What is asserted |
| --- | --- |
| Markup writer | Every row of the escaping table in [04 §5](04-document-model.md#5-escaping), including line-start rules and `]` inside content blocks |
| Writer invariants | No double blank lines, no trailing whitespace, single trailing newline, 2-space indentation |
| Nodes | Each node emits the documented Typst for a representative input; empty content emits nothing |
| Lengths | Formatting of `cm`, `mm`, `pt`, `em`, `%`, `fr`; invariant culture; `NaN`/`Infinity` rejected |
| Document | `ToMarkup()` is idempotent and stable across two calls; metadata and page setup ordering |
| Template manifest | TOML parsing, missing fields, unknown types, `min_typst` comparison |
| Template binding | camelCase mapping, required/unknown/type-mismatch errors, `null` + default, array and object emission, string escaping |
| Diagnostics mapping | JSON payload → `TypstDiagnostic`, 1-based positions, `null` location, `IsInSource` |
| Options validation | Root path rules, page index ranges, PPI bounds, path traversal rejection |
| Exceptions | `Message` shape, `Code` values, `Diagnostics` non-empty for compilation errors |

## 3. ABI tests (Rust)

| Test | Asserts |
| --- | --- |
| `abi_version` | Returns 1 and a semver string |
| Every symbol | Callable with valid arguments; returns `TN_OK` |
| `NULL` arguments | Return `TN_ERR_INVALID_ARGUMENT`, never crash |
| Empty source | Compiles to a one-page document |
| Invalid UTF-8 path | `TN_ERR_INVALID_ARGUMENT` |
| Corrupt font | `TN_ERR_INVALID_ARGUMENT` at `add_font`, no panic |
| Panic path | A deliberately panicking test hook returns `TN_ERR_PANIC` with a traceback |
| Buffer ownership | `tn_buffer_free` on `{NULL,0}` is a no-op; double free is prevented by the caller contract and asserted with a canary |
| Handle lifetime | `tn_document_free` after `tn_world_free` is safe |
| Concurrency | 8 threads, 8 worlds, 200 compilations, no data race (run under `--test-threads` and, nightly, under Miri for the FFI types) |

## 4. Integration tests

| Group | Cases |
| --- | --- |
| Compilation | Minimal document; multi-page; imports; images; custom font; missing font fallback; math; table; footnote; reference |
| Formats | PDF (magic bytes, page count via a PDF reader), SVG (well-formed XML, one root per page), PNG (dimensions match PPI × page size), HTML (contains expected tags), Markdown, text |
| Merged exports | `ExportSvgMerged` and `ExportPngMerged` stack every page: the merged height is the sum of the pages, and a single-page document merges to one page |
| PDF options | PDF/A-2b and PDF/UA-1 write their metadata; a page range subsets the output; an untagged export is smaller; an unknown range and an incompatible standard combination are rejected |
| Fonts | A directory adds faces; a missing directory adds none; a document compiles with a font that only exists in the scanned directory |
| Async and helpers | `CompileAsync` returns a usable result, honours a cancelled token and surfaces a compilation error; the five `CompileTo*` helpers return the right format |
| In-memory templates | A template loads from a file set, renders, and rejects a missing entry or a missing `render`; a bundle writes to a zip |
| Clone leaks | 10 000 `Clone()` and 1 000 `WithSource()` calls keep managed memory flat, so the shared-font clone releases its native world |
| Document model coverage | Every node compiles: layout (`Columns`, `Align`, `Box`, `Block`, `Grid`, `Stack`, `Pad`, `Place`, `Hide`, `Rotate`, `Scale`, `Move`, `Skew`, `HSpace`, `VSpace`), lists, inline nodes, references, metadata, bibliography, introspection, the three export option types, the bundle, the embedded loader and the fixed clock |
| Documentation drift | Every `Typst*` type and every `.Method(` in a C# block of the specifications, the README and the wiki resolves against the public surface, and every `tn_*` function named in the ABI spec is exported by the crate; a guard test fails if the docs are not found, so the check cannot pass vacuously |
| Native library resolver | The search order is pinned: `runtimes/<rid>/native/`, then `runtimes/<os>/native/` (the universal macOS dylib), then the application directory; the file name and the RID match the platform |
| Concurrent dispose | Eight threads compile from one world while a ninth disposes it, twenty rounds; every caller sees a result or `ObjectDisposedException`, never a crash. A late caller on a disposed world gets `ObjectDisposedException` |
| TOML depth | A nesting of 1, 10 or 64 parses; 65, 1 000 and 100 000 throw a `FormatException` instead of overflowing the stack, and a manifest surfaces it as a `TypstTemplateException` |
| Template numbers | A finite `number` parameter renders, including `1e300`, `1e-300`, `double.MaxValue` and `double.Epsilon`; `NaN` and both infinities throw a `TypstTemplateException` naming the parameter |
| List-marker escaping | A paragraph starting with `1.` stays a paragraph (no `<ol>` in the HTML export); `1.5` is left alone; the unit tests pin the emitted markup for `1.`, `42.` and the non-marker cases |
| Template paths | A manifest `entry` of `..`, an absolute path or a drive letter is rejected; a nested entry inside the template still loads. Each case plants a real file outside the directory, so the test fails for the right reason |
| Bundle paths | `WriteTo` and `WriteToZip` reject `..`, an absolute path, a drive letter and an empty path; a nested relative path still works |
| Public API shape | No two public types share a name across namespaces, every public type lives in a `M3scalinum.Text.Typst` namespace, every public async method ends with `Async` |
| Introspection coverage | Named counters, `counter.at`, `state.at`, `context` text/let/measure, an outline with and without a title, and the typed HTML element with and without attributes |
| Page setup | Uniform and per-side margins, `MarginX`/`MarginY`, the last-call-wins rule, `Landscape`, `Fill`, `Columns`, argument validation; a landscape page is wider than tall, a portrait one taller than wide, and a footer with `PageNumber`/`PageCount` compiles |
| Diagnostic snippets | The caret renderer: gutter width, single-char span, multi-line span, hints after the snippet, `includeSnippet: false`, no span; the native layer fills `SourceLine` for errors, warnings and included files |
| Compilation options | `WarningsAsErrors` fails on a warning and leaves a clean document alone; `Kind` reports the pass; `FontFamilies` lists the embedded families and grows with a scanned directory |
| Determinism | Same world compiled twice → identical bytes; two processes → identical bytes |
| Diagnostics | Error with position; warning-only; error in an import; error in a package |
| Templates | Reference invoice template rendered with a typed options record; missing required parameter; unknown parameter |
| Packages | A package resolved from the cache directory; from the data directory; a missing package failing offline with the package name in the diagnostic |
| Dependency injection | Lifetimes, option propagation, defaults, validation failures, idempotence, replacement, one end-to-end compile through the container |
| Evaluation | Outline, labels and metadata of a real document, with final page numbers; a non-paged document rejected |
| Injection | 18 hostile strings through paragraphs, headings, tables, lists, code, quotes, links and metadata: every one stays text and the document still compiles |
| Concurrency | 16 worlds in parallel; a shared compiler across 8 parallel worlds; diagnostics that do not cross-talk |
| Document model | A model-built document compiles and its text is extractable |
| Lifetime | 10 000 compile/export/dispose cycles; handle count and managed heap flat |
| AOT | The sample app published with `PublishAot` runs and produces a PDF |
| Reference conformance | The 12 stdlib smoke documents compile (see §10) |

## 5. Golden files

```
tests/fixtures/
├── documents/            *.typ inputs
├── expected/
│   ├── pdf/              *.pdf   (compared by page count + extracted text, not bytes)
│   ├── svg/              *.svg   (compared byte-for-byte)
│   ├── png/              *.png   (compared by pixel hash)
│   ├── html/             *.html  (compared byte-for-byte)
│   ├── markdown/         *.md    (compared byte-for-byte)
│   └── text/             *.txt   (compared byte-for-byte)
└── fonts/                Inter, JetBrains Mono (OFL), a CJK subset
```

Rules:

- **PDF is not compared byte-for-byte in CI.** PDF output embeds a producer string and object
  ordering that can shift with a Typst patch release; the test asserts page count, page size and
  extracted text. Byte equality is asserted only in the determinism test, within one process and
  one version.
- SVG, HTML, Markdown and text are compared byte-for-byte. A diff is a failure, and the CI job
  uploads the unified diff.
- PNG is compared by SHA-256 of the decoded pixels, so a change in the encoder does not fail the
  test but a change in rendering does.
- Updating a golden file requires the `UPDATE_GOLDEN=1` environment variable and a review of the
  diff in the PR. There is no "accept all" switch.

## 6. Fixtures

- Fonts are committed (OFL-licensed) so that rendering does not depend on the host's fonts.
- Images are small PNGs generated by a script, committed as bytes.
- The reference template lives in `templates/invoice` and is used by both the sample app and the
  integration tests, so the documentation cannot drift from the code.

## 7. Performance tests

`benchmarks/Text.Typst.Benchmarks` is dependency-free on purpose: seven scenarios, 50 iterations
each, the **minimum** reported (the most stable estimator for CPU-bound work), and a 1.5× gate
against `benchmarks/baseline.json`.

| Scenario | Baseline (linux-x64, WSL) |
| --- | --- |
| `compile-small` | 2.97 ms |
| `compile-report` | 3.28 ms |
| `export-pdf` | 3.90 ms |
| `export-png` | 9.27 ms |
| `export-html` | 2.51 ms |
| `export-markdown` | 2.68 ms |
| `document-model` (100 builds) | 1.91 ms |

A shared runner varies by tens of percent between runs, so a tighter gate would fail on noise; 1.5×
still catches a real regression such as a lost cache or an extra copy.

**The leak tests are not in that table: they are correctness tests, not benchmarks.** Four of them
measure managed memory around a full collect, which is architecture-independent. The fifth measures
the process working set, and it warms up with 5 000 worlds before it starts measuring: the first
allocations grow the process to its steady state — runtime arenas, CRT pools, GC segments — and that
ramp is a property of the allocator, not of this library. Measured from a cold start, the same loop
reported 11 MiB on x64 and 468 MiB on Windows ARM64 for identical code, which fails a threshold that
is supposed to detect a leak. Excluding the ramp makes the signal the plateau, where a real leak
still grows and a pool does not.

## 7.1 Coverage

The project uses Microsoft.Testing.Platform, so `--collect:"XPlat Code Coverage"` does not work.
`dotnet-coverage` does:

```bash
dotnet-coverage collect -f cobertura -o unit.xml \
  "dotnet test tests/Text.Typst.Tests -c Release --no-build"
dotnet-coverage collect -f cobertura -o pdf.xml \
  "dotnet test tests/Text.Pdf.Tests -c Release --no-build"
dotnet-coverage collect -f cobertura -o int.xml \
  "dotnet test tests/Text.Typst.Integration.Tests -c Release --no-build --filter FullyQualifiedName!~LeakTests"
dotnet-coverage merge -f cobertura -o all.xml unit.xml pdf.xml int.xml
```

Two gates, one per train, because a change to one train does not start the other's workflow:

| Gate | Workflow | Collects | `--require` |
| --- | --- | --- | --- |
| compiler | `ci-sharptypst.yml` | the four suites above | the five assemblies |
| suite | `ci-text.yml` | `pdf.xml` | `M3scalinum.Text.Pdf` |

Each fails the build when production line coverage drops below **90 %**, and the merged report is
uploaded as an artifact. The compiler gate still measures the suite packages when it runs — they are
built anyway — but it is not what protects them: a pull request that only touches `src/Text.Pdf/**`
never starts that workflow, so the gate that would catch it is the one in `ci-text.yml`.

**`--require` scopes the measurement, it does not only assert presence.** The suite workflow collects
two suites, and `M3scalinum.Text.Typst` is linked into the output as a dependency — measured at
4.8 %, because nothing in those suites compiles a document. Counting it gave 25.9 % and a gate that
failed on every run. Restricted to the assembly the train owns, the same report reads 92.6 %.

The paths that drive the compiler are exercised by `Text.Typst.Integration.Tests`, which needs the
native library and therefore runs in the compiler workflow. The suite gate measures the managed half.

**Every suite must be in the merge, and the gate enforces it.** `coverage-gate.py` takes
`--require <assembly>…` and fails when one of them has no lines in the report: a suite that is not
collected makes its package look uncovered, which is a 0.8-point drop with no visible cause. That is
exactly what happens when a suite is added and the job still merges the files it knew about.

All five production assemblies merged: **92.2 % of production lines**. The classes below 80 % are the ones that cannot
be reached from a test — `NativeLibraryResolver` (the RID fallback paths), the source-generated
`TypstJsonContext` write methods for DTOs that are only deserialized, and `TypstTemplateAttribute`,
which is metadata for the generator and never read at runtime.

## 8. What is not tested, and why

| Not tested | Reason |
| --- | --- |
| Typst's own layout correctness | It is Typst's test suite, not ours |
| Every Typst function through the model | The model covers a subset by design; `Raw` is the escape hatch |
| Byte-identical PDF across Typst versions | Not a property Typst guarantees |
| Windows/macOS font rendering differences | Fonts are committed; system fonts are opt-in and out of the contract |
| Fuzzing the ABI | Planned for v1.1 (`cargo-fuzz` on the JSON config parser) |

## 9. Test naming and structure

- One test class per unit under test (`MarkupWriterTests`, `TemplateBinderTests`).
- Test names are sentences: `Escapes_hash_at_start_of_paragraph`.
- Arrange/Act/Assert with blank lines, no comments restating the code.
- No `Thread.Sleep`, no retries, no network. A test that needs the network is not a test.
- Integration tests are marked with a trait `[Trait("Category", "Integration")]` so they can be
  filtered out on machines without natives. With the xunit v3 runner the filter is a query:
  `dotnet test tests/Text.Typst.Tests -- -filter "/*/*/*[Category!=Integration]"`.

## 10. Reference conformance

The Typst reference is the specification of the language. Three tests keep M3scalinum.Text.Typst honest about
it (details in [11 §5](11-reference-coverage.md#5-conformance)):

| Test | Asserts |
| --- | --- |
| Inventory drift | `tests/fixtures/reference-inventory.json` matches the matrix in [11](11-reference-coverage.md); a Typst upgrade that adds or removes an item fails the build with the list of items to review |
| Stdlib smoke | `tests/fixtures/conformance/<category>.typ` — 12 documents, 151 items, each invoked at least once — compile on all three platforms |
| Typed surface | Every L1/L2 row of the matrix has a unit test for the emitted Typst and an integration test that compiles it |

The smoke documents are the answer to "is the whole reference available": they are compiled by the
shipped native library, not by a mock, and a function the compiler cannot reach fails CI.
