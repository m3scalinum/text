#!/usr/bin/env python3
"""Packs the five format packages and their forty per-RID satellites.

`M3scalinum.Text.Barcodes`, `.Docx`, `.Ocr`, `.Pdf` and `.Xlsx` no longer carry a native library:
each crate ships as `M3scalinum.Text.<Format>.Native.<Rid>`, and a consumer adds the two packages
their runtime needs (Spec 08 §6). NuGet cannot pick the native for a consumer — dependency groups are
per target framework, never per RID — so the split exists only if the packages do, and they have to
be produced by one command that knows the set.

`src/Text.Format.Native.Package` is one project parameterised by `NativeFormat` and `NativeRid`:
five managed packages and forty satellites, from two properties. Forty-five copied csproj files would
be forty-five places to forget.

The build runs once per format and the pack eight times, which is not an optimisation: `dotnet pack
--no-build` reads the assets file of the last restore, and that file only knows the managed package a
satellite depends on if the project was *built* with that format. Packing a RID before building its
format produces a satellite with no dependency at all.

Usage: pack-format-packages.py --output DIR [--revision N] [--configuration Release] [--no-build]
       pack-format-packages.py --list

`--no-build` skips the per-format build, for a workflow that already ran it with the same properties.
Stdlib only, like the other scripts here: it runs on a bare CPython in CI.
"""

import argparse
import subprocess
import sys
from pathlib import Path

#: The five crates that ship a library of their own, and the managed package each belongs to.
FORMATS = ["barcodes", "docx", "ocr", "pdf", "xlsx"]

#: The RIDs every format has a satellite for, in the order the specification lists them.
RIDS = [
    "win-x64",
    "win-arm64",
    "linux-x64",
    "linux-arm64",
    "linux-musl-x64",
    "linux-musl-arm64",
    "osx-arm64",
    "osx-x64",
]

#: One project, one package per (format, RID).
SATELLITE = "src/Text.Format.Native.Package"

ROOT = Path(__file__).resolve().parent.parent


def suffix_of(fmt: str) -> str:
    """`barcodes` -> `Barcodes`, the folder and the package's segment."""
    return fmt[0].upper() + fmt[1:]


def package_id(fmt: str, rid: str) -> str:
    """The satellite package id: `("pdf", "osx-arm64")` -> `M3scalinum.Text.Pdf.Native.OsxArm64`.

    The same rule `src/Text.Format.Native.Package` spells out in its `NativePackageSuffix` table and
    `tests/verify-package.py` in `format_satellite_id`: a suffix that drifts is a satellite nobody can
    find on the feed, so it fails here instead.
    """
    return f"M3scalinum.Text.{suffix_of(fmt)}.Native." + "".join(
        part[0].upper() + part[1:] for part in rid.split("-")
    )


def run(command: list[str]) -> None:
    print(f"== {' '.join(command)}", flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def main(arguments: argparse.Namespace) -> int:
    formats = arguments.formats
    output = Path(arguments.output).resolve()
    output.mkdir(parents=True, exist_ok=True)

    try:
        subprocess.run(["dotnet", "--version"], check=True, capture_output=True)
    except (OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"dotnet is not available: {error}") from error

    # The managed packages first: a satellite depends on its own, and no feed resolves against a
    # package it does not hold.
    for fmt in formats:
        project = f"src/Text.{suffix_of(fmt)}"
        command = ["dotnet", "pack", project, "-c", arguments.configuration, "-o", str(output),
                   f"-p:BuildRevision={arguments.revision}"]
        run(command + (["--no-build"] if arguments.no_build else []))

    for fmt in formats:
        if not arguments.no_build:
            # The assets file has to know the managed package this satellite pins.
            run(["dotnet", "build", SATELLITE, "-c", arguments.configuration,
                 f"-p:NativeFormat={fmt}"])

        for rid in RIDS:
            run(["dotnet", "pack", SATELLITE, "-c", arguments.configuration, "-o", str(output),
                 "--no-build", f"-p:BuildRevision={arguments.revision}",
                 f"-p:NativeFormat={fmt}", f"-p:NativeRid={rid}"])

    packages = sorted(package for package in output.glob("*.nupkg"))
    expected = len(formats) + len(formats) * len(RIDS)

    print(f"\n{len(packages)} package(s) in {output}")
    for package in packages:
        print(f"  {package.name:<62}{package.stat().st_size / 1e6:>8.2f} MB")

    # A satellite is one RID and no assembly; a pack that silently dropped one — no `NativeRid`, an
    # unknown RID — would leave forty-nine files where fifty were expected, and the missing one is
    # the only thing that makes the install work on that platform.
    missing = [
        package_id(fmt, rid)
        for fmt in formats
        for rid in RIDS
        if not any(p.name.startswith(package_id(fmt, rid) + ".") for p in packages)
    ]

    if missing:
        print(f"packed no package for: {', '.join(missing)}", file=sys.stderr)
        return 1

    if len(packages) != expected:
        print(f"expected {expected} packages, found {len(packages)}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", help="directory to write the .nupkg files to")
    parser.add_argument("--revision", type=int, default=0, help="BuildRevision, the fourth version component")
    parser.add_argument("--configuration", default="Release", help="build configuration (default Release)")
    parser.add_argument("--formats", default=",".join(FORMATS),
                        help="comma-separated subset of barcodes,docx,ocr,pdf,xlsx (default: all five)")
    parser.add_argument("--no-build", action="store_true",
                        help="skip the per-format build; only with a build of the same properties")
    parser.add_argument("--list", action="store_true", help="print the package set and exit")
    parsed = parser.parse_args()

    if parsed.list:
        for fmt in FORMATS:
            print(f"{'M3scalinum.Text.' + suffix_of(fmt):<50}src/Text.{suffix_of(fmt)}")
        for fmt in FORMATS:
            for rid in RIDS:
                print(f"{package_id(fmt, rid):<50}{SATELLITE} -p:NativeFormat={fmt} -p:NativeRid={rid}")
        sys.exit(0)

    parsed.formats = [fmt.strip() for fmt in parsed.formats.split(",") if fmt.strip()]
    unknown = [fmt for fmt in parsed.formats if fmt not in FORMATS]

    if unknown:
        parser.error(f"unknown format(s): {', '.join(unknown)}; expected a subset of {', '.join(FORMATS)}")

    if not parsed.output:
        parser.error("--output is required")

    sys.exit(main(parsed))
