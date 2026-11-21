# Importing Markdown

`TypstDocument.FromMarkdown` turns a Markdown document or fragment into the document model, so a
README, a changelog or a specification can be typeset without a conversion step outside the library.
The import is one-way: Markdown in, a document model out, then Typst source.

```csharp
using M3scalinum.Text.Documents;

var document = TypstDocument.FromMarkdown(markdown);

byte[] pdf = TypstCompiler.CompileToPdf(document);
```

Use `MarkdownImporter` when you need to know what the import dropped:

```csharp
var importer = new MarkdownImporter(new TypstMarkdownImportOptions
{
    AssetResolver = source => source == "logo.png" ? "assets/logo.png" : null,
});

var result = importer.Import(markdown);

foreach (var dropped in result.DroppedConstructs)
{
    // "html-block: 2", "reference-definition: 1", …
}
```

## What is supported

| Markdown | Becomes |
| --- | --- |
| `#` … `######`, setext `===` / `---` | heading of that level |
| paragraph | paragraph, soft breaks as spaces |
| fenced and indented code blocks | code block, language from the info string |
| `-`, `+`, `*` bullet lists | bullet list |
| `1.`, `1)` ordered lists | numbered list |
| tight and loose lists, nesting by indentation | list, with or without blank lines between items |
| `>` block quote | quote |
| `---`, `***`, `___` | divider |
| GFM tables, `:---` / `---:` / `:---:` | table, with per-column alignment |
| `**strong**`, `__strong__` | bold |
| `*em*`, `_em_` | italic |
| `~~strike~~` | strikethrough |
| `code span` | inline raw text |
| `[text](destination)`, `[text][label]`, `[label]` | link, titles dropped |
| `<https://…>`, `<mail@example.com>` | link |
| `[^1]` with `[^1]: …` | footnote |
| two trailing spaces or `\` before a line break | line break |
| `![alt](destination)`, standalone | figure, alt as caption, through the asset resolver |

## What is not

Raw HTML — a block or an inline tag — is **never passed through**: it is dropped and counted as
`html-block` or `html-inline`, because the model has no HTML node and letting it through would be a
markup-injection hole. `RawHtml = TypstMarkdownRawHtmlPolicy.Throw` makes the first HTML block fail
the import instead.

`<details>`, HTML comments and custom containers such as `::: warning` are not containers: they are
dropped HTML or ordinary paragraphs. Task list checkboxes, bare-URL autolinking, heading ids, link
titles, the start number of an ordered list and image dimensions are not imported.

Footnotes are numbered by Typst in the order they appear, so a second reference to one footnote
label cannot reuse it: the second reference stays literal text and is counted as
`footnote-reference`. Numbering beyond document order is therefore lost.

Images are never fetched. Every destination goes through `AssetResolver`; an unresolved destination
throws `TypstMarkdownImportException` at import time, not as a compiler error later.

## What the parser handles

It is hand-written and has no dependency of its own: a line-based block pass, a pass that collects
reference link and footnote definitions before anything is converted, and a recursive inline pass.
It is not a conforming CommonMark implementation. It normalises CRLF and lone CR to LF, ignores a
byte-order mark, and expands tabs to four-column tab stops. It keeps an unclosed fence running to the
end of the document, pads a short table row and drops a cell past the header's column count, and
leaves an undefined reference as literal text — malformed input degrades, it does not throw.

Escaping is done by the markup writer, as everywhere in the model: text with `* _ # $ < > @ [ ]`,
a link label, an alt text and a table cell all become literal output, never Typst syntax.

The full contract — every construct, every deliberate loss — is section 13 of
[04 — Document model](https://github.com/m3scalinum/text/blob/main/docs/specifications/04-document-model.md).
