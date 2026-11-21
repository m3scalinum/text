# 08 — Build and packaging

## 1. Repository layout

```
M3scalinum.Text.SharpTypst/
├── .editorconfig
├── .gitattributes
├── .gitignore
├── global.json
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── M3scalinum.Text.SharpTypst.slnx
├── README.md
├── CHANGELOG.md              (the project as a whole)
├── CHANGELOG-SharpTypst.md   (the compiler packages)
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
│   ├── M3scalinum.Text.SharpTypst/                  (public managed API)
│   ├── M3scalinum.Text.SharpTypst.DependencyInjection/  (AddSharpTypst, options, validation)
│   ├── M3scalinum.Text.SharpTypst.Native/           (P/Invoke, internal)
│   └── M3scalinum.Text.SharpTypst.Templates.Generator/  (Roslyn generator, netstandard2.0)
├── tests/
│   ├── M3scalinum.Text.SharpTypst.Tests/
│   ├── M3scalinum.Text.SharpTypst.Integration.Tests/
│   └── fixtures/                   (typ, fonts, images, golden files)
├── samples/
│   └── M3scalinum.Text.SharpTypst.Samples/
└── templates/
    └── invoice/                    (reference template)
```

`M3scalinum.Text.SharpTypst.slnx` is the solution format (XML, no GUIDs). `dotnet build` at the root builds the
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
    <Product>M3scalinum.Text.SharpTypst</Product>
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

- `InvariantGlobalization` is on: the library formats numbers and dates with the invariant culture
  by contract, and a document must not change because the host's locale did.
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

`src/Text.SharpTypst/M3scalinum.Text.SharpTypst.csproj` packs the natives under the standard `runtimes/<rid>/native/`
layout, so no `buildTransitive` targets are needed:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageId>M3scalinum.Text.SharpTypst</PackageId>
  <RootNamespace>M3scalinum.Text.SharpTypst</RootNamespace>
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
`M3scalinum.Text.SharpTypst.Native.win-x64` style split packages (six versions to keep in lockstep, six publish
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
satellite `M3scalinum.Text.SharpTypst.Native.LinuxMusl` package was rejected: it saves no download, since NuGet
fetches the whole `.nupkg` regardless, and it moves the cost onto the Alpine users, who have to
discover the package — without it the resolver finds the glibc build and fails with a
`DllNotFoundException` that names nothing. The asset sits under `runtimes/linux-musl-x64/native/`,
which `NativeLibraryResolver` probes first on a musl host (Spec 02 §9).

`typst-kit`'s downloader links OpenSSL, which no musl host provides, so `Cargo.toml` vendors it for
that triple alone; every other target keeps the system library.

**Decision — `M3scalinum.Text.SharpTypst.Native` is merged, not published.** Every type in that assembly is
`internal`, so it is an implementation detail. The project reference carries `PrivateAssets="all"`
and the assembly is added to the package through `TargetsForTfmSpecificBuildOutput`, so the
`.nuspec` has no dependencies and a consumer installs exactly one package. Rejected: publishing it
as a second package, which would expose nothing and add a version to keep in lockstep.

## 7. Versioning and releases

Two families, two versions.

**The packages that embed the compiler** — `M3scalinum.Text.SharpTypst` and its two satellites —
version themselves **after Typst**: the package version *is* the Typst version, plus a build
revision.

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
| `InformationalVersion` | `0.15.1.42 (Typst 0.15.1)+<commit>` | What `TypstEnvironment.SharpTypstVersion` returns; the SDK appends the source commit |

A consumer can therefore tell which compiler a package carries from its version alone, and a
`0.15.1.*` → `0.15.2.*` bump is unambiguously a Typst upgrade.

**The packages that do not embed it** — `M3scalinum.Text.Pdf` (PAdES signing,
[14](14-pdf-signature.md)), `M3scalinum.Text.Pdf.FactureX` ([13](13-facturx.md)) and
`M3scalinum.Text.Templates` (the layout pack, [05](05-templates.md)) — carry whatever
version their tag names. `v0.51.2` publishes `0.51.2`, `v1.0.0` publishes `1.0.0`:

```
v0.51.2        the tag
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

Two packages share the compiler-derived version: `M3scalinum.Text.SharpTypst` (the library) and
`M3scalinum.Text.SharpTypst.DependencyInjection` (the container integration, §12 of this document
set). The second depends on the first, and NuGet stamps the referenced project's `PackageVersion` as
the dependency version — so the satellite can never name a `M3scalinum.Text.SharpTypst` that was not
built from the same commit.

**Decision — the compiler train packs its satellites in one job.** NuGet derives `>= <version>`
from a project reference, and no `ProjectReference` metadata overrides it; an exact range would need
a `PackageReference` to a package that does not exist yet at restore time. What protects the family
is not the range but the build: the three compiler packages are packed in one job from one revision,
and `tests/verify-package.py` fails the release when a satellite's `M3scalinum.Text.SharpTypst`
dependency is not the version being packed.

**Decision — the Pdf train packs against a published core.** `M3scalinum.Text.Pdf.FactureX` renders
through `M3scalinum.Text.SharpTypst`, but it releases on its own schedule. A project reference would
stamp it with whatever version this checkout happens to build, which on a Pdf release is a core
version nobody published. Its project file therefore switches on `CoreVersion`: empty, it uses the
project reference and a change to the wrapper flows in immediately; set, it uses a package reference
to that exact published version. `release-text.yml` resolves the newest stable
`M3scalinum.Text.SharpTypst` from NuGet's flat-container index and sets it, so a Pdf release always
names a core a consumer can actually restore.

A third package, `M3scalinum.Text.SharpTypst.Templates.Generator`, is an analyzer: it ships no `lib/` output, only
`analyzers/dotnet/cs/` and a `build/` props file that adds every `template.toml` to
`AdditionalFiles`. It shares the same version and has no runtime dependency on `M3scalinum.Text.SharpTypst`.

`M3scalinum.Text.Pdf.FactureX` is the e-invoicing layer ([13](13-facturx.md)): it carries no native
asset, and depends on `M3scalinum.Text.SharpTypst` for rendering only.

`VersioningTests` enforces the link: the assembly version must start with the version the native
library reports, so bumping the Rust crate without bumping `TypstVersion` fails the build.

- Three changelogs, because the project releases twice and does not release both halves at the
  same time. `CHANGELOG.md` records what the project gains as a whole and indexes the two others;
  `CHANGELOG-SharpTypst.md` carries the compiler train, `CHANGELOG-Text.md` the suite train. Each
  release workflow takes its page body from its own train file, so a release page can never quote
  the other train, and a compiler entry names the Typst version it bundles.
- A Typst upgrade is a **minor** version of `M3scalinum.Text.SharpTypst` unless it changes emitted
  output, in which case it is a major (golden files change).

`EnablePackageValidation` is on for the six packable projects. With no
`PackageValidationBaselineVersion` it still does real work: it compares the API surface of
`net8.0`, `net9.0` and `net10.0` against `net11.0`, which is exactly where a conditional
`PackageReference` or an `#if` would quietly drop a type. Once 1.0 is on nuget.org, set the baseline
version and `dotnet pack` starts failing on a removed member, a changed signature or a narrowed
parameter type instead of letting it reach a consumer.

## 8. CI

The suite is split along the same line as the versions. `.github/workflows/ci-sharptypst.yml`
owns everything that needs the compiler; `.github/workflows/ci-text.yml` builds, tests and packs the
three packages that do not.

`ci-sharptypst.yml`:

| Job | Runner | Steps |
| --- | --- | --- |
| `native` | `windows-latest`, `windows-11-arm`, `ubuntu-latest`, `ubuntu-24.04-arm`, `macos-15-intel` | `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test`, `cargo build --release`, upload artifact. The `linux-musl-x64` entry installs `musl-tools`, builds for `x86_64-unknown-linux-musl` with `-C linker=musl-gcc`, and uploads the same `libsharp_typst.so` name |
| `managed` | `ubuntu-latest` | `dotnet restore`, `dotnet build -c Release`, `dotnet test tests/Text.SharpTypst.Tests` (no natives needed) |
| `integration` | matrix over the five host RIDs | download the matching native artifact, `dotnet restore`, `dotnet build`, `dotnet test tests/Text.SharpTypst.Integration.Tests` against the real compiler |
| `musl` | `ubuntu-latest` | publish the sample for `linux-musl-x64` self-contained and run it inside `mcr.microsoft.com/dotnet/runtime-deps:*-alpine`: the glibc build loads on the runner and fails there, which is the failure that matters |
| `aot` | `ubuntu-latest` | publish `samples/Text.SharpTypst.Samples` with `PublishAot=true` and run it |
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
| `facturx` | `ubuntu-latest` | builds, runs the Factur-X unit tests and the samples, then extracts the attachment from each PDF and validates it against the **official schema** with the FNFE-MPE reference library, plus PDF/A-3B with veraPDF. That validator, not the string assertions, is what the conformance claim rests on |
| `text` | `ubuntu-latest` | restore, build, both unit suites (neither needs a native library), `dotnet pack` for `M3scalinum.Text.Pdf` and `M3scalinum.Text.Pdf.FactureX` |

The render validation needs the compiler, which is why `facturx` builds one here: keeping the pair
self-contained costs twelve minutes of Rust in one job instead of coupling the two workflows.

`tests/verify-package.py` is the guard that would have caught two shipped defects: a package with no
`runtimes/` entry (the native job had not run) and a dependency on `M3scalinum.Text.SharpTypst.Native`, which is never
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
| `release-text.yml` | `v0.51.2` | `M3scalinum.Text.Pdf` and `M3scalinum.Text.Pdf.FactureX`, revision from the fourth component, core pinned to the newest published `M3scalinum.Text.SharpTypst` |

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
build's own changelog section — `CHANGELOG-SharpTypst.md` or `CHANGELOG-Text.md` — and attaches the
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
| `dotnet test tests/Text.SharpTypst.Tests` | Unit tests, no native dependency |
| `cargo test --manifest-path native/sharp-typst/Cargo.toml` | ABI and exporter tests |
| `dotnet test -p:BuildNative=true` | Full build including Cargo, then all tests |
| `dotnet run --project samples/Text.SharpTypst.Samples` | Generates the sample outputs into `artifacts/samples/` |

## 10. Third-party notices

M3scalinum.Text.SharpTypst is Apache-2.0, the same licence as Typst. Sharing it is the simplest correct choice: the
library embeds the compiler, so a different licence would mean tracking two sets of obligations for
one binary. Apache-2.0 also carries a patent grant (section 3) and a trademark clause (section 6),
both of which matter for a binding that ships someone else's compiler.

Three files travel with the source and inside the NuGet package:

| File | Contents |
| --- | --- |
| `LICENSE` | The Apache License, Version 2.0, verbatim |
| `NOTICE` | The M3scalinum.Text.SharpTypst copyright, and the statement that the product includes Typst |
| `THIRD-PARTY-NOTICES.md` | The notices Typst itself carries for the components it embeds, reproduced verbatim — required by section 4(d) |

`THIRD-PARTY-NOTICES.md` is not generated: it is Typst's own `NOTICE`, copied at each Typst upgrade.
A generated per-crate list from `Cargo.lock` would be more complete but would not satisfy 4(d), which
asks for the *upstream* notices, not a summary of them.
