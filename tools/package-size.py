#!/usr/bin/env python3
"""Report what a `.nupkg` costs a consumer, per runtime identifier.

NuGet 3 has no partial download: a package is one immutable zip behind one URL, and a restore
fetches the whole file. The RID decides which assets a build *copies out* of it, never which bytes
cross the wire. So the download price of `M3scalinum.Text.Typst` is the sum of the six native
libraries it carries (Spec 08 §6), and the useful question is not "how big is the package" but "how
much of it can the build on this machine reach".

This script answers both from the packed file, without a feed, a restore or a network call:

* every `runtimes/<rid>/native/` entry, its compressed size, and its share of the package;
* the rest -- `lib/`, the licence files, the nuspec -- as one shared cost;
* for each RID the package carries, what a consumer on that RID downloads (whole package) against
  what their build would copy (that RID's native entry plus the shared cost), and the difference.

Pass several packages to model a split layout: the shared cost is then summed across them, so a
satellite package set can be compared with the single package it would replace.

`--split` models what a consumer of a satellite layout downloads: a package that carries their RID
(or no native at all) is fetched whole, a package that carries only other RIDs is not fetched at
all. Without it every package passed is assumed to be downloaded, which is the right model for one
package and the wrong one for a set.

Usage: package-size.py <package.nupkg|directory>... [--top N] [--split] [--json]

Sizes are bytes divided by 10^6 (MB) in the table and by 2^20 (MiB) in the total line, so a number
here can be compared with `ls -l`, `du` and a browser's download dialog without a conversion.
Exit status: 0 when every package was read, 1 when one could not be opened, 2 on bad arguments.
Standard library only: the script must run on a bare CPython.
"""

from __future__ import annotations

import argparse
import json
import sys
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

NATIVE_PREFIX = "runtimes/"


def mb(count: int) -> float:
    """Bytes as MB (10^6), the unit of the tables."""
    return count / 1e6


@dataclass
class Package:
    path: Path
    #: Compressed size of every entry, keyed by its name in the zip.
    entries: dict[str, int] = field(default_factory=dict)
    #: Compressed size of each RID's `native/` subtree.
    natives: dict[str, int] = field(default_factory=dict)

    @property
    def total(self) -> int:
        return sum(self.entries.values())

    @property
    def shared(self) -> int:
        """Everything a build copies whatever the RID: the assemblies, the nuspec, the licences."""
        return self.total - sum(self.natives.values())

    @property
    def name(self) -> str:
        return self.path.name


def native_rid(entry: str) -> str | None:
    """The RID an entry belongs to, or `None` when the entry is shared.

    `runtimes/<rid>/native/<file>` is the layout the packages use (Spec 08 §6); anything else under
    `runtimes/` -- `runtimes/<rid>/lib/<tfm>/` -- is framework-specific and is left in the shared
    cost, which overstates it slightly and never understates a native.
    """
    if not entry.startswith(NATIVE_PREFIX):
        return None

    parts = entry.split("/")

    if len(parts) < 4 or parts[2] != "native":
        return None

    return parts[1]


def read(path: Path) -> Package:
    package = Package(path=path)

    with zipfile.ZipFile(path) as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue

            package.entries[info.filename] = info.compress_size
            rid = native_rid(info.filename)

            if rid is not None:
                package.natives[rid] = package.natives.get(rid, 0) + info.compress_size

    return package


def report(packages: list[Package], top: int, split: bool) -> None:
    shared_total = sum(package.shared for package in packages)
    natives_total = sum(sum(package.natives.values()) for package in packages)
    grand_total = shared_total + natives_total

    for package in packages:
        print(f"{package.name} — {mb(package.total):.2f} MB ({package.total / 2**20:.2f} MiB)")

        rows = sorted(
            ((size, name) for name, size in package.entries.items()),
            reverse=True,
        )

        for size, name in rows[:top]:
            print(f"  {name:<58} {mb(size):>8.2f} MB  {100 * size / package.total:>5.1f}%")

        if len(rows) > top:
            rest = sum(size for size, _ in rows[top:])
            print(f"  {'(' + str(len(rows) - top) + ' more entries)':<58} {mb(rest):>8.2f} MB  {100 * rest / package.total:>5.1f}%")

        print()

    # One set of packages, one consumer: the shared cost is paid once, the native of their RID once.
    rids = sorted({rid for package in packages for rid in package.natives})

    print(f"{'RID':<16}{'downloads':>12}{'copies':>12}{'unreachable':>14}")

    for rid in rids:
        # In a split layout a consumer restores only the packages that carry their RID or no native
        # at all; the satellites for the other RIDs are never fetched.
        fetched = [
            package for package in packages
            if not split or not package.natives or rid in package.natives
        ]
        downloaded = sum(package.total for package in fetched)
        shared = sum(package.shared for package in fetched)
        native = sum(package.natives.get(rid, 0) for package in fetched)
        copies = shared + native
        unreachable = downloaded - copies

        print(
            f"{rid:<16}{mb(downloaded):>10.2f} MB{mb(copies):>10.2f} MB"
            f"{mb(unreachable):>11.2f} MB  ({100 * unreachable / downloaded:.1f}%)"
        )

    layout = "split: one satellite plus the packages without natives" if split else "every package fetched"

    print(f"\n{'total':<16}{mb(grand_total):>10.2f} MB   shared {mb(shared_total):.2f} MB, "
          f"natives {mb(natives_total):.2f} MB, {len(packages)} package(s) — {layout}")


def main(paths: list[str], top: int, split: bool, as_json: bool) -> int:
    files: list[Path] = []

    for argument in paths:
        path = Path(argument)

        if path.is_dir():
            files.extend(sorted(path.glob("*.nupkg")))
        else:
            files.append(path)

    if not files:
        print("no .nupkg found", file=sys.stderr)
        return 2

    packages = []

    for path in files:
        try:
            packages.append(read(path))
        except (OSError, zipfile.BadZipFile) as error:
            print(f"{path}: {error}", file=sys.stderr)
            return 1

    if as_json:
        print(json.dumps(
            [
                {
                    "package": package.name,
                    "total": package.total,
                    "shared": package.shared,
                    "natives": package.natives,
                }
                for package in packages
            ],
            indent=2,
        ))
    else:
        report(packages, top, split)

    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="+", help="a .nupkg, or a directory of them")
    parser.add_argument("--top", type=int, default=10, help="entries to list per package (default 10)")
    parser.add_argument(
        "--split",
        action="store_true",
        help="model a satellite layout: a consumer fetches the packages carrying their RID, not the other RIDs'",
    )
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    arguments = parser.parse_args()

    sys.exit(main(arguments.paths, arguments.top, arguments.split, arguments.json))
