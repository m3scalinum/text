#!/usr/bin/env python3
"""Fails when production line coverage drops below the floor.

Reads a Cobertura report, ignores the test assemblies and the compiler-generated closures, and
compares the remaining line coverage against FLOOR.

It also fails when an assembly that must be covered has no lines in the report at all. That is the
failure mode that let the gate slip: a new test project was added, the coverage job kept merging
only the two it knew about, and the new package's lines were counted as uncovered instead of
uncollected — a 0.8-point drop with no obvious cause.

With --require, the floor applies to those assemblies only. Each release train gates its own
packages: the suite workflow collects two suites, and the compiler assembly is linked into the
output as a dependency, so counting its lines there measured code no test in that run touches.

Usage: coverage-gate.py <cobertura.xml> [floor] [--require ASSEMBLY ...]
"""

import sys
import xml.etree.ElementTree as ET

DEFAULT_FLOOR = 90.0


def production_lines(root: ET.Element, only: set[str] | None = None) -> tuple[int, int]:
    covered = 0
    total = 0

    # Walk the packages first: the assembly a class belongs to is the package it sits under, and
    # --require scopes the measurement to a subset of them.
    for package in root.iter("package"):
        assembly = package.get("name") or ""

        if only is not None and assembly not in only:
            continue

        for cls in package.iter("class"):
            name = cls.get("name") or ""

            # Test assemblies and compiler-generated closures are not production code.
            if ".Tests." in name or name.endswith(".Tests") or "<>" in name:
                continue

            lines = cls.findall("./lines/line")
            if not lines:
                continue

            total += len(lines)
            covered += sum(1 for line in lines if int(line.get("hits", "0")) > 0)

    return covered, total


def assemblies_in(root: ET.Element) -> set[str]:
    names = set()

    for package in root.iter("package"):
        name = package.get("name") or ""
        if name:
            names.add(name)

    return names


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2

    floor = DEFAULT_FLOOR
    required: list[str] = []

    index = 2
    while index < len(argv):
        if argv[index] == "--require":
            index += 1
            while index < len(argv) and not argv[index].startswith("--"):
                required.append(argv[index])
                index += 1
            continue

        floor = float(argv[index])
        index += 1

    root = ET.parse(argv[1]).getroot()
    covered, total = production_lines(root, set(required) if required else None)

    if total == 0:
        print("no production lines found in the report", file=sys.stderr)
        return 1

    present = assemblies_in(root)
    missing = [name for name in required if name not in present]

    if missing:
        for name in missing:
            print(
                f"no coverage recorded for {name}: a test project is not being collected",
                file=sys.stderr,
            )

        return 1

    percent = covered / total * 100
    scope = ", ".join(sorted(required)) if required else "every assembly in the report"
    print(f"production line coverage: {covered}/{total} = {percent:.1f}% (floor {floor:.1f}%)")
    print(f"measured: {scope}")

    if percent < floor:
        print(f"coverage dropped below the floor by {floor - percent:.1f} points", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
