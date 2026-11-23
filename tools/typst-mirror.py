#!/usr/bin/env python3
"""Fill and verify a mirror of `@preview` Typst packages.

A mirror is a directory laid out like Typst's own package cache --

    <mirror>/<namespace>/<name>/<version>/...

-- plus a `mirror.json` that lists, for every package it holds, every file with its SHA-256 and its
size. The native library reads that manifest before it builds a world, so a compile either runs over
the bytes the manifest describes or does not run at all.

Why: a cache is a directory somebody else can write to, and the compiler reads whatever it finds
there. A file edited in place is served without a word. A manifest makes that edit loud.

What verification proves is *integrity*, not *authenticity*: it says the files are the ones this
manifest describes. A mirror that can rewrite a file can rewrite the manifest too, so the manifest
has to come from somewhere the deployment trusts -- a signed image, a build artifact, a read-only
layer. See `docs/specifications/03-compilation.md` section 2.5.

Usage:

    # Fill a mirror from the registry (needs the network), then verify it.
    python3 tools/typst-mirror.py --mirror /opt/typst-mirror \\
        --package @preview/cetz:0.3.1 --package @preview/tablex:0.0.8

    # Fill it from a directory a `typst` CLI already populated, in an air-gapped job.
    python3 tools/typst-mirror.py --mirror /opt/typst-mirror --source ~/.cache/typst/packages \\
        --package @preview/cetz:0.3.1

    # Verify, in the container image or in the CI job that builds it.
    python3 tools/typst-mirror.py --mirror /opt/typst-mirror --verify

The script is standard library only, like the others under `tools/`, so it runs in a build image with
nothing installed but Python 3.11 or newer.

Exit status: 0 when the mirror is filled or verified, 1 when verification refuses it, 2 when the
request itself is wrong (an unreadable manifest, a package the registry does not serve).
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import shutil
import sys
import tarfile
import tempfile
import urllib.error
import urllib.request
from pathlib import Path

MANIFEST = "mirror.json"
FORMAT = 1
USER_AGENT = "typst-mirror/1 (+https://github.com/m3scalinum/text)"
DEFAULT_REGISTRY = "https://packages.typst.org"

SPEC = re.compile(r"^@(?P<namespace>[a-z0-9][a-z0-9-]*)/(?P<name>[a-z0-9][a-z0-9-]*)"
                  r":(?P<version>\d+\.\d+\.\d+)$")
VERSION = re.compile(r"^\d+\.\d+\.\d+$")


class MirrorError(Exception):
    """A request that cannot be carried out: usage, environment, registry."""


def parse_spec(spec: str) -> tuple[str, str, str]:
    """`@preview/cetz:0.3.1` into `("preview", "cetz", "0.3.1")`."""
    match = SPEC.match(spec)
    if match is None:
        raise MirrorError(f"`{spec}` is not a `@namespace/name:version` spec")
    return match["namespace"], match["name"], match["version"]


def package_dir(mirror: Path, spec: str) -> Path:
    namespace, name, version = parse_spec(spec)
    return mirror / namespace / name / version


def forward(path: Path, base: Path) -> str:
    """The path of `path` relative to `base`, with forward slashes."""
    return path.relative_to(base).as_posix()


def sha256_file(path: Path) -> tuple[str, int]:
    """The digest and the size of a file, read in chunks so a font does not sit in memory."""
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as handle:
        while chunk := handle.read(1 << 16):
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


def collect_files(directory: Path) -> dict[str, Path]:
    """Every file under `directory`, keyed by its path relative to it.

    Symbolic links are refused: a link can point outside the mirror, where verification and the
    compiler would read different bytes, which is exactly how a digest check gets bypassed.
    """
    files: dict[str, Path] = {}

    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            raise MirrorError(f"`{path}` is a symbolic link, and a mirror may not contain one")
        if path.is_file():
            files[forward(path, directory)] = path

    return files


def discover(mirror: Path) -> dict[str, list[str]]:
    """The packages the mirror holds, as `spec -> file paths`.

    Every `<namespace>/<name>/<version>` directory is a package; anything shallower is the mirror's
    own bookkeeping and is ignored. Discovering rather than trusting a list is what lets `fill` be
    run twice without losing the packages the first run added.
    """
    packages: dict[str, list[str]] = {}

    if not mirror.is_dir():
        return packages

    for namespace in sorted(mirror.iterdir()):
        if not namespace.is_dir() or namespace.name.startswith("."):
            continue
        for name in sorted(namespace.iterdir()):
            if not name.is_dir():
                continue
            for version in sorted(name.iterdir()):
                if not version.is_dir() or not VERSION.match(version.name):
                    continue
                spec = f"@{namespace.name}/{name.name}:{version.name}"
                packages[spec] = sorted(collect_files(version))

    return packages


def write_manifest(mirror: Path) -> dict[str, int]:
    """Writes `mirror.json` from what the mirror holds, and reports what was written."""
    packages: dict[str, list[dict[str, object]]] = {}
    total_bytes = 0
    total_files = 0

    for spec, paths in discover(mirror).items():
        entries = []
        for relative in paths:
            digest, size = sha256_file(mirror / spec_to_path(spec) / relative)
            entries.append({"path": relative, "sha256": digest, "size": size})
            total_bytes += size
            total_files += 1
        packages[spec] = entries

    manifest = {
        "format": FORMAT,
        "generatedBy": USER_AGENT,
        "packages": packages,
    }

    (mirror / MANIFEST).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    return {"packages": len(packages), "files": total_files, "bytes": total_bytes}


def spec_to_path(spec: str) -> Path:
    namespace, name, version = parse_spec(spec)
    return Path(namespace) / name / version


def load_manifest(mirror: Path) -> dict:
    """Reads and validates `mirror.json`."""
    path = mirror / MANIFEST

    if not path.is_file():
        raise MirrorError(
            f"package mirror `{mirror}` has no {MANIFEST}; create one with "
            f"`python3 tools/typst-mirror.py --mirror {mirror} --package @preview/<name>:<version>`")

    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise MirrorError(f"`{path}` is not valid JSON: {error}") from error

    if not isinstance(document, dict) or "packages" not in document:
        raise MirrorError(f"`{path}` has no `packages` object")

    if document.get("format") != FORMAT:
        raise MirrorError(
            f"`{path}` declares manifest format {document.get('format')!r}, and this script writes "
            f"and reads {FORMAT}")

    if not isinstance(document["packages"], dict):
        raise MirrorError(f"`{path}`'s `packages` is not an object")

    return document


def verify(mirror: Path) -> list[str]:
    """Checks the mirror against its manifest, returning one message per problem.

    The wording matches the native library's, because an operator reads whichever of the two fires
    first and has to reach the same conclusion from either.
    """
    document = load_manifest(mirror)
    problems: list[str] = []

    for spec, entries in document["packages"].items():
        try:
            directory = package_dir(mirror, spec)
        except MirrorError:
            problems.append(f"`{spec}` is not a `@namespace/name:version` spec")
            continue

        if not directory.is_dir():
            problems.append(
                f"package `{spec}` is listed in the mirror at `{mirror}` but `{directory}` does not "
                f"exist")
            continue

        present = collect_files(directory)

        for entry in entries:
            relative = entry.get("path")
            if relative not in present:
                problems.append(f"package `{spec}` in the mirror at `{mirror}` is missing `{relative}`")
                continue

            digest, size = sha256_file(present[relative])

            if "size" in entry and entry["size"] != size:
                problems.append(
                    f"package `{spec}` in the mirror at `{mirror}` has a `{relative}` of {size} "
                    f"bytes, and the manifest records {entry['size']}")
                continue

            if entry.get("sha256", "").lower() != digest:
                problems.append(
                    f"package `{spec}` in the mirror at `{mirror}` has a `{relative}` whose SHA-256 "
                    f"is {digest}, and the manifest records {entry.get('sha256')}")

        listed = {entry.get("path") for entry in entries}
        for relative in present:
            if relative not in listed:
                problems.append(
                    f"package `{spec}` in the mirror at `{mirror}` holds `{relative}`, which "
                    f"{MANIFEST} does not list")

    for spec in discover(mirror):
        if spec not in document["packages"]:
            problems.append(
                f"the mirror at `{mirror}` holds package `{spec}`, which {MANIFEST} does not list")

    return problems


def download(spec: str, destination: Path, registry: str) -> None:
    """Downloads `spec` from a Universe-compatible registry and unpacks it into `destination`."""
    namespace, name, version = parse_spec(spec)
    url = f"{registry.rstrip('/')}/{namespace}/{name}-{version}.tar.gz"

    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            archive = response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            raise MirrorError(f"`{spec}` is not in the registry at `{registry}` ({url})") from error
        raise MirrorError(f"`{url}` answered {error.code} {error.reason}") from error
    except urllib.error.URLError as error:
        raise MirrorError(f"`{url}` could not be reached: {error.reason}") from error

    unpack(io.BytesIO(archive), destination, url)


def unpack(archive: io.BytesIO, destination: Path, origin: str) -> None:
    """Extracts a tarball, refusing any member that could escape `destination`."""
    destination.mkdir(parents=True, exist_ok=True)

    with tarfile.open(fileobj=archive, mode="r:gz") as tar:
        for member in tar.getmembers():
            target = Path(member.name)

            if target.is_absolute() or ".." in target.parts:
                raise MirrorError(f"`{origin}` holds `{member.name}`, which escapes the package")

            if not (member.isfile() or member.isdir()):
                raise MirrorError(f"`{origin}` holds `{member.name}`, which is not a file or directory")

            tar.extract(member, destination, set_attrs=False)


def copy_from(source: Path, spec: str, destination: Path) -> None:
    """Copies a package out of a directory a `typst` CLI already populated."""
    origin = source / spec_to_path(spec)

    if not origin.is_dir():
        raise MirrorError(f"`{spec}` is not in `{source}` (expected `{origin}`)")

    shutil.copytree(origin, destination, dirs_exist_ok=True)


def fill(mirror: Path, specs: list[str], registry: str, source: Path | None, force: bool) -> None:
    """Adds `specs` to the mirror, then rewrites the manifest."""
    mirror.mkdir(parents=True, exist_ok=True)

    for spec in specs:
        destination = package_dir(mirror, spec)

        if destination.is_dir() and not force and any(destination.iterdir()):
            print(f"{spec}: already in the mirror", file=sys.stderr)
            continue

        # Unpack beside the package, then move: an interrupted download must not leave a
        # half-populated package that the manifest would then describe.
        staging = Path(tempfile.mkdtemp(prefix="typst-mirror-", dir=mirror))

        try:
            if source is not None:
                copy_from(source, spec, staging / "package")
            else:
                download(spec, staging / "package", registry)

            destination.parent.mkdir(parents=True, exist_ok=True)
            if destination.exists():
                shutil.rmtree(destination)
            shutil.move(str(staging / "package"), str(destination))
        finally:
            shutil.rmtree(staging, ignore_errors=True)

        print(f"{spec}: fetched into {destination}", file=sys.stderr)

    counts = write_manifest(mirror)
    print(
        f"{mirror}/{MANIFEST}: {counts['packages']} package(s), {counts['files']} file(s), "
        f"{counts['bytes']} byte(s)")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Fill or verify a mirror of @preview Typst packages.",
        epilog="Exit status: 0 success, 1 verification refused the mirror, 2 the request was wrong.")
    parser.add_argument("--mirror", required=True, type=Path,
                        help="the directory to fill or verify")
    parser.add_argument("--package", action="append", default=[], metavar="SPEC", dest="packages",
                        help="@namespace/name:version to add; repeatable")
    parser.add_argument("--registry", default=DEFAULT_REGISTRY,
                        help=f"registry base URL (default {DEFAULT_REGISTRY})")
    parser.add_argument("--source", type=Path, default=None,
                        help="copy packages from this directory instead of downloading them")
    parser.add_argument("--force", action="store_true",
                        help="re-fetch a package already in the mirror")
    parser.add_argument("--verify", action="store_true",
                        help="verify the mirror against its manifest and change nothing")

    arguments = parser.parse_args(argv)

    try:
        if arguments.verify:
            if arguments.packages:
                raise MirrorError("--verify and --package are mutually exclusive")
            problems = verify(arguments.mirror)

            if problems:
                for problem in problems:
                    print(f"refused: {problem}", file=sys.stderr)
                return 1

            document = load_manifest(arguments.mirror)
            files = sum(len(entries) for entries in document["packages"].values())
            print(f"{arguments.mirror}: {len(document['packages'])} package(s), {files} file(s) "
                  f"match {MANIFEST}")
            return 0

        if not arguments.packages:
            raise MirrorError("nothing to do: pass --package, or --verify")

        fill(arguments.mirror, arguments.packages, arguments.registry, arguments.source,
             arguments.force)
        return 0
    except MirrorError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
