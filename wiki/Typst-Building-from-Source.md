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
dotnet test tests/Text.Typst.Tests -c Release --no-build
dotnet test tests/Text.Templates.Generator.Tests -c Release --no-build
dotnet test tests/Text.Typst.Integration.Tests -c Release --no-build
```

| Suite | What it covers |
| --- | --- |
| `M3scalinum.Text.Typst.Tests` | The managed layer against a fake native. Fast, no Rust needed. |
| `M3scalinum.Text.Templates.Generator.Tests` | The Roslyn generator. |
| `M3scalinum.Text.Typst.Integration.Tests` | The real compiler: formats, templates, conformance, limits, leaks, DI. |

The leak tests take about 50 s.

## Coverage

```bash
dotnet tool install -g dotnet-coverage

dotnet-coverage collect -f cobertura -o unit.xml \
  "dotnet test tests/Text.Typst.Tests -c Release --no-build"

dotnet-coverage collect -f cobertura -o int.xml \
  "dotnet test tests/Text.Typst.Integration.Tests -c Release --no-build --filter FullyQualifiedName!~LeakTests"

dotnet-coverage merge -f cobertura -o all.xml unit.xml int.xml
```

The project uses Microsoft.Testing.Platform, so `--collect:"XPlat Code Coverage"` does not work;
`dotnet-coverage` does.

The `coverage` job of `ci-sharptypst.yml` runs the same three commands and then `tests/coverage-gate.py`, which
fails the build below 90 % of production lines. The merged report is uploaded as an artifact.

## Package

```bash
python tools/pack-typst-packages.py --output out --revision 42
```

That is the whole package set: the managed compiler, its `M3scalinum.Text.Documents` dependency and
the seven per-RID satellites. One `dotnet pack` per satellite, from one project parameterised by
`-p:NativeRid=<rid>` — the same file names, the same `runtimes/<rid>/native/` layout and the same
`Exists(...)` conditions the single package used to carry.

| Package | Contents |
| --- | --- |
| `M3scalinum.Text.Typst` | The managed library only, plus `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`. ~0.5 MB. |
| `M3scalinum.Text.Typst.Native.WinX64` (and six siblings) | One native library under `runtimes/<rid>/native/`, the licence files, and a dependency on `M3scalinum.Text.Typst` at the exact version. |
| `M3scalinum.Text.Typst.DependencyInjection` | `AddTypst()`, options, validation. Depends on `M3scalinum.Text.Typst` at the exact version. |
| `M3scalinum.Text.Templates.Generator` | A Roslyn analyzer. No `lib/`, only `analyzers/` and `build/`. |

The satellites are `WinX64`, `WinArm64`, `LinuxX64`, `LinuxArm64`, `LinuxMuslX64`, `OsxArm64` and
`OsxX64`; `--list` prints the set. The split exists because NuGet fetches a package whole and picks
assets by RID only afterwards: one package carrying all seven libraries costs every consumer 168 MB
to use 23. macOS is two thin dylibs, `runtimes/osx-arm64/native/` and `runtimes/osx-x64/native/`,
each built on its own runner; there is no universal `osx` file and no `lipo` step.

`M3scalinum.Text.Typst.Native` — the assembly, not the satellite packages — is **not** published:
its types are internal and it is merged into `M3scalinum.Text.Typst` through
`TargetsForTfmSpecificBuildOutput`.

Before publishing, verify the set:

```bash
python3 tests/verify-package.py out/*.nupkg
```

It fails when the managed package carries a native library again, when a satellite has the wrong RID,
no library or an assembly, when a satellite pins another core version, when the set is missing one of
the seven, or when the package depends on the unpublished `M3scalinum.Text.Typst.Native` or lacks a
licence file. A single path still checks that one package, which is what the `pack` job of
`ci-sharptypst.yml` does.

## Versioning

`0.15.1.<revision>`: the major and minor track the embedded Typst compiler, the build revision
distinguishes two builds of the same compiler. The release workflow tags the full version
(`sharptypst-v0.15.1.4`) and takes the revision from the tag's fourth component.

## Current state of the package

The native libraries are produced by the `native` job of `ci-sharptypst.yml` and
`release-sharptypst.yml`. A satellite packed on a workstation without them contains no
`runtimes/` entry — the `Exists(...)` condition makes that a package, not an error — and
`tests/verify-package.py` is what refuses it. Until a release
has been published, build from source and set `SHARPTYPST_NATIVE_PATH`.
