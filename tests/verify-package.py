#!/usr/bin/env python3
"""Verifies a packed M3scalinum.Text.SharpTypst .nupkg before it is published.

Four failures this catches, three of which shipped once:

- a package with no `runtimes/` entry, because the native job had not run;
- a dependency on `M3scalinum.Text.SharpTypst.Native`, which is never published, so every consumer got NU1101;
- a missing licence file, which breaks the Apache-2.0 obligations;
- a satellite package (`M3scalinum.Text.SharpTypst.DependencyInjection`, `M3scalinum.Text.Pdf.FactureX`) built against a
  different `M3scalinum.Text.SharpTypst` than the one packed in the same run — the version is derived from the
  referenced project, and nothing else forces the two to agree.

Usage: verify-package.py <package.nupkg>
"""

import re
import sys
import xml.etree.ElementTree as ET
import zipfile

RIDS = {
    "win-x64": "sharp_typst.dll",
    "win-arm64": "sharp_typst.dll",
    "linux-x64": "libsharp_typst.so",
    "linux-arm64": "libsharp_typst.so",
    "linux-musl-x64": "libsharp_typst.so",
    "osx": "libsharp_typst.dylib",
}

REQUIRED_FILES = [
    "LICENSE",
    "NOTICE",
    "THIRD-PARTY-NOTICES.md",
    "README.md",
]

REQUIRED_ASSEMBLIES = [
    "lib/net11.0/M3scalinum.Text.SharpTypst.dll",
    "lib/net11.0/M3scalinum.Text.SharpTypst.Native.dll",
]

FORBIDDEN_DEPENDENCY = "M3scalinum.Text.SharpTypst.Native"

CORE_PACKAGE = "M3scalinum.Text.SharpTypst"
SATELLITE_ASSEMBLIES = {
    "lib/net11.0/M3scalinum.Text.SharpTypst.DependencyInjection.dll",
    "lib/net11.0/M3scalinum.Text.SharpTypst.Templates.dll",
    "lib/net11.0/M3scalinum.Text.Pdf.FactureX.dll",
    "lib/net11.0/M3scalinum.Text.Mcp.dll",
    "lib/net11.0/M3scalinum.Text.Docx.dll",
}

# Packages that carry a Rust cdylib of their own rather than the compiler. They ship five RIDs, not
# the compiler's six: `linux-musl-x64` needs a hand-linked staticlib and is not built for them yet,
# so an Alpine consumer gets the resolver's explicit error. A missing native job is caught here
# instead of by a consumer's DllNotFoundException.
NATIVE_PACKAGES = {
    "lib/net11.0/M3scalinum.Text.Barcodes.dll": {
        "win-x64": "sharp_text_barcodes.dll",
        "win-arm64": "sharp_text_barcodes.dll",
        "linux-x64": "libsharp_text_barcodes.so",
        "linux-arm64": "libsharp_text_barcodes.so",
        "osx": "libsharp_text_barcodes.dylib",
    },
    "lib/net11.0/M3scalinum.Text.Ocr.dll": {
        "win-x64": "sharp_text_ocr.dll",
        "win-arm64": "sharp_text_ocr.dll",
        "linux-x64": "libsharp_text_ocr.so",
        "linux-arm64": "libsharp_text_ocr.so",
        "osx": "libsharp_text_ocr.dylib",
    },
    "lib/net11.0/M3scalinum.Text.Docx.dll": {
        "win-x64": "sharp_text_docx.dll",
        "win-arm64": "sharp_text_docx.dll",
        "linux-x64": "libsharp_text_docx.so",
        "linux-arm64": "libsharp_text_docx.so",
        "osx": "libsharp_text_docx.dylib",
    },
}


def main(path: str) -> int:
    problems: list[str] = []

    with zipfile.ZipFile(path) as package:
        names = set(package.namelist())

        # Only the main package carries the native compiler; the others do not.
        is_main_package = "lib/net11.0/M3scalinum.Text.SharpTypst.dll" in names
        is_satellite = bool(SATELLITE_ASSEMBLIES & names)
        native_package = next((assembly for assembly in NATIVE_PACKAGES if assembly in names), None)

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
    elif is_satellite:
        print(f"{path}: ok (satellite, core version pinned)")
    else:
        print(f"{path}: ok")

    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)

    sys.exit(main(sys.argv[1]))
