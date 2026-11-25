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
have built (`STAGED_NATIVE_PACKAGES`); pass it wherever those libraries exist.

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
NATIVE_PACKAGES = {
    "lib/net11.0/M3scalinum.Text.Barcodes.dll": {
        "win-x64": "sharp_text_barcodes.dll",
        "win-arm64": "sharp_text_barcodes.dll",
        "linux-x64": "libsharp_text_barcodes.so",
        "linux-arm64": "libsharp_text_barcodes.so",
        "linux-musl-x64": "libsharp_text_barcodes.so",
        "linux-musl-arm64": "libsharp_text_barcodes.so",
        "osx-arm64": "libsharp_text_barcodes.dylib",
        "osx-x64": "libsharp_text_barcodes.dylib",
    },
    "lib/net11.0/M3scalinum.Text.Ocr.dll": {
        "win-x64": "sharp_text_ocr.dll",
        "win-arm64": "sharp_text_ocr.dll",
        "linux-x64": "libsharp_text_ocr.so",
        "linux-arm64": "libsharp_text_ocr.so",
        "linux-musl-x64": "libsharp_text_ocr.so",
        "linux-musl-arm64": "libsharp_text_ocr.so",
        "osx-arm64": "libsharp_text_ocr.dylib",
        "osx-x64": "libsharp_text_ocr.dylib",
    },
    "lib/net11.0/M3scalinum.Text.Docx.dll": {
        "win-x64": "sharp_text_docx.dll",
        "win-arm64": "sharp_text_docx.dll",
        "linux-x64": "libsharp_text_docx.so",
        "linux-arm64": "libsharp_text_docx.so",
        "linux-musl-x64": "libsharp_text_docx.so",
        "linux-musl-arm64": "libsharp_text_docx.so",
        "osx-arm64": "libsharp_text_docx.dylib",
        "osx-x64": "libsharp_text_docx.dylib",
    },
    "lib/net11.0/M3scalinum.Text.Xlsx.dll": {
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

# Packages that do carry a Rust library, but that a workflow with no Rust build for them still packs:
# ci-sharptypst's `pack` job packs every package to prove it packs and stages only `sharp-typst`, so
# the six `sharp_text_pdf` libraries are absent there by design. Their RID list is required with
# `--native`, which every job that does build them passes — ci-text's `examples` job and the
# release's `pack` job — so a package that ships without one is still red somewhere.
STAGED_NATIVE_PACKAGES = {
    "lib/net11.0/M3scalinum.Text.Pdf.dll": {
        "win-x64": "sharp_text_pdf.dll",
        "win-arm64": "sharp_text_pdf.dll",
        "linux-x64": "libsharp_text_pdf.so",
        "linux-arm64": "libsharp_text_pdf.so",
        "linux-musl-x64": "libsharp_text_pdf.so",
        "linux-musl-arm64": "libsharp_text_pdf.so",
        "osx-arm64": "libsharp_text_pdf.dylib",
        "osx-x64": "libsharp_text_pdf.dylib",
    },
}


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

        # Only the managed package carries the compiler; the format packages do not, and the native
        # satellites carry nothing but one library.
        is_core = ident == CORE_PACKAGE or "lib/net11.0/M3scalinum.Text.Typst.dll" in names
        is_satellite = bool(SATELLITE_ASSEMBLIES & names)
        native_package = next((assembly for assembly in NATIVE_PACKAGES if assembly in names), None)
        staged_package = next(
            (assembly for assembly in STAGED_NATIVE_PACKAGES if assembly in names), None
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

            summary = f"ok (managed only, {len(REQUIRED_FILES)} licence files)"

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

        elif native_package is not None:
            kind = "native-package"

            for rid, library in NATIVE_PACKAGES[native_package].items():
                entry = f"runtimes/{rid}/native/{library}"
                if entry not in names:
                    problems.append(f"missing native asset: {entry}")

            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            summary = f"ok ({len(NATIVE_PACKAGES[native_package])} RIDs, {len(REQUIRED_FILES)} licence files)"

        elif staged_package is not None:
            kind = "staged-package"

            # The licence files are always required: the csproj packs them unconditionally, so their
            # absence is a defect in the package itself and not a job's missing Rust build.
            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

            if require_native:
                for rid, library in STAGED_NATIVE_PACKAGES[staged_package].items():
                    entry = f"runtimes/{rid}/native/{library}"
                    if entry not in names:
                        problems.append(f"missing native asset: {entry}")

            rid_count = len(STAGED_NATIVE_PACKAGES[staged_package]) if require_native else 0
            summary = f"ok ({rid_count} RIDs checked, {len(REQUIRED_FILES)} licence files)"

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
        "rid": rid_of_satellite,
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
) -> str:
    """Writes the smallest package `check` can judge: one assembly, the licence files, a nuspec.

    `pins` writes the core dependency a satellite declares, which is what a set is judged on: a
    satellite that names no core version, or another one, breaks the consumer's restore.
    """
    path = os.path.join(directory, f"selftest-{index}.nupkg")
    dependencies = (
        "    <dependencies>\n"
        f'      <group targetFramework="net11.0">\n'
        f'        <dependency id="{CORE_PACKAGE}" version="{pins}" />\n'
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
    barcodes = NATIVE_PACKAGES["lib/net11.0/M3scalinum.Text.Barcodes.dll"]
    pdf = STAGED_NATIVE_PACKAGES["lib/net11.0/M3scalinum.Text.Pdf.dll"]
    barcodes_without_arm64 = {
        rid: library for rid, library in barcodes.items() if rid != "linux-musl-arm64"
    }
    pdf_without_arm64 = {
        rid: library for rid, library in pdf.items() if rid != "linux-musl-arm64"
    }

    core_assembly = "lib/net11.0/M3scalinum.Text.Typst.dll"
    win_x64_satellite = NATIVE_SATELLITES["win-x64"]

    cases = [
        (
            "a native package carrying every RID",
            "lib/net11.0/M3scalinum.Text.Barcodes.dll",
            barcodes,
            False,
            0,
            f"ok ({len(barcodes)} RIDs, 4 licence files)",
        ),
        (
            "a native package without its linux-musl-arm64 library",
            "lib/net11.0/M3scalinum.Text.Barcodes.dll",
            barcodes_without_arm64,
            False,
            1,
            "missing native asset: runtimes/linux-musl-arm64/native/libsharp_text_barcodes.so",
        ),
        (
            "a staged package with --native carrying every RID",
            "lib/net11.0/M3scalinum.Text.Pdf.dll",
            pdf,
            True,
            0,
            f"ok ({len(pdf)} RIDs checked, 4 licence files)",
        ),
        (
            "a staged package with --native and no linux-musl-arm64 library",
            "lib/net11.0/M3scalinum.Text.Pdf.dll",
            pdf_without_arm64,
            True,
            1,
            "missing native asset: runtimes/linux-musl-arm64/native/libsharp_text_pdf.so",
        ),
        (
            "a staged package without --native and no linux-musl-arm64 library",
            "lib/net11.0/M3scalinum.Text.Pdf.dll",
            pdf_without_arm64,
            False,
            0,
            None,
        ),
        (
            "the managed compiler package carrying no native library",
            core_assembly,
            {},
            False,
            0,
            "ok (managed only, 4 licence files)",
        ),
        (
            "the managed compiler package carrying a native library again",
            core_assembly,
            {"win-x64": "sharp_typst.dll"},
            False,
            1,
            f"carries runtimes/win-x64/native/, which ships in {win_x64_satellite}",
        ),
        (
            "a native satellite carrying its RID",
            "",
            {"win-x64": "sharp_typst.dll"},
            False,
            0,
            "ok (win-x64 satellite, core 0.0.1)",
        ),
        (
            "a native satellite without its library",
            "",
            {},
            False,
            1,
            "missing native asset: runtimes/win-x64/native/sharp_typst.dll",
        ),
        (
            "a native satellite carrying another RID's library",
            "",
            {"win-x64": "sharp_typst.dll", "win-arm64": "sharp_typst.dll"},
            False,
            1,
            f"carries runtimes/win-arm64/native/, which ships in {NATIVE_SATELLITES['win-arm64']}",
        ),
    ]

    failures = 0

    with tempfile.TemporaryDirectory() as directory:
        for index, case in enumerate(cases):
            description, assembly, natives, require_native, expected, message = case

            ident = "M3scalinum.Text.Selftest"

            if assembly == core_assembly:
                ident = CORE_PACKAGE
            elif assembly == "":
                # A native satellite: its id is the only thing that says which RID it carries.
                ident = NATIVE_SATELLITES[next(iter(natives), "win-x64")]

            path = _fabricate(
                directory,
                index,
                assembly,
                natives,
                ident=ident,
                # Only a satellite declares the core dependency; a fabricated native package that
                # did would be judged by a rule it is not subject to.
                pins="0.0.1" if assembly == "" else None,
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

    for description, paths, expected, message in cases:
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

    return failures, len(cases)


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
