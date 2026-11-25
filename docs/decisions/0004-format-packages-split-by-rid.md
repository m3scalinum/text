# 0004 — The five format packages are split by RID too

**Status:** accepted. **Area:** `M3scalinum.Text.Barcodes`, `.Docx`, `.Ocr`, `.Pdf`, `.Xlsx`, packaging.

## Decision

Each of the five format packages ships as a managed package plus one satellite per RID —
`M3scalinum.Text.Pdf` and `M3scalinum.Text.Pdf.Native.WinX64`, and the same for the other four and the
other seven RIDs. `src/Text.Format.Native.Package` packs the forty satellites from two properties, and
`tests/verify-package.py` refuses a managed format package that carries a native again.

This reverses §6.5 of `08-build-and-packaging.md`, which recorded the opposite, and the reversal is the
reason this record exists.

## Context

The compiler was split in 0003's neighbourhood — `M3scalinum.Text.Typst` plus seven satellites — after
#96 measured that a single package cost every consumer 168 MB to use 22.8 MB. The five format packages
were explicitly left alone, on two arguments written into §6.5:

- their libraries were "a few megabytes", so "the download a `win-x64` consumer pays for is a fraction
  of the compiler's";
- a split "would repeat the two-line install for packages that are usually pulled in transitively".

Both are false, and the first was false by an order of magnitude. Measured on the packages the CI
produces (`example-packages`, run of d3269db):

| Package | The package | One platform loads | Wasted |
| --- | ---: | ---: | ---: |
| `Ocr` | 111.3 MB | 12.5 MB | 89 % |
| `Pdf` | 63.0 MB | 6.4 MB | 90 % |
| `Barcodes` | 42.9 MB | 4.0 MB | 91 % |
| `Xlsx` | 13.0 MB | ~1.6 MB | 88 % |
| `Docx` | 9.6 MB | ~1.2 MB | 87 % |

And the second argument was already untrue at the time it was written: `Docx`, `Xlsx`, `Mcp` and
`Templates` depend on `M3scalinum.Text.Typst`, whose native ships as a satellite, so every consumer of
one of those packages already wrote a second line — the split does not introduce the two-line install,
it names it.

## Why not the alternatives

- **Leave them whole.** That is the status quo the numbers above reject: 89 to 91 % of every download
  is a library the runtime will never load.
- **One shared bundle per RID** (`M3scalinum.Text.Native.WinX64` carrying all five libraries): fewer
  packages, but a `Pdf` consumer then pays for `Ocr`'s 12.5 MB, which is the defect one level down.
- **Split only the heavy ones** (`Ocr`, `Pdf`): two conventions for five packages of one family, and
  `Barcodes` at 91 % waste would stay the worst offender.

## Consequences

- Forty more packages to pack and publish: forty-five per suite release instead of five. The release
  workflow packs the set with `tools/pack-format-packages.py`, and the guard fails a set with a hole.
- `samples/examples/run.sh` resolves the satellite from the feed for every format example, and knows
  that `Docx` and `Xlsx` need the compiler's satellite as well as their own.
- A consumer's install grows by one line and their download shrinks by six to nine times.
