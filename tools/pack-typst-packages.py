#!/usr/bin/env python3
"""Packs the managed compiler package and its seven per-RID satellites.

`M3scalinum.Text.Typst` no longer carries a native library: the seven `sharp_typst` binaries ship as
`M3scalinum.Text.Typst.Native.<Rid>`, and a consumer adds the two packages their runtime needs
(Spec 08 §6). NuGet cannot pick the native for a consumer — dependency groups are per target
framework, never per RID — so the split exists only if the packages do, and they have to be produced
by one command that knows the set.

`src/Text.Typst.Native.Package` is one project parameterised by `NativeRid`: the same file names,
the same `runtimes/<rid>/native/` layout and the same `Exists(...)` conditions the single package
used, packed seven times. Seven copied csproj files would be seven places to forget.

Usage: pack-typst-packages.py --output DIR [--revision N] [--configuration Release] [--no-build]
       pack-typst-packages.py --list

`--no-build` passes `--no-build` to every `dotnet pack`, for a workflow that built the solution
first. Stdlib only, like the other scripts here: it runs on a bare CPython in CI.
"""

import argparse
import subprocess
import sys
from pathlib import Path

#: The RIDs the compiler wrapper has a native library for, in the order the specification lists them.
RIDS = [
    "win-x64",
    "win-arm64",
    "linux-x64",
    "linux-arm64",
    "linux-musl-x64",
    "osx-arm64",
    "osx-x64",
]

#: Packed before the rest: the compiler package's only NuGet dependency, and no package resolves
#: against a feed that does not hold it.
MANAGED = ["src/Text.Documents", "src/Text.Typst"]

#: One project, one package per RID.
SATELLITE = "src/Text.Typst.Native.Package"

ROOT = Path(__file__).resolve().parent.parent


def package_id(rid: str) -> str:
    """The satellite package id for a RID: `linux-musl-x64` -> `LinuxMuslX64`.

    The same rule `src/Text.Typst.Native.Package` spells out in its `NativePackageSuffix` table, and
    `main` checks the packed files against it: a suffix that drifts there is a satellite nobody can
    find on the feed, so it fails here instead.
    """
    return "M3scalinum.Text.Typst.Native." + "".join(
        part[0].upper() + part[1:] for part in rid.split("-")
    )


def require_dotnet() -> None:
    """Fails early when `dotnet` is not on PATH, rather than on the first of nine packs."""
    try:
        subprocess.run(["dotnet", "--version"], check=True, capture_output=True)
    except (OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"dotnet is not available: {error}") from error


def pack(project: str, output: Path, configuration: str, revision: int, no_build: bool,
         extra: list[str] | None = None) -> None:
    """Runs one `dotnet pack` from the repository root, so relative paths resolve as they do in CI."""
    command = [
        "dotnet", "pack", project,
        "-c", configuration,
        "-o", str(output),
        f"-p:BuildRevision={revision}",
    ]

    if no_build:
        command.append("--no-build")

    command.extend(extra or [])

    print(f"== {' '.join(command)}", flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def main(arguments: argparse.Namespace) -> int:
    output = Path(arguments.output).resolve()
    output.mkdir(parents=True, exist_ok=True)

    if not arguments.no_build:
        require_dotnet()

    for project in MANAGED:
        pack(project, output, arguments.configuration, arguments.revision, arguments.no_build)

    for rid in RIDS:
        pack(
            SATELLITE,
            output,
            arguments.configuration,
            arguments.revision,
            arguments.no_build,
            extra=[f"-p:NativeRid={rid}"],
        )

    packages = sorted(output.glob("*.nupkg"))

    # A satellite is one RID and no assembly; a pack that silently dropped one — no `NativeRid`, an
    # unknown RID — would leave eight files where nine were expected, and the missing one is the
    # only thing that makes the install work on that platform.
    print(f"\n{len(packages)} package(s) in {output}")

    for package in packages:
        print(f"  {package.name:<58}{package.stat().st_size / 1e6:>8.2f} MB")

    expected = len(MANAGED) + len(RIDS)
    if len(packages) != expected:
        print(f"expected {expected} packages, found {len(packages)}", file=sys.stderr)
        return 1

    missing = [
        package_id(rid) for rid in RIDS
        if not any(package.name.startswith(package_id(rid) + ".") for package in packages)
    ]

    if missing:
        print(f"packed no package for {', '.join(missing)}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", help="directory to write the .nupkg and .snupkg files to")
    parser.add_argument("--revision", type=int, default=0, help="BuildRevision, the fourth version component")
    parser.add_argument("--configuration", default="Release", help="build configuration (default Release)")
    parser.add_argument("--no-build", action="store_true", help="pass --no-build to every dotnet pack")
    parser.add_argument("--list", action="store_true", help="print the package set and exit")
    parsed = parser.parse_args()

    if parsed.list:
        print(f"{'M3scalinum.Text.Documents':<45}src/Text.Documents")
        print(f"{'M3scalinum.Text.Typst':<45}src/Text.Typst")
        for rid in RIDS:
            print(f"{package_id(rid):<45}{SATELLITE} -p:NativeRid={rid}")
        sys.exit(0)

    if not parsed.output:
        parser.error("--output is required")

    sys.exit(main(parsed))
