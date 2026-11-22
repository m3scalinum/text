# One example per package

Each directory holds a single `Program.cs`: the smallest program that would fail if the package it
names were incomplete. `run.sh` compiles it against the `.nupkg` — never against `src/` — and runs
it, so the example sees exactly what a consumer downloads.

```bash
# pack what you want to test, then build and run the example against that folder
dotnet pack src/Text.Pdf -c Release -o /path/to/feed
samples/examples/run.sh Pdf M3scalinum.Text.Pdf 0.1.0 /path/to/feed
```

The package version is the one in the file name, never a constant in this directory: CI reads it
from the packed file. `--build-only` stops after `dotnet build`, which is all a Windows host can
prove for a package whose native library is a Linux `.so`.

## What each one proves

| Example | Package | Proof |
| --- | --- | --- |
| `Pdf` | `M3scalinum.Text.Pdf` | reads the text of a PDF built in the example, signs it, verifies the signature, reads the signed bytes again |
| `Templates` | `M3scalinum.Text.Templates` | the seven layouts load from the package's resources and the invoice renders to a PDF |
| `Mcp` | `M3scalinum.Text.Mcp` | `initialize`, `tools/list` (five tools) and a real `tools/call` of `list_templates`, in process |
| `Barcodes` | `M3scalinum.Text.Barcodes` | QR Code and Code 128 encoded, then decoded back to the same payload, through the native library |
| `Ocr` | `M3scalinum.Text.Ocr` | the native library loads and answers on the ABI; a real recognition needs models the package does not ship (`M3SCALINUM_OCR_MODELS`) |
| `Docx` | `M3scalinum.Text.Docx` | a committed `.docx` is read by the native reader, then converted to the document model |
| `Xlsx` | `M3scalinum.Text.Xlsx` | a workbook built in the example is read by the native reader into typed cells |
| `Typst` | `M3scalinum.Text.Typst` | minimal source compiled to a PDF and to text by the native compiler |
| `Documents` | `M3scalinum.Text.Documents` | the model, the Markdown export and the HTML export, with no native library at all |

The generated project — `Example.csproj` and a `NuGet.config` restricted to the feed and
nuget.org — is written to the scratch directory, not committed. It deliberately inherits nothing
from the repository: no `Directory.Build.props`, no central package versions, no project reference.
