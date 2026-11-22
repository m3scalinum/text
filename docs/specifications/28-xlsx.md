# 28 — Reading Excel workbooks

## 1. Scope

`M3scalinum.Text.Xlsx` reads an Excel `.xlsx` workbook into typed sheets and cells, and turns a sheet
into a `TypstDocument` of the suite's own model, so a workbook compiles through the compiler an
application already hosts:

```text
XlsxWorkbook workbook = XlsxReader.Read(File.ReadAllBytes("facture.xlsx"));

TypstDocument typst = workbook.ToTypstDocument();

var world = new TypstWorldBuilder().AddSource("main.typ", typst.ToMarkup());

using CompilationResult pdf = new TypstCompiler().Compile(world.Build());

File.WriteAllBytes("facture.pdf", pdf.ExportPdf());
```

Bytes in, a document out: no Excel, no Open XML SDK, no file system once the bytes are in hand.
Reading and conversion are separate calls over separate objects, so a host can read a workbook,
inspect a cell, and decide what to render.

**In scope.** Every sheet, in workbook order, with its used region; typed cell values — text, number,
boolean, date, time of day, duration and error — and formulas with the result the workbook cached for
them.

**Out of scope, deliberately.** Table 1 in §7 lists what is dropped and why.

## 2. Why `calamine`

A spreadsheet reader is a parser of attacker-supplied XML inside a zip container: shared strings,
styles, dimensions and formulas, each with its own way of lying about the next allocation. Writing
that here would be a project of its own, and the suite's rule is a Rust dependency where one is
proven.

`calamine` 0.38.0 satisfies the rule. It is MIT, it is a pure-Rust reader, it has no network and no
file-system API on the parse path, and it reads `.xlsx` through `zip` 9.8.1 — the same `zip` line
`native/text-docx` uses, so the suite's container position does not fork per format. Its own features
are taken **off**:

| Feature | Why it is not enabled |
| --- | --- |
| `chrono` (and its deprecated synonym `dates`) | Dates arrive as serial numbers, which this crate converts itself: one dependency less, and the conversion is then testable without a calendar crate. |
| `picture` | A workbook's pictures are not part of what a report cites. Enabling it would copy media into memory for nothing. |

**Decision — one JSON AST per workbook, not a per-cell ABI.** Reading a workbook is one call that
returns everything; a host that wants one cell reads the tree it already holds. A per-cell entry point
would turn a 20 000-cell invoice into 20 000 boundary crossings, and would need a handle per cell.

## 3. The native crate

`native/text-xlsx` builds `sharp_text_xlsx`, a `cdylib` with the conventions of `sharp_typst`
(Spec 02): `extern "C"`, `catch_unwind` at every entry point, owned buffers freed by the caller, one
owned error object per failure. Its symbols are prefixed `tx_` so a process that loads the DOCX
reader, this reader and the compiler never has to wonder which crate answered.

| Symbol | Purpose |
| --- | --- |
| `tx_abi_version` | 1. The host refuses a library it does not know. |
| `tx_workbook_parse` | Bytes in, an owned handle out. Options are optional JSON. |
| `tx_workbook_ast_json` | The whole workbook as JSON, in a caller-owned buffer. |
| `tx_workbook_free` | Releases the handle and the tree it owns. |
| `tx_workbook_live_count` | Handles alive right now. |
| `tx_buffer_free`, `tx_error_free`, `tx_error_code`, `tx_error_json` | The shapes of Spec 02, verbatim. |

Status codes are the ones `02-native-abi.md` defines where the meaning matches:

| Code | Name | Meaning |
| --- | --- | --- |
| 0 | `ok` | |
| 1 | `invalid_argument` | A null pointer, an options payload that does not parse, a null handle |
| 2 | `invalid_handle` | A handle this library does not own |
| 3 | `panic` | A panic caught at the boundary; the bug is here |
| 9 | `limit` | Over the byte ceiling, or a used region over the cell ceiling |
| **11** | **`malformed_package`** | Not a zip package, or a part of it does not parse |
| 13 | `internal` | A failure this library does not classify |

`missing_part` (12) and `unsupported` (10) are defined by the ABI and not raised here: `calamine`
reports a package without `xl/workbook.xml` as a zip or XML failure, and this reader has nothing it
refuses as unsupported — a cell of a type it does not know becomes a blank cell, not an error.

### 3.1 The bounds

A workbook a sender supplies is bounded before it is shaped:

| Bound | Value | What it stops |
| --- | --- | --- |
| Whole workbook | `Options::max_bytes`, 64 MiB by default | A range request gone wrong, a 3 GB upload |
| Used region | `Options::max_cells`, 1 000 000 by default | A sheet whose dimension claims seventeen billion cells, which would become a table no renderer could lay out |

The byte ceiling is checked before the package is opened, so an accidental upload fails in
microseconds. The cell ceiling measures `rows × columns` of the used region, not the number of cells
that hold a value: a sparse sheet with data at `A1` and `XFD1048576` is refused, because rendering it
means building the rectangle.

**Decision — the used region is measured, and reported.** `calamine` builds its range from the cells
it actually reads (`Range::from_sparse`), so the `<dimension>` element a file declares neither sizes
nor shifts what this reader sees. The range this crate checks is the one that was read.

**Limit — the check runs after `calamine` allocates the range.** A sheet whose two furthest non-empty
cells span a huge rectangle is allocated by `calamine` before this reader can refuse it: the `limit`
error stops the tree from being shaped and rendered, not the allocation inside the dependency. A host
that opens untrusted workbooks at scale should therefore lower `maxBytes`, which bounds the sheet XML
that can carry such a rectangle, and keep the byte ceiling of its own transport in front of this
reader.

### 3.2 Options

`tx_workbook_parse`'s third argument is `{"maxBytes": number, "maxCells": number}`, or null for the
defaults. An unknown key is ignored, an absent one takes its default, and a payload that is not JSON
is `invalid_argument`.

## 4. The AST

`tx_workbook_ast_json` returns the whole workbook in one buffer.

```json
{
  "sheets": [
    {
      "name": "Facture",
      "rows": 7,
      "columns": 5,
      "rowOffset": 0,
      "columnOffset": 0,
      "cells": [
        {"row": 0, "column": 0, "type": "text", "text": "Facture 2026-014"},
        {"row": 0, "column": 2, "type": "date", "iso": "2026-01-01", "dateOnly": true},
        {"row": 2, "column": 1, "type": "number", "number": 1000.0},
        {"row": 2, "column": 2, "type": "boolean", "value": true},
        {"row": 3, "column": 3, "type": "error", "error": "#DIV/0!"},
        {"row": 4, "column": 1, "type": "number", "number": 1200.0, "formula": "=B3+B4"},
        {"row": 5, "column": 1, "type": "blank", "formula": "=B3*0.2"}
      ]
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `rows`, `columns` | The size of the used region |
| `rowOffset`, `columnOffset` | Where the used region starts on the sheet, zero-based: a sheet whose data begins at `C5` reports `4` and `2` |
| `row`, `column` | Position **inside** the used region, zero-based |
| `type` | `text`, `number`, `boolean`, `date`, `duration`, `error` or `blank` |
| `iso` | A date as ISO 8601 without a zone: `2026-01-01` or `2026-01-01T10:30:00.500` |
| `dateOnly` | True when the time part is midnight |
| `seconds` | A duration in seconds, from a `[h]:mm:ss`-style format |
| `formula` | The formula with its leading `=`, when the cell holds one |

**Decision — the region is normalised, and its origin is reported.** Cells are coordinates inside the
rectangle the sheet uses, and the offsets say where that rectangle sits. A table rendered from a sheet
whose data starts at row 5 therefore has no four empty rows above it, and a report can still cite the
cell's Excel name.

**Decision — a formula with a cached value is a value with a formula.** Excel writes the result of its
last calculation into the package; that result is the cell's `type`, and `formula` is how it got
there. A reader that discarded the cached value would have to evaluate the formula, which means
implementing Excel, and a reader that discarded the formula would lose the only record of what the
cell computes.

**Decision — a formula with no cached value is `blank` with a `formula`.** A workbook can hold a
formula whose result was never computed — written by a producer that does not calculate, or by Excel
with calculation off. Reporting it as `blank` alone would say the cell is empty; the `formula` says
what it would hold. A cell with no value and no formula is absent from `cells` altogether.

**Decision — `calamine` is asked for the sheet twice.** `worksheet_range` reads values, including
cached formula results, and `worksheet_formula` reads the `<f>` text, expanding shared formulas. They
are two passes over the same XML because `calamine` offers them as two calls; the alternative — its
streaming `XlsxCellReader` — would put this crate's AST in the middle of a stateful loop for a
saving that is measurable only on workbooks near the byte ceiling.

## 5. The managed model

`XlsxReader.Read(byte[])` (and `Read(Stream)`) returns an `XlsxWorkbook`:

| Member | Type | Meaning |
| --- | --- | --- |
| `Sheets` | `IReadOnlyList<XlsxSheet>` | The sheets, in workbook order |
| `Find(string)` | `XlsxSheet?` | A sheet by tab name, or null |
| `ToTypstDocument()` | `TypstDocument` | The whole workbook, and §6 says how |

An `XlsxSheet` carries `Name`, `RowCount`, `ColumnCount`, `FirstRow`, `FirstColumn`, `Cells`, and
`Find(row, column)` for a position inside the region. `XlsxCell` carries `Row`, `Column`, `Value`,
`Formula` and `DisplayText`, and `Reference(sheet)` for its `A1` name.

`XlsxCellValue` is a closed hierarchy — the constructors are internal — so a `switch` over it is
exhaustive: `XlsxTextValue` (`Text`), `XlsxNumberValue` (`Number`), `XlsxBooleanValue` (`Value`),
`XlsxDateValue` (`Value`, `IsDateOnly`), `XlsxDurationValue` (`Value` as a `TimeSpan`),
`XlsxErrorValue` (`Error`) and `XlsxBlankValue`. Every one of them answers `DisplayText`, which is
what a rendered table shows:

| Value | `DisplayText` |
| --- | --- |
| Text | the characters |
| Number | the value in the invariant culture, `G15` |
| Boolean | `TRUE` or `FALSE`, as Excel prints it |
| Date, date only | `2026-01-15` |
| Date with a time | `2026-01-15 10:30:00` |
| Duration | `36:00:00` |
| Error | `#DIV/0!` |
| Blank with a formula | the formula, `=B3*0.2` |

`XlsxOptions` has two members: `MaxWorkbookBytes` (default 64 MiB) and `MaxCells` (default
1 000 000). Both are validated before anything is read, so a host's typo is an
`ArgumentOutOfRangeException`, not a native call.

**Decision — the tree is built in managed code from the JSON, not exposed as JSON.** The AST is the
wire format between two libraries; the tree is the API. Reading the JSON into DTOs and then into the
public types buys a model with no `type` discriminator for a caller to switch on, a `DateTime` where
the wire has a string, and one place to compute `DisplayText`.

## 6. Conversion to Typst

`XlsxWorkbook.ToTypstDocument()` renders every sheet as a `= ` heading named after its tab, followed
by the table its used region covers. `XlsxSheet.ToTypstDocument()` renders one sheet: the table, and
nothing else.

| Excel | Typst |
| --- | --- |
| One sheet | `#table(columns: (1fr, …, 1fr))`, one row per row of the region |
| A cell with a value | the cell's `DisplayText` as content |
| An empty position | an empty cell, so the columns stay aligned |
| A sheet in a workbook | `= Sheet name` above its table |
| An empty sheet | nothing: no heading, no table |

**Decision — a cell is text, not a number.** The model's table takes `TypstContent`, and a numeric
cell written as a number would print with Typst's default number formatting, which is not the
workbook's. A report cites the value as the spreadsheet shows it; `DisplayText` is that, and a host
that wants arithmetic reads `XlsxNumberValue.Number` instead.

**Decision — column widths are `1fr` each.** A spreadsheet's column widths are in character units and
its row heights in points, and neither survives `calamine`'s range: what the reader has is a
rectangle of values. Equal fractions keep the table inside the page, which is what a report needs.

## 7. What is lost

| Not read | Why, and what the reader does instead |
| --- | --- |
| Merged cells | `calamine`'s merged-region table is not consulted, so a merge reads as its top-left value and empty cells beside it. The values are all present; the merge is not. |
| Cell formatting | Bold, fill, borders, number formats and alignment are not read. A number shows as its value in the invariant culture, not in the format the sheet displays. |
| Formulas that were never calculated | Reported as `blank` with their formula; see §4. |
| Shared-formula templates | `calamine` expands a shared formula onto the cells that carry it; the anchor cell's template text is what those cells report. |
| Charts, images, pivot tables, comments | Not read at all: a report built from a workbook cites values. |
| Named ranges, defined names | Ignored. |
| The 1904 date system | Converted, not reported: `calamine` carries the epoch and the serial becomes the right ISO date either way. |
| Conditional formatting, data validation | Not read: they are rules about the sheet, not values in it. |
| `.xls`, `.xlsb`, `.ods` | Not supported. The package reads `.xlsx`; `calamine` could read the others, and each would need its own tests before this package claimed them. |

The rule behind the table: **nothing that is not read throws.** A workbook with charts reads, and the
charts are lost. Only a package that cannot be opened — not a zip, over the byte ceiling, a region
over the cell ceiling — is an exception, because there is then nothing to hand back.

## 8. Licence

| Crate | Version | Licence | Copyright |
| --- | --- | --- | --- |
| `calamine` | 0.38.0 | MIT | © 2016 Johann Tuffe (`https://github.com/tafia/calamine`) |
| `quick-xml` | 0.42.0 | MIT | the quick-xml contributors |
| `encoding_rs` | 0.8.35 | (Apache-2.0 OR MIT) AND BSD-3-Clause | © Mozilla Foundation, and the WHATWG Encoding Standard data |
| `codepage` | 0.1.3 | Apache-2.0 OR MIT | © 2016 Johann Tuffe |
| `atoi_simd` | 0.18.1 | MIT OR Apache-2.0 | the `atoi_simd` contributors |
| `fast-float2` | 0.2.4 | MIT OR Apache-2.0 | the `fast-float2` contributors |
| `zip` | 9.8.1 | MIT | © 2014 Mathijs van de Nes |
| `zopfli` | 0.8.3 | Apache-2.0 | the `zopfli` contributors |
| `zlib-rs` | 0.6.8 | Zlib | © 2024 Trifecta Tech Foundation |
| `byteorder` | 1.5.0 | Unlicense OR MIT | © 2015 Andrew Gallant |

Every row was read from the crate's published `Cargo.toml` and its shipped licence files, not from a
summary. `calamine` declares `license = "MIT"` and ships `LICENSE-MIT.md` with `Copyright (c) 2016
Johann Tuffe`. `encoding_rs` is `(Apache-2.0 OR MIT) AND BSD-3-Clause` because the WHATWG encoding
tables it embeds carry the three-clause BSD; this crate takes the Rust code under the Apache half and
reproduces the BSD text for the data. `zlib-rs` is the one Zlib-licensed crate in the suite's whole
native graph, and `THIRD-PARTY-NOTICES.md` reproduces its text.

`THIRD-PARTY-NOTICES.md` at the repository root is the file a release ships, and it is maintained by
the release, not by this specification.

## 9. Tests and fixtures

`tests/Text.Xlsx.Tests` builds its workbook in memory, from raw OOXML, with `XlsxFixture` — content
types, both relationship files, the workbook, its sheets, a shared string table and a style table with
one date format and one duration format. Nothing is committed as bytes, so the fixture cannot drift
from the builder.

- The reader tests assert a text cell, a number, a boolean, a date, a duration, an error, a formula
  with its cached value, a formula without one, the used region, a region that starts at `C5`, two
  sheets in workbook order, an empty sheet, `A1` references and the positions a cell reports.
- The guard tests assert the typed failures: a null array, a null stream, empty bytes, a stream over
  the byte limit, bytes over it, a region over the cell limit, a limit that is not positive, bytes
  that are not a package, and a zip that is not a workbook.
- The value tests assert `DisplayText` for every shape, including a duration past twenty-four hours.
- The conversion tests assert the markup a sheet produces, that one sheet carries no heading, that a
  workbook names each sheet, that an empty sheet renders no table, and that a cached value is what
  the table shows.
- The end-to-end test reads the fixture, converts it, compiles the pages with the real compiler and
  asserts the bytes are a PDF and the page count is one. It skips itself when
  `SHARPTYPST_NATIVE_PATH` is absent, which is what the `extras` job of `ci-text.yml` provides.

The native crate carries its own unit tests, run with `cargo test --lib`:
`parse` — every cell type read back from a package the test writes, a date with a time, a duration, a
formula with and without a cached value, several sheets, an offset region, an empty sheet, bytes that
are not a package, the byte ceiling and the cell ceiling; `ffi` — the buffer and error shapes;
`lib` — the options payload, the handle count and the null handle.

The `extras` job of `ci-text.yml` runs `dotnet test tests/Text.Xlsx.Tests` with
`SHARPTEXTXLSX_NATIVE_PATH` pointing at the Linux build, measures the assembly alone, and fails the
run under 90 % line coverage of `M3scalinum.Text.Xlsx`.
