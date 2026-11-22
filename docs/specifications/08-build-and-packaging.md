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
  <RootNamespace>M3scalinum.Text.Typst</RootNamespace>
  <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
  <IsAotCompatible>true</IsAotCompatible>
  <IsTrimmable>true</IsTrimmable>
</PropertyGroup>

<ItemGroup>
  <None Include="$(NativeArtifactsDir)/win-x64/sharp_typst.dll"
        Pack="true" PackagePath="runtimes/win-x64/native/" Visible="false" />
  <None Include="$(NativeArtifactsDir)/linux-x64/libsharp_typst.so"
        Pack="true" PackagePath="runtimes/linux-x64/native/" Visible="false" />
  <None Include="$(NativeArtifactsDir)/osx/libsharp_typst.dylib"
        Pack="true" PackagePath="runtimes/osx/native/" Visible="false" />
  <None Include="../../THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="/" />
  <None Include="../../README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

`$(NativeArtifactsDir)` defaults to `native/artifacts/` and is overridden in CI to the directory
where the matrix jobs dropped their binaries.

**Decision — one package with six RIDs, not six runtime packages.** The natives are 43–100 MB each;
a single package is simpler to version, and NuGet downloads the whole `.nupkg` either way — the RID
filters what is copied into the output, not what is downloaded. Rejected:
`M3scalinum.Text.Typst.Native.win-x64` style split packages (six versions to keep in lockstep, six publish
steps).

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
plain link would produce an empty library. The result is ~75 MB, which takes the package from 135 MB
to ~170 MB compressed — a 26 % download increase for every consumer, Windows and macOS included. A
satellite `M3scalinum.Text.Typst.Native.LinuxMusl` package was rejected: it saves no download, since NuGet
fetches the whole `.nupkg` regardless, and it moves the cost onto the Alpine users, who have to
discover the package — without it the resolver finds the glibc build and fails with a
`DllNotFoundException` that names nothing. The asset sits under `runtimes/linux-musl-x64/native/`,
which `NativeLibraryResolver` probes first on a musl host (Spec 02 §9).

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
