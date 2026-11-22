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

`src/Text.Typst/M3scalinum.Text.Typst.csproj` packs the natives under the standard `runtimes/<rid>/native/`
layout, so no `buildTransitive` targets are needed:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageId>M3scalinum.Text.Typst</PackageId>
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>

<ItemGroup>
  <None Include="$(NativeArtifactsDir)/win-x64/sharp_typst.dll"
        Pack="true" PackagePath="runtimes/win-x64/native/" Visible="false"
        Condition="Exists('$(NativeArtifactsDir)/win-x64/sharp_typst.dll')" />
  <None Include="$(NativeArtifactsDir)/linux-x64/libsharp_typst.so"
        Pack="true" PackagePath="runtimes/linux-x64/native/" Visible="false"
        Condition="Exists('$(NativeArtifactsDir)/linux-x64/libsharp_typst.so')" />
  <None Include="$(NativeArtifactsDir)/osx/libsharp_typst.dylib"
        Pack="true" PackagePath="runtimes/osx/native/" Visible="false"
        Condition="Exists('$(NativeArtifactsDir)/osx/libsharp_typst.dylib')" />
  <None Include="../../THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="/" />
  <None Include="../../README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

(The file lists all six RIDs; three are shown here. Each entry is conditioned on the artifact's
existence, so a pack on a machine that built no natives still produces a package — and
`tests/verify-package.py` is what turns that package red instead of a consumer.)

`$(NativeArtifactsDir)` defaults to `native/artifacts/` and is overridden in CI to the directory
where the matrix jobs dropped their binaries.

### 6.1 What the package weighs, measured

`tools/package-size.py` reads a packed `.nupkg` and reports every entry's compressed size, so the
figures below are the file the feed serves and not an estimate:

```bash
# One `pack` job's output: one table per package, then what each RID costs.
python3 tools/package-size.py artifacts/packages/

# A split layout, compared with the single package it would replace.
python3 tools/package-size.py artifacts/packages/M3scalinum.Text.Typst*.nupkg
```

They are from
`M3scalinum.Text.Typst.0.15.1.217.nupkg`, the artifact of the `pack` job of `ci-sharptypst` run
45024810474:

| Entry | Compressed | Share |
| --- | ---: | ---: |
| `runtimes/osx/native/libsharp_typst.dylib` | 44.59 MB | 26.5 % |
| `runtimes/linux-musl-x64/native/libsharp_typst.so` | 33.24 MB | 19.7 % |
| `runtimes/linux-x64/native/libsharp_typst.so` | 23.43 MB | 13.9 % |
| `runtimes/linux-arm64/native/libsharp_typst.so` | 22.97 MB | 13.6 % |
| `runtimes/win-x64/native/sharp_typst.dll` | 22.35 MB | 13.3 % |
| `runtimes/win-arm64/native/sharp_typst.dll` | 21.28 MB | 12.6 % |
| `lib/` (4 TFMs), nuspec, licences, readme — 26 entries | 0.44 MB | 0.3 % |
| **`M3scalinum.Text.Typst.0.15.1.217.nupkg`** | **168.32 MB** | |

The six natives are 167.87 MB: 99.7 % of what a consumer downloads is binaries, and five of the six
are binaries no build on their machine can load.

### 6.2 What a consumer downloads, per RID

NuGet 3 has no partial download. A package is one immutable zip behind one URL, a restore fetches
the whole file, and the RID decides only which entries are copied out of it afterwards. Measured, on
a project that references the package above:

- `dotnet restore` puts 168.32 MB in the global packages folder, plus 0.43 MB for
  `M3scalinum.Text.Documents`, whatever RID the project is later built for;
- `dotnet build -r win-x64` copies one native out of it — `sharp_typst.dll`, 49.61 MB raw, at the
  root of the output;
- the same build **without** `-r` copies all six, 379.20 MB raw under `runtimes/`, because a
  RID-agnostic build has no basis to choose. A `publish` without a RID carries the same 379 MB.

| RID | downloads | copies out | paid for, unusable |
| --- | ---: | ---: | ---: |
| `win-x64` | 168.32 MB | 22.80 MB | 145.52 MB (86.5 %) |
| `win-arm64` | 168.32 MB | 21.72 MB | 146.59 MB (87.1 %) |
| `linux-x64` | 168.32 MB | 23.88 MB | 144.44 MB (85.8 %) |
| `linux-arm64` | 168.32 MB | 23.41 MB | 144.90 MB (86.1 %) |
| `linux-musl-x64` | 168.32 MB | 33.69 MB | 134.63 MB (80.0 %) |
| `osx` | 168.32 MB | 45.03 MB | 123.28 MB (73.2 %) |

"Copies out" is the RID's own native plus the shared 0.44 MB; it is what that consumer's build
actually uses of the file.

**Decision — one package with six RIDs, and the price of it.** §6.2 makes the price exact: a `win-x64`
consumer downloads 168.32 MB and their build copies out 22.80 MB of it, so 86.5 % of the transfer is
five binaries their machine cannot load. A per-RID split — `M3scalinum.Text.Typst` carrying managed
code only, one `M3scalinum.Text.Typst.Native.<rid>` per RID — would take that consumer to 22.80 MB and
a musl consumer to 33.69 MB, for one extra line in the project file. It is rejected anyway, for
reasons that are not about bytes:

- NuGet cannot pick the satellite for the consumer. Dependencies are grouped per target framework and
  never per RID, no client downloads part of a package, and nothing in a project file says which
  native it needs before the RID is set. So a split package set is a two-line install by construction.
- The install every consumer is taught is one line: `README.md`, `wiki/Typst-Getting-Started.md` and
  `samples/examples/run.sh` all show `dotnet add package M3scalinum.Text.Typst` alone. After a split
  that command installs an assembly that throws `DllNotFoundException` at the first compile, on a
  path that names the glibc asset — a failure at run time where there used to be none at all, and one
  the compiler cannot see.
- Those three files are outside a packaging change, so the split and the documentation of it cannot
  ship in the same release. Shipping half of it — a package that no longer works as documented, for a
  saving users cannot find — is a worse trade than the download it saves.

So the split is not a packaging detail to be decided here: it is a change to the install story, and it
is worth doing only with the quickstart, the samples and the six satellites in one release.
`tools/package-size.py` is what the decision is re-checked against, and 22.80 MB (`win-x64`) /
33.69 MB (`linux-musl-x64`) are the numbers a split would have to deliver. What this section settles
is the other half of the question: the package does **not** lose a quarter of its weight today, and
the reason is that the only lever large enough to do it — 86.5 % for `win-x64` — is a change to how
the package is installed, not to how it is packed.

Rejected — publishing the satellites *alongside* a package that still carries every native: that keeps
the download unchanged and adds six versions to keep in lockstep.

**Decision — `linux-musl-x64` ships in the main package.** NuGet resolves `linux-musl-x64` to the
`linux-x64` asset, so an Alpine or chiseled-musl container silently gets a glibc binary and fails at
load; the artifact has to exist. Rust cannot emit a `cdylib` for a musl target — it drops the crate
type with a warning and produces only the `staticlib` — so the shared object is linked by hand:

```bash
cargo build --release --target x86_64-unknown-linux-musl
symbols=$(nm -g --defined-only target/x86_64-unknown-linux-musl/release/libsharp_typst.a \
  | awk '$2=="T" && $3 ~ /^tn_/ {print "-Wl,-u,"$3}')
musl-gcc -shared -o libsharp_typst.so $symbols libsharp_typst.a -lpthread -ldl -lm
strip --strip-all libsharp_typst.so
```

The `-u` flags are what keep the `tn_*` entry points: nothing in the archive references them, so a
plain link would produce an empty library. The result is 73.79 MB of library, 33.24 MB on the wire —
19.7 % of the package, and 24.6 % of the 135.08 MB the package would weigh without it. That is a real
cost for every consumer, Windows and macOS included, and it is the largest single asset after the
universal macOS dylib. A satellite `M3scalinum.Text.Typst.Native.LinuxMusl` package was rejected
twice over: it reaches only 19.7 % of the package — under the quarter this was measured against —
and it makes the Alpine install the worst of the six, since NuGet would no longer fetch it on its own,
and the package that is easy to forget is the one without which the application cannot start on musl.
Without it the resolver finds the glibc build and fails with a `DllNotFoundException` that names
nothing useful. The asset sits under `runtimes/linux-musl-x64/native/`, which `NativeLibraryResolver`
probes first on a musl host (Spec 02 §9).

**Measured — compressing the asset instead, and why it is rejected.** The alternative to a split is to
ship the library compressed and unpack it on arrival. `xz -6` on the musl library gives 23.53 MB
against the 33.24 MB the `.nupkg` zip already achieves: 9.71 MB, 5.8 % of the package, for a
`runtimes/` subtree that no longer holds the library itself. The resolver would then have to unpack
73.79 MB into a writable cache before the first compile — and a chiseled or hardened Alpine container,
which is the reason the musl build exists, is the one place that cache may not exist. Applied to all
six natives the same ratio lands at 113.84 MB of package — 32.4 % less than 168.32 MB, so the option
does reach a quarter; it is the cache, not the ratio, that disqualifies it.

`typst-kit`'s downloader links OpenSSL, which no musl host provides, so `Cargo.toml` vendors it for
that triple alone; every other target keeps the system library.

**Decision — `M3scalinum.Text.Typst.Native` is merged, not published.** Every type in that assembly is
`internal`, so it is an implementation detail. The project reference carries `PrivateAssets="all"`
and the assembly is added to the package through `TargetsForTfmSpecificBuildOutput`, so the
`.nuspec` has no dependencies and a consumer installs exactly one package. Rejected: publishing it
as a second package, which would expose nothing and add a version to keep in lockstep.

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
| `pack` | `ubuntu-latest` | download all six artifacts, `dotnet pack -c Release` for the three compiler packages, `tests/verify-package.py` on each, upload `.nupkg`/`.snupkg` |

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

macOS is the exception: the `osx` artifact is a universal dylib. Both slices are cross-compiled on
`macos-15-intel` — `rustup` installs `aarch64-apple-darwin` and `x86_64-apple-darwin`, then `lipo`
joins the two. .NET resolves `osx-arm64` and `osx-x64` to `osx` by fallback, so one file covers both
architectures. Both the build and the integration leg stay on the Intel pool: the arm64 macOS pool is
throttled hard enough by GitHub that a job is regularly dropped before a runner is ever assigned.

Two release workflows, one per tag namespace:

| Workflow | Tag | Packs |
| --- | --- | --- |
| `release-sharptypst.yml` | `sharptypst-v0.15.1.42` | the three compiler packages, revision taken from the tag's fourth component |
| `release-text.yml` | `v0.1.0` | `M3scalinum.Text.Pdf`, `.Templates`, `.Mcp`, `.Barcodes`, `.Ocr` and `.Docx`; the tag is the version, and the core is pinned to the newest published `M3scalinum.Text.Typst` |

Each validates its tag against its own version property and refuses a tag that does not start with
it, so `sharptypst-v0.16.0.1` cannot silently publish a suite package, and a bare `v0.16.0.1`
cannot publish a compiler package. `release-sharptypst.yml` runs the full
native matrix; `release-text.yml` runs none, since nothing it packs contains a native asset. A manual
`workflow_dispatch` on either can run the pipeline without publishing, and falls back to the run
number for the revision.

`publish` needs both `pack` and `test`, so a tag cannot ship a build that was never exercised. The
`pack` job runs `tests/verify-package.py` on every package before uploading it, and `publish` pushes
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
| `samples/examples/run.sh <example> <package> <version> <feed>` | Compiles and runs one example against a packed package; `--build-only` stops after the compile |
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
