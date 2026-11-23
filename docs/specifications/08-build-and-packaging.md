# 08 — Build and packaging

## 1. Repository layout

```
M3scalinum.Text.Typst/
├── .editorconfig
├── .gitattributes
├── .gitignore
├── global.json
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── M3scalinum.Text.Typst.slnx
├── README.md
├── CHANGELOG.md              (the project as a whole)
├── CHANGELOG-Typst.md   (the compiler packages)
├── CHANGELOG-Text.md         (the suite packages)
├── THIRD-PARTY-NOTICES.md
├── docs/
│   └── specifications/            (this document set)
├── native/
│   └── sharp-typst/
│       ├── Cargo.toml
│       ├── Cargo.lock
│       ├── build.rs                (optional: version stamping)
│       └── src/
│           ├── lib.rs              (exports, panic guard)
│           ├── ffi/                (types, buffers, errors, handles)
│           ├── world.rs            (typst::World implementation)
│           ├── compile.rs
│           └── export/{pdf,svg,png,html,markdown,text}.rs
├── src/
│   ├── M3scalinum.Text.Typst/                  (public managed API)
│   ├── M3scalinum.Text.Documents/              (the document model, no compiler)
│   ├── M3scalinum.Text.Typst.DependencyInjection/  (AddTypst, options, validation)
│   ├── M3scalinum.Text.Typst.Native/           (P/Invoke, internal)
│   ├── M3scalinum.Text.Typst.Native.Package/   (the per-RID satellites, one project, §6.3)
│   └── M3scalinum.Text.Templates.Generator/  (Roslyn generator, netstandard2.0)
├── tests/
│   ├── M3scalinum.Text.Typst.Tests/
│   ├── M3scalinum.Text.Typst.Integration.Tests/
│   └── fixtures/                   (typ, fonts, images, golden files)
├── samples/
│   └── M3scalinum.Text.Typst.Samples/
└── templates/
    └── invoice/                    (reference template)
```

`M3scalinum.Text.Typst.slnx` is the solution format (XML, no GUIDs). `dotnet build` at the root builds the
managed projects only; the Rust crate is built by an explicit target (§5).

## 2. `global.json`

```json
{
  "sdk": {
    "version": "11.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

Pinning the SDK feature band keeps analyzers and the source generator deterministic across
machines. `rollForward: latestFeature` allows patch and feature-band updates without editing the
file. The `test.runner` entry is required: from .NET 10 the SDK defaults `dotnet test` to VSTest,
which cannot run a Microsoft.Testing.Platform project.

## 3. `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <!-- Tests, samples and benchmarks. The shipped libraries set TargetFrameworks instead. -->
    <TargetFramework>net11.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>

    <!-- Quality gates -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <AnalysisMode>Recommended</AnalysisMode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>

    <!-- Reproducibility -->
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>

    <!-- Packaging defaults; individual projects opt in with IsPackable -->
    <IsPackable>false</IsPackable>
    <Authors>m3scalinum</Authors>
    <Company>m3scalinum</Company>
    <Product>M3scalinum.Text.Typst</Product>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryType>git</RepositoryType>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <TypstVersion>0.15.1</TypstVersion>
    <BuildRevision Condition="'$(BuildRevision)' == ''">0</BuildRevision>
    <Version>$(TypstVersion).$(BuildRevision)</Version>
    <AssemblyVersion>$(TypstVersion).0</AssemblyVersion>
  </PropertyGroup>

  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <PropertyGroup Condition="'$(IsTestProject)' == 'true'">
    <NoWarn>$(NoWarn);CA1707;CA1861</NoWarn>
    <OutputType>Exe</OutputType>
    <IsTestingPlatformApplication>true</IsTestingPlatformApplication>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>
</Project>
```

Notes:

- `InvariantGlobalization` is on: nothing the library formats comes from the process culture. Dates,
  numbers and currencies are formatted from a per-language pattern table (`TypstFormatting`), so a
  document cannot change because the host's locale did.
- `PathMap` strips absolute paths from PDBs so builds are reproducible across machines.
- `CS1591` (missing XML comment) is suppressed only because `internal` members are documented in
  these specifications instead; public members still carry XML docs by review.
- `CA1707` (underscores in member names) and `CA1861` (constant array arguments) are suppressed for
  test projects only: test names are sentences, and `Assert.Equal(new[] { … }, …)` is the readable
  form.
- xunit.v3 is a Microsoft.Testing.Platform runner, not a VSTest library. It needs `OutputType=Exe`,
  `IsTestingPlatformApplication=true`, and no `Microsoft.NET.Test.Sdk`. The SDK also needs the
  runner selected in `global.json` (§2), otherwise `dotnet test` falls back to VSTest and fails with
  "Could not find testhost".

## 4. `Directory.Packages.props`

Central package management: versions live here, projects reference without `Version`.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup Label="Build">
    <PackageVersion Include="Microsoft.SourceLink.GitHub" Version="11.0.100" />
  </ItemGroup>

  <ItemGroup Label="Testing">
    <PackageVersion Include="xunit.v3" Version="4.0.3" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.0" />
  </ItemGroup>
</Project>
```

Versions are pinned at scaffold time against nuget.org; this table is the single place to bump
them. No project file may contain a `Version=` attribute on a `PackageReference`.

`Microsoft.SourceLink.GitHub` 8.x pulls `Microsoft.Build.Tasks.Git` 8.0.0, which carries a
moderate-severity advisory; with `TreatWarningsAsErrors` the restore fails with `NU1902`. Pin the
current major instead of suppressing the warning.

## 5. Building the native library

`dotnet build` does not invoke Cargo. The native build is an explicit target in
`Directory.Build.targets`:

```xml
<Project>
  <PropertyGroup>
    <BuildNative Condition="'$(BuildNative)' == ''">false</BuildNative>
    <NativeProfile Condition="'$(NativeProfile)' == ''">release</NativeProfile>
    <NativeTargetDir>$(MSBuildThisFileDirectory)native/sharp-typst/target/$(NativeProfile)</NativeTargetDir>
  </PropertyGroup>

  <Target Name="BuildNativeLibrary"
          Condition="'$(BuildNative)' == 'true'"
          BeforeTargets="BeforeBuild">
    <Exec Command="cargo build --profile $(NativeProfile) --manifest-path &quot;$(MSBuildThisFileDirectory)native/sharp-typst/Cargo.toml&quot;" />
  </Target>
</Project>
```

Rationale: a `dotnet build` that silently shells out to Cargo makes the managed build depend on a
Rust toolchain, breaks IDE incremental builds and surprises contributors. The flag is explicit and
CI always sets it.

Local workflow:

```bash
cargo build --release --manifest-path native/sharp-typst/Cargo.toml
dotnet build -p:BuildNative=true
```

## 6. Native asset packaging

`M3scalinum.Text.Typst` carries no native library. The compiler ships as one package per runtime
identifier, and a consumer installs two packages — the compiler, then the satellite for the machine
they run on:

```bash
dotnet add package M3scalinum.Text.Typst
dotnet add package M3scalinum.Text.Typst.Native.WinX64
```

| RID | Satellite package | Library inside it |
| --- | --- | --- |
| `win-x64` | `M3scalinum.Text.Typst.Native.WinX64` | `runtimes/win-x64/native/sharp_typst.dll` |
| `win-arm64` | `M3scalinum.Text.Typst.Native.WinArm64` | `runtimes/win-arm64/native/sharp_typst.dll` |
| `linux-x64` | `M3scalinum.Text.Typst.Native.LinuxX64` | `runtimes/linux-x64/native/libsharp_typst.so` |
| `linux-arm64` | `M3scalinum.Text.Typst.Native.LinuxArm64` | `runtimes/linux-arm64/native/libsharp_typst.so` |
| `linux-musl-x64` | `M3scalinum.Text.Typst.Native.LinuxMuslX64` | `runtimes/linux-musl-x64/native/libsharp_typst.so` |
| `osx-arm64` | `M3scalinum.Text.Typst.Native.OsxArm64` | `runtimes/osx-arm64/native/libsharp_typst.dylib` |
| `osx-x64` | `M3scalinum.Text.Typst.Native.OsxX64` | `runtimes/osx-x64/native/libsharp_typst.dylib` |

Each satellite is a package of one binary: `lib/<tfm>/_._` for the four target frameworks the
compiler supports and nothing else, so it cannot shadow an assembly; the licence files; and a
dependency on `M3scalinum.Text.Typst` at the exact version, so installing the satellite alone is a
complete install and the two versions can never disagree.

There is no `linux-musl-arm64` satellite. `Cargo.toml` vendors OpenSSL for `x86_64-unknown-linux-musl`
alone, so `typst-kit`'s downloader has no vendored TLS for the aarch64 musl triple and the target is
not built at all (09 §11). An Alpine arm64 consumer has no package to install, and the load error
says so by naming the RIDs that exist.

### 6.1 Why one package became eight

NuGet 3 has no partial download: a package is one immutable zip behind one URL, a restore fetches the
whole file, and the RID decides only which entries are copied out of it afterwards. Measured on
`M3scalinum.Text.Typst.0.15.1.217.nupkg`, the single package that carried every native:

| RID | downloads | its build copies out | paid for, unusable |
| --- | ---: | ---: | ---: |
| `win-x64` | 168.32 MB | 22.80 MB | 145.52 MB (86.5 %) |
| `linux-musl-x64` | 168.32 MB | 33.69 MB | 134.63 MB (80.0 %) |
| `osx-arm64` | 168.32 MB | 21.64 MB | 146.68 MB (87.2 %) |

Six of the seven libraries a `win-x64` consumer downloads cannot be loaded on their machine, and no
restore can avoid them. Nothing about the RID is known to the client when dependencies are resolved —
dependency groups in a `.nuspec` are keyed by target framework and never by RID, and a
`PackageReference` is unconditional — so the package graph cannot express "fetch the Alpine build if
this is Alpine", and a package that carries all seven has to be as large as all seven.

The split is a change to the install story, not to the packaging alone, and it ships with the
documentation of it: `README.md`, `wiki/Typst-Getting-Started.md` and `samples/examples/run.sh` all
show the two lines, and the examples restore the satellite for the RID they run on, so the recipe a
consumer reads is the recipe CI exercises.

**Rejected — a meta-package per platform family** (`M3scalinum.Text.Typst.Native.Windows` depending on
both Windows satellites). It halves the number of ids to remember and doubles the download for the
half of the users who picked the wrong architecture: an x64 host would fetch the arm64 library, which
is what the split exists to stop. It also adds an id to every release that carries no binary of its own.

**Rejected — publishing the satellites *alongside* a package that still carries every native.** The
download does not move, and eight versions have to stay in lockstep.

### 6.2 What a consumer downloads, measured

`tools/package-size.py` reads a packed `.nupkg` and reports every entry's compressed size, so the
figures below are the files a feed serves and not an estimate. `--split` models the layout: a consumer
fetches the packages that carry their RID or no native at all, and never the satellites for the other
six RIDs.

```bash
# The whole set, modelled per RID.
python tools/pack-typst-packages.py --output out
python tools/package-size.py out/*.nupkg --split

# One profile: what a win-x64 project restores.
python tools/package-size.py out/M3scalinum.Text.Typst.0.15.1.nupkg \
    out/M3scalinum.Text.Documents.0.15.1.nupkg \
    out/M3scalinum.Text.Typst.Native.WinX64.0.15.1.nupkg
```

| Package | Compressed |
| --- | ---: |
| `M3scalinum.Text.Typst` (managed only: 4 TFMs, nuspec, licences, readme) | 0.49 MB |
| `M3scalinum.Text.Documents` (the compiler's only dependency) | 0.60 MB |
| `M3scalinum.Text.Typst.Native.WinX64` | 22.38 MB |
| `M3scalinum.Text.Typst.Native.WinArm64` | 21.31 MB |
| `M3scalinum.Text.Typst.Native.LinuxX64` | 23.46 MB |
| `M3scalinum.Text.Typst.Native.LinuxArm64` | 23.00 MB |
| `M3scalinum.Text.Typst.Native.LinuxMuslX64` | 33.28 MB |
| `M3scalinum.Text.Typst.Native.OsxArm64` | 20.87 MB |
| `M3scalinum.Text.Typst.Native.OsxX64` | 23.33 MB |
| **the whole set, nine packages** | **168.72 MB** |

The set weighs what the single package weighed, because nothing was compressed differently — the
natives are the same files, separated. What changes is the number under one consumer:

| RID | before: downloads | after: downloads | saved |
| --- | ---: | ---: | ---: |
| `win-x64` | 168.54 MB | **23.47 MB** | 145.07 MB (86.1 %) |
| `win-arm64` | 168.54 MB | 22.40 MB | 146.14 MB (86.7 %) |
| `linux-x64` | 168.54 MB | 24.55 MB | 143.99 MB (85.4 %) |
| `linux-arm64` | 168.54 MB | 24.09 MB | 144.45 MB (85.7 %) |
| `linux-musl-x64` | 168.54 MB | **34.36 MB** | 134.18 MB (79.6 %) |
| `osx-arm64` | 168.54 MB | **21.96 MB** | 146.58 MB (87.0 %) |
| `osx-x64` | 168.54 MB | 24.42 MB | 144.12 MB (85.5 %) |

Both columns include `M3scalinum.Text.Documents` (0.60 MB). The targets were 22.80 MB (`win-x64`) and
33.69 MB (`linux-musl-x64`) for the compiler package alone — native plus 0.44 MB of shared cost. The
two-package install adds `Text.Documents`, which the single package pulled in as a dependency all
along: 22.35 (the library) + 0.49 (the managed package) + 0.60 (`Text.Documents`) + 0.03 (a
satellite's own licence files) = 23.47 MB.

A consumer who installs the compiler and stops there downloads 1.09 MB and gets the load error of
§6.4 at the first compile — not a document.

**How these figures were obtained.** No CI artifact was reachable from the workstation that packed
them, so the seven `sharp_typst` binaries in these packages are placeholders: incompressible random
bytes of the compressed size the real entry has on the feed — 22.35, 21.28, 23.43, 22.97 and
33.24 MB, with the two macOS thin slices sharing that universal file's 44.59 MB. Random bytes do not
deflate, so each packed entry weighs what the placeholder weighs and the download column is the real
one to within a few kilobytes: the same exercise on the single package reproduces 167.94 MB against
the artifact's 168.32 MB, and a `win-x64` copy of 22.84 MB against 22.80 MB. The raw sizes a build
copies out of those files are the artifact's own — 49.61 MB for `win-x64`, 73.79 MB for the musl
library — and do not come from a placeholder. The first release that packs this layout re-measures
with the artifacts and replaces the table.

### 6.3 One project, seven packages

`src/Text.Typst.Native.Package` is a single csproj parameterised by `NativeRid`. It selects the
library, the `runtimes/<rid>/native/` path and the package id suffix, and `dotnet pack` runs once per
RID:

```xml
<PropertyGroup>
  <NativeRid Condition="'$(NativeRid)' == ''">$(RuntimeIdentifier)</NativeRid>
  <NativeLibrary Condition="'$(NativeRid)' == 'win-x64'">sharp_typst.dll</NativeLibrary>
  <NativePackageSuffix Condition="'$(NativeRid)' == 'win-x64'">WinX64</NativePackageSuffix>
  <PackageId Condition="'$(NativeLibrary)' != ''">M3scalinum.Text.Typst.Native.$(NativePackageSuffix)</PackageId>
  <IncludeBuildOutput>false</IncludeBuildOutput>
</PropertyGroup>

<ItemGroup>
  <None Include="$(NativeArtifactsDir)/$(NativeRid)/$(NativeLibrary)"
        Pack="true" PackagePath="runtimes/$(NativeRid)/native/" Visible="false"
        Condition="Exists('$(NativeArtifactsDir)/$(NativeRid)/$(NativeLibrary)')" />
</ItemGroup>
```

Seven copied csproj files would restate the same seven RIDs, the same `Exists(...)` conditions, the
same licence items and the same `_._` placeholders, and would drift on the first file added or
renamed. The set is written once and read three times: the csproj's suffix table,
`tools/pack-typst-packages.py` (the RID list, and a check that every expected package id was packed)
and `tests/verify-package.py` (the same ids, by the same rule).

The artifact side is unchanged: the same file names, the same `runtimes/<rid>/native/` layout and the
same `Exists(...)` condition under which the single package packed each one. A pack on a machine that
built no natives still produces a satellite, and `tests/verify-package.py` refuses it instead of a
consumer. `dotnet pack` without `NativeRid` packs nothing: a build of the solution must not fail, and
there is no package to name.

Packing is one command:

```bash
python tools/pack-typst-packages.py --output out --revision 42
```

It packs `Text.Documents`, the managed compiler and the seven satellites, and fails when the nine
files it expected are not the nine it wrote.

### 6.4 The resolution rule, and the error when no satellite is installed

NuGet resolves dependencies per target framework. There is no RID-conditional dependency, no
`runtimes/` entry that pulls a package in, and no client that downloads part of a package; the RID
becomes relevant only when a build or a publish lays assets into an output directory. So the
resolution rule is about the project file, and it has one line per package:

- the project references `M3scalinum.Text.Typst` and one satellite for the machine it runs or
  publishes for;
- a project that publishes for several RIDs references several satellites — that is the only way to
  get both `runtimes/linux-x64/` and `runtimes/win-x64/` into separate publish outputs, and each one
  costs its own download;
- the satellite already depends on the compiler, so
  `dotnet add package M3scalinum.Text.Typst.Native.WinX64` alone installs both. Adding the compiler
  first is what keeps the project file readable and what `dotnet list package` shows first.

**With no satellite, the load fails with a message that names the package.** `NativeLibraryResolver`
probes `runtimes/<rid>/native/`, then `runtimes/<portable-rid>/native/`, then `runtimes/<os>/native/`,
then the application directory, then the loader's own search path (`LD_LIBRARY_PATH`, a system
install), and finally throws `TypstNativeLibraryNotFoundException`:

```
The native Typst library is not installed for this process (RID win-x64).

M3scalinum.Text.Typst carries the managed compiler only: the native library lives in a package of its own,
and this project does not reference the one for this runtime.

    dotnet add package M3scalinum.Text.Typst.Native.WinX64

Probed:
  C:\app\runtimes\win-x64\native\sharp_typst.dll
  C:\app\runtimes\win\native\sharp_typst.dll
  C:\app\sharp_typst.dll

Set SHARPTYPST_NATIVE_PATH to the full path of a library you built yourself (cargo build --release in
native/sharp-typst) to load that instead.
```

The type derives from `DllNotFoundException`, so a host that already catches that keeps working; what
changed is that the message also names the RID, the package that installs it and the paths searched. A
bare `DllNotFoundException` would leave the reader to work out the packaging from a file name.
`SHARPTYPST_NATIVE_PATH` still wins over the probing order, and when it points at a file that does not
exist the message says so.

**Measured — the two installs.** A console project with `M3scalinum.Text.Typst 0.15.1` and nothing else
restores 1.09 MB, compiles, and throws `TypstNativeLibraryNotFoundException` with the text above at its
first call. The same project with `M3scalinum.Text.Typst.Native.WinX64` added restores 23.47 MB and
loads the library from `runtimes/win-x64/native/` in its own output directory.

### 6.5 The RID set of the packages that are not the compiler

The five format packages carry a Rust library of their own, and each still ships **eight** RIDs in one
package: the compiler's seven plus `linux-musl-arm64`, which the compiler does not have.

| Package | RIDs | Count |
| --- | --- | ---: |
| `M3scalinum.Text.Typst` (the satellites) | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-arm64`, `osx-x64` | 7 |
| `M3scalinum.Text.Barcodes`, `.Ocr`, `.Docx`, `.Xlsx`, `.Pdf` | the seven above, plus `linux-musl-arm64` | 8 |

Those five are not split: each one's library is a few megabytes and its consumers use the one binary
their RID needs, so the download a `win-x64` consumer pays for is a fraction of the compiler's. A split
there would repeat the two-line install for packages that are usually pulled in transitively.

**The five native packages ship `linux-musl-arm64`.** `linux-musl-x64` alone left the other
architecture open: NuGet resolves `linux-musl-arm64` to `linux-arm64` without a word, so an Alpine
arm64 image — a Graviton node, or an Apple Silicon host running Docker — loaded a glibc library and
failed with the very `DllNotFoundException` the x64 RID was added to remove.
`M3scalinum.Text.Barcodes`, `.Ocr`, `.Docx`, `.Xlsx` and `.Pdf` carry
`runtimes/linux-musl-arm64/native/libsharp_text_*.so` as a seventh RID, built by the same
`native (${{ matrix.crate }}, ${{ matrix.rid }})` matrix entry and demanded by
`tests/verify-package.py` like the other six.

**The linker, and why the arm64 pool builds it.** The entry runs on `ubuntu-24.04-arm` with
`musl-tools`, whose `musl-gcc` there *is* an aarch64 musl compiler: the runner and the target share an
architecture, so `--target aarch64-unknown-linux-musl` needs no cross toolchain and no
`[target.aarch64-unknown-linux-musl]` linker block in any crate. Cross-compiling from the x64 pool
would have meant `aarch64-linux-musl-cross` plus a cargo configuration per crate for a target that
has a native runner; the repository already builds `linux-arm64` there, and this entry follows that
choice instead of inventing a second one. Rust still refuses a `cdylib` for a musl target, so the
link is the x64 recipe unchanged: the staticlib, `-Wl,-u` for every exported `bt_`/`to_`/`td_`/
`tx_`/`tp_` symbol, `-lpthread -ldl -lm`, then `strip --strip-all`. The entry also runs the crate's
`cargo test --lib` for that target, on a statically linked binary that executes on the glibc runner
because it carries its own libc.

**What it costs.** Forty native builds per pull request instead of thirty-five — five crates times
eight RIDs — each cached on its own crate-and-RID key, so a managed-only change still restores
libraries instead of compiling them, and five new cache entries to fill the first time. One more
job on the arm64 pool, `musl (Alpine arm64)`, publishes the five examples self-contained for
`linux-musl-arm64` and runs them in `mcr.microsoft.com/dotnet/runtime-deps:11.0-alpine`. Every
consumer downloads the extra arm64 slice whether they need it or not — the same trade the x64 musl
entry made, and the `.nupkg` sizes the release job publishes are where the delta is measured.

**Where the seventh RID is declared.** In the five csproj files, in the same form as the seventh:

```xml
<None Include="$(NativeArtifactsDir)\linux-musl-arm64\libsharp_text_barcodes.so"
      Pack="true" PackagePath="runtimes/linux-musl-arm64/native/" Visible="false"
      Condition="Exists('$(NativeArtifactsDir)\linux-musl-arm64\libsharp_text_barcodes.so')" />
```

When the RID was first added it was not: that change was outside `src/`, so
`.github/native/linux-musl-arm64.targets` injected the missing `<None>` item at pack time through the
two hooks `Microsoft.Common.targets` exposes — `CustomAfterMicrosoftCommonTargets` for the inner
single-target build and `CustomAfterMicrosoftCommonCrossTargetingTargets` for the outer
multi-targeting one — which `ci-text.yml` and `release-text.yml` exported as environment variables. A
`dotnet pack` run by hand there therefore produced a six-RID package and said nothing about it, while
the same command in CI produced seven. The five csproj files carry the line themselves, the two exports
are gone and the file is deleted. `tests/verify-package.py --selftest` — run by the `extras` job —
fails if either requirement is deleted from the verifier's own tables.

**What `linux-musl-arm64` does not cover.** The compiler has no such satellite, for the OpenSSL reason
above: an Alpine arm64 consumer has nothing to install, and the load error names the seven RIDs that
exist instead of a glibc library that would fail one call later.

**The musl library is linked by hand.** Rust cannot emit a `cdylib` for a musl target — it drops the
crate type with a warning and produces only the `staticlib` — so the shared object is linked from the
archive, which is why the `LinuxMuslX64` satellite is the largest of the seven:

```bash
cargo build --release --target x86_64-unknown-linux-musl
symbols=$(nm -g --defined-only target/x86_64-unknown-linux-musl/release/libsharp_typst.a \
  | awk '$2=="T" && $3 ~ /^tn_/ {print "-Wl,-u,"$3}')
musl-gcc -shared -o libsharp_typst.so $symbols libsharp_typst.a -lpthread -ldl -lm
strip --strip-all libsharp_typst.so
```

The `-u` flags are what keep the `tn_*` entry points: nothing in the archive references them, so a
plain link would produce an empty library. The result is 73.79 MB of library, 33.24 MB on the wire.
Its asset sits under `runtimes/linux-musl-x64/native/`, which `NativeLibraryResolver` probes first on a
musl host (Spec 02 §9): NuGet's RID graph resolves `linux-musl-x64` to the `linux-x64` asset, so the
glibc build would be found first and fail to load.

### 6.6 macOS: two thin dylibs, not one universal one

`runtimes/osx/native/libsharp_typst.dylib` is replaced by
`runtimes/osx-arm64/native/libsharp_typst.dylib` and `runtimes/osx-x64/native/libsharp_typst.dylib`, and
the same for the five `libsharp_text_*` files. The resolver probes `runtimes/<rid>/` before
`runtimes/osx/` (Spec 02 §9), so an arm64 Mac finds its own file first and the `osx` fallback is
simply never reached; it stays in the probing order for an application that lays a universal dylib
beside its executable. `lipo` is no longer in any workflow, and each architecture is built and tested
on its own runner: `macos-15` for `osx-arm64`, `macos-15-intel` for `osx-x64`.

| | before: one universal dylib | after: two thin dylibs |
| --- | ---: | ---: |
| macOS payload in the `.nupkg`, deflated | 44.59 MB | 44.59 MB |
| raw bytes an `osx-arm64` consumer's build copies out | 45.03 MB | 21.2 MB |
| raw bytes an `osx-x64` consumer's build copies out | 45.03 MB | 23.8 MB |
| raw bytes copied that the machine cannot load | 23.8 MB | none |

Two deflate streams weigh 0.4 KB more than one, and the download is now a satellite either way. What
changes is the file that lands in an ARM Mac's build output — the x86_64 half is no longer copied,
signed or archived — and the same in reverse on Intel.

**How those figures were obtained.** The universal column is the `osx` entry of
`M3scalinum.Text.Typst.0.15.1.217.nupkg`, 44.59 MB deflated and 45.03 MB raw. The split columns are
that file's two Mach-O slices, because `lipo` concatenates thin images and nothing else: an arm64
consumer copies the arm64 share of 45.03 MB and no longer the rest. The share was measured rather than
assumed — on a real universal dylib (`libSkiaSharp.dylib` 3.116.1, 15,217,856 bytes) the fat header
splits into 7,156,928 bytes of arm64 (47.0 %) and 8,030,464 bytes of x86_64 (52.8 %) — and the
package-level cost was checked by zipping the same universal file both ways: one entry of 7,136,315
compressed bytes against two of 3,355,012 and 3,781,544, 397 bytes apart. The exact figures for
`libsharp_typst` and the five `libsharp_text_*` come out of `tools/package-size.py` on the first
release that packs the split; the ratio decides only how the 45.03 MB is shared, not whether half of
it stops being copied.

### 6.7 Alternatives measured and rejected

**Compressing the library and unpacking it on arrival.** `xz -6` on the musl library gives 23.53 MB
against the 33.24 MB the `.nupkg` zip already achieves: 9.71 MB, 5.8 % of what the compiler package
weighed, for a `runtimes/` subtree that no longer holds the library itself. The resolver would then
have to unpack 73.79 MB into a writable cache before the first compile — and a chiseled or hardened
Alpine container, which is the reason the musl build exists, is the one place that cache may not
exist. Applied to all six natives the same ratio lands at 113.84 MB of package — 32.4 % less than
168.32 MB, so the option does reach a quarter; it is the cache, not the ratio, that disqualifies it.
The split reaches 86.1 % for `win-x64` without unpacking anything.

**Publishing `M3scalinum.Text.Typst.Native` as a package.** Every type in that assembly is `internal`,
so it is an implementation detail — with one exception, the exception type below. The project
reference carries `PrivateAssets="all"` and the assembly is added to the managed package through
`TargetsForTfmSpecificBuildOutput`, so a consumer installs the compiler and one satellite, and nothing
else. Publishing it would expose nothing and add a version to keep in lockstep.
`tests/verify-package.py` fails a package that depends on it.

`TypstNativeLibraryNotFoundException` is the one public type in that assembly, and it is public because
a host catches it: the resolution rule of §6.4 has a failure mode of its own, and a type a caller cannot
name is not a contract.

### 6.8 What only a published tag proves

Everything above is measured from `.nupkg` files, and everything below needs `nuget.org` to answer:

- that a restore of `M3scalinum.Text.Typst` plus one satellite resolves for `net8.0`, `net9.0`,
  `net10.0` and `net11.0` on a clean cache, and that the satellite's `lib/<tfm>/_._` is enough for
  every one of them;
- that all eight packages are in the feed together — the release job pushes them in one run with
  `--skip-duplicate`, which publishes them all but not atomically;
- that `dotnet publish -r linux-musl-x64 --self-contained` lays the musl library into the output of a
  container whose base does not carry it;
- the provenance side: the SBOM and the attestation of each satellite, which the release job generates
  and attests per package, and which no local pack can vouch for.

The `pack` job packs the set and verifies it as a set — a missing satellite is a `NU1101` in a
consumer's restore and nothing in this repository — so the first tag is what turns those four
statements into facts.

## 7. Versioning and releases

Two families, two versions.

**The packages that embed the compiler** — `M3scalinum.Text.Typst` and its two satellites — and the
document model it compiles, `M3scalinum.Text.Documents`, version themselves **after Typst**: the
package version *is* the Typst version, plus a build revision. The model belongs to this train because
its public surface is the Typst document vocabulary and moves with the compiler.

```
0.15.1.42
└──┬──┘ └┬┘
   │     └── BuildRevision: the tag's fourth component on a release, the CI run number otherwise
   └──────── TypstVersion: the compiler this package embeds
```

| Property | Value | Why |
| --- | --- | --- |
| `TypstVersion` | `0.15.1` | Single source of truth, in `Directory.Build.props` |
| `BuildRevision` | The tag's fourth component on a release, the CI run number otherwise, `0` locally | Distinguishes two builds of the same Typst |
| `Version` | `0.15.1.42` | The package version |
| `AssemblyVersion` | `0.15.1.0` | **Stable across builds**: a rebuild must not change the assembly identity, or every consumer needs a binding redirect for a patch-level change |
| `FileVersion` | `0.15.1.42` | What the file system and the diagnostics report |
| `InformationalVersion` | `0.15.1.42 (Typst 0.15.1)+<commit>` | What `TypstEnvironment.LibraryVersion` returns; the SDK appends the source commit |

A consumer can therefore tell which compiler a package carries from its version alone, and a
`0.15.1.*` → `0.15.2.*` bump is unambiguously a Typst upgrade.

**The packages that do not embed it** — `M3scalinum.Text.Pdf` (PAdES signing,
[14](14-pdf-signature.md)), `.Templates` (the layout pack, [05](05-templates.md)), `.Mcp` (the
protocol server, [17](17-mcp.md)), and the three that arrived with their own engine: `.Barcodes`
([20](20-barcodes.md)), `.Ocr` ([21](21-ocr.md)) and `.Docx` ([22](22-docx.md)) — carry whatever
version their tag names. `v0.1.0` publishes `0.1.0`, `v1.0.0` publishes `1.0.0`:

```
v0.1.0        the tag
     │
     └── the package version, in full
```

The reasoning is the same as for the compiler family, inverted. There, the number must encode the
embedded Typst, so the fourth component counts wrapper releases under a fixed compiler. Here there is
nothing to encode: a signing fix published as `0.15.1.43` would read as a compiler release and would
jump to `0.16.0.0` the day Typst does, for code that contains no compiler at all. Fixing the number
to a property would only mean editing two files to ship the same version, so the release passes the
tag as `-p:SuiteVersion=<tag>` and the projects take it verbatim.

`TextVersion`, in `Directory.Build.props`, survives as the default an **untagged** build uses — a
developer packing locally, or the `pack` job of `ci-text.yml`. Bump it to the last released version
after tagging, so a local build never claims to be older than what shipped. Neither package is
published yet, so starting at `0.1.0` costs nothing.

Two packages share the compiler-derived version: `M3scalinum.Text.Typst` (the library) and
`M3scalinum.Text.Typst.DependencyInjection` (the container integration, §12 of this document
set). The second depends on the first, and NuGet stamps the referenced project's `PackageVersion` as
the dependency version — so the satellite can never name a `M3scalinum.Text.Typst` that was not
built from the same commit.

**Decision — the compiler train packs its satellites in one job.** NuGet derives `>= <version>`
from a project reference, and no `ProjectReference` metadata overrides it; an exact range would need
a `PackageReference` to a package that does not exist yet at restore time. What protects the family
is not the range but the build: the three compiler packages are packed in one job from one revision,
and `tests/verify-package.py` fails the release when a satellite's `M3scalinum.Text.Typst`
dependency is not the version being packed.

**Decision — the suite train packs against a published core.** `M3scalinum.Text.Templates`,
`.Mcp` and `.Docx` call into `M3scalinum.Text.Typst`, but they release on their own schedule. A
project reference would stamp them with whatever version this checkout happens to build, which on a
suite release is a core version nobody published. Their project files therefore switch on
`CoreVersion`: empty, they use the project reference and a change to the wrapper flows in
immediately; set, they use a package reference to that exact published version. `release-text.yml`
resolves the newest stable `M3scalinum.Text.Typst` from NuGet's flat-container index and sets it, so
a suite release always names a core a consumer can actually restore.

A third package, `M3scalinum.Text.Templates.Generator`, is an analyzer: it ships no `lib/` output, only
`analyzers/dotnet/cs/` and a `build/` props file that adds every `template.toml` to
`AdditionalFiles`. It shares the same version and has no runtime dependency on `M3scalinum.Text.Typst`.

`VersioningTests` enforces the link: the assembly version must start with the version the native
library reports, so bumping the Rust crate without bumping `TypstVersion` fails the build.

- Three changelogs, because the project releases twice and does not release both halves at the
  same time. `CHANGELOG.md` records what the project gains as a whole and indexes the two others;
  `CHANGELOG-Typst.md` carries the compiler train, `CHANGELOG-Text.md` the suite train. Each
  release workflow takes its page body from its own train file, so a release page can never quote
  the other train, and a compiler entry names the Typst version it bundles.
- A Typst upgrade is a **minor** version of `M3scalinum.Text.Typst` unless it changes emitted
  output, in which case it is a major (golden files change).

`EnablePackageValidation` is on for the thirteen packable projects. With no
`PackageValidationBaselineVersion` it still does real work: it compares the API surface of
`net8.0`, `net9.0` and `net10.0` against `net11.0`, which is exactly where a conditional
`PackageReference` or an `#if` would quietly drop a type. Once 1.0 is on nuget.org, set the baseline
version and `dotnet pack` starts failing on a removed member, a changed signature or a narrowed
parameter type instead of letting it reach a consumer.

## 8. CI

The suite is split along the same line as the versions. `.github/workflows/ci-sharptypst.yml`
owns everything that needs the compiler; `.github/workflows/ci-text.yml` builds, tests and packs the
nine packages that do not.

`ci-sharptypst.yml`:

| Job | Runner | Steps |
| --- | --- | --- |
| `native` | `windows-latest`, `windows-11-arm`, `ubuntu-latest`, `ubuntu-24.04-arm`, `macos-15-intel` | `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test`, `cargo build --release`, upload artifact. The `linux-musl-x64` entry installs `musl-tools`, builds for `x86_64-unknown-linux-musl` with `-C linker=musl-gcc`, and uploads the same `libsharp_typst.so` name |
| `managed` | `ubuntu-latest` | `dotnet restore`, `dotnet build -c Release`, `dotnet test tests/Text.Typst.Tests` (no natives needed) |
| `integration` | matrix over the five host RIDs | download the matching native artifact, `dotnet restore`, `dotnet build`, `dotnet test tests/Text.Typst.Integration.Tests` against the real compiler |
| `musl` | `ubuntu-latest` | publish the sample for `linux-musl-x64` self-contained and run it inside `mcr.microsoft.com/dotnet/runtime-deps:*-alpine`: the glibc build loads on the runner and fails there, which is the failure that matters |
| `aot` | `ubuntu-latest` | publish `samples/Text.Typst.Samples` with `PublishAot=true` and run it |
| `benchmarks` | `ubuntu-latest` | `--check benchmarks/baseline.json`, 1.5× gate on the minimum |
| `coverage` | `ubuntu-latest` | `dotnet-coverage` over the four suites, merge, `tests/coverage-gate.py` fails below 90 % of production lines |
| `pack` | `ubuntu-latest` | download all seven native artifacts, `dotnet pack -c Release` for the managed compiler and its seven satellites (`tools/pack-typst-packages.py`), `tests/verify-package.py` on the set, upload `.nupkg`/`.snupkg` |

The coverage job stays here rather than moving with the packages it partially measures: it is the
only run where all four suites execute together, and a gate over half the assemblies would have to
drop the threshold to mean anything.

`ci-text.yml`:

| Job | Runner | Steps |
| --- | --- | --- |
| `native` | `ubuntu-latest` | one `cargo build --release` for `linux-x64`, uploaded as `native-linux-x64`. No six-RID matrix: nothing here ships a native asset |
| `text` | `ubuntu-latest` | restore, build, the `Pdf` and `Mcp` unit suites (neither needs a native library), `dotnet pack` for `M3scalinum.Text.Pdf` |
| `examples` | `ubuntu-latest` | download the compiler's `linux-x64` native artifact and the fifteen `native-extras` ones, restore, build, `dotnet pack` the nine packages under test into one local feed, then `samples/examples/run.sh` once per package; the nine `.nupkg` are uploaded for the `sbom` job |
| `sbom` | `ubuntu-latest` | download the `example-packages` artifact, `.github/sbom/sbom.py generate` then `verify` over those nine packages, then empty each SBOM in turn and fail if `verify` accepts it (§12) |

The `examples` job is the one that proves a `.nupkg` is enough. Every other job in both
workflows tests a project reference: the assembly is on disk because the build put it there, the
dependency resolves because another project in the solution provides it, and the native library is
found because a test environment variable names its path. None of those holds for a consumer, so
each package gets one example — a single `Program.cs` under `samples/examples/<Package>/` — and
`run.sh` generates a console project **outside the repository**, with an exact `PackageReference`
and a `NuGet.config` whose only sources are the feed this run packed and nuget.org. The generated
project inherits no `Directory.Build.props` and no central package versions, so the package is the
only path to the library.

The version is read from the packed file name, never written in the source. Each example prints
what it obtained — page count, extracted text, cells read, decodes, layout count — and exits
non-zero when that value is empty, so a package that lost an assembly (`FileNotFoundException`), a
dependency (`NU1101`), a resource (an empty template catalog) or a runtime native asset
(`DllNotFoundException`) turns the job red instead of shipping. Nine packages, nine examples:
`Pdf` reads, signs and verifies; `Templates` renders the invoice layout; `Mcp` runs `initialize`,
`tools/list` and a `tools/call` in process; `Barcodes` encodes and decodes a QR Code and a Code 128;
`Docx` reads a committed `.docx` and converts it to the document model; `Xlsx` reads a workbook
built in the example; `Typst` compiles a document; `Documents` writes Markdown and HTML with no
native library at all; `Ocr` proves the native library loads and answers on the ABI, because the
package ships no model — the caller passes the weights — so a real recognition needs
`M3SCALINUM_OCR_MODELS` and is not what CI runs.

Local developer experience adds one command: `samples/examples/run.sh <example> <package>
<version> <feed>` builds and runs one example from a feed, and `--build-only` stops after the
compile, which is all a Windows host can prove for a package whose native library is a Linux `.so`.

The render validation needs the compiler, which is why `facturx` builds one here: keeping the pair
self-contained costs twelve minutes of Rust in one job instead of coupling the two workflows.

`tests/verify-package.py` is the guard that would have caught two shipped defects: a package with no
`runtimes/` entry (the native job had not run) and a dependency on `M3scalinum.Text.Typst.Native`, which is never
published and made every consumer fail with `NU1101`. It also asserts the four licence files are
inside, which is an Apache-2.0 obligation.

The ARM entries use GitHub's native ARM runners rather than cross-compilation: `typst-kit`'s
downloader links OpenSSL on Linux, so a cross build would need the target's OpenSSL development
headers. Building on the target architecture avoids that entirely.

macOS ships two thin dylibs and builds each on its own runner: `osx-arm64` on `macos-15`,
`osx-x64` on `macos-15-intel`. There is no cross target and no `lipo` — each job is a host build, and
each integration leg downloads the artifact for the architecture it runs on, so both slices are
executed rather than the fat file's x86_64 half alone. The universal file cost every Apple-silicon
consumer an x86_64 copy they could not load; §6.6 measures what removing it recovers.

Two release workflows, one per tag namespace:

| Workflow | Tag | Packs |
| --- | --- | --- |
| `release-sharptypst.yml` | `sharptypst-v0.15.1.42` | the managed compiler, its seven per-RID satellites and the two satellite packages, revision taken from the tag's fourth component |
| `release-text.yml` | `v0.1.0` | `M3scalinum.Text.Pdf`, `.Templates`, `.Mcp`, `.Barcodes`, `.Ocr` and `.Docx`; the tag is the version, and the core is pinned to the newest published `M3scalinum.Text.Typst` |

Each validates its tag against its own version property and refuses a tag that does not start with
it, so `sharptypst-v0.16.0.1` cannot silently publish a suite package, and a bare `v0.16.0.1`
cannot publish a compiler package. `release-sharptypst.yml` runs the compiler's
seven-RID matrix; `release-text.yml` runs the eight-RID one for the five packages that carry their own
library, and packs the rest from those artifacts. A manual
`workflow_dispatch` on either can run the pipeline without publishing, and falls back to the run
number for the revision.

`publish` needs both `pack` and `test`, so a tag cannot ship a build that was never exercised. The
`pack` job runs `tests/verify-package.py` on the whole set before uploading it, and `publish` pushes
the `.snupkg` symbol packages alongside the `.nupkg` ones.

Publishing uses nuget.org trusted publishing rather than a stored API key. The `publish` job holds
`id-token: write`, `NuGet/login@v1` exchanges a GitHub OIDC token for a temporary API key, and the
pushes use that key. The key is valid for one hour and the OIDC token is single-use, so the login
step sits immediately before the pushes. The nuget.org policy is bound to the repository, the
workflow file name and the `nuget` environment; no secret is configured in the repository. That
policy is bound per workflow file, so `release-text.yml` needs its own trusted-publishing entry on
nuget.org before its first release.

`release` needs `publish`, so the GitHub release page is created only once the packages are on
nuget.org, and only for a tag: it titles the page with the version, takes its body from that
build's own changelog section — `CHANGELOG-Typst.md` or `CHANGELOG-Text.md` — and attaches the
same `.nupkg` and `.snupkg` files that were pushed.

**Decision — the release fails rather than inventing notes.** No job writes a changelog: renaming
`## [Unreleased]` to `## [<version>]` is part of preparing a release, done by hand in the commit the
tag points at. The `release` job exits non-zero when that section is absent or empty, naming the
file, so a tag cannot publish a page with no notes.
It is the single job holding `contents: write`; a re-run edits the existing page and replaces its
assets instead of failing on the duplicate.

Rules:

- `cargo clippy` runs with `-D warnings`; `cargo fmt --check` is mandatory.
- The integration job fails if a golden file differs, and uploads the diff as an artifact.
- `pack` runs on every PR (artifact only) and publishes on a `v*` tag.
- No job publishes to nuget.org without a tag and a manual environment approval.

## 9. Local developer experience

| Command | Effect |
| --- | --- |
| `dotnet build` | Managed projects only; fails fast if the native library is missing at test time |
| `dotnet test tests/Text.Typst.Tests` | Unit tests, no native dependency |
| `cargo test --manifest-path native/sharp-typst/Cargo.toml` | ABI and exporter tests |
| `dotnet test -p:BuildNative=true` | Full build including Cargo, then all tests |
| `dotnet run --project samples/Text.Typst.Samples` | Generates the sample outputs into `artifacts/samples/` |
| `samples/examples/run.sh <example> <package> <version> <feed>` | Compiles and runs one example against a packed package; `--build-only` stops after the compile, and an example of `M3scalinum.Text.Typst` restores the satellite for its RID |
| `python3 tools/pack-typst-packages.py --output out` | Packs `Text.Documents`, the managed compiler and the seven satellites (§6.3) |
| `python3 tools/bench-compare.py run --a <rev> --b <rev>` | Alternates the benchmark between two revisions and reports a verdict with a measured noise floor (§13) |

## 10. Third-party notices

M3scalinum.Text.Typst is Apache-2.0, the same licence as Typst. Sharing it is the simplest correct choice: the
library embeds the compiler, so a different licence would mean tracking two sets of obligations for
one binary. Apache-2.0 also carries a patent grant (section 3) and a trademark clause (section 6),
both of which matter for a binding that ships someone else's compiler.

Three files travel with the source and inside the NuGet package:

| File | Contents |
| --- | --- |
| `LICENSE` | The Apache License, Version 2.0, verbatim |
| `NOTICE` | The M3scalinum.Text.Typst copyright, and the statement that the product includes Typst |
| `THIRD-PARTY-NOTICES.md` | The notices Typst itself carries for the components it embeds, reproduced verbatim — required by section 4(d) |

`THIRD-PARTY-NOTICES.md` is not generated: it is Typst's own `NOTICE`, copied at each Typst upgrade.
A generated per-crate list from `Cargo.lock` would be more complete but would not satisfy 4(d), which
asks for the *upstream* notices, not a summary of them.

## 11. Rust version floors

`rust-version` in a crate's `[package]` is a promise to whoever builds this repository: with that
toolchain or newer, the crate compiles. It is not a preference, and it is not an estimate. The floor
of a crate is a property of its resolved dependency graph, computed as the highest `rust-version`
declared by any package the crate actually pulls in — transitive packages included, every target
included — or the floor of the crate's own edition when that is higher. Edition 2024 asks for Rust
1.85, so no crate here can promise less.

The rule follows from what the number means to a consumer:

- **A declared value below the graph's floor is a broken promise.** Cargo accepts the manifest and
  then fails inside a dependency with `requires rustc 1.89`, naming a crate the consumer never
  heard of. This is the failure the check exists for.
- **A declared value above the graph's floor is a refusal.** It turns away toolchains that would
  have compiled the crate, for no reason anyone can act on.

The committed `Cargo.lock` is what the floor is computed over, not the version ranges in the
manifests: the lock is what every build of this repository resolves. Bumping a dependency therefore
moves the floor, and the comment above each `rust-version` names the dependency that sets it so a
reviewer can see which bump did it.

| Crate | `rust-version` | Imposed by | Edition |
| --- | --- | --- | --- |
| `sharp-typst` | 1.92 | The `typst` 0.15.1 crates and the PDF stack they pull in — fifteen `typst-*`, nine `hayro`, two `krilla` crates; 26 packages declare 1.92 | 2024 (1.85) |
| `sharp-text-barcodes` | 1.85 | `rxing` 0.9.4, `image` 0.25.8, `moxcms` 0.7.11, `pxfm` 0.1.30 and `unicode-segmentation` 1.13.3 all declare 1.85, which is also the edition 2024 floor | 2024 (1.85) |
| `sharp-text-docx` | 1.89 | `zip` 9.8.1 declares 1.89; `roxmltree` 0.21 asks for less, and edition 2024 asks 1.85 | 2024 (1.85) |
| `sharp-text-ocr` | 1.94 | `rten` 0.28.0 and `rten-simd` 0.28.0 declare 1.94. `hayro` 0.9.0 declares 1.92, which is why this crate's floor is not `hayro`'s | 2024 (1.85) |
| `sharp-text-xlsx` | 1.89 | `zip` 9.8.1, reached through `calamine` 0.38.0 — `calamine` itself declares 1.88, and `zip` is also a dev-dependency for the tests that write real packages | 2024 (1.85) |

### The check

`tools/check-msrv.py` computes the floor per crate and compares it with the declared value. It
prints a table of crate, declared value, computed floor and the dependency that imposes it, and
exits 1 when a crate is off in either direction, naming the file, the declared value, the computed
value and the chain that reaches the dependency.

```bash
python3 tools/check-msrv.py                    # cargo metadata --locked, needs cargo and the registry
python3 tools/check-msrv.py --offline          # Cargo.lock plus the cargo registry cache, no cargo
python3 tools/check-msrv.py --verbose text-ocr # one crate, every package that declares a floor
```

The default mode asks `cargo metadata --locked` for each crate, so the graph compared is the one
Cargo resolves from the committed lock, dev-dependencies and target-specific dependencies included.
`--offline` makes no cargo call: it reads each `Cargo.lock` for the resolved set and the
`rust-version` field out of every dependency's manifest in the cargo registry cache. A manifest that
has never been fetched cannot be read; the script then falls back to the crate's own declared value
and prints a `WARNING` naming each package it could not read, so a degraded verdict says so instead
of passing silently.

`.github/workflows/msrv.yml` runs the default mode on every pull request and on demand, on
`ubuntu-latest` with `contents: read`. **The control is static: no job downloads an old toolchain
and nothing is compiled with 1.89.** Building each crate at its floor on five platforms would be a
matrix of its own for a number that can be read out of the graph in seconds, and a build on the
runner's current stable toolchain proves nothing about the floor — 1.97 compiles whatever the
manifest claims. What the static control buys is the pair of failures a stable build hides: a
promise lower than a dependency allows, and a refusal of toolchains that would have worked.

The proof that a crate compiles at all remains the release workflows, which build each crate for
real on the five RIDs with the stable toolchain. Those runs demonstrate the crates compile at the
current stable version; they do not demonstrate the declared floor, and no job here claims to.

## 12. SBOM and build provenance

A package that embeds a typesetting compiler and four Rust `cdylib`s cannot be audited by reading
its `.nuspec`: the native side is invisible there, and so are the RID-layout assets and the crates
those assets were built from. Each release therefore attaches one **CycloneDX 1.6 SBOM per
`.nupkg`** to the release page, and each `.nupkg` and each SBOM carries a **build provenance
attestation** the workflow signed. Both are produced by the job that packed the package, from the
package itself.

### 12.1 What the SBOM contains

The file is named `<PackageId>-<Version>.cdx.json` and sits next to the `.nupkg` of the same
version.

| Part | Content | Where it comes from |
| --- | --- | --- |
| `metadata.component` | The package itself: `pkg:nuget/M3scalinum.Text.Docx@0.1.0`, one `m3scalinum:rid` property per RID the package ships, and `m3scalinum:embedded-crates` naming the crates it carries | the `.nuspec` id/version and the `runtimes/` layout |
| `library` components, `pkg:nuget/…` | Every package the `.nuspec` declares, with the version this run resolved. When `obj/project.assets.json` is on disk — always true in the release `pack` job, which restored and packed there — the restore graph is added too, with `m3scalinum:resolved-from: project.assets.json` | the `.nuspec` dependency groups, then the assets file |
| `library` components, `pkg:cargo/…` | Every package of the embedded crate's `Cargo.lock`, name and version, with `m3scalinum:embedded-crate` naming which of the five crates pulled it in and `m3scalinum:root` on the crate itself | `native/<crate>/Cargo.lock` |
| `file` components, managed | Each `lib/<tfm>/<assembly>.dll` with its SHA-256. This is what a package with no dependency and no native asset — `M3scalinum.Text.Documents` — consists of, and the one thing a consumer can compare byte for byte with what they load | the `.nupkg` zip |
| `file` components, native | Each `runtimes/<rid>/native/<library>`, with its SHA-256 and its RID as a property, so a downloaded native asset can be checked against the SBOM without unpacking anything | the `.nupkg` zip |
| `platform` components | One per RID, so the RID set is a first-class part of the graph rather than a string in prose | the `runtimes/` layout |

Build-only packages the restore pulls in — `Microsoft.SourceLink.*`, `Microsoft.NET.ILLink.Tasks`,
`Microsoft.Build.Tasks.Git` — are listed with CycloneDX `scope: "excluded"` and
`m3scalinum:scope: build-only`, and carry that marking rather than being dropped: an auditor asking
"was anything compiled into this that is not in the SBOM" gets an answer, not a gap. Project
references that end up inside the package (`M3scalinum.Text.Documents` inside
`M3scalinum.Text.Docx`) are listed as `project-reference` with `scope: required`.

The Rust side is the full lock, every target included, because the package ships one binary per RID
and no `Cargo.lock` is per-RID: filtering by target would claim the `win-x64` binary was built
without crates that are in the other four.

### 12.2 The tool, and why not an existing one

`.github/sbom/sbom.py` writes and checks the SBOMs. It has three subcommands: `generate`, `verify`
and `self-test`.

`dotnet CycloneDX` and `cyclonedx-nuget` both read a **project or a solution** and describe the
managed restore graph. That is the wrong graph twice over here: the native side is a `Cargo.lock`
no .NET tool parses, and the package's `runtimes/` layout — the RIDs a consumer actually resolves —
exists only inside the `.nupkg`. Merging a managed SBOM with a hand-built Rust section would be two
tools, a merge step and a second source of truth. This script reads the artifact instead: the
`.nuspec` and the zip entries out of the `.nupkg`, and `native/<crate>/Cargo.lock` for each crate
the layout names. Everything it needs is the Python 3.11 standard library (`zipfile`, `tomllib`,
`xml.etree`, `hashlib`), which every `ubuntu-latest` runner already has — no `dotnet tool install`,
no `pip install`, no registry call, so the SBOM a run produces depends on the package and the
commit, not on a feed.

The map from native library to crate is explicit, `CRATE_BY_LIBRARY` in the script. A package that
starts shipping a new native library fails `generate` with the library name it could not place
rather than emitting an SBOM with no Rust section in it.

### 12.3 The gate

`verify` re-reads the `.nupkg` and derives, from the package alone, what the SBOM must contain. The
job fails when:

- the SBOM has no components;
- a package that embeds a native library has no `pkg:cargo/` component for that crate;
- the SBOM's `platform` components are not exactly the RID set the `.nupkg` ships — a missing RID,
  or one the package does not ship;
- a dependency the `.nuspec` declares is absent;
- an assembly the package ships under `lib/` has no `file` component;
- `metadata.component` names a different package or version than the `.nupkg`;
- any component lacks a name or a version.

It runs in three places. In both release workflows, in `pack` right after `tests/verify-package.py`,
so a release stops before publishing rather than attaching a voucher it cannot stand behind; again
in the `attest` job, on the downloaded artifacts, because those are the files the attestation
covers. In `ci-text.yml`, the `sbom` job runs `generate` and `verify` over the nine packages the
`examples` job just packed, then empties each SBOM in turn and fails if `verify` accepts it: a
verifier that only ever passes proves nothing. `python3 .github/sbom/sbom.py self-test` does the
same four checks locally against a synthetic package.

What the gate proves is that the SBOM describes this package. What it does not prove is that the
crate graph is the one in the binary: the SBOM is generated from the committed `Cargo.lock`, and the
lock is what the release job built from, but nothing re-derives the crate set from the compiled
`.so`. A stale lock committed alongside a hand-patched binary would pass.

### 12.4 The attestation

The `attest` job, in both release workflows, runs `actions/attest-build-provenance@v4` over
`artifacts/packages/*.nupkg` and `artifacts/sbom/*.cdx.json`. Its permissions are only `contents:
read`, `id-token: write` and `attestations: write`; `publish`, and in the compiler workflow `test`,
are declared as prerequisites, so a run whose attestation failed never reaches nuget.org.

What it proves: the digest of that `.nupkg` — and of that SBOM — was produced by this workflow, in
this repository, at that commit, on that runner, and the identity in the attestation is GitHub's
OIDC claim for the run. A file modified after the fact no longer matches its subject digest, so
`gh attestation verify` fails on it.

What it does not prove: that the package is correct, safe, or free of the bugs the tests miss; that
the SBOM is complete beyond the checks of §12.3; that the release page's assets were not
re-uploaded (the attestation covers the digest, not the page); and that a package built outside
this workflow is authentic — there is no attestation for it at all, which is a different answer from
a failed check. It also says nothing about reproducibility: two runs produce two attestations for
two different digests.

GitHub issues attestations only for **public** repositories, or for GitHub Enterprise Cloud. This
repository is public — `gh repo view m3scalinum/text --json visibility` returns `PUBLIC` — so
`gh attestation verify` is the intended consumer path. Were it private, the job would fail at the
attestation step, and the honest fallback would be a CI signature over a key the consumer has to
obtain; that is not what is implemented.

### 12.5 Verifying as a consumer

```bash
# The package and its SBOM, from the release page of the version you install.
gh release download v0.1.0 --repo m3scalinum/text \
  --pattern 'M3scalinum.Text.Docx.*' --pattern 'M3scalinum.Text.Docx-*.cdx.json'

# Provenance: exits non-zero when the digest is not the one the workflow signed.
gh attestation verify M3scalinum.Text.Docx.0.1.0.nupkg --repo m3scalinum/text

# The SBOM and the package agree on the native asset, and the RID list is what you expect.
python3 - M3scalinum.Text.Docx-0.1.0.cdx.json <<'PY'
import json, sys
sbom = json.load(open(sys.argv[1], encoding="utf-8"))
for component in sbom["components"]:
    if component["type"] == "platform":
        print("rid", component["name"])
    if (component.get("purl") or "").startswith("pkg:cargo/"):
        print("crate", component["name"], component["version"])
PY
```

`gh attestation verify` needs a token for this repository, or `--owner` plus an explicit bundle
path, for a machine that has none.

**Status.** The YAML of both release workflows and of `ci-text.yml` parses; `generate`, `verify` and
`self-test` were run locally over a real `M3scalinum.Text.Docx` `.nupkg` packed from this tree, and
`verify` was shown to fail on an emptied SBOM. No release workflow has been executed for this
section: no attestation has been issued, and `gh attestation verify` has not returned zero on a real
asset. That part is proven by the first tagged run, and by nothing here.

## 13. Comparing a change locally

A developer does not need the CI runner to know whether a change is a regression, but the artifact
that answers that question in CI cannot answer it here: `benchmarks/baseline.json` is a Linux
number. This section describes the local substitute and, at least as importantly, the cases where it
declines to answer.

### 13.1 What a local `--check` cannot say

`benchmarks/baseline.json` is recorded by the `benchmarks` job of `ci-sharptypst.yml`, on
`ubuntu-latest`, against the `linux-x64` native library that same run built. Those numbers are
absolute milliseconds: `compile-small` at 2.97 ms is a statement about one CPU model, one runtime
version and one glibc. Replaying `--check` on a developer host compares a Windows or macOS run
against a Linux reference, and a gaming laptop is not 20 % faster than a GitHub runner in any sense
that matters — it just is, and the ratio is meaningless.

The second problem is temporal, not spatial. A single run of the harness warms up for ten
iterations and then measures fifty. On a cold process that warm-up is not enough: the JIT is still
tiering, the frequency governor still ramping, the page cache still filling. Two runs of *identical*
code on the same box routinely differ by a few percent, and occasionally by tens. So the naive local
A/B — build the old revision, run it, build the new one, run it, divide — answers with the machine's
mood, not with the change.

Running it locally does not merely produce a different number; it produces a wrong verdict. On the
Windows host this section was written on, `--check benchmarks/baseline.json` against the *unmodified*
tree reports five scenarios 2× to 50× faster than the Linux baseline and two as regressions —
`export-png` at ×1.70, `document-model` at ×2.48 — and exits 1:

```
compile-small          0.07 ms  baseline     2.97 ms  x0.02
export-png            15.80 ms  baseline     9.27 ms  x1.70
document-model         4.74 ms  baseline     1.91 ms  x2.48
export-png regressed by 70%
document-model regressed by 148%
```

The same commit is green on CI. A developer who trusted that output would spend an afternoon on a
regression that does not exist.

The CI gate stays where it is: 1.5× on the minimum, on the Linux runner, against the stored
baseline. Nothing in this section replaces it, and `--check` is not what a developer should run
locally.

### 13.2 Why two revisions cannot share a process

The clean experiment is to measure both revisions in one process, alternating them, so that JIT
warm-up and the machine's state are common to both. It is not available here, for two independent
reasons:

- **One assembly identity.** `M3scalinum.Text.Typst`, `M3scalinum.Text.Documents` and
  `M3scalinum.Text.Typst.Native` can exist twice in one process only in two `AssemblyLoadContext`s.
  That works for pure managed code, but `Text.Typst` reaches the compiler through
  `Text.Typst.Native`, and `TypstWorldBuilder` hands the native layer managed callbacks. A delegate
  from the default context cannot be passed to a method compiled against the second context's copy
  without either reflection on every call — which dominates a 3 ms compile — or a shared interface
  assembly, which is a refactor of the public surface to serve the benchmark.
- **One native library per process.** Even with the managed split solved, the Rust side is a single
  loaded `sharp_typst` with process-global state (the world registry, the font book, the allocator
  counters). Both revisions would end up calling the same compiler binary, so the A/B would compare
  managed wrappers over identical native work — precisely not the experiment.

So the two-revisions-in-one-process mode is rejected rather than approximated. What is left is the
next best thing: **one process per revision, alternated**, which keeps the pairing in time instead
of in memory.

### 13.3 The campaign

`tools/bench-compare.py run` does the whole thing:

```bash
# Both checkouts must contain benchmarks/Text.Typst.Benchmarks; the native library is shared.
export SHARPTYPST_NATIVE_PATH=C:/src/text/native/artifacts/win-x64/sharp_typst.dll

python3 tools/bench-compare.py run \
  --a C:/src/text-main --b C:/src/text-change \
  --rounds 7 --out artifacts/bench-ab
```

It builds each checkout once into its own output directory
(`dotnet build -c Release -o <out>/bin/a`), then runs the two harnesses alternately — round 1
`A B`, round 2 `B A`, round 3 `A B`… — one process per revision per round, each writing
`--samples <out>/results/{a,b}-<n>.json`. Building first matters: `dotnet run` re-evaluates the
project on every invocation, and a build check inside the measurement window is noise that costs
nothing to remove.

The reversal is not cosmetic. A fixed `A B A B` order biases every pair by whatever the machine does
between the first and second process — a turbo budget spent, a background indexer waking up, an
allocator's cache warming — and that bias would be attributed to B every time. Reversing it each
round turns the bias into a difference between rounds, which the noise floor absorbs.

Each round also passes `--min-iteration-ms 1`, which makes the harness time a **block** of repeated
calls rather than one call for scenarios whose single call is far below a millisecond:
`compile-small` is 30 µs to 100 µs on a modern host, and a ratio computed on 30 µs is a
measurement of the scheduler. The harness calibrates the repeat count after warm-up, executes the
body that many times inside one timed iteration, and writes the count into the samples file, so both
sides are measured at the same scale and a reader can see what the number means. `--write` and
`--check` never repeat: the baseline keeps its one-call-per-iteration semantics, and CI is
unaffected.

Each `--samples` file carries the raw per-iteration timings, the summary statistics, the repeat
count, the runtime version, the OS, the architecture and the processor count, plus the `--label` the
script passes so the report can name the revision. That is enough for the comparison to detect a campaign that mixed
machines, which it warns about instead of dividing through.

`compare` reads files produced elsewhere, for a campaign split across two machines or resumed later:

```bash
python3 tools/bench-compare.py compare \
  --a results/a-0.json results/a-1.json results/a-2.json \
  --b results/b-0.json results/b-1.json results/b-2.json
```

`--samples` is also the harness's own interface, for a campaign driven by something else:

```bash
dotnet build -c Release benchmarks/Text.Typst.Benchmarks
dotnet benchmarks/Text.Typst.Benchmarks/bin/Release/net11.0/Text.Typst.Benchmarks.dll \
  --samples artifacts/a.json --label main --iterations 50 --warmup 10
```

### 13.4 What the verdict means

For each scenario:

| Quantity | How it is computed | Why |
| --- | --- | --- |
| per-run value | median of the iteration timings of that run | the minimum is the luckiest iteration; the median is what a user would experience and is stable across runs |
| effect | median over rounds of `candidate_median / baseline_median`, paired by round | a ratio above 1 is slower; pairing removes the machine's state from the ratio |
| noise floor | median absolute relative difference between *adjacent* runs of the *same* revision | the only honest source for it: identical code, this machine, this session. Adjacent, because two consecutive runs of one revision are separated by the same slice of machine time as the two sides of a round, so their disagreement is the null of exactly this comparison |
| interval | 2 000-sample percentile bootstrap on the paired ratio, 95 % | with 7 rounds the interval is coarse, and it is printed so that this stays visible |
| resolution | `max(noise, half-width of the interval)` | the smallest effect this campaign could have seen |

A scenario gets one of five verdicts, and only two of them are conclusions about the code:

- **REGRESSION** — the whole 95 % interval is above `1 + noise`. The report states the smallest
  slowdown the data support.
- **IMPROVEMENT** — the whole interval is below `1 - noise`.
- **NO CHANGE** — the interval contains 1.0. This is a statement about resolution, not proof of
  equality: the band is printed, so "+0 % at ±1.4 %" cannot be read as "nothing moved".
- **INCONCLUSIVE** — the interval excludes 1.0 but stays inside the noise band. An effect is
  visible, but smaller than the machine's run-to-run spread; it cannot be attributed to the change.
- **REFUSED** — the resolution is wider than `--max-noise` (default 10 %), or fewer than three
  paired rounds were supplied. Nobody can be told what a 3 % change means when the campaign can only
  see 25 %.

The order matters. The two conclusion tests run first, against the noise band, because a large
effect is decidable even on a noisy host: to explain a 2.5× regression by machine drift, the machine
would have to drift further than the change itself. Only below that does the budget decide whether
the campaign was strong enough to answer the question at all.

Exit status: `0` no regression and every scenario concluded, `1` a regression was established, `2`
at least one scenario could not be concluded (noise, or too few rounds), `3` bad usage. A developer
who gets `2` re-runs on a quieter machine or with more rounds; a developer who gets `1` reads the
scenario line and its interval.

The threshold is a budget, not a discovery. One percent is below what this harness resolves on a
development host — timer granularity, JIT tiering and a shared L3 make a 1 % effect invisible long
before the statistics matter. Ten percent is where the tool stops pretending: a machine that varies
that much between two adjacent runs of identical code cannot answer a question about a 5 %
regression, and the tool says so instead of printing a ratio. `--max-noise` moves the line for a
known-noisy host; it does not make the host quieter.

### 13.5 What the measurement does not cover

- **Absolute performance.** A local A/B is a delta. It says nothing about whether the build is fast
  in the sense the CI baseline means; that number belongs to the Linux runner and the 1.5× gate.
- **Memory.** The harness times wall-clock work and nothing else: no allocation counter, no
  working-set sample, no GC counts. A change that halves the time and doubles the allocations looks
  like an improvement here.
- **Tail latency.** Per-iteration timings are recorded, so p95 is in the `--samples` file, but the
  verdict is computed on the median. A change that leaves the median alone and ruins the tail of a
  server workload is invisible to this tool — and to `--check`, which compares minima.
- **Cross-machine and cross-runtime deltas.** Two `--samples` files from different hosts produce a
  warning and a ratio that is not a measurement. A different .NET version, `Debug` instead of
  `Release`, and a build running in the background invalidate a campaign the same way.
- **Anything smaller than the noise floor.** The resolution is measured per campaign and per
  scenario, never promised: it is the larger of the machine's run-to-run spread and the width of
  the interval. Below it the tool refuses to name a direction, which is the point.

### 13.6 What was measured, and where the limits showed up

Two campaigns on the host this section was written on: Windows 11, x64, eight logical cores, with
an IDE, a source-control GUI and another build all resident — a normal developer desktop, not a
quiet one. Nine rounds each, `--min-iteration-ms 1`.

*The null.* The same commit on both sides, built twice into two directories:

```
scenario            base ms  cand ms   ratio  95% CI              noise  verdict
compile-small          0.95     1.07   1.151  [0.695, 1.922]      37.3%  REFUSED
compile-report         1.07     1.12   1.017  [0.707, 1.294]      25.3%  REFUSED
export-pdf             1.63     1.54   0.918  [0.693, 1.174]      34.5%  REFUSED
export-png            10.29     9.45   0.910  [0.576, 1.093]      23.9%  REFUSED
export-html            1.13     1.24   1.017  [0.946, 1.427]      25.7%  REFUSED
export-markdown        1.18     1.18   0.940  [0.606, 1.333]      46.7%  REFUSED
document-model         2.26     2.13   1.185  [0.752, 1.999]      47.3%  REFUSED
```

Seven refusals, no invented verdict, on code that had not changed at all. That is the failure mode
the tool exists to avoid: a naive A/B on this box would have read "compile-small 15 % slower".

*The regression.* A second campaign, same host, with a deliberately wasteful re-serialisation of the
markup added to `TypstDocument.ToMarkup` — a measurement fixture, never committed:

```
scenario            base ms  cand ms   ratio  95% CI              noise  verdict
compile-small          0.90     0.88   0.953  [0.701, 2.200]      38.1%  REFUSED
compile-report         1.09     1.04   0.967  [0.798, 1.237]      18.1%  REFUSED
export-pdf             1.57     1.18   0.736  [0.688, 1.040]      20.6%  REFUSED
export-png             8.11     8.89   1.050  [0.782, 1.369]      26.1%  REFUSED
export-html            1.14     1.05   0.992  [0.828, 1.288]      21.1%  REFUSED
export-markdown        1.21     1.18   0.941  [0.697, 1.207]      16.9%  REFUSED
document-model         2.09     4.42   2.501  [1.744, 2.816]      22.2%  REGRESSION
```

The affected scenario is named with an interval that clears the noise band by a wide margin; the six
that the change could not move are refused rather than declared clean. Both halves of the design are
visible in one run: a large effect is decided on a noisy host, a small one is not decided at all.

The honest reading of these numbers is that a shared developer desktop is a poor measurement
instrument: 20 % to 47 % between two adjacent runs of identical code. A quiet machine, fewer
resident processes and more rounds lower the floor — nothing else does, and the tool reports the
floor it actually got rather than the one the reader hopes for. On this host, the verdict that a
change smaller than a few percent is a regression is not obtainable locally, and the CI job remains
the only place that can produce it.

### 13.7 A reference container, and why it is not the default

`mcr.microsoft.com/dotnet/runtime:11.0` with the native library mounted would make the *host*
reproducible, and would be the answer if a team needed baseline-shaped absolute numbers on demand.
It is not what this section recommends for everyday work: the image is a gigabyte, it needs Docker,
and it buys nothing for a delta between two revisions on one machine — the A/B in §13.3 already
cancels the host, because both sides run on it within seconds of each other. Keep the container for
the case the A/B cannot cover: reproducing a CI disagreement on a developer's desk, where the
question is "does this number also happen on the runner's glibc", not "did this change regress".

## 14. A package mirror in CI and in an image

§2.3 of [03](03-compilation.md#23-packages) makes a compile offline by default; §15 of the same
document describes what a package mirror is and what its manifest proves. This section is the other
half: where the mirror is *built*, so that a compile ever only reads one.

**Decision — the mirror is filled at build time, never at run time.** A container that fetches
packages on first request moves the network into the request path: cold starts depend on a
third-party registry, a registry outage becomes an application outage, and two replicas can run
different bytes of the same package. Filling the mirror in the image build (or in the CI job that
produces the artifact) moves all of that to a step whose failure is a red build.

### 14.1 Filling it in CI

`tools/typst-mirror.py` is standard library only, so the step needs nothing beyond the Python that
the runner already has:

```yaml
- name: Fill the package mirror
  run: |
    python3 tools/typst-mirror.py --mirror "$RUNNER_TEMP/typst-mirror" \
        --package @preview/cetz:0.3.1 \
        --package @preview/tablex:0.0.8

- name: Verify the package mirror
  run: python3 tools/typst-mirror.py --mirror "$RUNNER_TEMP/typst-mirror" --verify
```

Two habits make that step cheap and its result worth caching:

- **Cache the mirror on the package list, not on the source.** The key is the list of specs the
  build asks for; a source change that does not touch an import must not re-download anything.
  `--package` is idempotent — a package already in the mirror is left alone unless `--force` is
  passed — so restoring a cache and re-running the step costs one directory listing per package.
- **Upload the mirror as an artifact and let the deploy consume it,** rather than re-filling in each
  environment. One job fetches; every later job verifies. The manifest travels with the packages,
  which is what makes the two impossible to separate by accident.

`--verify` is the gate that makes the artifact trustworthy afterwards: it exits `1` on a file whose
digest changed, on a file that is present but unlisted, and on a package directory the manifest does
not describe. Run it once in the job that builds the mirror and once in the job that consumes it —
the second run is what catches an artifact that was truncated in transit.

Sizes are small enough that none of this needs a decision: `@preview/cetz:0.3.1` is 39 files and
280 839 bytes, manifest included, which is smaller than most test fixtures in this repository.

### 14.2 In a container image

Fill in the builder stage, copy the result into the final image, and let the application point at it:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS mirror
WORKDIR /src
COPY tools/typst-mirror.py tools/
RUN python3 tools/typst-mirror.py --mirror /opt/typst-mirror \
        --package @preview/cetz:0.3.1 && \
    python3 tools/typst-mirror.py --mirror /opt/typst-mirror --verify

FROM mcr.microsoft.com/dotnet/runtime:11.0
COPY --from=mirror /opt/typst-mirror /opt/typst-mirror
```

```csharp
using var world = new TypstWorldBuilder()
    .WithPackages(packages => packages.MirrorDirectory = "/opt/typst-mirror")
    .AddSource("main.typ", markup)
    .Build();
```

The image layer is immutable in the sense that matters: the bytes were frozen when the image was
built, the manifest was written at the same moment, and the digest of the image is the digest of
both. That is the property a writable cache volume does not have — and the reason a mirror on a
volume should be verified again in the process that reads it, or mounted read-only.

Nothing is downloaded at start. A package the image does not hold fails the world with the package
and the mirror named ([03 §15.3](03-compilation.md#153-the-cache-only-mode)), which is a deployment
error that shows up on the first request instead of on the first package a user happens to import.

### 14.3 What the mirror does not fix

- **It does not pin versions.** A document that imports `@preview/cetz:0.3.1` still names the
  version; the mirror only decides which bytes that version is. §7's versioning rules cover the
  rest.
- **It does not authenticate the packages.** The digests prove integrity against the manifest, and a
  mirror that can rewrite a package can rewrite the manifest — see [03 §15.4](03-compilation.md#154-what-it-proves-and-what-it-does-not).
  A signature over `mirror.json`, or an image digest, is what closes that gap.
- **It is not a substitute for `deny.toml`.** That gate covers the crates and packages this
  repository depends on; a Typst package a *document* imports is a runtime input, and the mirror is
  where its contents are fixed.
