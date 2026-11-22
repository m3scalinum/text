#!/usr/bin/env python3
"""Hold every crate's `rust-version` to the floor of its dependency graph.

`rust-version` is a promise to whoever builds this repository: with that toolchain or newer, the
crate compiles. It is not a stylistic choice and it cannot be estimated by reading a changelog --
a crate's floor is the maximum `rust-version` declared by the packages it actually pulls in, plus
the floor its own edition imposes. A manifest that says `1.88` while `zip 9.8.1` asks for `1.89`
turns a correct `cargo build` on an old toolchain into a confusing compile error; a manifest that
says `1.94` while nothing in the tree asks for more than `1.89` refuses toolchains that would have
worked.

This script computes the floor from the resolved graph instead of trusting the number in the
manifest, compares the two, and names the dependency that sets the floor when they differ.

Two modes:

* default -- ask `cargo metadata --locked` for each crate. The committed `Cargo.lock` is what
  decides, so the answer is the graph that will really be built, dev-dependencies and
  target-specific dependencies included. Needs cargo and, on a cold cache, the network.
* `--offline` -- make no cargo call at all: read each `Cargo.lock` for the resolved set, then read
  the `rust-version` field out of every dependency's manifest in the cargo registry cache. A
  manifest that is not cached cannot be read; the script then falls back to the crate's own
  declared value, prints a `WARNING` naming every package it could not read, and says the verdict
  is degraded. This is the mode for a machine without the registry, not the mode for CI.

Exit status: 0 when every crate is aligned, 1 when one is not, 2 when the check could not run at
all (no cargo, an unreadable lock, a manifest without `rust-version`). Standard library only,
Python 3.11 or newer for `tomllib`.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

try:
    import tomllib
except ModuleNotFoundError:  # pragma: no cover - the message is the point
    sys.stderr.write("check-msrv.py needs Python 3.11 or newer for tomllib\n")
    raise SystemExit(2)

REPO_ROOT = Path(__file__).resolve().parent.parent
NATIVE_DIR = REPO_ROOT / "native"

# The lowest toolchain each edition compiles with, from the Rust edition guide. Edition 2015 has no
# minimum beyond 1.0; the others were introduced with the release named here.
EDITION_FLOOR = {
    "2015": "1.0",
    "2018": "1.31",
    "2021": "1.56",
    "2024": "1.85",
}


class CheckError(Exception):
    """A condition that stops the check from producing a verdict."""


# ---------------------------------------------------------------------------------------------
# Versions
# ---------------------------------------------------------------------------------------------


def parse_version(text: str) -> tuple[int, int, int]:
    """Turn `1.85`, `1.94.0` or `1.85.0` into a comparable triple."""
    parts = text.strip().split(".")
    if not 2 <= len(parts) <= 3:
        raise CheckError(f"{text!r} is not a major.minor[.patch] version")

    try:
        numbers = [int(part) for part in parts]
    except ValueError as error:
        raise CheckError(f"{text!r} is not a major.minor[.patch] version") from error

    while len(numbers) < 3:
        numbers.append(0)

    return numbers[0], numbers[1], numbers[2]


def format_version(version: tuple[int, int, int]) -> str:
    """Render a triple the way Cargo writes it: `1.85`, or `1.85.3` when a patch matters."""
    major, minor, patch = version
    return f"{major}.{minor}" if patch == 0 else f"{major}.{minor}.{patch}"


def read_rust_version(manifest: dict, origin: str) -> str | None:
    """The `rust-version` of a package table, or None when the manifest does not declare one."""
    version = manifest.get("rust-version")
    if version is None:
        return None
    # `rust-version.workspace = true` needs the workspace root, which a published crate never has
    # and which this repository does not use. Say so rather than guess.
    if not isinstance(version, str):
        raise CheckError(f"{origin}: rust-version is not a literal string")
    return version


def edition_floor(manifest: dict, origin: str) -> tuple[tuple[int, int, int], str]:
    """The floor the package's own edition imposes, with the edition for the report."""
    edition = manifest.get("edition", "2015")
    if not isinstance(edition, str) or edition not in EDITION_FLOOR:
        raise CheckError(f"{origin}: unsupported edition {edition!r}")
    return parse_version(EDITION_FLOOR[edition]), edition


# ---------------------------------------------------------------------------------------------
# Manifests and the resolved graph
# ---------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Package:
    """One node of the resolved graph: enough to name it and read its floor."""

    name: str
    version: str
    rust_version: tuple[int, int, int] | None
    # How the package is reached from the crate under test: "normal", "build" or "dev".
    kinds: tuple[str, ...] = ()


@dataclass
class Graph:
    """The dependency graph of one crate, keyed by a stable package key."""

    root_key: str
    packages: dict[str, Package]
    edges: dict[str, list[str]]
    edition: str
    edition_floor: tuple[int, int, int]
    declared_raw: str | None
    # Package keys whose manifest could not be read (offline mode only).
    unreadable: list[str] = field(default_factory=list)


@dataclass
class Assessment:
    """The verdict for one crate."""

    crate_dir: Path
    graph: Graph

    @property
    def declared(self) -> tuple[int, int, int] | None:
        return None if self.graph.declared_raw is None else parse_version(self.graph.declared_raw)

    @property
    def required(self) -> tuple[int, int, int]:
        """The floor: the highest `rust-version` reachable from the root, or the edition's."""
        floor = self.graph.edition_floor
        for key, package in self.graph.packages.items():
            if key == self.graph.root_key or package.rust_version is None:
                continue
            floor = max(floor, package.rust_version)
        return floor

    @property
    def status(self) -> str:
        if self.graph.declared_raw is None:
            return "missing"
        declared = self.declared
        assert declared is not None
        if declared < self.required:
            return "too-low"
        if declared > self.required:
            return "too-high"
        return "ok"

    def origin(self) -> str:
        """The dependency that sets the floor, with the path that reaches it."""
        required = self.required
        candidates = [
            (key, package)
            for key, package in self.graph.packages.items()
            if key != self.graph.root_key and package.rust_version == required
        ]

        if not candidates:
            return f"edition {self.graph.edition} (floor {format_version(self.graph.edition_floor)})"

        # Prefer the shortest path from the crate, so the explanation names something a reader can
        # find in the manifest; ties break on the name to keep the report stable.
        parents, distance = self._paths()
        key = min(candidates, key=lambda c: (distance.get(c[0], 1 << 30), c[1].name))[0]
        labels: list[str] = []
        node: str | None = key
        while node is not None and node != self.graph.root_key:
            package = self.graph.packages[node]
            labels.append(f"{package.name} {package.version}")
            node = parents[node]
        labels.reverse()

        read = " › ".join(labels)
        shared = len(candidates)
        suffix = f" — {shared} packages declare {format_version(required)}" if shared > 1 else ""
        return f"{read}{suffix}"

    def _paths(self) -> tuple[dict[str, str | None], dict[str, int]]:
        """Breadth-first parents and distances from the root, so a chain can be explained."""
        parents: dict[str, str | None] = {self.graph.root_key: None}
        distance: dict[str, int] = {self.graph.root_key: 0}
        queue = [self.graph.root_key]
        while queue:
            current = queue.pop(0)
            for child in self.graph.edges.get(current, []):
                if child in parents:
                    continue
                parents[child] = current
                distance[child] = distance[current] + 1
                queue.append(child)

        return parents, distance


def crate_dirs(selected: list[str] | None) -> list[Path]:
    """Every crate directory under native/, or the ones named on the command line."""
    if not NATIVE_DIR.is_dir():
        raise CheckError(f"{NATIVE_DIR} does not exist; run the script from the repository")

    found = sorted(p.parent for p in NATIVE_DIR.glob("*/Cargo.toml"))
    if selected:
        wanted = {name.lower() for name in selected}
        found = [p for p in found if p.name.lower() in wanted]
        missing = wanted - {p.name.lower() for p in found}
        if missing:
            raise CheckError(f"no crate named {', '.join(sorted(missing))} under native/")

    if not found:
        raise CheckError(f"no Cargo.toml under {NATIVE_DIR}")

    return found


def graph_from_metadata(crate_dir: Path) -> Graph:
    """Build the graph from `cargo metadata --locked`, which is the graph Cargo will build."""
    manifest = crate_dir / "Cargo.toml"
    command = [
        "cargo",
        "metadata",
        "--format-version",
        "1",
        "--locked",
        "--manifest-path",
        str(manifest),
    ]

    try:
        completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    except FileNotFoundError as error:
        raise CheckError("cargo is not installed; use --offline to skip the registry") from error

    if completed.returncode != 0:
        raise CheckError(
            f"`cargo metadata --locked` failed for {manifest} (the lock is stale or the registry "
            f"is unreachable, which is not an MSRV verdict):\n{completed.stderr.strip()}"
        )

    metadata = json.loads(completed.stdout)
    by_id = {package["id"]: package for package in metadata["packages"]}
    nodes = {node["id"]: node for node in metadata["resolve"]["nodes"]}

    root = next(
        (
            package
            for package in metadata["packages"]
            if Path(package["manifest_path"]).resolve() == manifest.resolve()
        ),
        None,
    )
    if root is None:
        raise CheckError(f"{manifest} is not a package of its own metadata")

    manifest_table = tomllib.loads(manifest.read_text(encoding="utf-8"))
    floor, edition = edition_floor(manifest_table["package"], str(manifest))
    declared = read_rust_version(manifest_table["package"], str(manifest))

    packages: dict[str, Package] = {}
    edges: dict[str, list[str]] = {}

    def visit(package_id: str, kinds: tuple[str, ...] = ()) -> None:
        package = by_id[package_id]
        key = package["id"]
        known = packages.get(key)
        if known is None:
            rust_version = package.get("rust_version")
            packages[key] = Package(
                name=package["name"],
                version=package["version"],
                rust_version=None if rust_version is None else parse_version(rust_version),
                kinds=kinds,
            )
        elif kinds and not known.kinds:
            packages[key] = Package(known.name, known.version, known.rust_version, kinds)

    visit(root["id"])
    edges[root["id"]] = []

    visited = {root["id"]}
    queue = [root["id"]]
    while queue:
        current = queue.pop(0)
        for dependency in nodes.get(current, {}).get("deps", []):
            child = dependency["pkg"]
            edge_kinds = tuple(sorted({kind["kind"] or "normal" for kind in dependency["dep_kinds"]}))
            visit(child, edge_kinds)
            edges.setdefault(current, [])
            if child not in edges[current]:
                edges[current].append(child)
            edges.setdefault(child, [])
            if child not in visited:
                visited.add(child)
                queue.append(child)

    return Graph(
        root_key=root["id"],
        packages=packages,
        edges=edges,
        edition=edition,
        edition_floor=floor,
        declared_raw=declared,
    )


def graph_from_lockfile(crate_dir: Path) -> Graph:
    """Build the graph from Cargo.lock and the registry cache, without calling cargo."""
    manifest_path = crate_dir / "Cargo.toml"
    manifest_table = tomllib.loads(manifest_path.read_text(encoding="utf-8"))
    package_table = manifest_table["package"]
    floor, edition = edition_floor(package_table, str(manifest_path))
    declared = read_rust_version(package_table, str(manifest_path))
    name = package_table["name"]
    version = package_table["version"]

    lock_path = crate_dir / "Cargo.lock"
    if not lock_path.is_file():
        raise CheckError(f"{lock_path} is missing; the committed lock is what the check reads")
    lock = tomllib.loads(lock_path.read_text(encoding="utf-8"))

    entries: list[dict] = lock.get("package", [])
    root_entry = next(
        (
            entry
            for entry in entries
            if entry.get("name") == name
            and entry.get("version") == version
            and "source" not in entry
        ),
        None,
    )
    if root_entry is None:
        raise CheckError(f"{lock_path} has no local package {name} {version}")

    by_name: dict[str, list[dict]] = {}
    for entry in entries:
        by_name.setdefault(entry["name"], []).append(entry)

    def key_of(entry: dict) -> str:
        return f"{entry['name']} {entry['version']}"

    def resolve(reference: str) -> dict:
        """A Cargo.lock dependency reference is `name`, or `name version` when the name repeats."""
        direct = entries_by_key.get(reference)
        if direct is not None:
            return direct
        matches = by_name.get(reference, [])
        if len(matches) != 1:
            raise CheckError(f"{lock_path}: cannot disambiguate dependency {reference!r}")
        return matches[0]

    entries_by_key = {key_of(entry): entry for entry in entries}

    cache_roots = registry_cache_roots()
    packages: dict[str, Package] = {}
    edges: dict[str, list[str]] = {}
    unreadable: list[str] = []

    def load(entry: dict) -> str:
        key = key_of(entry)
        if key in packages:
            return key

        rust_version: tuple[int, int, int] | None = None
        if "source" in entry:
            located = find_cached_manifest(name=entry["name"], version=entry["version"], roots=cache_roots)
            if located is None:
                unreadable.append(key)
            else:
                cached = tomllib.loads(located.read_text(encoding="utf-8"))
                raw = read_rust_version(cached.get("package", {}), str(located))
                rust_version = None if raw is None else parse_version(raw)

        packages[key] = Package(name=entry["name"], version=entry["version"], rust_version=rust_version)
        return key

    root_key = load(root_entry)
    edges[root_key] = []

    queue = [root_entry]
    while queue:
        current = queue.pop(0)
        current_key = key_of(current)
        for reference in current.get("dependencies", []):
            child = resolve(reference)
            child_key = load(child)
            edges.setdefault(current_key, [])
            if child_key not in edges[current_key]:
                edges[current_key].append(child_key)
            edges.setdefault(child_key, [])
            if child is not current and child not in queue:
                queue.append(child)

    return Graph(
        root_key=root_key,
        packages=packages,
        edges=edges,
        edition=edition,
        edition_floor=floor,
        declared_raw=declared,
        unreadable=unreadable,
    )


def registry_cache_roots() -> list[Path]:
    """Every `registry/src/<index>/` directory in the cargo home."""
    cargo_home = Path(os.environ.get("CARGO_HOME", Path.home() / ".cargo"))
    return sorted((cargo_home / "registry" / "src").glob("*"))


def find_cached_manifest(name: str, version: str, roots: list[Path]) -> Path | None:
    """The extracted `Cargo.toml` of `name-version`, if the crate has ever been fetched."""
    for root in roots:
        candidate = root / f"{name}-{version}" / "Cargo.toml"
        if candidate.is_file():
            return candidate
    return None


# ---------------------------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------------------------


def report(assessments: list[Assessment], verbose: bool) -> int:
    """Print the table and every mismatch; return the exit status."""
    failures = [a for a in assessments if a.status != "ok"]
    warnings = [a for a in assessments if a.graph.unreadable]

    header = f"{'crate':<16} {'declared':<9} {'required':<9} {'status':<9} imposed by"
    print(header)
    print("-" * len(header))
    for assessment in assessments:
        declared = assessment.graph.declared_raw or "-"
        required = format_version(assessment.required)
        print(
            f"{assessment.crate_dir.name:<16} {declared:<9} {required:<9} "
            f"{assessment.status:<9} {assessment.origin()}"
        )
        if verbose:
            for key, package in sorted(assessment.graph.packages.items()):
                if package.rust_version is None:
                    continue
                kinds = ",".join(package.kinds) if package.kinds else "-"
                print(f"    {package.name} {package.version} [{kinds}] {format_version(package.rust_version)}")

    # Keep the table and the verdicts in the order they are meant to be read, even when stdout is a
    # pipe and stderr is not.
    sys.stdout.flush()

    for assessment in warnings:
        print(
            f"\nWARNING: {assessment.crate_dir.name}: {len(assessment.graph.unreadable)} dependency "
            "manifest(s) are not in the cargo cache, so their rust-version could not be read and the "
            "floor is degraded (the crate's own declared value is the only evidence):",
            file=sys.stderr,
        )
        for key in sorted(assessment.graph.unreadable):
            print(f"    {key}", file=sys.stderr)

    for assessment in failures:
        name = assessment.crate_dir.name
        manifest = assessment.crate_dir / "Cargo.toml"
        if assessment.status == "missing":
            print(
                f"\n{manifest}: no `rust-version` under [package]. The graph requires "
                f"{format_version(assessment.required)}; write `rust-version = "
                f'"{format_version(assessment.required)}"`.',
                file=sys.stderr,
            )
            continue

        direction = "too low" if assessment.status == "too-low" else "too high"
        print(
            f"\n{manifest}: `rust-version = \"{assessment.graph.declared_raw}\"` is {direction}. "
            f"The graph requires {format_version(assessment.required)}.",
            file=sys.stderr,
        )
        print(f"    imposed by {assessment.origin()}", file=sys.stderr)
        if assessment.status == "too-low":
            print(
                f'    Raise it to `rust-version = "{format_version(assessment.required)}"`; a lower '
                "value promises a toolchain the dependency refuses.",
                file=sys.stderr,
            )
        else:
            print(
                f'    Lower it to `rust-version = "{format_version(assessment.required)}"`; the '
                "higher value refuses toolchains that would compile the crate.",
                file=sys.stderr,
            )

    if failures:
        print(
            f"\n{len(failures)} of {len(assessments)} crate(s) are not aligned.",
            file=sys.stderr,
        )
        return 1

    if warnings:
        print(
            f"\n{len(assessments)} crates read, {len(warnings)} with a degraded floor (see above).",
            file=sys.stderr,
        )
    else:
        print(f"\n{len(assessments)} crates aligned.")
    return 0


# ---------------------------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------------------------


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Compare every crate's rust-version with the floor of its dependency graph."
    )
    parser.add_argument(
        "--offline",
        action="store_true",
        help="read Cargo.lock and the cargo registry cache instead of running cargo metadata",
    )
    parser.add_argument("--verbose", action="store_true", help="list every package that declares a floor")
    parser.add_argument("crates", nargs="*", help="crate directory names to check (default: all)")
    arguments = parser.parse_args(argv)

    try:
        directories = crate_dirs(arguments.crates or None)
        build = graph_from_lockfile if arguments.offline else graph_from_metadata
        assessments = [Assessment(crate_dir=directory, graph=build(directory)) for directory in directories]
    except CheckError as error:
        print(f"check-msrv: {error}", file=sys.stderr)
        return 2

    return report(assessments, arguments.verbose)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
