#!/usr/bin/env python3
"""Verifies a packed M3scalinum.Text.Typst .nupkg before it is published.

Six failures this catches, four of which shipped once:

- a package with no `runtimes/` entry, because the native job had not run;
- a dependency on `M3scalinum.Text.Typst.Native`, which is never published, so every consumer got NU1101;
- a missing licence file, which breaks the Apache-2.0 obligations;
- a satellite package (`M3scalinum.Text.Typst.DependencyInjection`, `M3scalinum.Text.Templates`) built against a
  different `M3scalinum.Text.Typst` than the one packed in the same run — the version is derived from the
  referenced project, and nothing else forces the two to agree;
- a managed package that still carries `runtimes/<rid>/native/` — the natives belong to the per-RID
  satellites, and a release that ships both charges every consumer for both;
- a native satellite with the wrong RID, no library, or an assembly, and a set that is missing one of
  the seven.

Usage: verify-package.py <package.nupkg>... [--native]
       verify-package.py --selftest

Pass the whole package set to check the set as well: with a `M3scalinum.Text.Typst` among the
arguments, all seven satellites have to be there, and each has to pin that exact core version. A
single path is checked on its own, which is what the `pack` jobs of the CI workflows do.

`--native` additionally requires every RID library of a package whose natives a workflow may not
have built; pass it wherever those libraries exist.

`--selftest` fabricates packages in a temporary directory and asserts this script's own verdicts on
them, including that a package without its `linux-musl-arm64` library is refused and that a set
missing one satellite is refused. It is run by the `extras` job of `ci-text.yml`, so the
requirements below cannot be deleted without CI going red.
"""

import contextlib
import io
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

# The compiler package's RIDs, and the satellite that carries each one's library. It gets no
# `linux-musl-arm64` build yet: its Cargo.toml vendors OpenSSL for the x86_64 musl triple alone, so
# the target needs a vendoring change this list would then have one more line for (08 §6, 09 §11).
# macOS is two thin dylibs, one per architecture, rather than one universal file: a consumer copies
# out the RID their build targets.
RIDS = {
    "win-x64": "sharp_typst.dll",
    "win-arm64": "sharp_typst.dll",
    "linux-x64": "libsharp_typst.so",
    "linux-arm64": "libsharp_typst.so",
    "linux-musl-x64": "libsharp_typst.so",
    "osx-arm64": "libsharp_typst.dylib",
    "osx-x64": "libsharp_typst.dylib",
}

#: The package id that carries one RID's library: `linux-musl-x64` -> `M3scalinum.Text.Typst.Native.LinuxMuslX64`.
#: The same rule `src/Text.Typst.Native.Package` spells out in its `NativePackageSuffix` table and
#: `tools/pack-typst-packages.py` in `package_id`; a drift between the three is a satellite a
#: consumer cannot find, so this is where it goes red.
NATIVE_SATELLITES = {
    rid: f"M3scalinum.Text.Typst.Native.{''.join(part.capitalize() for part in rid.split('-'))}"
    for rid in RIDS
}

REQUIRED_FILES = [
    "LICENSE",
    "NOTICE",
    "THIRD-PARTY-NOTICES.md",
    "README.md",
    "icon.png",
]

REQUIRED_ASSEMBLIES = [
    "lib/net11.0/M3scalinum.Text.Typst.dll",
    "lib/net11.0/M3scalinum.Text.Typst.Native.dll",
]

FORBIDDEN_DEPENDENCY = "M3scalinum.Text.Typst.Native"

CORE_PACKAGE = "M3scalinum.Text.Typst"
SATELLITE_ASSEMBLIES = {
    "lib/net11.0/M3scalinum.Text.Typst.DependencyInjection.dll",
    "lib/net11.0/M3scalinum.Text.Templates.dll",
    "lib/net11.0/M3scalinum.Text.Mcp.dll",
    "lib/net11.0/M3scalinum.Text.Docx.dll",
    "lib/net11.0/M3scalinum.Text.Xlsx.dll",
}

# Packages that carry a Rust library of their own rather than the compiler. They ship eight RIDs —
# the compiler's seven plus `linux-musl-arm64`, which the compiler does not have. Neither musl RID can
# be a cdylib: the CI job builds each crate's staticlib and links it with `musl-gcc`, the compiler's
# recipe. A missing native job is caught here instead of by a consumer's DllNotFoundException on
# Alpine, x64 or arm64.
#: The five packages that carry a Rust library of their own, and the library each RID's satellite
#: ships. Since the split (Spec 08 §6) the managed package carries none of them: a consumer adds
#: `M3scalinum.Text.<Format>` and `M3scalinum.Text.<Format>.Native.<Rid>`, exactly as a consumer of
#: the compiler adds the compiler's satellite.
FORMAT_LIBRARIES = {
    "Barcodes": {
        "win-x64": "sharp_text_barcodes.dll",
        "win-arm64": "sharp_text_barcodes.dll",
        "linux-x64": "libsharp_text_barcodes.so",
        "linux-arm64": "libsharp_text_barcodes.so",
        "linux-musl-x64": "libsharp_text_barcodes.so",
        "linux-musl-arm64": "libsharp_text_barcodes.so",
        "osx-arm64": "libsharp_text_barcodes.dylib",
        "osx-x64": "libsharp_text_barcodes.dylib",
    },
    "Docx": {
        "win-x64": "sharp_text_docx.dll",
        "win-arm64": "sharp_text_docx.dll",
        "linux-x64": "libsharp_text_docx.so",
        "linux-arm64": "libsharp_text_docx.so",
        "linux-musl-x64": "libsharp_text_docx.so",
        "linux-musl-arm64": "libsharp_text_docx.so",
        "osx-arm64": "libsharp_text_docx.dylib",
        "osx-x64": "libsharp_text_docx.dylib",
    },
    "Ocr": {
        "win-x64": "sharp_text_ocr.dll",
        "win-arm64": "sharp_text_ocr.dll",
        "linux-x64": "libsharp_text_ocr.so",
        "linux-arm64": "libsharp_text_ocr.so",
        "linux-musl-x64": "libsharp_text_ocr.so",
        "linux-musl-arm64": "libsharp_text_ocr.so",
        "osx-arm64": "libsharp_text_ocr.dylib",
        "osx-x64": "libsharp_text_ocr.dylib",
    },
    "Pdf": {
        "win-x64": "sharp_text_pdf.dll",
        "win-arm64": "sharp_text_pdf.dll",
        "linux-x64": "libsharp_text_pdf.so",
        "linux-arm64": "libsharp_text_pdf.so",
        "linux-musl-x64": "libsharp_text_pdf.so",
        "linux-musl-arm64": "libsharp_text_pdf.so",
        "osx-arm64": "libsharp_text_pdf.dylib",
        "osx-x64": "libsharp_text_pdf.dylib",
    },
    "Xlsx": {
        "win-x64": "sharp_text_xlsx.dll",
        "win-arm64": "sharp_text_xlsx.dll",
        "linux-x64": "libsharp_text_xlsx.so",
        "linux-arm64": "libsharp_text_xlsx.so",
        "linux-musl-x64": "libsharp_text_xlsx.so",
        "linux-musl-arm64": "libsharp_text_xlsx.so",
        "osx-arm64": "libsharp_text_xlsx.dylib",
        "osx-x64": "libsharp_text_xlsx.dylib",
    },
}

#: The assembly each managed format package must carry, keyed by the format suffix.
FORMAT_ASSEMBLIES = {
    f"lib/net11.0/M3scalinum.Text.{suffix}.dll": suffix for suffix in FORMAT_LIBRARIES
}

#: The eight RIDs every format has a satellite for.
FORMAT_RIDS = list(next(iter(FORMAT_LIBRARIES.values())))

#: `("Barcodes", "osx-arm64")` -> `M3scalinum.Text.Barcodes.Native.OsxArm64`. The same rule
#: `src/Text.Format.Native.Package` spells out in its `NativePackageSuffix` table and
#: `tools/pack-format-packages.py` in `package_id`; a drift between the three is a satellite a
#: consumer cannot find, so it goes red here.
def format_satellite_id(suffix: str, rid: str) -> str:
    return f"M3scalinum.Text.{suffix}.Native." + "".join(
        part[0].upper() + part[1:] for part in rid.split("-")
    )


def _text(root: ET.Element, name: str) -> str | None:
    """The text of the first element with local name `name`, whatever namespace the nuspec uses."""
    return next((element.text for element in root.iter() if element.tag.endswith("}" + name)), None)


def _native_rids(names: set[str]) -> set[str]:
    """The RIDs a package carries a `runtimes/<rid>/native/` entry for."""
    rids = set()

    for name in names:
        parts = name.split("/")

        if len(parts) == 4 and parts[0] == "runtimes" and parts[2] == "native":
            rids.add(parts[1])

    return rids


def check(path: str, require_native: bool = False) -> dict:
    """Checks one packed file, and reports what it is so `main` can judge the set afterwards.

    The verdict is a dict: `problems` (empty when clean), `summary` (the line a clean package
    prints), `kind` (`core`, `native-satellite`, `satellite` or `other`), `version`, `pins` (the
    exact core version a satellite declares) and `rid` (the RID a native satellite carries).
    """
    problems: list[str] = []

    with zipfile.ZipFile(path) as package:
        names = set(package.namelist())
        nuspec = next((name for name in names if name.endswith(".nuspec") and "/" not in name), None)

        ident = version = pins = None
        pins_core = False
        declared_dependencies: dict[str, str] = {}

        if nuspec is None:
            problems.append("no .nuspec in the package")
        else:
            root = ET.fromstring(package.read(nuspec))
            ident = _text(root, "id")
            version = _text(root, "version")

            # The nuspec lives in a default XML namespace, so match on the local name.
            for element in root.iter():
                if not element.tag.endswith("dependency"):
                    continue

                declared_dependencies[element.get("id", "")] = element.get("version", "")

                if element.get("id") == FORBIDDEN_DEPENDENCY:
                    problems.append(
                        f"depends on {FORBIDDEN_DEPENDENCY}, which is never published"
                    )

                if element.get("id") == CORE_PACKAGE:
                    pins_core = True
                    declared = element.get("version", "")
                    lower = re.sub(r"^[\[(]", "", declared).split(",")[0].strip()

                    # The satellite must pin an exact core version, but not necessarily its own:
                    # M3scalinum.Text.Templates follows the suite (0.1.x) while the compiler
                    # wrapper it builds on follows Typst (0.15.1.x). NuGet writes an exact pin as a
                    # bare version, and a range as `[1.0.0, )` or `(1.0.0, 2.0.0)`.
                    if not lower or "," in declared or declared.startswith("("):
                        problems.append(
                            f"depends on {CORE_PACKAGE} {declared}; name the core version the package was built against"
                        )
                    else:
                        pins = lower

        rid_of_satellite = next(
            (
                rid for rid, package_id in NATIVE_SATELLITES.items()
                if ident is not None and package_id.casefold() == ident.casefold()
            ),
            None,
        )

        # A format satellite is identified by its package id: `M3scalinum.Text.Pdf.Native.OsxArm64`.
        format_satellite = None
        if ident:
            match = re.fullmatch(
                r"M3scalinum\.Text\.(Barcodes|Docx|Ocr|Pdf|Xlsx)\.Native\.([A-Za-z0-9]+)", ident
            )
            if match:
                suffix, rid_suffix = match.groups()
                rid = next(
                    (candidate for candidate in FORMAT_RIDS
                     if "".join(part[0].upper() + part[1:] for part in candidate.split("-")) == rid_suffix),
                    None,
                )
                if rid is None:
                    problems.append(f"unknown RID suffix '{rid_suffix}' in {ident}")
                else:
                    format_satellite = (suffix, rid)

        # Only the managed package carries the compiler; the format packages do not, and the native
        # satellites carry nothing but one library.
        is_core = ident == CORE_PACKAGE or "lib/net11.0/M3scalinum.Text.Typst.dll" in names
        is_satellite = bool(SATELLITE_ASSEMBLIES & names)
        format_package = next(
            (suffix for assembly, suffix in FORMAT_ASSEMBLIES.items() if assembly in names), None
        )

        kind = "other"
        summary = "ok"

        if is_core:
            kind = "core"

            # The split is the point of the package set, so this is a checked property and not a
            # convention: a release that packed the natives back into the managed package would
            # double the download of every consumer who also adds a satellite.
            for rid in sorted(_native_rids(names)):
                problems.append(
                    f"carries runtimes/{rid}/native/, which ships in "
                    f"{NATIVE_SATELLITES.get(rid, 'a satellite')}: the managed package carries no native library"
                )

            for required in REQUIRED_FILES + REQUIRED_ASSEMBLIES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            summary = f"ok (managed only, {len(REQUIRED_FILES)} required files)"

        elif rid_of_satellite is not None:
            kind = "native-satellite"
            expected = f"runtimes/{rid_of_satellite}/native/{RIDS[rid_of_satellite]}"

            if expected not in names:
                problems.append(f"missing native asset: {expected}")

            for rid in sorted(_native_rids(names) - {rid_of_satellite}):
                problems.append(
                    f"carries runtimes/{rid}/native/, which ships in {NATIVE_SATELLITES[rid]}"
                )

            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            assemblies = sorted(
                name for name in names if name.startswith("lib/") and name.endswith(".dll")
            )
            if assemblies:
                problems.append(f"carries {assemblies[0]}: a native satellite ships no assembly")

            if not pins_core:
                problems.append(f"a satellite package must depend on {CORE_PACKAGE}")

            summary = f"ok ({rid_of_satellite} satellite, core {pins or 'unpinned'})"

        elif format_satellite is not None:
            kind = "format-satellite"
            suffix, satellite_rid = format_satellite
            expected = f"runtimes/{satellite_rid}/native/{FORMAT_LIBRARIES[suffix][satellite_rid]}"

            if expected not in names:
                problems.append(f"missing native asset: {expected}")

            for rid in sorted(_native_rids(names) - {satellite_rid}):
                problems.append(
                    f"carries runtimes/{rid}/native/, which ships in {format_satellite_id(suffix, rid)}"
                )

            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            assemblies = sorted(
                name for name in names if name.startswith("lib/") and name.endswith(".dll")
            )
            if assemblies:
                problems.append(f"carries {assemblies[0]}: a native satellite ships no assembly")

            managed = f"M3scalinum.Text.{suffix}"
            if managed not in declared_dependencies:
                problems.append(f"a format satellite must depend on {managed}")
            else:
                declared = declared_dependencies[managed]
                if not declared or "," in declared or declared.startswith("("):
                    problems.append(
                        f"depends on {managed} {declared}; name the version the satellite was built against"
                    )

            summary = f"ok ({suffix} satellite, {satellite_rid}, managed {declared_dependencies.get(managed, 'unpinned')})"

        elif format_package is not None:
            kind = "format-package"

            # The other half of the split: a managed format package that packed its natives back in
            # would double the download of every consumer who also adds the satellite.
            for rid in sorted(_native_rids(names)):
                problems.append(
                    f"carries runtimes/{rid}/native/, which ships in "
                    f"{format_satellite_id(format_package, rid)}: the managed package carries no native library"
                )

            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            summary = f"ok (managed only, {len(REQUIRED_FILES)} required files)"

        elif is_satellite:
            kind = "satellite"

            if not pins_core:
                problems.append(f"a satellite package must depend on {CORE_PACKAGE}")

            summary = "ok (satellite, core version pinned)"

    return {
        "path": path,
        "problems": problems,
        "summary": summary,
        "kind": kind,
        "version": version,
        "pins": pins,
        "rid": rid_of_satellite or (format_satellite[1] if format_satellite else None),
        "format": (format_satellite[0] if format_satellite else format_package),
        "pins_format": (
            declared_dependencies.get(f"M3scalinum.Text.{format_satellite[0]}")
            if format_satellite else None
        ),
    }


def main(paths: list[str], require_native: bool = False) -> int:
    problems: list[str] = []
    results = []

    for path in paths:
        result = check(path, require_native)
        problems.extend(result["problems"])
        results.append(result)

    cores = [result for result in results if result["kind"] == "core"]
    satellites = {result["rid"]: result for result in results if result["kind"] == "native-satellite"}

    # The set, and not one file: NuGet cannot pick a native by RID, so the seven satellites are the
    # only thing that makes the split installable, and a satellite pinned to another core version is
    # a restore error in the consumer's project rather than in this run.
    if len(paths) > 1 and cores:
        core_version = cores[0]["version"] or ""

        for rid, package_id in NATIVE_SATELLITES.items():
            satellite = satellites.get(rid)

            if satellite is None:
                problems.append(f"the set has no {package_id} (RID {rid})")
            elif satellite["pins"] and satellite["pins"] != core_version:
                problems.append(
                    f"{package_id} pins {CORE_PACKAGE} {satellite['pins']}, the set packs {core_version}"
                )

    elif len(paths) > 1 and satellites:
        problems.append(f"the set holds native satellites and no {CORE_PACKAGE}")

    # The same rule for the five format families: a managed format package in the set must be joined
    # by its eight satellites, and each satellite must pin the exact version it was built against.
    format_satellites = {
        (result["format"], result["rid"]): result
        for result in results
        if result["kind"] == "format-satellite"
    }
    format_packages = {
        result["format"]: result for result in results if result["kind"] == "format-package"
    }

    for suffix, package in (format_packages.items() if len(paths) > 1 else []):
        for rid in FORMAT_RIDS:
            expected = format_satellite_id(suffix, rid)
            satellite = format_satellites.get((suffix, rid))

            if satellite is None:
                problems.append(f"the set has no {expected} (RID {rid})")
            elif satellite["pins_format"] and satellite["pins_format"] != package["version"]:
                problems.append(
                    f"{expected} pins {package['version'] and suffix} {satellite['pins_format']}, "
                    f"the set packs {package['version']}"
                )

    if len(paths) > 1 and format_satellites and not format_packages:
        problems.append(
            "the set holds format satellites and no managed format package to join them to"
        )

    # One line per distinct problem: a nuspec repeats its dependencies for every target framework.
    problems = list(dict.fromkeys(problems))

    if problems:
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        print(f"{', '.join(paths)}: {len(problems)} problem(s)", file=sys.stderr)
        return 1

    for result in results:
        print(f"{result['path']}: {result['summary']}")

    return 0


NUSPEC = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{ident}</id>
    <version>{version}</version>
    <authors>verify-package.py --selftest</authors>
    <description>A fabricated package, never published.</description>
{dependencies}  </metadata>
</package>
"""


def _fabricate(
    directory: str,
    index: int,
    assembly: str,
    natives: dict[str, str],
    ident: str = "M3scalinum.Text.Selftest",
    pins: str | None = None,
    pins_id: str = CORE_PACKAGE,
) -> str:
    """Writes the smallest package `check` can judge: one assembly, the required files, a nuspec.

    `pins` writes the core dependency a satellite declares, which is what a set is judged on: a
    satellite that names no core version, or another one, breaks the consumer's restore.
    """
    path = os.path.join(directory, f"selftest-{index}.nupkg")
    dependencies = (
        "    <dependencies>\n"
        f'      <group targetFramework="net11.0">\n'
        f'        <dependency id="{pins_id}" version="{pins}" />\n'
        "      </group>\n"
        "    </dependencies>\n"
        if pins is not None
        else ""
    )

    entries = {
        "Fabricated.nuspec": NUSPEC.format(ident=ident, version="0.0.1", dependencies=dependencies),
    }

    # A fabricated core carries the two assemblies the real package merges; anything else carries the
    # one assembly its case names, and a native satellite carries none.
    if ident == CORE_PACKAGE:
        entries.update({required: "assembly" for required in REQUIRED_ASSEMBLIES})
    elif assembly:
        entries[assembly] = "assembly"
    entries.update({required: "licence" for required in REQUIRED_FILES})
    entries.update(
        {f"runtimes/{rid}/native/{library}": "library" for rid, library in natives.items()}
    )

    with zipfile.ZipFile(path, "w") as package:
        for name, payload in entries.items():
            package.writestr(name, payload)

    return path


def selftest() -> int:
    """Asserts this script's verdicts on fabricated packages, the refusals included.

    Each case names the message it expects as well as the exit status, so deleting a RID from the
    tables above, or the "the managed package carries no native" rule, turns the case red instead of
    quietly shrinking what a run checks.
    """
    barcodes_assembly = "lib/net11.0/M3scalinum.Text.Barcodes.dll"
    core_assembly = "lib/net11.0/M3scalinum.Text.Typst.dll"
    win_x64_satellite = NATIVE_SATELLITES["win-x64"]
    barcodes_satellite = format_satellite_id("Barcodes", "win-x64")

    cases = [
        (
            "a managed format package carrying no native library",
            barcodes_assembly,
            {},
            False,
            0,
            "ok (managed only, 5 required files)",
            None,
            None,
        ),
        (
            "a managed format package carrying a native library again",
            barcodes_assembly,
            {"win-x64": FORMAT_LIBRARIES["Barcodes"]["win-x64"]},
            False,
            1,
            f"carries runtimes/win-x64/native/, which ships in {barcodes_satellite}",
            None,
            None,
        ),
        (
            "a format satellite carrying its own RID",
            "",
            {"win-x64": FORMAT_LIBRARIES["Barcodes"]["win-x64"]},
            False,
            0,
            "ok (Barcodes satellite, win-x64, managed 0.1.0)",
            barcodes_satellite,
            "M3scalinum.Text.Barcodes",
        ),
        (
            "a format satellite without its library",
            "",
            {},
            False,
            1,
            "missing native asset: runtimes/win-x64/native/sharp_text_barcodes.dll",
            barcodes_satellite,
            "M3scalinum.Text.Barcodes",
        ),
        (
            "a format satellite carrying another RID's library",
            "",
            {
                "win-x64": FORMAT_LIBRARIES["Barcodes"]["win-x64"],
                "win-arm64": FORMAT_LIBRARIES["Barcodes"]["win-arm64"],
            },
            False,
            1,
            f"carries runtimes/win-arm64/native/, which ships in {format_satellite_id('Barcodes', 'win-arm64')}",
            barcodes_satellite,
            "M3scalinum.Text.Barcodes",
        ),
        (
            "a format satellite that depends on nothing",
            "",
            {"win-x64": FORMAT_LIBRARIES["Barcodes"]["win-x64"]},
            False,
            1,
            "a format satellite must depend on M3scalinum.Text.Barcodes",
            barcodes_satellite,
            None,
        ),
        (
            "the managed compiler package carrying no native library",
            core_assembly,
            {},
            False,
            0,
            "ok (managed only, 5 required files)",
            CORE_PACKAGE,
            None,
        ),
        (
            "the managed compiler package carrying a native library again",
            core_assembly,
            {"win-x64": "sharp_typst.dll"},
            False,
            1,
            f"carries runtimes/win-x64/native/, which ships in {win_x64_satellite}",
            CORE_PACKAGE,
            None,
        ),
        (
            "a native satellite carrying its RID",
            "",
            {"win-x64": "sharp_typst.dll"},
            False,
            0,
            "ok (win-x64 satellite, core 0.0.1)",
            win_x64_satellite,
            CORE_PACKAGE,
        ),
        (
            "a native satellite without its library",
            "",
            {},
            False,
            1,
            "missing native asset: runtimes/win-x64/native/sharp_typst.dll",
            win_x64_satellite,
            CORE_PACKAGE,
        ),
        (
            "a native satellite carrying another RID's library",
            "",
            {"win-x64": "sharp_typst.dll", "win-arm64": "sharp_typst.dll"},
            False,
            1,
            f"carries runtimes/win-arm64/native/, which ships in {NATIVE_SATELLITES['win-arm64']}",
            win_x64_satellite,
            CORE_PACKAGE,
        ),
    ]

    failures = 0

    with tempfile.TemporaryDirectory() as directory:
        for index, case in enumerate(cases):
            description, assembly, natives, require_native, expected, message, ident, pins_id = case

            # A satellite's id is the only thing that says which RID it carries, and which package it
            # completes: the compiler's satellites pin 0.0.1, a format satellite pins its managed
            # package at 0.1.0.
            package_ident = ident or "M3scalinum.Text.Selftest"
            pins = None
            if pins_id and package_ident in NATIVE_SATELLITES.values():
                pins = "0.0.1"
            elif pins_id:
                pins = "0.1.0"

            path = _fabricate(
                directory,
                index,
                assembly,
                natives,
                ident=package_ident,
                pins=pins,
                pins_id=pins_id or CORE_PACKAGE,
            )
            captured = io.StringIO()

            with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
                status = main([path], require_native)

            output = captured.getvalue()

            if status != expected:
                print(
                    f"  {description}: expected exit {expected}, got {status}\n{output}",
                    file=sys.stderr,
                )
                failures += 1
            elif message is not None and message not in output:
                print(
                    f"  {description}: expected {message!r} in the output\n{output}",
                    file=sys.stderr,
                )
                failures += 1

        set_failures, set_cases = _selftest_set(directory)
        failures += set_failures

    total = len(cases) + set_cases

    if failures:
        print(f"selftest: {failures} of {total} case(s) failed", file=sys.stderr)
        return 1

    print(f"selftest: {total} cases passed")
    return 0


def _selftest_set(directory: str) -> tuple[int, int]:
    """The set-level verdicts: the managed package plus seven satellites, then a set with a hole.

    A single path cannot answer "was the release complete", and NuGet cannot either — it fetches only
    what a project names, so a satellite that was never published is a `NU1101` in a consumer's
    restore and nothing in this repository. These cases are what make the set a checked thing.
    """
    core = _fabricate(directory, 100, "lib/net11.0/M3scalinum.Text.Typst.dll", {}, ident=CORE_PACKAGE)
    complete = {
        rid: _fabricate(
            directory,
            200 + index,
            "",
            {rid: library},
            ident=NATIVE_SATELLITES[rid],
            pins="0.0.1",
        )
        for index, (rid, library) in enumerate(RIDS.items())
    }
    pinned_elsewhere = _fabricate(
        directory, 300, "", {"osx-x64": RIDS["osx-x64"]}, ident=NATIVE_SATELLITES["osx-x64"], pins="0.0.0"
    )

    with_hole = dict(complete)
    del with_hole["osx-x64"]

    with_mismatch = dict(complete)
    with_mismatch["osx-x64"] = pinned_elsewhere

    barcodes = _fabricate(
        directory, 400, "lib/net11.0/M3scalinum.Text.Barcodes.dll", {}, ident="M3scalinum.Text.Barcodes"
    )
    barcodes_satellites = {
        rid: _fabricate(
            directory,
            401 + index,
            "",
            {rid: library},
            ident=format_satellite_id("Barcodes", rid),
            pins="0.0.1",
            pins_id="M3scalinum.Text.Barcodes",
        )
        for index, (rid, library) in enumerate(FORMAT_LIBRARIES["Barcodes"].items())
    }
    barcodes_with_hole = dict(barcodes_satellites)
    del barcodes_with_hole["osx-arm64"]

    format_cases = [
        (
            "a complete format set",
            [barcodes, *barcodes_satellites.values()],
            0,
            "ok (Barcodes satellite, win-x64, managed 0.0.1)",
        ),
        (
            "a format set without its osx-arm64 satellite",
            [barcodes, *barcodes_with_hole.values()],
            1,
            f"the set has no {format_satellite_id('Barcodes', 'osx-arm64')} (RID osx-arm64)",
        ),
    ]

    cases = [
        (
            "a complete set",
            [core, *complete.values()],
            0,
            "ok (win-x64 satellite, core 0.0.1)",
        ),
        (
            "a set without its osx-x64 satellite",
            [core, *with_hole.values()],
            1,
            f"the set has no {NATIVE_SATELLITES['osx-x64']} (RID osx-x64)",
        ),
        (
            "a set whose osx-x64 satellite pins another core version",
            [core, *with_mismatch.values()],
            1,
            f"pins {CORE_PACKAGE} 0.0.0, the set packs 0.0.1",
        ),
    ]

    failures = 0

    for description, paths, expected, message in cases + format_cases:
        captured = io.StringIO()

        with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
            status = main(paths)

        output = captured.getvalue()

        if status != expected or message not in output:
            print(
                f"  {description}: expected exit {expected} and {message!r}, got {status}\n{output}",
                file=sys.stderr,
            )
            failures += 1

    return failures, len(cases) + len(format_cases)


if __name__ == "__main__":
    # `--native` asserts that the package carries every RID's native library. It is passed by the jobs
    # that build them, and omitted by a job that packs a package whose Rust build it does not run.
    if "--selftest" in sys.argv[1:]:
        if len(sys.argv) != 2:
            print(__doc__, file=sys.stderr)
            sys.exit(2)

        sys.exit(selftest())

    arguments = [argument for argument in sys.argv[1:] if argument != "--native"]
    require_native = "--native" in sys.argv

    # Several paths are checked as a set: the release job passes everything it packed, so a missing
    # satellite is caught before it is a NU1101 in a consumer's restore. A pack job may keep passing
    # one file per call.
    if not arguments:
        print(__doc__, file=sys.stderr)
        sys.exit(2)

    sys.exit(main(arguments, require_native))
