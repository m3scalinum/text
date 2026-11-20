# Contributing to M3scalinum.Text.SharpTypst

Thanks for taking the time to contribute. This document covers what you need to build the project,
what CI will check, and what a pull request is expected to contain.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). For a security issue, do
not open a public issue — follow [SECURITY.md](SECURITY.md).

## Prerequisites

- .NET SDK 11
- Rust 1.92 or newer (edition 2024)
- Python 3, for the packaging and coverage scripts

On Windows, build the native crate under WSL: that is where it is developed and tested.

## Build

```bash
export PATH=$HOME/.cargo/bin:$HOME/.dotnet:$PATH
export DOTNET_ROOT=$HOME/.dotnet
export CARGO_TARGET_DIR=$HOME/.cache/sharp-typst-target
export SHARPTYPST_NATIVE_PATH=$HOME/.cache/sharp-typst-target/release/libsharp_typst.so

cargo build --release --manifest-path native/sharp-typst/Cargo.toml
dotnet build -c Release
```

The integration tests load the native library through `SHARPTYPST_NATIVE_PATH`; without it they fail
with `DllNotFoundException`.

## Test

```bash
dotnet test tests/Text.SharpTypst.Tests -c Release
dotnet test tests/Text.SharpTypst.Templates.Generator.Tests -c Release
dotnet test tests/Text.SharpTypst.Integration.Tests -c Release
```

| Suite | What it covers |
| --- | --- |
| `M3scalinum.Text.SharpTypst.Tests` | The managed layer against a fake native. Fast, no Rust needed. |
| `M3scalinum.Text.SharpTypst.Templates.Generator.Tests` | The Roslyn generator. |
| `M3scalinum.Text.SharpTypst.Integration.Tests` | The real compiler: formats, templates, conformance, limits, leaks, DI. |

The leak tests take about 50 seconds. The full sequence, including the coverage commands, is in
[Building from Source](wiki/SharpTypst-Building-from-Source.md).

## Gates

CI runs these on every pull request, and a pull request is not merged until they pass:

```bash
cargo fmt --check --manifest-path native/sharp-typst/Cargo.toml
cargo clippy --manifest-path native/sharp-typst/Cargo.toml --all-targets -- -D warnings
dotnet build -c Release
dotnet test tests/Text.SharpTypst.Tests -c Release
```

Plus, on the platforms that can run them: the integration suite against the real compiler, a 90 %
line-coverage floor, a Native AOT publish, and a benchmark comparison against
`benchmarks/baseline.json`.

## Code style

- `.editorconfig` is authoritative; `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` are on, so
  a style violation is a build failure.
- Public API members carry XML documentation — `GenerateDocumentationFile` is on and missing docs are
  warnings.
- Test names are sentences with underscores (`Compile_With_Invalid_Markup_Reports_A_Diagnostic`).
- Rust follows `rustfmt` defaults; `clippy` runs with `-D warnings`.

## What to change where

| Change | Where |
| --- | --- |
| User-facing behaviour | `wiki/` |
| A contract the implementation is held to | `docs/specifications/` |
| Anything a consumer can observe | `CHANGELOG-SharpTypst.md` for the compiler packages, `CHANGELOG-Text.md` for the suite, both under `[Unreleased]`. `CHANGELOG.md` summarises the project when a change crosses both |
| The C ABI | `docs/specifications/02-native-abi.md` **and** the Rust crate **and** `M3scalinum.Text.SharpTypst.Native` |

A change to the native ABI is the one that needs the most care: the Rust side, the P/Invoke
declarations and the specification must move together, and the integration suite must cover the new
path.

## Pull requests

1. Open an issue first for anything larger than a bug fix, so the design is agreed before you write
   it.
2. Keep a pull request to one concern. A refactor and a behaviour change in the same diff are hard to
   review and hard to revert.
3. Fill in the pull request template, including the platforms you actually ran.
4. Add tests. A bug fix without a test that fails before it is not finished.

Commit messages are free-form but should say what changed and why. There is no required prefix.

## Versioning

Two families, two versions. The packages that embed the compiler — `M3scalinum.Text.SharpTypst`,
`.DependencyInjection`, `.Templates.Generator` — version themselves after Typst:
`0.15.1.<revision>`. A change that requires a newer Typst is a version bump, not a patch, and it goes
in `CHANGELOG-SharpTypst.md`.

`M3scalinum.Text.Pdf` and `M3scalinum.Text.Pdf.FactureX` embed nothing and follow `TextVersion`
instead, `0.1.<revision>` for now. Their entries go in `CHANGELOG-Text.md`. A change that crosses
both trains also gets a line in `CHANGELOG.md`.

## Releasing

The two trains tag independently:

| Train | Tag | Workflow | Changelog |
| --- | --- | --- | --- |
| Compiler | `sharptypst-v0.15.1.42` | `release-sharptypst.yml` | `CHANGELOG-SharpTypst.md` |
| Suite | `v0.51.2` | `release-text.yml` | `CHANGELOG-Text.md` |

Nothing writes the changelogs on your behalf. Before pushing a tag:

1. In that train's changelog, rename `## [Unreleased]` to `## [<version>] — <date>`.
2. Add a fresh, empty `## [Unreleased]` above it.
3. Commit, then push the tag.

The two trains read their tag differently. For the compiler train the tag is
`sharptypst-v<TypstVersion>.<revision>` — the first three components must be `TypstVersion`, so a
Typst upgrade starts by bumping it in `Directory.Build.props`. For the suite the tag **is** the
version (`v0.51.2` publishes `0.51.2`); `TextVersion` is only the default an untagged build uses, so
bump it to the version you just released.

The release job takes the page body from the section named by the tag, and fails when that section
is missing or empty — a tag cannot publish a release with no notes.
