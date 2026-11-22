#!/usr/bin/env python3
"""Renders a per-package coverage table from the Cobertura reports the CI jobs upload.

Each job of `ci-text.yml` measures its own assemblies and fails below 90 %; nobody sees the whole
picture without opening four artifacts. This script reads every `*.xml` under a directory, prints
one row per shipped package — covered lines, total lines, percentage, floor, verdict — as markdown
for the run summary and as JSON for anything that wants to consume it.

It fails when a package under `src/` that this workflow is supposed to measure has no report at
all. That is the defect the table exists to catch: a job that stopped uploading, a package dropped
from a `--require` list, a new project nobody wired up. A package missing from the table reads as
"no problem here" when it means "nobody looked".

The expected set is read from `src/`, never from the reports — a report can only tell you about
packages it already contains, which is exactly the question being asked. Projects are expected when
their csproj sets a `PackageId` and does not set `IsPackable` to false. Each one is then either
measured by this workflow (a row with a verdict) or measured elsewhere (`MEASURED_ELSEWHERE`, a row
saying where). A package in neither list is an error, so adding a project to `src/` without a test
project that measures it is red here instead of invisible.

Usage:
  coverage-table.py [REPORT_DIR] [--floor 90] [--markdown PATH] [--json PATH]
  coverage-table.py --selftest

REPORT_DIR defaults to `artifacts/coverage`. --markdown and --json write the table; the same
markdown is always printed on stdout.
"""

import argparse
import importlib.util
import json
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_FLOOR = 90.0
DEFAULT_REPORTS = "artifacts/coverage"
SRC = Path(__file__).resolve().parent.parent / "src"

# Packages this workflow measures. The value is the artifact job whose report carries them, used
# for the error message when a report goes missing: "no report for X" is only actionable with the
# job that was supposed to upload it.
MEASURED_HERE = {
    "M3scalinum.Text.Barcodes": "extras",
    "M3scalinum.Text.Docx": "extras",
    "M3scalinum.Text.Ocr": "extras",
    "M3scalinum.Text.Xlsx": "extras",
    "M3scalinum.Text.Mcp": "mcp",
    "M3scalinum.Text.Pdf": "suite",
    "M3scalinum.Text.Templates": "templates",
}

# Packages of `src/` that this workflow packs (the examples and musl jobs) but does not measure:
# the compiler train's suites run in its own workflow, and an artifact of another run is not
# reachable from here. They still get a row — a table that silently omits four of the eleven
# packages is the same blindness in a nicer font.
MEASURED_ELSEWHERE = {
    "M3scalinum.Text.Documents": "ci-sharptypst.yml coverage",
    "M3scalinum.Text.Templates.Generator": "compiled into the consumer, no own report",
    "M3scalinum.Text.Typst": "ci-sharptypst.yml coverage",
    "M3scalinum.Text.Typst.DependencyInjection": "ci-sharptypst.yml coverage",
}


def load_gate():
    """Imports tests/coverage-gate.py by path, so both scripts count lines the same way."""
    path = Path(__file__).resolve().parent / "coverage-gate.py"
    spec = importlib.util.spec_from_file_location("coverage_gate", path)
    assert spec is not None and spec.loader is not None, f"cannot load {path}"
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def packaged_projects(src: Path = SRC) -> dict[str, str]:
    """Package id -> project directory, read from src/."""
    projects = {}

    for csproj in sorted(src.glob("*/*.csproj")):
        text = csproj.read_text(encoding="utf-8")
        packable = re.search(r"<IsPackable>\s*(\w+)\s*</IsPackable>", text)
        package = re.search(r"<PackageId>\s*([\w.]+)\s*</PackageId>", text)

        if package is None:
            continue
        if packable is not None and packable.group(1).lower() == "false":
            continue

        projects[package.group(1)] = csproj.parent.name

    return projects


def report_totals(report: Path, gate) -> dict[str, tuple[int, int]]:
    """Package -> (covered, total) production lines in one Cobertura file."""
    root = ET.parse(report).getroot()
    totals = {}

    for package in gate.assemblies_in(root):
        totals[package] = gate.production_lines(root, {package})

    return totals


def collect(reports_dir: Path, gate) -> tuple[dict[str, tuple[int, int]], dict[str, list[str]], list[str]]:
    """Merges every report: package -> best (covered, total), package -> files, files read.

    A package can appear in more than one report (the suite job and the compiler job both build
    `Text.Pdf`). When it does, the report with the most lines wins: a merged run collected more
    of the assembly than a single suite, so it is the closer measurement of the same code.
    """
    best: dict[str, tuple[int, int]] = {}
    sources: dict[str, list[str]] = {}
    files = []

    for report in sorted(reports_dir.glob("*.xml")):
        totals = report_totals(report, gate)
        files.append(report.name)

        for package, (covered, total) in totals.items():
            sources.setdefault(package, []).append(report.name)
            if package not in best or total > best[package][1]:
                best[package] = (covered, total)

    return best, sources, files


def build(reports_dir: Path, floor: float, gate) -> tuple[bool, dict]:
    """Builds the table. Returns (ok, table) where ok is false only for a missing report."""
    projects = packaged_projects()

    unaccounted = sorted(set(projects) - set(MEASURED_HERE) - set(MEASURED_ELSEWHERE))

    totals, sources, files = collect(reports_dir, gate)
    rows = []
    missing = []

    for package in sorted(MEASURED_HERE):
        if package in totals:
            covered, total = totals[package]
            percent = covered / total * 100 if total else 0.0
            rows.append(
                {
                    "package": package,
                    "covered": covered,
                    "total": total,
                    "percent": round(percent, 1),
                    "floor": floor,
                    "verdict": "pass" if percent >= floor else "below floor",
                    "measured_by": MEASURED_HERE[package],
                    "reports": sources.get(package, []),
                }
            )
            continue

        missing.append(package)
        rows.append(
            {
                "package": package,
                "covered": None,
                "total": None,
                "percent": None,
                "floor": floor,
                "verdict": "no report",
                "measured_by": MEASURED_HERE[package],
                "reports": [],
            }
        )

    for package in sorted(MEASURED_ELSEWHERE):
        rows.append(
            {
                "package": package,
                "covered": None,
                "total": None,
                "percent": None,
                "floor": floor,
                "verdict": "measured elsewhere",
                "measured_by": MEASURED_ELSEWHERE[package],
                "reports": [],
            }
        )

    table = {
        "floor": floor,
        "reports": files,
        "packages_expected": len(MEASURED_HERE),
        "packages_in_src": len(projects),
        "rows": rows,
        "missing": missing,
        "unaccounted": unaccounted,
    }

    return not missing and not unaccounted, table


def markdown(table: dict) -> str:
    lines = [
        "### Coverage per package",
        "",
        f"Floor {table['floor']:.0f} % per package, applied by each job's own gate; this table is the "
        f"summary, not a second gate. Sources: {', '.join(table['reports']) or 'none'}.",
        "",
        "| Package | Lines | Covered | Coverage | Floor | Verdict |",
        "| --- | ---: | ---: | ---: | ---: | --- |",
    ]

    for row in table["rows"]:
        lines_total = "—" if row["total"] is None else str(row["total"])
        covered = "—" if row["covered"] is None else str(row["covered"])
        percent = "—" if row["percent"] is None else f"{row['percent']:.1f} %"

        if row["verdict"] == "measured elsewhere":
            percent = "—"
            verdict = f"measured elsewhere ({row['measured_by']})"
        elif row["verdict"] == "no report":
            verdict = f"**no report** (expected from `{row['measured_by']}`)"
        else:
            verdict = f"{row['verdict']} ({row['measured_by']})"

        lines.append(
            f"| {row['package']} | {lines_total} | {covered} | {percent} | {row['floor']:.0f} % | {verdict} |"
        )

    if table["missing"]:
        lines += ["", "**No report for:** " + ", ".join(table["missing"])]

    if table["unaccounted"]:
        lines += [
            "",
            "**Shipped but not accounted for:** "
            + ", ".join(table["unaccounted"])
            + " — add it to `MEASURED_HERE` with its job, or to `MEASURED_ELSEWHERE` saying where.",
        ]

    return "\n".join(lines) + "\n"


def selftest() -> int:
    """Fabricates Cobertura files and proves the aggregation and the missing-report refusal."""
    gate = load_gate()

    def cobertura(packages: dict[str, tuple[int, int]]) -> str:
        body = []

        for name, (covered, total) in packages.items():
            hits = [f'<line number="{i}" hits="{1 if i <= covered else 0}"/>' for i in range(1, total + 1)]
            body.append(
                f'<package name="{name}"><classes><class name="{name}.Doc">'
                f"<lines>{''.join(hits)}</lines></class></classes></package>"
            )

        return (
            '<?xml version="1.0" encoding="utf-8"?><coverage><packages>'
            + "".join(body)
            + "</packages></coverage>"
        )

    with tempfile.TemporaryDirectory() as tmp:
        reports = Path(tmp)
        (reports / "coverage.xml").write_text(cobertura({"M3scalinum.Text.Pdf": (95, 100)}), encoding="utf-8")
        (reports / "extras.xml").write_text(
            cobertura(
                {
                    "M3scalinum.Text.Barcodes": (80, 100),
                    "M3scalinum.Text.Docx": (100, 100),
                }
            ),
            encoding="utf-8",
        )

        ok, table = build(reports, 90.0, gate)
        rows = {row["package"]: row for row in table["rows"]}

        assert not ok, "a report set missing five packages must fail"
        assert table["missing"] == sorted(
            ["M3scalinum.Text.Ocr", "M3scalinum.Text.Xlsx", "M3scalinum.Text.Mcp", "M3scalinum.Text.Templates"]
        ), table["missing"]
        assert rows["M3scalinum.Text.Pdf"]["percent"] == 95.0, rows["M3scalinum.Text.Pdf"]
        assert rows["M3scalinum.Text.Barcodes"]["verdict"] == "below floor", rows["M3scalinum.Text.Barcodes"]
        assert rows["M3scalinum.Text.Typst"]["verdict"] == "measured elsewhere"
        assert "**no report**" in markdown(table)

        # Every expected package accounted for: a package absent from the reports, not just a
        # package below its floor, is what the table refuses.
        complete = Path(tmp) / "complete"
        complete.mkdir()
        (complete / "coverage.xml").write_text(
            cobertura(
                {
                    "M3scalinum.Text.Barcodes": (92, 100),
                    "M3scalinum.Text.Docx": (91, 100),
                    "M3scalinum.Text.Mcp": (90, 100),
                    "M3scalinum.Text.Ocr": (99, 100),
                    "M3scalinum.Text.Pdf": (95, 100),
                    "M3scalinum.Text.Templates": (100, 100),
                    "M3scalinum.Text.Xlsx": (93, 100),
                }
            ),
            encoding="utf-8",
        )

        ok, table = build(complete, 90.0, gate)
        assert ok, table["missing"]
        assert len(table["rows"]) == len(MEASURED_HERE) + len(MEASURED_ELSEWHERE)

    print("selftest ok: aggregation, 90 % verdicts and refusal of a missing report")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="?", default=DEFAULT_REPORTS, help=f"directory of Cobertura files (default {DEFAULT_REPORTS})")
    parser.add_argument("--floor", type=float, default=DEFAULT_FLOOR, help=f"per-package floor, default {DEFAULT_FLOOR:.0f}")
    parser.add_argument("--markdown", help="write the table as markdown here")
    parser.add_argument("--json", dest="json_path", help="write the table as JSON here")
    parser.add_argument("--selftest", action="store_true", help="prove the aggregation on fabricated reports")
    args = parser.parse_args(argv[1:])

    if args.selftest:
        return selftest()

    reports = Path(args.reports)

    if not reports.is_dir():
        print(f"no report directory at {reports}: nothing was uploaded", file=sys.stderr)
        return 1

    ok, table = build(reports, args.floor, load_gate())
    text = markdown(table)

    print(text, end="")

    if args.markdown:
        Path(args.markdown).write_text(text, encoding="utf-8")

    if args.json_path:
        Path(args.json_path).write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")

    for package in table["missing"]:
        print(
            f"no coverage report for {package}: the `{MEASURED_HERE[package]}` job should have uploaded one",
            file=sys.stderr,
        )

    for package in table["unaccounted"]:
        print(
            f"{package} is packed from src/ but neither measured here nor named as measured elsewhere",
            file=sys.stderr,
        )

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
