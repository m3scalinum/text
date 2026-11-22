#!/usr/bin/env python3
"""Check the repository against the Rust crate policy in docs/specifications/dependencies.md.

The policy is a table: one row per crate this repository has decided about, carrying the licence
read at the source, the traction, the date of the last release and the decision. This script holds
the table and the tree to each other.

In the default (online) mode it asks crates.io for every documented crate and reports a licence that
no longer matches the document, a crate with no release for a year, and a crate that a Cargo.toml
declares but the document does not cover. With --offline it makes no HTTP call at all and checks
only what the document and the tree can answer for themselves: coverage, well-formed rows, and the
one-year rule read from the date the document records. The CI workflow runs the offline mode on
every pull request and the online mode on demand.

Exit status: 0 when the tree conforms, 1 when it does not, 2 when the network makes the check
impossible. Standard library only: the script must run on a bare CPython.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

# ---------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------

API = "https://crates.io/api/v1/crates/"
# crates.io asks for a User-Agent that identifies the caller and a way to reach it.
USER_AGENT = "m3scalinum-dependency-policy/1.0 (+https://github.com/m3scalinum/text)"
DOCUMENT = os.path.join("docs", "specifications", "dependencies.md")
STALE_DAYS = 365
DECISIONS = ("adopted", "rejected", "transitive")
MIN_REASON = 12
HTTP_ATTEMPTS = 3
HTTP_PAUSE = 0.4

# Directories that never hold a manifest of this repository.
SKIP_DIRS = {".git", "target", "node_modules", "bin", "obj", "artifacts", ".vs"}

DEPENDENCY_SECTIONS = ("dependencies", "dev-dependencies", "build-dependencies")

ISO_DATE = re.compile(r"\d{4}-\d{2}-\d{2}")


# ---------------------------------------------------------------------------------------------
# The manifest side: every direct Rust dependency the repository declares
# ---------------------------------------------------------------------------------------------


def _strip_quotes(value: str) -> str:
    return value.strip().strip('"').strip("'")


def _split_section(section: str) -> list[str]:
    """Split a TOML section header on dots that are not inside quotes."""

    parts, current, quoted = [], [], False
    for char in section:
        if char == '"':
            quoted = not quoted
            current.append(char)
        elif char == "." and not quoted:
            parts.append("".join(current))
            current = []
        else:
            current.append(char)

    parts.append("".join(current))
    return [_strip_quotes(part) for part in parts]


def dependencies_in_manifest(path: str) -> list[tuple[str, str]]:
    """Return (crate, section) for every direct dependency declared in one Cargo.toml."""

    found: list[tuple[str, str]] = []
    section = ""
    section_is_dependency = False
    inline_crate: str | None = None

    with open(path, encoding="utf-8") as handle:
        for raw in handle:
            line = raw.split("#", 1)[0].rstrip()
            if not line.strip():
                continue

            header = re.match(r"^\s*\[\[?([^\]]+)\]\]?\s*$", line)
            if header:
                parts = _split_section(header.group(1))
                section = ".".join(parts)
                # `[dependencies]`, `[dev-dependencies]`, `[target.'cfg(...)'.dependencies]`,
                # `[workspace.dependencies]`. `[dependencies.foo]` is the long form of one crate.
                section_is_dependency = bool(parts) and parts[-1] in DEPENDENCY_SECTIONS
                inline_crate = None
                if (
                    len(parts) >= 2
                    and parts[-2] in DEPENDENCY_SECTIONS
                    and parts[-1] not in DEPENDENCY_SECTIONS
                ):
                    inline_crate = parts[-1]
                    found.append((inline_crate, section))
                    section_is_dependency = False
                continue

            if inline_crate is not None:
                continue

            if not section_is_dependency:
                continue

            assignment = re.match(r"^([A-Za-z0-9_-]+)\s*=", line)
            if assignment:
                found.append((assignment.group(1), section))

    return found


def declared_crates(root: str) -> dict[str, list[str]]:
    """Map every declared crate name to the manifests that declare it."""

    crates: dict[str, list[str]] = {}
    for directory, subdirectories, files in os.walk(root):
        subdirectories[:] = sorted(d for d in subdirectories if d not in SKIP_DIRS)
        if "Cargo.toml" not in files:
            continue

        path = os.path.join(directory, "Cargo.toml")
        relative = os.path.relpath(path, root).replace(os.sep, "/")
        for crate, section in dependencies_in_manifest(path):
            crates.setdefault(crate, []).append(f"{relative} [{section}]")

    return crates


# ---------------------------------------------------------------------------------------------
# The document side: the register table
# ---------------------------------------------------------------------------------------------


def normalize_crate(name: str) -> str:
    """crates.io treats `-` and `_` as the same character in a crate name."""

    return name.strip().lower().replace("_", "-")


def license_tokens(expression: str) -> frozenset[str]:
    """Reduce a licence expression to the set of licences it names.

    `MIT OR Apache-2.0`, `Apache-2.0 OR MIT` and `MIT/Apache-2.0` are the same grant, and the
    document is allowed to write any of them. `non-standard` stays a single token.
    """

    text = expression.lower()
    text = re.sub(r"\b(?:and|or)\b", ",", text)
    text = text.replace("(", ",").replace(")", ",")
    return frozenset(part.strip() for part in re.split(r"[,/]", text) if part.strip())


def clean_cell(cell: str) -> str:
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", cell)
    text = text.replace("`", "").replace("*", "").replace("\\|", "|")
    return text.strip()


def split_row(line: str) -> list[str]:
    body = line.strip()
    if body.startswith("|"):
        body = body[1:]
    if body.endswith("|"):
        body = body[:-1]
    return [clean_cell(cell) for cell in body.split("|")]


def find_columns(header: list[str]) -> dict[str, int]:
    aliases = {
        "crate": ("crate", "name"),
        "version": ("version",),
        "license": ("licence", "license"),
        "downloads": ("downloads", "traction"),
        "updated": ("last update", "last release", "updated", "last updated"),
        "decision": ("decision", "verdict"),
    }

    columns: dict[str, int] = {}
    lowered = [cell.lower() for cell in header]
    for key, names in aliases.items():
        for index, cell in enumerate(lowered):
            if any(cell == name for name in names):
                columns[key] = index
                break

    return columns


def parse_document(path: str) -> tuple[list[dict], list[str]]:
    """Read the register. Returns the rows and the problems found while reading it."""

    rows: list[dict] = []
    problems: list[str] = []
    columns: dict[str, int] | None = None

    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()

    for number, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped.startswith("|"):
            columns = None
            continue

        cells = split_row(stripped)
        if columns is None:
            if cells and cells[0].lower() in ("crate", "name"):
                columns = find_columns(cells)
                missing = {"crate", "license", "updated", "decision"} - set(columns)
                if missing:
                    problems.append(
                        f"{DOCUMENT}: the register table at line {number} lacks "
                        f"the column(s) {', '.join(sorted(missing))}"
                    )
            continue

        if all(set(cell) <= set("-: ") for cell in cells):
            continue  # the |---|---| separator
        if not cells or not cells[0]:
            continue

        def cell(key: str) -> str:
            index = columns.get(key)
            return cells[index] if index is not None and index < len(cells) else ""

        crate = cell("crate").strip()
        if not crate:
            continue

        license = cell("license")
        decision_cell = cell("decision")
        date_match = ISO_DATE.search(cell("updated"))

        decision_match = re.match(r"[A-Za-z]+", decision_cell)
        decision = decision_match.group(0).lower() if decision_match else ""
        reason = decision_cell[decision_match.end() :] if decision_match else decision_cell
        reason = re.sub(r"^[\s—–:-]+", "", reason).strip()

        row = {
            "crate": crate,
            "key": normalize_crate(crate),
            "version": cell("version"),
            "license": license,
            "downloads": cell("downloads"),
            "updated": date_match.group(0) if date_match else "",
            "decision": decision,
            "reason": reason,
            "line": number,
        }

        if not license:
            problems.append(f"{DOCUMENT}:{number}: `{crate}` has no licence")
        if not row["updated"]:
            problems.append(
                f"{DOCUMENT}:{number}: `{crate}` has no ISO date (YYYY-MM-DD) in the Last update column"
            )
        if decision not in DECISIONS:
            problems.append(
                f"{DOCUMENT}:{number}: `{crate}` has decision '{decision or decision_cell}', "
                f"expected one of {', '.join(DECISIONS)}"
            )
        if decision in DECISIONS and len(reason) < MIN_REASON:
            problems.append(f"{DOCUMENT}:{number}: `{crate}` records a decision with no reason")

        rows.append(row)

    if not rows:
        problems.append(f"{DOCUMENT}: no register table found")

    return rows, problems


# ---------------------------------------------------------------------------------------------
# crates.io
# ---------------------------------------------------------------------------------------------


def fetch_crate(name: str) -> dict:
    """Ask crates.io for one crate. Raises RuntimeError when it cannot be answered."""

    url = API + name
    request = urllib.request.Request(
        url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"}
    )

    last: str = ""
    for attempt in range(1, HTTP_ATTEMPTS + 1):
        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return {"crate": None, "versions": []}
            last = f"HTTP {error.code}"
            if error.code not in (429, 500, 502, 503, 504):
                break

        except (urllib.error.URLError, TimeoutError, OSError) as error:
            last = str(error)

        if attempt < HTTP_ATTEMPTS:
            time.sleep(HTTP_PAUSE * 3 * attempt)

    raise RuntimeError(f"crates.io could not be asked about `{name}`: {last}")


def license_of(payload: dict) -> str:
    """The licence crates.io publishes for the release the crate's max_version names."""

    crate = payload.get("crate") or {}
    versions = payload.get("versions") or []
    max_version = crate.get("max_version")

    for version in versions:
        if version.get("num") == max_version:
            return (version.get("license") or "").strip()
    for version in versions:
        if version.get("license"):
            return version["license"].strip()

    return ""


# ---------------------------------------------------------------------------------------------
# The checks
# ---------------------------------------------------------------------------------------------


def date_of(text: str) -> dt.date | None:
    match = ISO_DATE.search(text or "")
    if not match:
        return None
    try:
        return dt.date.fromisoformat(match.group(0))
    except ValueError:
        return None


def check(root: str, offline: bool) -> tuple[list[str], list[str], dict]:
    """Return (violations, notes, statistics)."""

    violations: list[str] = []
    notes: list[str] = []

    document = os.path.join(root, DOCUMENT)
    if not os.path.isfile(document):
        return [f"{DOCUMENT}: missing; the crate policy has no register"], notes, {}

    rows, problems = parse_document(document)
    violations.extend(problems)

    declared = declared_crates(root)
    documented = {row["key"]: row for row in rows}
    today = dt.date.today()

    # 1. Coverage: a crate in a manifest must have a row.
    for crate in sorted(declared):
        if normalize_crate(crate) not in documented:
            places = ", ".join(sorted(declared[crate]))
            violations.append(
                f"`{crate}` is declared in {places} but has no row in {DOCUMENT}"
            )

    # 2. An adopted crate must actually be declared somewhere.
    for row in rows:
        if row["decision"] == "adopted" and row["key"] not in {
            normalize_crate(crate) for crate in declared
        }:
            violations.append(
                f"{DOCUMENT}:{row['line']}: `{row['crate']}` is adopted but no Cargo.toml declares it"
            )

    statistics = {"documented": len(rows), "declared": len(declared), "live": 0}

    if offline:
        # The one-year rule read from the date the document records: all this mode can know.
        for row in rows:
            if row["decision"] not in ("adopted", "transitive"):
                continue

            recorded = date_of(row["updated"])
            if recorded is None:
                continue  # already reported by parse_document
            if (today - recorded).days > STALE_DAYS:
                violations.append(
                    f"`{row['crate']}` ({row['decision']}) has had no release since {recorded}, "
                    f"as {DOCUMENT} records it"
                )

        notes.append(
            "offline: crates.io was not asked; the licences and dates are the ones the document records"
        )
        return violations, notes, statistics

    # 4. Online: the licence and the release date crates.io publishes.
    for row in rows:
        payload = fetch_crate(row["crate"])
        crate = payload.get("crate")
        if not crate:
            violations.append(f"`{row['crate']}` is documented but crates.io does not know it")
            continue

        statistics["live"] += 1

        live_license = license_of(payload)
        if live_license and license_tokens(live_license) != license_tokens(row["license"]):
            violations.append(
                f"`{row['crate']}`: {DOCUMENT} records the licence '{row['license']}', "
                f"crates.io publishes '{live_license}'"
            )

        updated = date_of(crate.get("updated_at", ""))
        recorded = date_of(row["updated"])
        if updated is None:
            continue

        if recorded is not None and updated < recorded:
            notes.append(
                f"`{row['crate']}`: {DOCUMENT} records {recorded} but the last release is "
                f"{updated} — the row is ahead of crates.io"
            )
        elif recorded is not None and updated > recorded:
            notes.append(
                f"`{row['crate']}`: a newer release exists ({updated}); the row still records "
                f"{recorded}"
            )

        if row["decision"] in ("adopted", "transitive") and (today - updated).days > STALE_DAYS:
            violations.append(
                f"`{row['crate']}` ({row['decision']}) has had no release since {updated}"
            )

        time.sleep(HTTP_PAUSE)

    return violations, notes, statistics


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Check the Rust crates against docs/specifications/dependencies.md."
    )
    parser.add_argument(
        "--offline",
        action="store_true",
        help="make no HTTP call; check coverage and the one-year rule from the document alone",
    )
    parser.add_argument(
        "--root",
        default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        help="repository root (defaults to the parent of tools/)",
    )
    arguments = parser.parse_args(argv)

    root = os.path.abspath(arguments.root)
    mode = "offline" if arguments.offline else "online"
    print(f"dependency policy: {mode} check of {root}", flush=True)

    try:
        violations, notes, statistics = check(root, arguments.offline)
    except RuntimeError as error:
        print(f"cannot verify: {error}", file=sys.stderr)
        return 2

    if statistics:
        print(
            f"  {statistics['documented']} crate(s) in the register, "
            f"{statistics['declared']} declared in Cargo.toml, "
            f"{statistics.get('live', 0)} confirmed against crates.io",
            flush=True,
        )

    for note in notes:
        print(f"  note: {note}", flush=True)

    if violations:
        print(f"\n{len(violations)} violation(s):", file=sys.stderr)
        for violation in violations:
            print(f"  - {violation}", file=sys.stderr)
        return 1

    print("  ok: every manifest entry is recorded, no licence moved, nothing is stale")
    return 0


if __name__ == "__main__":
    sys.exit(main())
