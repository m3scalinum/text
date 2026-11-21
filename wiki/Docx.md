# Reading Word documents

`M3scalinum.Text.Docx` reads a Word `.docx` file and renders it with the compiler the suite already
hosts. Word is the format the documents this suite replaces actually arrive in, so the package exists
to answer one question: can a `.docx` be turned into a PDF by this suite, without Word and without the
Open XML SDK?

Yes, for the parts listed below. Bytes in, a document out:

```text
using M3scalinum.Text.Docx;
using M3scalinum.Text.SharpTypst.Compilation;

DocxDocument document = DocxReader.Read(File.ReadAllBytes("received.docx"));

var world = new TypstWorldBuilder()
    .AddSource("main.typ", document.ToTypstDocument().ToMarkup());

// An image is referenced by its part name; without the asset the compile fails on that image.
foreach (var (name, bytes) in document.Images)
{
    world.AddAsset(name, bytes);
}

using CompilationResult result = new TypstCompiler().Compile(world.Build());

File.WriteAllBytes("received.pdf", result.Export(ExportFormat.Pdf));
```

## What you get

A `DocxDocument` with three members: `Blocks`, `Images` and `Page`.

`Blocks` is the body in document order. A `DocxParagraph` knows its `HeadingLevel` (1 to 6, when
Word's style says so), its `List` (`NumberingId`, `Level`, `Ordered`), its `Inlines` and its `Text`.
An inline is a `DocxTextRun` — with `Bold`, `Italic`, `Underline`, `Strike`, `Script`, `Color` and
`SizePoints` — a `DocxImageRun`, or a `DocxLineBreak`. A `DocxTableBlock` has `Columns` and `Rows` of
`DocxTableCell`, each with `Colspan`, `Rowspan` and its own `Blocks`.

`Images` maps a part name (`word/media/image1.png`) to the bytes exactly as the package stored them:
nothing is decoded or re-encoded, so a JPEG stays a JPEG.

`Page` is the page setup Word recorded, in points, or `null` when the document states none.

## Rendering it

`ToTypstDocument()` builds the suite's own `TypstDocument` — not a second model, not a string of
Typst source assembled by hand:

| Word | Typst |
| --- | --- |
| Heading 1 to 6 | `=` to `======` |
| Bold, italic | `*strong*`, `_emphasis_` |
| Underline, strike | `#underline[…]`, `#strike[…]` |
| Superscript, subscript | `#super[…]`, `#sub[…]` |
| Colour, size | `#text(fill: …, size: …)` |
| A bulleted list | `- item` |
| A numbered list | `+ item` |
| A table with `gridSpan` / `vMerge` | `#table(…)` with `colspan:` / `rowspan:` |
| An embedded picture | `#figure(image("word/media/image1.png"))` |
| Page size and margins | `#set page(…)` |

An image is registered as a world asset under the same name the document uses, which is why the
`foreach` above is not optional: the compiler will not find the file otherwise.

## What you do not get

- **No styles beyond the run.** A custom paragraph style that sets spacing, borders or a font is not
  resolved: the run properties a run states are read, inherited ones are not.
- **No footnotes or endnotes.** The reference is not read at all, so a note's text is lost.
- **Tracked changes read as accepted.** Insertions are kept, deletions dropped, no revision marks.
- **No fields.** A table of contents, a page reference or a `DATE` field does not render.
- **No hyperlink targets.** A link's text survives; its URL does not.
- **No list nesting.** A paragraph's level is in the model, but a list renders flat: a sub-bullet is
  another item of the same list.
- **No headers or footers, no comments, no charts, no shapes.** Only a picture drawing becomes an
  image.
- **No EMF/WMF rasterisation.** The bytes are kept and the MIME type is right, but Typst cannot draw
  them, and the compile fails at that image.

Nothing in that list throws: a document with footnotes reads, and the footnote is lost. Only a package
that cannot be opened throws — not a zip (`MalformedPackage`), a missing `word/document.xml`
(`MissingPart`), or a document over the size limit (`Limit`).

## Options

```text
var options = new DocxOptions
{
    ExtractImages = false,               // no media is copied out at all
    MaxDocumentBytes = 8 * 1024 * 1024,  // refuse anything larger before opening it
};

DocxDocument textOnly = DocxReader.Read(package, options);
```

`DocxReader.Read(Stream)` buffers the stream whole, because a `.docx` is a zip and its index sits at
the end of the file.

## Where the native library comes from

The reader is a Rust cdylib of its own, `sharp_text_docx`, next to `sharp_typst`. A framework-dependent
application finds it under `runtimes/<rid>/native/`; `SHARPTEXTDOCX_NATIVE_PATH` overrides the search
when you build it yourself:

```bash
export PATH=$HOME/.cargo/bin:$PATH
cd native/text-docx && cargo build --release
export SHARPTEXTDOCX_NATIVE_PATH=$PWD/target/release/sharp_text_docx.dll
```

The specification — the AST, the ABI, what is lost and why `docx-rs` was chosen — is
`docs/specifications/22-docx.md`.
