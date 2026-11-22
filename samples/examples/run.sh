#!/usr/bin/env bash
# Builds one example against the package it names, in a console project outside the repository, so
# the .nupkg on the local feed is the only way the example can see the library. A project reference
# here would prove nothing: the item exists to catch a package that is missing an assembly, a
# dependency or a native library.
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
#                      ci-text.yml runs the four native packages inside Alpine this way). The output
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

rm -rf "$work"
mkdir -p "$work"
cp -R "$here/$example/." "$work/"
rm -f "$work/README.md"

# The project is generated rather than committed: it must not inherit the repository's
# Directory.Build.props, its central package management, or any ProjectReference. A fresh package
# cache per run keeps a package left over from an earlier build from standing in for this one.
export NUGET_PACKAGES="$work/.nuget"

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
  </ItemGroup>

  <ItemGroup>
    <None Include="fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
EOF

echo "== $example: $package $version from $feed"

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
