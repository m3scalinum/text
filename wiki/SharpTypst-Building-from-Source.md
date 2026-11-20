# Building from Source

## Prerequisites

- .NET SDK 11
- Rust 1.92 or newer (edition 2024)
- On Windows, build under WSL: the crate and the tests are developed there.

## Build

```bash
export PATH=$HOME/.cargo/bin:$HOME/.dotnet:$PATH
export DOTNET_ROOT=$HOME/.dotnet
export CARGO_TARGET_DIR=$HOME/.cache/sharp-typst-target
export SHARPTYPST_NATIVE_PATH=$HOME/.cache/sharp-typst-target/release/libsharp_typst.so

cd native/sharp-typst
cargo fmt --check
cargo clippy --release -- -D warnings
cargo build --release

cd ../..
dotnet build -c Release
```

The integration tests need `SHARPTYPST_NATIVE_PATH`; without it they fail with
`DllNotFoundException`.

## Test

```bash
dotnet test tests/Text.SharpTypst.Tests -c Release --no-build
dotnet test tests/Text.SharpTypst.Templates.Generator.Tests -c Release --no-build
dotnet test tests/Text.SharpTypst.Integration.Tests -c Release --no-build
```

| Suite | What it covers |
| --- | --- |
| `M3scalinum.Text.SharpTypst.Tests` | The managed layer against a fake native. Fast, no Rust needed. |
| `M3scalinum.Text.SharpTypst.Templates.Generator.Tests` | The Roslyn generator. |
| `M3scalinum.Text.SharpTypst.Integration.Tests` | The real compiler: formats, templates, conformance, limits, leaks, DI. |

The leak tests take about 50 s.

## Coverage

```bash
dotnet tool install -g dotnet-coverage

dotnet-coverage collect -f cobertura -o unit.xml \
  "dotnet test tests/Text.SharpTypst.Tests -c Release --no-build"

dotnet-coverage collect -f cobertura -o int.xml \
  "dotnet test tests/Text.SharpTypst.Integration.Tests -c Release --no-build --filter FullyQualifiedName!~LeakTests"

dotnet-coverage merge -f cobertura -o all.xml unit.xml int.xml
```

The project uses Microsoft.Testing.Platform, so `--collect:"XPlat Code Coverage"` does not work;
`dotnet-coverage` does.

The `coverage` job of `ci-sharptypst.yml` runs the same three commands and then `tests/coverage-gate.py`, which
fails the build below 90 % of production lines. The merged report is uploaded as an artifact.

## Package

```bash
dotnet pack src/Text.SharpTypst -c Release -o out -p:BuildRevision=42
dotnet pack src/Text.SharpTypst.DependencyInjection -c Release -o out -p:BuildRevision=42
dotnet pack src/Text.SharpTypst.Templates.Generator -c Release -o out -p:BuildRevision=42
```

Three packages:

| Package | Contents |
| --- | --- |
| `M3scalinum.Text.SharpTypst` | The managed library, the native library for every RID, `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`. |
| `M3scalinum.Text.SharpTypst.DependencyInjection` | `AddSharpTypst()`, options, validation. Depends on `M3scalinum.Text.SharpTypst` at the exact version. |
| `M3scalinum.Text.SharpTypst.Templates.Generator` | A Roslyn analyzer. No `lib/`, only `analyzers/` and `build/`. |

`M3scalinum.Text.SharpTypst.Native` is **not** published: its types are internal and it is merged into `M3scalinum.Text.SharpTypst`
through `TargetsForTfmSpecificBuildOutput`, so a consumer installs one package.

Before publishing, verify a package:

```bash
python3 tests/verify-package.py out/M3scalinum.Text.SharpTypst.0.15.1.42.nupkg
```

It fails when a RID has no native library, when the package depends on the unpublished
`M3scalinum.Text.SharpTypst.Native`, or when a licence file is missing. The `pack` job of `ci-sharptypst.yml` runs it on every
package it builds.

## Versioning

`0.15.1.<revision>`: the major and minor track the embedded Typst compiler, the build revision
distinguishes two builds of the same compiler. The release workflow tags the full version
(`sharptypst-v0.15.1.4`) and takes the revision from the tag's fourth component.

## Current state of the package

The native libraries are produced by the `native` job of `ci-sharptypst.yml` and
`release-sharptypst.yml`; a locally packed
`.nupkg` contains **no** `runtimes/` entry and is not installable by a third party. Until a release
has been published, build from source and set `SHARPTYPST_NATIVE_PATH`.
