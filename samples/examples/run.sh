#!/usr/bin/env bash
# Builds one example against the package it names, in a console project outside the repository, so
# the .nupkg on the local feed is the only way the example can see the library. A project reference
# here would prove nothing: the item exists to catch a package that is missing an assembly, a
# dependency or a native library.
#
# `M3scalinum.Text.Typst` is managed only: the native compiler ships in a per-RID satellite package
# (`M3scalinum.Text.Typst.Native.WinX64` and its six siblings, Spec 08 §6). This script restores the
# satellite for the RID the example runs on, so the two lines a consumer writes are the two lines
# the examples exercise — and a feed without that satellite fails here rather than at the first
# compile of a package nobody can use.
#
# usage: run.sh <example> <package id> <version> <feed directory> [--build-only | --self-contained <rid>]
#
#   <example>          directory name under this one, holding Program.cs (and fixtures/)
#   <package id>       the package to reference, for example M3scalinum.Text.Docx
#   <version>          the exact version to restore, read from the .nupkg file name
#   <feed directory>   the folder holding the .nupkg files packed by the same CI run
#   --build-only       restore and compile, do not run (all the proof a Windows host can offer for a
#                      package whose native library is a Linux .so)
#   --self-contained <rid>
#                      publish a self-contained application for <rid> instead of running it, so the
#                      package can be executed in a container of that runtime (the `musl` job of
#                      ci-text.yml runs the five native packages inside Alpine this way). The output
#                      directory is $M3SCALINUM_PUBLISH_DIR, default $work/publish.
set -euo pipefail

if [ "$#" -lt 4 ]; then
    echo "usage: $0 <example> <package id> <version> <feed directory> [--build-only | --self-contained <rid>]" >&2
    exit 2
fi

example=$1
package=$2
version=$3
feed=$4
mode=${5:-}

here=$(cd "$(dirname "$0")" && pwd)

# The RID this example runs on: the one `--self-contained` names, or this host's own.
host_rid() {
    local rid=""

    if command -v dotnet >/dev/null 2>&1; then
        rid=$(dotnet --info 2>/dev/null | awk -F': *' '/^ *RID:/ { print $2; exit }')
    fi

    if [ -z "$rid" ]; then
        local machine
        machine=$(uname -m)
        machine=${machine/x86_64/x64}
        machine=${machine/aarch64/arm64}

        case "$(uname -s)" in
            MINGW*|MSYS*|CYGWIN*) rid="win-$machine" ;;
            Darwin) rid="osx-$machine" ;;
            *) rid="linux-$machine" ;;
        esac
    fi

    # A framework-dependent runtime reports the portable RID on Alpine, where the glibc satellite
    # cannot be loaded. The libc decides, exactly as `NativeLibraryResolver` decides it at load.
    if [ "${rid#linux-}" != "$rid" ] && [ "${rid#linux-musl-}" = "$rid" ]; then
        if [ -f /etc/alpine-release ] || [ -e /lib/ld-musl-x86_64.so.1 ] || [ -e /lib/ld-musl-aarch64.so.1 ]; then
            rid="linux-musl-${rid#linux-}"
        fi
    fi

    printf '%s' "$rid"
}

# The suffix a satellite package carries for a RID: `linux-musl-x64` -> `LinuxMuslX64`.
rid_suffix() {
    case "$1" in
        win-x64) echo WinX64 ;;
        win-arm64) echo WinArm64 ;;
        linux-x64) echo LinuxX64 ;;
        linux-arm64) echo LinuxArm64 ;;
        linux-musl-x64) echo LinuxMuslX64 ;;
        linux-musl-arm64) echo LinuxMuslArm64 ;;
        osx-arm64) echo OsxArm64 ;;
        osx-x64) echo OsxX64 ;;
        *) return 1 ;;
    esac
}

# The satellite package a package's native library ships in, for a RID. Only the compiler wrapper is
# split by RID (Spec 08 §6); every other package carries its natives in the same .nupkg and this
# prints nothing. `M3scalinum.Text.Templates` is named beside it because its example renders a
# layout: `Template.RenderDocument` takes a `TypstCompiler`, so a consumer of the templates package
# that compiles installs the same second package a consumer of the compiler does.
satellite_package() {
    local id=$1 rid=$2

    case "$id" in
        M3scalinum.Text.Typst|M3scalinum.Text.Templates|M3scalinum.Text.Docx|M3scalinum.Text.Xlsx) ;;
        *) return 0 ;;
    esac

    case "$rid" in
        win-x64) echo M3scalinum.Text.Typst.Native.WinX64 ;;
        win-arm64) echo M3scalinum.Text.Typst.Native.WinArm64 ;;
        linux-x64) echo M3scalinum.Text.Typst.Native.LinuxX64 ;;
        linux-arm64) echo M3scalinum.Text.Typst.Native.LinuxArm64 ;;
        linux-musl-x64) echo M3scalinum.Text.Typst.Native.LinuxMuslX64 ;;
        osx-arm64) echo M3scalinum.Text.Typst.Native.OsxArm64 ;;
        osx-x64) echo M3scalinum.Text.Typst.Native.OsxX64 ;;
        *)
            echo "no native package is published for $rid: the compiler wrapper has satellites for" \
                 "win-x64, win-arm64, linux-x64, linux-arm64, linux-musl-x64, osx-arm64 and osx-x64" >&2
            return 1
            ;;
    esac
}

# The five format packages carry no native library since the split (Spec 08 §6): a consumer adds
# `M3scalinum.Text.<Format>.Native.<Rid>` beside the managed package, exactly as a consumer of the
# compiler adds the compiler's satellite. Docx and Xlsx render through the compiler, so their example
# needs both.
format_satellite() {
    local id=$1 rid=$2 suffix

    case "$id" in
        M3scalinum.Text.Barcodes) suffix=Barcodes ;;
        M3scalinum.Text.Docx)     suffix=Docx ;;
        M3scalinum.Text.Ocr)      suffix=Ocr ;;
        M3scalinum.Text.Pdf)      suffix=Pdf ;;
        M3scalinum.Text.Xlsx)     suffix=Xlsx ;;
        *) return 0 ;;
    esac

    printf 'M3scalinum.Text.%s.Native.%s' "$suffix" "$(rid_suffix "$rid")"
}

rid=${6:-}
if [ -z "$rid" ]; then
    rid=$(host_rid)
fi

satellite=$(satellite_package "$package" "$rid")
format_native=$(format_satellite "$package" "$rid")

# The generated project is written outside the repository, so it inherits neither
# Directory.Build.props nor the central package versions. The path is converted for the native
# `dotnet` on Windows, which cannot open an MSYS `/tmp/...` path.
scratch="${TMPDIR:-/tmp}"

if command -v cygpath >/dev/null 2>&1; then
    scratch=$(cygpath -m "$scratch")
fi

work="$scratch/m3scalinum-examples/$example"

if [ ! -f "$feed/$package.$version.nupkg" ]; then
    echo "the feed $feed holds no $package.$version.nupkg" >&2
    exit 1
fi

# Each satellite's version is read from the feed, never derived from <version>: the compiler's
# travels with the compiler train (0.15.1.x) while a format satellite carries its managed package's
# version (0.1.x), and a Templates example restores 0.1.0 of a package that pins 0.15.1.
satellite_references=""
for required_satellite in $satellite $format_native; do
    required_nupkg=$(ls "$feed/$required_satellite".*.nupkg 2>/dev/null | head -n 1 || true)

    if [ -z "$required_nupkg" ]; then
        # The package under test is here and a library it needs is not: a green run would prove
        # nothing, because the example would fail at its first compile with a message about a feed.
        echo "the feed $feed holds no $required_satellite.<version>.nupkg: pack" \
             "src/Text.Typst.Native.Package or src/Text.Format.Native.Package for RID $rid" \
             "alongside the other packages (tools/pack-typst-packages.py and" \
             "tools/pack-format-packages.py pack the sets)" >&2
        exit 1
    fi

    required_version=$(basename "$required_nupkg")
    required_version=${required_version#"$required_satellite".}
    required_version=${required_version%.nupkg}
    satellite_references="$satellite_references    <PackageReference Include=\"$required_satellite\" Version=\"$required_version\" />
"
done

rm -rf "$work"
mkdir -p "$work"
cp -R "$here/$example/." "$work/"
rm -f "$work/README.md"

# The project is generated rather than committed: it must not inherit the repository's
# Directory.Build.props, its central package management, or any ProjectReference. A fresh package
# cache per run keeps a package left over from an earlier build from standing in for this one.
export NUGET_PACKAGES="$work/.nuget"

# The satellite is a second PackageReference, the way a consumer writes it: NuGet cannot choose it
# from the RID, so the project has to name it (Spec 08 §6).


cat > "$work/Example.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="$package" Version="$version" />
${satellite_references}
  </ItemGroup>

  <ItemGroup>
    <None Include="fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
EOF

echo "== $example: $package $version from $feed (RID $rid${satellite:+, plus $satellite}${format_native:+, plus $format_native})"

# The sources are a file rather than `--source` arguments: a local folder and nuget.org, the first
# for the package under test and the second for its published dependencies. The `<clear/>` keeps a
# machine-level source from supplying a same-named package that is older or newer.
cat > "$work/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="examples" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

dotnet restore "$work/Example.csproj"
dotnet build "$work/Example.csproj" -c Release --no-restore

if [ "$mode" = "--build-only" ]; then
    echo "== $example: compiled from $package.$version.nupkg (not run)"
    exit 0
fi

if [ "$mode" = "--self-contained" ]; then
    rid=${6:-}
    if [ -z "$rid" ]; then
        echo "--self-contained needs a RID, for example linux-musl-x64" >&2
        exit 2
    fi
    publish=${M3SCALINUM_PUBLISH_DIR:-$work/publish}

    # `publish` restores again with the RID: the restore above resolved the portable assets only,
    # and the RID decides which `runtimes/<rid>/native/` entry of the package is laid down next to
    # the executable. No `--no-restore` here for that reason.
    dotnet publish "$work/Example.csproj" -c Release -r "$rid" --self-contained -o "$publish"

    echo "== $example: published self-contained for $rid into $publish"
    exit 0
fi

dotnet run --project "$work/Example.csproj" -c Release --no-build
