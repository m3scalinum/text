# Reading Excel workbooks

`M3scalinum.Text.Xlsx` reads an Excel `.xlsx` workbook and renders it with the compiler the suite
already hosts. Invoicing arrives in spreadsheets as often as in Word or in PDF, and a report has to be
able to cite a workbook, so the package answers one question: can a `.xlsx` become a PDF through this
suite, without Excel and without the Open XML SDK?

Yes, for the parts listed below. Bytes in, a document out:

```csharp
using M3scalinum.Text.Xlsx;
using M3scalinum.Text.Typst.Compilation;

XlsxWorkbook workbook = XlsxReader.Read(File.ReadAllBytes("facture.xlsx"));

var world = new TypstWorldBuilder()
    .AddSource("main.typ", workbook.ToTypstDocument().ToMarkup());

using CompilationResult result = new TypstCompiler().Compile(world.Build());

File.WriteAllBytes("facture.pdf", result.Export(ExportFormat.Pdf));
```

## What you get

An `XlsxWorkbook` with two members that matter: `Sheets`, in workbook order, and `Find(name)` for a
sheet by its tab name.

An `XlsxSheet` knows its `Name`, its `RowCount` and `ColumnCount`, where its used region starts on the
sheet (`FirstRow`, `FirstColumn`), and its `Cells` — only the ones that hold a value or a formula.
`Find(row, column)` gets one by position inside that region.

An `XlsxCell` carries `Row`, `Column`, `Value`, `Formula` and `DisplayText`, and `Reference(sheet)`
gives its `A1` name — `C5`, `AA100`.

## The values

`Value` is one of seven shapes, each a class of its own so a `switch` over it is exhaustive:

| Value | Holds | `DisplayText` |
| --- | --- | --- |
| `XlsxTextValue` | `Text` | the characters |
| `XlsxNumberValue` | `Number`, a `double` | `1234.5` |
| `XlsxBooleanValue` | `Value` | `TRUE` or `FALSE` |
| `XlsxDateValue` | `Value`, a `DateTime`, and `IsDateOnly` | `2026-01-15` or `2026-01-15 10:30:00` |
| `XlsxDurationValue` | `Value`, a `TimeSpan` | `36:00:00` |
| `XlsxErrorValue` | `Error` | `#DIV/0!` |
| `XlsxBlankValue` | nothing | empty, or the formula |

A date is a date because the workbook's number format says so, not because the number looks like one:
Excel stores every date as a serial day count and `calamine` resolves the format.

## Formulas

Excel writes the result of its last calculation into the package, and that cached result is the cell's
`Value`. `Formula` is the formula, with its `=`:

```text
cell.Value      → XlsxNumberValue { Number = 1200 }
cell.Formula    → "=B3+B4"
cell.DisplayText → "1200"
```

A formula the workbook never calculated has no cached value. It reads as `XlsxBlankValue` and keeps
its `Formula`, and `DisplayText` gives the formula — the only thing that cell says.

## Rendering it

`ToTypstDocument()` on a workbook emits a `= Sheet name` heading per sheet, then the table its used
region covers. On a sheet, it emits the table and nothing else.

| Excel | Typst |
| --- | --- |
| A sheet | `#table(columns: (1fr, …))` |
| A cell with a value | its `DisplayText` |
| An empty position | an empty cell, so the columns stay aligned |
| A workbook | `= Sheet name` above each table |

Each column is `1fr`: what the reader has is a rectangle of values, not the sheet's column widths.

## Limits

Two ceilings, both configurable through `XlsxOptions`:

| Ceiling | Default | What it stops |
| --- | --- | --- |
| `MaxWorkbookBytes` | 64 MiB | A 3 GB upload, a range request gone wrong |
| `MaxCells` | 1 000 000 | A used region no renderer could lay out |

A workbook over either fails with `XlsxException` and an `XlsxErrorCode` — `Limit` for a ceiling,
`MalformedPackage` for bytes that are not a workbook, `InvalidArgument` for an empty one.

## What is not read

Merged cells (the values are there, the merge is not), cell formatting and number formats beyond the
date detection, charts, images, pivot tables, comments and named ranges. `.xls`, `.xlsb` and `.ods` are
not read either: this package reads `.xlsx`. Annotation drift is a bug, so
[specifications 28](https://github.com/m3scalinum/text/blob/main/docs/specifications/28-xlsx.md) is
the normative list.

**Nothing that is not read throws.** A workbook with charts reads, and the charts are lost. Only a
package that cannot be opened is an exception.
