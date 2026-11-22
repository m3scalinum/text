#!/usr/bin/env python3
"""Write one minimal, valid `.xlsx` into the corpus directory.

The repository ships no `.xlsx` fixture: `native/text-xlsx/src/fixture.rs` writes every test
workbook in memory, from the parts Excel writes, rather than committing a binary. The fuzzer needs
one honest starting point anyway — a random four kilobytes is not a zip, and libFuzzer would spend
the whole window learning the container format instead of the cells. This script writes the same
parts, deterministically, so the seed can be regenerated and diffed rather than trusted.

Usage: python3 fuzz/scripts/seed-xlsx.py fuzz/corpus/xlsx
"""

from __future__ import annotations

import sys
import zipfile
from pathlib import Path

CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
"""

ROOT_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
"""

WORKBOOK = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
"""

WORKBOOK_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
"""

SHARED_STRINGS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
  <si><t>Regime</t></si>
  <si><t>Periode</t></si>
</sst>
"""

SHEET = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetData>
    <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1"><v>12</v></c></row>
    <row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2"><v>3.5</v></c><c r="C2"><v>1</v></c></row>
  </sheetData>
</worksheet>
"""

STYLES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
  <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
  <borders count="1"><border/></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
</styleSheet>
"""

PARTS = (
    ("[Content_Types].xml", CONTENT_TYPES),
    ("_rels/.rels", ROOT_RELS),
    ("xl/workbook.xml", WORKBOOK),
    ("xl/_rels/workbook.xml.rels", WORKBOOK_RELS),
    ("xl/worksheets/sheet1.xml", SHEET),
    ("xl/sharedStrings.xml", SHARED_STRINGS),
    ("xl/styles.xml", STYLES),
)

# A fixed timestamp: the same script, the same bytes, on every machine and every run.
EPOCH = (1980, 1, 1, 0, 0, 0)


def write(directory: Path) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / "seed.xlsx"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as package:
        for name, text in PARTS:
            entry = zipfile.ZipInfo(name, EPOCH)
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o644 << 16
            package.writestr(entry, text.encode("utf-8"))
    return target


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    print(write(Path(argv[1])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
