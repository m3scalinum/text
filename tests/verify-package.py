#!/usr/bin/env python3
"""Verifies a packed M3scalinum.Text.Typst .nupkg before it is published.

Four failures this catches, three of which shipped once:

- a package with no `runtimes/` entry, because the native job had not run;
- a dependency on `M3scalinum.Text.Typst.Native`, which is never published, so every consumer got NU1101;
- a missing licence file, which breaks the Apache-2.0 obligations;
- a satellite package (`M3scalinum.Text.Typst.DependencyInjection`, `M3scalinum.Text.Pdf.FactureX`) built against a
  different `M3scalinum.Text.Typst` than the one packed in the same run — the version is derived from the
  referenced project, and nothing else forces the two to agree.

Usage: verify-package.py <package.nupkg> [--native]
       verify-package.py --selftest

`--native` additionally requires every RID library of a package whose natives a workflow may not
have built (`STAGED_NATIVE_PACKAGES`); pass it wherever those libraries exist.

`--selftest` fabricates packages in a temporary directory and asserts this script's own verdicts on
them, including that a package without its `linux-musl-arm64` library is refused. It is run by the
`extras` job of `ci-text.yml`, so the requirement below cannot be deleted without CI going red.
"""

import contextlib
import io
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

# The compiler package's RIDs. It gets no `linux-musl-arm64` build yet: its Cargo.toml vendors
# OpenSSL for the x86_64 musl triple alone, so the target needs a vendoring change this list
# would then have one more line for (08 §6, 09 §11). macOS is two thin dylibs, one per
# architecture, rather than one universal file: a consumer copies out the RID their build targets.
RIDS = {
    "win-x64": "sharp_typst.dll",
    "win-arm64": "sharp_typst.dll",
    "linux-x64": "libsharp_typst.so",
    "linux-arm64": "libsharp_typst.so",
    "linux-musl-x64": "libsharp_typst.so",
    "osx-arm64": "libsharp_typst.dylib",
    "osx-x64": "libsharp_typst.dylib",
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
    "lib/net11.0/M3scalinum.Text.Pdf.FactureX.dll",
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


def main(path: str, require_native: bool = False) -> int:
    problems: list[str] = []

    with zipfile.ZipFile(path) as package:
        names = set(package.namelist())

        # Only the main package carries the native compiler; the others do not.
        is_main_package = "lib/net11.0/M3scalinum.Text.Typst.dll" in names
        is_satellite = bool(SATELLITE_ASSEMBLIES & names)
        native_package = next((assembly for assembly in NATIVE_PACKAGES if assembly in names), None)
        staged_package = next(
            (assembly for assembly in STAGED_NATIVE_PACKAGES if assembly in names), None
        )

        if is_main_package:
            for rid, library in RIDS.items():
                entry = f"runtimes/{rid}/native/{library}"
                if entry not in names:
                    problems.append(f"missing native asset: {entry}")

            for required in REQUIRED_FILES + REQUIRED_ASSEMBLIES:
                if required not in names:
                    problems.append(f"missing file: {required}")

        if native_package is not None:
            for rid, library in NATIVE_PACKAGES[native_package].items():
                entry = f"runtimes/{rid}/native/{library}"
                if entry not in names:
                    problems.append(f"missing native asset: {entry}")

            for required in REQUIRED_FILES:
                if required not in names:
                    problems.append(f"missing file: {required}")

        if staged_package is not None:
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

        nuspec = next((name for name in names if name.endswith(".nuspec")), None)
        if nuspec is None:
            problems.append("no .nuspec in the package")
        else:
            root = ET.fromstring(package.read(nuspec))

            version = next(
                (element.text for element in root.iter() if element.tag.endswith("}version")),
                None,
            )

            pins_core = False

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
                    # M3scalinum.Text.Pdf.FactureX follows the suite (0.1.x) while the compiler
                    # wrapper it builds on follows Typst (0.15.1.x). NuGet writes an exact pin as a
                    # bare version, and a range as `[1.0.0, )` or `(1.0.0, 2.0.0)`.
                    if not lower or "," in declared or declared.startswith("("):
                        problems.append(
                            f"depends on {CORE_PACKAGE} {declared}; name the core version the package was built against"
                        )

            if is_satellite and not pins_core:
                problems.append(f"a satellite package must depend on {CORE_PACKAGE}")

    # One line per distinct problem: a nuspec repeats its dependencies for every target framework.
    problems = list(dict.fromkeys(problems))

    if problems:
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        print(f"{path}: {len(problems)} problem(s)", file=sys.stderr)
        return 1

    if is_main_package:
        print(f"{path}: ok ({len(RIDS)} RIDs, {len(REQUIRED_FILES)} licence files)")
    elif native_package is not None:
        print(f"{path}: ok ({len(NATIVE_PACKAGES[native_package])} RIDs, {len(REQUIRED_FILES)} licence files)")
    elif staged_package is not None:
        rid_count = len(STAGED_NATIVE_PACKAGES[staged_package]) if require_native else 0
        print(f"{path}: ok ({rid_count} RIDs checked, {len(REQUIRED_FILES)} licence files)")
    elif is_satellite:
        print(f"{path}: ok (satellite, core version pinned)")
    else:
        print(f"{path}: ok")

    return 0


NUSPEC = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>M3scalinum.Text.Selftest</id>
    <version>0.0.1</version>
    <authors>verify-package.py --selftest</authors>
    <description>A fabricated package, never published.</description>
  </metadata>
</package>
"""


def _fabricate(directory: str, index: int, assembly: str, natives: dict[str, str]) -> str:
    """Writes the smallest package `main` can judge: one assembly, the licence files, a nuspec."""
    path = os.path.join(directory, f"selftest-{index}.nupkg")
    entries = {"Fabricated.nuspec": NUSPEC, assembly: "assembly"}
    entries.update({required: "licence" for required in REQUIRED_FILES})
    entries.update(
        {f"runtimes/{rid}/native/{library}": "library" for rid, library in natives.items()}
    )

    with zipfile.ZipFile(path, "w") as package:
        for name, payload in entries.items():
            package.writestr(name, payload)

    return path


def selftest() -> int:
    """Asserts this script's verdicts on fabricated packages, the refusal of a missing RID included.

    Each case names the message it expects as well as the exit status, so deleting a RID from the
    tables above turns the case red instead of quietly shrinking what a run checks.
    """
    barcodes = NATIVE_PACKAGES["lib/net11.0/M3scalinum.Text.Barcodes.dll"]
    pdf = STAGED_NATIVE_PACKAGES["lib/net11.0/M3scalinum.Text.Pdf.dll"]
    barcodes_without_arm64 = {
        rid: library for rid, library in barcodes.items() if rid != "linux-musl-arm64"
    }
    pdf_without_arm64 = {
        rid: library for rid, library in pdf.items() if rid != "linux-musl-arm64"
    }

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
    ]

    failures = 0

    with tempfile.TemporaryDirectory() as directory:
        for index, case in enumerate(cases):
            description, assembly, natives, require_native, expected, message = case
            path = _fabricate(directory, index, assembly, natives)
            captured = io.StringIO()

            with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
                status = main(path, require_native)

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

    if failures:
        print(f"selftest: {failures} of {len(cases)} case(s) failed", file=sys.stderr)
        return 1

    print(f"selftest: {len(cases)} cases passed")
    return 0


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

    if len(arguments) != 1:
        print(__doc__, file=sys.stderr)
        sys.exit(2)

    sys.exit(main(arguments[0], require_native))
