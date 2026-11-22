# Dependencies — the Rust crate policy

Every `Cargo.toml` in this repository is a decision, and the reasoning behind each one used to live
in a head: a crate was checked once, adopted, and nothing recorded *why* it was acceptable or what
would make it stop being acceptable. This document is that record. It states the rule, lists every
crate this repository has adopted or rejected with the licence read at the source, and is checked by
[`tools/check-dependencies.py`](../../tools/check-dependencies.py) on every pull request.

The rule is deliberately narrow. Cargo makes a dependency cheap to add and invisible afterwards: a
new crate enters the build graph, the licence file, and the attack surface of a library that binds
this code into other people's applications. The cost of a decision is one row here.

## 1. The rule

A crate may be adopted when all three hold.

| Test | Requirement | Why |
| --- | --- | --- |
| **Licence** | Permissive, compatible with this repository's Apache-2.0: `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `Zlib`, `Unlicense`, or an `OR` expression built from them | The suite is redistributed under Apache-2.0. A copyleft crate would make the distribution terms of every consumer depend on a dependency they never chose |
| **Traction** | A real user base: the release is downloaded at the scale of its peers, or the crate is the reference implementation of the thing it does | An abandoned or unknown crate becomes ours to maintain at the worst moment |
| **Maintenance** | A release in the last **twelve months**, and a reachable maintainer | An unmaintained crate is a future fork, a future CVE nobody patches, and a Rust version floor that stops moving |

Rejected without further discussion, whatever the traction:

- **GPL-2.0, GPL-3.0, AGPL-3.0, SSPL, Commons Clause** — copyleft that reaches the consumer's
  application, or a field-of-use restriction. This is what removed `epub` (see §3).
- **A licence that cannot be identified** — `non-standard`, a custom text, or no licence field at
  all. Unreadable terms are not permissive terms; this is what removed `markdown-rs`.
- **A crate whose maintenance has stopped**, unless it is small enough to vendor and read in an
  afternoon, and then only with a note saying so.

Weak copyleft (`MPL-2.0`, `EPL-2.0`, `LGPL`) is neither adopted nor rejected by the table below: it
needs a decision of its own, recorded as a row, because it constrains how the file may be modified
rather than how the result may be distributed.

A crate that passes also has obligations: the name, version and licence go in
[`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md), and a `[target.'cfg(...)'.dependencies]`
entry such as `openssl` counts exactly like a plain one.

## 2. Adopting a crate

1. **Look at the manifest, not at a blog post.** `curl https://crates.io/api/v1/crates/<name>` and
   read `crate.max_version`, the matching entry of `versions[]` for its `license`, `crate.updated_at`
   and `crate.recent_downloads`. When the licence field is blank or surprising, read the `LICENSE`
   file in the linked repository instead, and record which of the two you read.
2. **Weigh the alternative.** Every crate is compared against writing the thing: `zip` and
   `roxmltree` were adopted for the DOCX reader, `docx-rs` was rejected (§3) because its model would
   have been discarded on arrival. A crate that replaces two hundred lines of our code has to be
   better than those two hundred lines.
3. **Add one row** to the register in §4 — adopted crates first, alphabetically, then the rejected
   candidates — with the licence, the traction, the last update, and a decision that says *why*, not
   a restatement of the tests it passed.
4. **Declare it in one manifest.** Keep `default-features = false` and enable the features the
   package actually needs: a default feature set is a dependency the reviewer never sees.
5. **Run `python3 tools/check-dependencies.py`** before opening the pull request. It fails when a
   manifest declares a crate the register does not cover, when crates.io publishes a licence that
   differs from the row, and when an adopted crate has had no release for a year.
6. **Record the obligation** in `THIRD-PARTY-NOTICES.md` when the licence requires attribution.

Removing a crate is the reverse: drop the row when the last manifest stops declaring it, or change
its decision to `Rejected` with the reason, so the next reader does not re-open the question.

## 3. What the register covers

Every direct entry of a `Cargo.toml` in this repository — `[dependencies]`, `[dev-dependencies]`,
`[build-dependencies]` and their `[target.*]` forms — plus the candidates that were examined and
refused, so that a refused crate is refused once. Crates reached only transitively are out of the
rule's scope, except where they are named here for a reason: `quick-xml` is the one such row,
because it arrived through `docx-rs` and would otherwise reappear as a suggestion once `docx-rs` is
gone.

## 4. The register

One row per crate. `Downloads` is `all-time / last 90 days` at the date of the row, as crates.io
reports it; `Last update` is the date of the crate's most recent release. The checker reads this
table by its header: keep the column names and the `YYYY-MM-DD` date, or it will say so.

| Crate | Version | Licence | Downloads | Last update | Decision |
| --- | --- | --- | --- | --- | --- |
| `hayro` | 0.9 | Apache-2.0 OR MIT | 5.9 M / 4.3 M | 2026-11-02 | Adopted — pure-Rust PDF rasteriser, so OCR of a scanned page needs no native PDF engine |
| `image` | 0.25 | MIT OR Apache-2.0 | 261 M / 88 M | 2026-03-10 | Adopted — PNG in the barcode crate, the caller's formats in OCR; pinned to the version rxing uses |
| `ocrs` | 0.14 | MIT OR Apache-2.0 | 649 k / 405 k | 2026-11-08 | Adopted — text detection and line recognition, the engine behind `M3scalinum.Text.Ocr` |
| `openssl` | 0.10 (musl only) | Apache-2.0 | 469 M / 91 M | 2026-11-01 | Adopted — `typst-kit` pulls native-tls, and a musl host has no libssl to link |
| `quick-xml` | — | MIT | 569 M / 188 M | 2026-08-22 | Transitive — reached only through `docx-rs`, never used directly; named here so it is refused once |
| `roxmltree` | 0.21 | MIT OR Apache-2.0 | 112 M / 45 M | 2026-10-24 | Adopted — read-only XML tree, no DTD and no entity expansion, which is what a DOCX from a sender needs |
| `rten` | 0.28 | MIT OR Apache-2.0 | 2.5 M / 1.2 M | 2026-11-08 | Adopted — the inference runtime the OCR models run on |
| `rten-tensor` | 0.28 | MIT OR Apache-2.0 | 2.5 M / 1.2 M | 2026-11-08 | Adopted — the tensor type the runtime hands back across the ABI |
| `rxing` | 0.9.4 | Apache-2.0 | 857 k / 285 k | 2026-11-08 | Adopted — the barcode engine, same licence as this repository, encoders and decoders only |
| `serde` | 1 | MIT OR Apache-2.0 | 1.76 B / 455 M | 2026-07-18 | Adopted — the derive plumbing behind every JSON payload |
| `serde_json` | 1 | MIT OR Apache-2.0 | 1.66 B / 458 M | 2026-10-11 | Adopted — the payload format of the C ABI, in three crates |
| `typst` | 0.15.1 | Apache-2.0 | 4.3 M / 2.2 M | 2026-07-17 | Adopted — it is the compiler this suite exists to bind |
| `typst-bundle` | 0.15.1 | Apache-2.0 | 32 k / 18 k | 2026-07-17 | Adopted — packages a document with the assets it resolves |
| `typst-html` | 0.15.1 | Apache-2.0 | 4.0 M / 2.1 M | 2026-07-17 | Adopted — the HTML exporter |
| `typst-kit` | 0.15.1 | Apache-2.0 | 1.3 M / 485 k | 2026-07-17 | Adopted — fonts, packages and the downloader `SystemWorld` needs |
| `typst-layout` | 0.15.1 | Apache-2.0 | 4.0 M / 2.1 M | 2026-07-17 | Adopted — the layout engine, one half of the compile step |
| `typst-pdf` | 0.15.1 | Apache-2.0 | 3.9 M / 2.0 M | 2026-07-17 | Adopted — the PDF exporter |
| `typst-render` | 0.15.1 | Apache-2.0 | 567 k / 270 k | 2026-07-17 | Adopted — rasterises a page for image export and for OCR |
| `typst-svg` | 0.15.1 | Apache-2.0 | 4.0 M / 2.1 M | 2026-07-17 | Adopted — the SVG exporter |
| `zip` | 9.8 | MIT | 357 M / 111 M | 2026-11-20 | Adopted — the DOCX container with deflate only, whose default features carry five decoders we refuse |
| `calamine` | 0.38 | MIT | 19.3 M / 7.8 M | 2026-11-02 | Rejected — no spreadsheet reader is in the document model's scope; the licence was never the objection |
| `docx-rs` | 0.4.22 | MIT | 4.0 M / 794 k | 2026-07-21 | Rejected — reads and writes through a document model we would throw away; `zip` and `roxmltree` are smaller |
| `epub` | 2.1.6 | GPL-3.0-or-later | 318 k / 96 k | 2026-11-01 | Rejected — GPL-3.0-or-later cannot be redistributed inside an Apache-2.0 library |
| `lopdf` | 0.47 | MIT | 31.9 M / 15.5 M | 2026-11-04 | Rejected — the PDF family is written in managed code and must own the byte layout |
| `markdown-rs` | 0.1.0 | non-standard | 14.5 k / 675 | 2023-07-02 | Rejected — the licence is not identifiable and the crate has had no release since 2023 |
| `rust_xlsxwriter` | 0.103 | MIT OR Apache-2.0 | 7.2 M / 3.4 M | 2026-11-18 | Rejected — no XLSX export is in scope, and every format this suite writes is produced by Typst |

### Decisions worth keeping

**`epub` was rejected on its licence, and the ZIP writer was written by hand.** `epub` 2.1.6
publishes `GPL-3.0-or-later`. Reading an EPUB is a ZIP container plus a little XHTML, and the cost of
writing that against `zip` and `roxmltree` is smaller than the cost of a copyleft notice on every
consumer of `M3scalinum.Text.Typst`. The alternative — an optional package under GPL — was refused
too: it would split the suite's licence story for one format.

**`markdown-rs` was rejected twice over.** The 0.1.0 release declares `license = "non-standard"`, so
its terms have to be read out of a file that does not resolve them, and it has 675 downloads in
ninety days with no release since July 2023. Traction and licence both fail, independently.

**`zip` is adopted with `default-features = false`.** Its default feature set enables the AES,
bzip2, lzma, zstd and deflate64 decoders. A `.docx` is stored or deflated; every other decoder is a
parser over untrusted bytes that this package would otherwise carry, and each one has had an
advisory. The feature list is part of the decision, not a build detail.

**`image` is pinned in `text-barcodes`.** `image = "=0.25.8"` keeps one copy in the tree, because
`rxing` depends on that version. `text-ocr` takes `0.25` and turns on only the six decoders the
caller's formats need.

**`openssl` is a target-scoped dependency, not a general one.** `typst-kit`'s system downloader pulls
`native-tls`, which links OpenSSL. On musl there is neither a system `libssl` nor a `pkg-config`
path to one, so the musl build vendors OpenSSL and every other target links the system library. It
is `Apache-2.0` on crates.io, which is the reason it could be adopted at all.

## 5. How the policy is checked

`tools/check-dependencies.py` reads every `Cargo.toml` in the tree, reads this register, and compares
the two. Standard library only: no `requests`, no `toml`, no `PyYAML`.

```
python3 tools/check-dependencies.py            # online: also asks crates.io
python3 tools/check-dependencies.py --offline  # no network at all
```

Both modes fail, with a non-zero exit status, when:

- a crate declared in any `Cargo.toml` has no row in §4 — the check that refuses a new Rust
  dependency that arrived without a decision;
- a row is unreadable: no licence, no `YYYY-MM-DD` date, a decision outside
  `Adopted` / `Rejected` / `Transitive`, or a decision with no reason;
- an adopted crate is not declared by any manifest — the register describes a tree that is not this
  one;
- an adopted or transitive crate has had no release for a year. The offline mode reads the date this
  document records; the online mode reads the date crates.io publishes, so a document that has
  stopped being refreshed cannot hide a stale crate.

The online mode additionally asks `https://crates.io/api/v1/crates/<name>` for every documented
crate, and fails when the licence in the register no longer matches the one published for
`crate.max_version` — the specific drift this file exists to catch, since a licence can change
between two releases of a crate that did not change a line of its API. It prints a note, without
failing, when a newer release exists than the row records.

`.github/workflows/dependency-policy.yml` runs the **offline** mode on every `pull_request`: a
coverage check must not fail because crates.io is slow or rate-limiting, and the register's contents
are a review question, not a CI question. The **online** mode runs only on `workflow_dispatch`, where
a person is watching and a network failure means "ask again", not "the tree is broken".

## 6. Maintenance

The register is refreshed when a crate is added, removed or upgraded across a minor version, and on
the first working day of a quarter regardless. A row's `Last update` and `Downloads` are a snapshot
of that day; the online check reads the live values, so a forgotten refresh shows up as a note rather
than as a false green.
