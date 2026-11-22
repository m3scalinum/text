#!/usr/bin/env python3
"""Writes and checks the CycloneDX SBOM of a packed M3scalinum .nupkg.

Why this script rather than `dotnet CycloneDX` or `cyclonedx-nuget`: those tools read a project or a
solution and describe the managed restore graph only. This package family also embeds a Rust
cdylib, whose dependency graph is a `Cargo.lock` no .NET tool reads, and whose platform assets are
directories inside the .nupkg (`runtimes/<rid>/native/`). The SBOM has to describe what the
artifact *contains*, not what the checkout resolves — so it is built from the artifact: the
`.nuspec` inside the package names the referenced packages, the `runtimes/` layout names the RIDs
and the native libraries, and each library names the crate whose `Cargo.lock` is the crate graph.
Python 3.11+ with its standard library is all it needs (`zipfile`, `tomllib`, `xml.etree`), which
every GitHub-hosted Ubuntu runner already has; no tool install, no network, no registry.

Three subcommands:

  generate --packages DIR --output DIR [--repo-root DIR]
      One `<id>-<version>.cdx.json` per `.nupkg`. Exit 1 when a native library names a crate the
      map below does not know: a silently crate-less SBOM is the failure this exists to prevent.

  verify --packages DIR --sbom DIR
      Re-derives what each package must list — crates, RIDs, declared dependencies — from the
      `.nupkg` itself and fails when the SBOM is empty, omits a crate the package embeds, or misses
      a RID the package ships.

  self-test [--repo-root DIR]
      Builds a synthetic package with one RID and one native library, then checks that `verify`
      passes it and rejects an empty SBOM, a crate-less SBOM and a RID-less SBOM.

Usage: python3 .github/sbom/sbom.py generate --packages artifacts/packages --output artifacts/sbom
"""

import argparse
import hashlib
import json
import pathlib
import re
import sys
import tempfile
import tomllib
import uuid
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone

# Native library stem (file name without lib/extension) -> (crate directory under native/, crate
# package name as it appears in Cargo.lock). The two differ: the crate is `sharp-text-docx`, its
# directory is `text-docx`. A package that starts shipping a new native library has to be added
# here; `generate` fails with the stem it could not place rather than emitting an SBOM with no
# Rust graph in it.
CRATE_BY_LIBRARY = {
    "sharp_typst": ("sharp-typst", "sharp-typst"),
    "sharp_text_barcodes": ("text-barcodes", "sharp-text-barcodes"),
    "sharp_text_ocr": ("text-ocr", "sharp-text-ocr"),
    "sharp_text_docx": ("text-docx", "sharp-text-docx"),
    "sharp_text_xlsx": ("text-xlsx", "sharp-text-xlsx"),
}

NUGET_PURL = "pkg:nuget/"
CARGO_PURL = "pkg:cargo/"


def local(tag):
    """Element tag without its XML namespace: the nuspec namespace changes between schema years."""
    return tag.rsplit("}", 1)[-1]


def library_stem(name):
    """`runtimes/osx/native/libsharp_text_docx.dylib` -> `sharp_text_docx`."""
    base = name.rsplit("/", 1)[-1]
    base = re.sub(r"\.(dll|so|dylib)$", "", base)
    return base.removeprefix("lib")


def read_nuspec(archive):
    """(id, version, [(dependency id, version range, target framework)]) from the package."""
    member = next((n for n in archive.namelist() if n.endswith(".nuspec") and "/" not in n), None)
    if member is None:
        raise SystemExit("package has no .nuspec at its root")
    root = ET.fromstring(archive.read(member))
    metadata = next((c for c in root if local(c.tag) == "metadata"), None)
    if metadata is None:
        raise SystemExit("nuspec has no <metadata>")
    ident = version = None
    for child in metadata:
        if local(child.tag) == "id":
            ident = (child.text or "").strip()
        elif local(child.tag) == "version":
            version = (child.text or "").strip()
    if not ident or not version:
        raise SystemExit("nuspec has no <id> or <version>")
    deps = []
    for group in metadata.iter():
        if local(group.tag) != "group":
            continue
        framework = group.attrib.get("targetFramework", "")
        for dep in group:
            if local(dep.tag) == "dependency":
                deps.append((dep.attrib.get("id", ""), dep.attrib.get("version", ""), framework))
    return ident, version, deps


def concrete_version(version_range):
    """`[0.51.0, )` and `0.51.0` both name 0.51.0; the range itself is kept as a property."""
    match = re.search(r"\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.]+)?", version_range or "")
    return match.group(0) if match else (version_range or "").strip("[]() ")


def package_assets(repo_root, package_id):
    """The `project.assets.json` of the project whose PackageId is `package_id`, if restored."""
    for assets in sorted(pathlib.Path(repo_root).glob("src/*/obj/project.assets.json")):
        try:
            data = json.loads(assets.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        project_path = ((data.get("project") or {}).get("restore") or {}).get("projectPath")
        if not project_path:
            continue
        csproj = assets.parents[1] / pathlib.Path(project_path).name
        if not csproj.is_file():
            continue
        try:
            declared = ET.parse(csproj).getroot()
        except ET.ParseError:
            continue
        ids = {(e.text or "").strip() for e in declared.iter() if local(e.tag) == "PackageId"}
        if package_id in ids:
            return data
    return None


def assets_components(assets, package_id):
    """Every package the restore graph resolved, marked direct when the package depends on it."""
    targets = assets.get("targets") or {}
    key = next((k for k in targets if k.startswith("net11.0")), next(iter(targets), None))
    if key is None:
        return []
    found = []
    for library, info in (targets.get(key) or {}).items():
        name, _, version = library.partition("/")
        if not name or name == package_id:
            continue
        kind = info.get("type", "package")
        # Build-only packages (SourceLink, ILLink, analyzers) are in the restore graph but not in
        # the shipped package: they are listed with scope "excluded" rather than as dependencies.
        runtime = kind == "project" or any(
            section in info for section in ("runtime", "compile", "runtimeTargets", "resources")
        )
        found.append(
            {
                "name": name,
                "version": version,
                "kind": kind,
                "runtime": runtime,
            }
        )
    return found


def cargo_components(archive, repo_root):
    """The resolved crate graph of every crate embedded in the package.

    Read from the crate's committed `Cargo.lock`, which is exactly what the release job built:
    `cargo build` writes it back, and the lock in the checkout is the lock on disk. Platform-only
    crates in the lock are components too — they are in the binary — but the graph is not filtered
    by target because the package ships one binary per RID and no lock is per-RID.
    """
    crates = []
    seen = set()
    for member in archive.namelist():
        parts = member.split("/")
        if len(parts) != 4 or parts[0] != "runtimes" or parts[2] != "native" or not parts[3]:
            continue
        stem = library_stem(member)
        mapped = CRATE_BY_LIBRARY.get(stem)
        if mapped is None:
            raise SystemExit(
                f"unknown native library '{parts[3]}' in runtimes/{parts[1]}/native: add its stem "
                f"'{stem}' to CRATE_BY_LIBRARY in .github/sbom/sbom.py so its Cargo.lock is listed"
            )
        crate_dir, crate_name = mapped
        if crate_name in seen:
            continue
        seen.add(crate_name)
        lock = pathlib.Path(repo_root) / "native" / crate_dir / "Cargo.lock"
        if not lock.is_file():
            raise SystemExit(f"crate '{crate_name}' is embedded but {lock} does not exist")
        with lock.open("rb") as handle:
            packages = tomllib.load(handle).get("package", [])
        for entry in packages:
            crates.append(
                {
                    "crate": crate_name,
                    "name": entry.get("name", ""),
                    "version": entry.get("version", ""),
                    "root": entry.get("name") == crate_name,
                }
            )
    return crates


def rid_layout(archive):
    """[(rid, member name, sha256)] for every native asset under runtimes/<rid>/native/."""
    layout = []
    for member in archive.namelist():
        parts = member.split("/")
        if len(parts) == 4 and parts[0] == "runtimes" and parts[2] == "native" and parts[3]:
            digest = hashlib.sha256(archive.read(member)).hexdigest()
            layout.append((parts[1], member, digest))
    return sorted(layout)


def assembly_layout(archive):
    """[(member name, sha256)] for every assembly under lib/.

    A package with no dependency and no native asset — `M3scalinum.Text.Documents` is one — would
    otherwise have an empty component list, which says "this package contains nothing" when it
    contains exactly the assemblies the consumer loads. Every shipped DLL is a component with its
    digest, so the SBOM is never empty for a package that has content and a consumer can check the
    bytes it runs.
    """
    assemblies = []
    for member in archive.namelist():
        if member.startswith("lib/") and member.endswith(".dll"):
            assemblies.append((member, hashlib.sha256(archive.read(member)).hexdigest()))
    return sorted(assemblies)


def build_sbom(package, repo_root):
    with zipfile.ZipFile(package) as archive:
        ident, version, deps = read_nuspec(archive)
        layout = rid_layout(archive)
        assemblies = assembly_layout(archive)
        crates = cargo_components(archive, repo_root)

        components = []
        seen = set()

        def add(component):
            key = (component["type"], component["purl"] or component["name"])
            if key in seen:
                return
            seen.add(key)
            components.append(component)

        # .NET: the declared dependencies are authoritative for what a consumer installs; the
        # restore graph adds what they pull in, with the version restore actually resolved.
        assets = package_assets(repo_root, ident)
        resolved = {c["name"]: c for c in assets_components(assets, ident)} if assets else {}
        for dep_id, dep_range, framework in deps:
            if not dep_id:
                continue
            hit = resolved.get(dep_id)
            if hit and hit["version"]:
                dep_version = hit["version"]
            elif hit is None and not dep_range:
                dep_version = ""
            else:
                dep_version = concrete_version(dep_range)
            add(
                {
                    "type": "library",
                    "name": dep_id,
                    "version": dep_version or "0.0.0",
                    "purl": f"{NUGET_PURL}{dep_id}@{dep_version or '0.0.0'}",
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "nuget"},
                        {"name": "m3scalinum:scope", "value": "direct"},
                        {"name": "m3scalinum:framework", "value": framework or "any"},
                        *(
                            [{"name": "m3scalinum:version-range", "value": dep_range}]
                            if dep_range and dep_range != dep_version
                            else []
                        ),
                    ],
                }
            )
        declared = {d[0] for d in deps}
        for entry in resolved.values():
            if entry["name"] in declared or not entry["version"]:
                continue
            add(
                {
                    "type": "library",
                    "name": entry["name"],
                    "version": entry["version"],
                    "purl": f"{NUGET_PURL}{entry['name']}@{entry['version']}",
                    "scope": "required" if entry["runtime"] else "excluded",
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "nuget"},
                        {
                            "name": "m3scalinum:scope",
                            "value": "required" if entry["runtime"] else "build-only",
                        },
                        {
                            "name": "m3scalinum:resolved-from",
                            "value": "project.assets.json" if entry["kind"] == "package" else "project-reference",
                        },
                    ],
                }
            )

        # Rust: one component per crate in each embedded crate's lock, with its own version.
        for entry in crates:
            add(
                {
                    "type": "library",
                    "name": entry["name"],
                    "version": entry["version"],
                    "purl": f"{CARGO_PURL}{entry['name']}@{entry['version']}",
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "cargo"},
                        {"name": "m3scalinum:embedded-crate", "value": entry["crate"]},
                        *([{"name": "m3scalinum:root", "value": "true"}] if entry["root"] else []),
                    ],
                }
            )

        # Shipped assemblies: the content a package at least always has, and the one thing a
        # consumer can compare byte for byte with what they run.
        for member, digest in assemblies:
            add(
                {
                    "type": "file",
                    "name": member,
                    "version": version,
                    "purl": None,
                    "hashes": [{"alg": "SHA-256", "content": digest}],
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "managed-assembly"},
                    ],
                }
            )

        # RIDs: both a property on the package and a platform sub-component carrying the file's
        # digest, so a consumer can check the RID list without opening the package.
        for rid, member, digest in layout:
            add(
                {
                    "type": "file",
                    "name": member,
                    "version": version,
                    "purl": None,
                    "hashes": [{"alg": "SHA-256", "content": digest}],
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "native-asset"},
                        {"name": "m3scalinum:rid", "value": rid},
                    ],
                }
            )
        for rid in sorted({entry[0] for entry in layout}):
            add(
                {
                    "type": "platform",
                    "name": rid,
                    "version": version,
                    "purl": None,
                    "properties": [
                        {"name": "m3scalinum:ecosystem", "value": "rid"},
                        {
                            "name": "m3scalinum:native-assets",
                            "value": str(sum(1 for entry in layout if entry[0] == rid)),
                        },
                    ],
                }
            )

        rids = sorted({entry[0] for entry in layout})
        return {
            "bomFormat": "CycloneDX",
            "specVersion": "1.6",
            "serialNumber": f"urn:uuid:{uuid.uuid4()}",
            "version": 1,
            "metadata": {
                "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "tools": {"components": [{"type": "application", "name": ".github/sbom/sbom.py"}]},
                "component": {
                    "type": "library",
                    "bom-ref": f"{NUGET_PURL}{ident}@{version}",
                    "name": ident,
                    "version": version,
                    "purl": f"{NUGET_PURL}{ident}@{version}",
                    "properties": [
                        {"name": "m3scalinum:rid", "value": rid} for rid in rids
                    ]
                    + [
                        {"name": "m3scalinum:embedded-crates", "value": ",".join(sorted({c["crate"] for c in crates})) or "none"},
                    ],
                },
            },
            "components": components,
        }


def expected_from_package(package):
    """What a complete SBOM for this package must contain, derived from the package alone."""
    with zipfile.ZipFile(package) as archive:
        ident, version, deps = read_nuspec(archive)
        layout = rid_layout(archive)
        assemblies = assembly_layout(archive)
    crates = set()
    for _, member, _ in layout:
        mapped = CRATE_BY_LIBRARY.get(library_stem(member))
        if mapped is None:
            raise SystemExit(f"unknown native library '{member}': CRATE_BY_LIBRARY has no crate for it")
        crates.add(mapped[1])
    return (
        ident,
        version,
        {d[0] for d in deps if d[0]},
        crates,
        {entry[0] for entry in layout},
        {name for name, _ in assemblies},
    )


def verify_sbom(package, sbom_path, repo_root):
    ident, version, declared_deps, crates, rids, assemblies = expected_from_package(package)
    problems = []

    try:
        sbom = json.loads(pathlib.Path(sbom_path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return [f"cannot read {sbom_path}: {exc}"]
    if sbom.get("bomFormat") != "CycloneDX":
        problems.append("bomFormat is not CycloneDX")
    components = sbom.get("components") or []
    if not components:
        problems.append("SBOM lists no components")

    meta = ((sbom.get("metadata") or {}).get("component") or {})
    if meta.get("name") != ident or meta.get("version") != version:
        problems.append(
            f"metadata.component is {meta.get('name')}@{meta.get('version')}, package is {ident}@{version}"
        )

    cargo_names, nuget_names, platform_names, file_names = set(), set(), set(), set()
    for component in components:
        if not component.get("name") or not component.get("version"):
            problems.append(f"component without name or version: {component!r}")
            continue
        purl = component.get("purl") or ""
        if purl.startswith(CARGO_PURL):
            cargo_names.add(component["name"])
        elif purl.startswith(NUGET_PURL):
            nuget_names.add(component["name"])
        if component.get("type") == "platform":
            platform_names.add(component["name"])
        file_names.add(component["name"])

    for crate in sorted(crates):
        if crate not in cargo_names:
            problems.append(f"package embeds crate '{crate}' but the SBOM lists no cargo component for it")
    if rids:
        missing = sorted(rids - platform_names)
        if missing:
            problems.append(f"SBOM omits RID(s) the package ships: {', '.join(missing)}")
        extra = sorted(platform_names - rids)
        if extra:
            problems.append(f"SBOM lists RID(s) the package does not ship: {', '.join(extra)}")
    missing_deps = sorted(declared_deps - nuget_names - {ident})
    if missing_deps:
        problems.append(f"SBOM omits declared dependenc(ies): {', '.join(missing_deps)}")
    missing_assemblies = sorted(assemblies - file_names)
    if missing_assemblies:
        problems.append(f"SBOM omits shipped assembl(ies): {', '.join(missing_assemblies)}")
    if not components and assemblies:
        problems.append("SBOM lists no components although the package ships assemblies")
    return problems


def cmd_generate(args):
    packages = sorted(pathlib.Path(args.packages).glob("*.nupkg"))
    if not packages:
        raise SystemExit(f"no .nupkg under {args.packages}: pack before generating the SBOM")
    output = pathlib.Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    failures = 0
    for package in packages:
        try:
            sbom = build_sbom(package, args.repo_root)
        except SystemExit as exc:
            print(f"{package.name}: {exc}", file=sys.stderr)
            failures += 1
            continue
        target = output / f"{sbom['metadata']['component']['name']}-{sbom['metadata']['component']['version']}.cdx.json"
        target.write_text(json.dumps(sbom, indent=2) + "\n", encoding="utf-8")
        counts = {}
        for component in sbom["components"]:
            ecosystem = next(
                (p["value"] for p in component.get("properties", []) if p["name"] == "m3scalinum:ecosystem"),
                "other",
            )
            counts[ecosystem] = counts.get(ecosystem, 0) + 1
        summary = ", ".join(f"{k}={v}" for k, v in sorted(counts.items()))
        print(f"{target.name}: {len(sbom['components'])} components ({summary})")
    return 1 if failures else 0


def cmd_verify(args):
    packages = sorted(pathlib.Path(args.packages).glob("*.nupkg"))
    if not packages:
        raise SystemExit(f"no .nupkg under {args.packages}: nothing to verify")
    sbom_dir = pathlib.Path(args.sbom)
    failures = 0
    for package in packages:
        ident, version, _, _, _, _ = expected_from_package(package)
        sbom_path = sbom_dir / f"{ident}-{version}.cdx.json"
        problems = verify_sbom(package, sbom_path, args.repo_root)
        if problems:
            failures += 1
            print(f"FAIL {package.name}", file=sys.stderr)
            for problem in problems:
                print(f"  - {problem}", file=sys.stderr)
        else:
            print(f"ok   {package.name} ({sbom_path.name})")
    return 1 if failures else 0


def _synthetic_package(directory, rid="linux-x64", library="libsharp_text_docx.so"):
    """A package shaped like a real one: nuspec with a dependency, one native asset per RID."""
    package = pathlib.Path(directory) / "synthetic.nupkg"
    nuspec = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>M3scalinum.Text.Docx</id>
    <version>0.0.1-selftest</version>
    <dependencies>
      <group targetFramework="net11.0">
        <dependency id="M3scalinum.Text.Typst" version="[0.51.0, )" />
      </group>
    </dependencies>
  </metadata>
</package>
"""
    with zipfile.ZipFile(package, "w") as archive:
        archive.writestr("M3scalinum.Text.Docx.nuspec", nuspec)
        archive.writestr("lib/net11.0/M3scalinum.Text.Docx.dll", b"not a real assembly")
        archive.writestr(f"runtimes/{rid}/native/{library}", b"not a real library")
    return package


def cmd_self_test(args):
    """Proves the checks run: a good SBOM passes, three broken ones are rejected."""
    failures = 0
    with tempfile.TemporaryDirectory() as directory:
        package = _synthetic_package(directory)
        sbom = build_sbom(package, args.repo_root)
        good = pathlib.Path(directory) / "good.cdx.json"
        good.write_text(json.dumps(sbom), encoding="utf-8")

        def expect(label, expected_problems, sbom_path):
            nonlocal failures
            problems = verify_sbom(package, sbom_path, args.repo_root)
            ok = bool(problems) if expected_problems else not problems
            print(f"{'ok  ' if ok else 'FAIL'} {label}" + (f" -> {problems}" if problems else ""))
            if not ok:
                failures += 1

        expect("complete SBOM is accepted", False, good)

        empty = pathlib.Path(directory) / "empty.cdx.json"
        empty.write_text(json.dumps({"bomFormat": "CycloneDX", "specVersion": "1.6", "version": 1, "components": []}), encoding="utf-8")
        expect("empty SBOM is rejected", True, empty)

        no_crates = json.loads(good.read_text(encoding="utf-8"))
        no_crates["components"] = [c for c in no_crates["components"] if not (c.get("purl") or "").startswith(CARGO_PURL)]
        no_crates_path = pathlib.Path(directory) / "no-crates.cdx.json"
        no_crates_path.write_text(json.dumps(no_crates), encoding="utf-8")
        expect("SBOM without the embedded crates is rejected", True, no_crates_path)

        no_rid = json.loads(good.read_text(encoding="utf-8"))
        no_rid["components"] = [c for c in no_rid["components"] if c.get("type") != "platform"]
        no_rid_path = pathlib.Path(directory) / "no-rid.cdx.json"
        no_rid_path.write_text(json.dumps(no_rid), encoding="utf-8")
        expect("SBOM without the shipped RIDs is rejected", True, no_rid_path)

        no_deps = json.loads(good.read_text(encoding="utf-8"))
        no_deps["components"] = [c for c in no_deps["components"] if c.get("name") != "M3scalinum.Text.Typst"]
        no_deps_path = pathlib.Path(directory) / "no-deps.cdx.json"
        no_deps_path.write_text(json.dumps(no_deps), encoding="utf-8")
        expect("SBOM without a declared dependency is rejected", True, no_deps_path)
    return 1 if failures else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo-root", default=".", help="checkout root holding native/<crate>/Cargo.lock and src/*/obj")
    sub = parser.add_subparsers(dest="command", required=True)

    generate = sub.add_parser("generate", help="write one CycloneDX SBOM per .nupkg")
    generate.add_argument("--packages", required=True)
    generate.add_argument("--output", required=True)
    generate.set_defaults(func=cmd_generate)

    verify = sub.add_parser("verify", help="fail when an SBOM does not describe its package")
    verify.add_argument("--packages", required=True)
    verify.add_argument("--sbom", required=True)
    verify.set_defaults(func=cmd_verify)

    test = sub.add_parser("self-test", help="check that verify accepts and rejects the right SBOMs")
    test.set_defaults(func=cmd_self_test)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
