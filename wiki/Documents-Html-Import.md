# Importing HTML

`TypstDocument.FromHtml` turns an HTML page or fragment into the document model, so an application
that already has HTML can typeset it without writing Typst by hand. The model goes back the other
way with `ToHtml`, so the same document can be served as HTML too.

```csharp
using M3scalinum.Text.Documents;

var document = TypstDocument.FromHtml(html);

byte[] pdf = TypstCompiler.CompileToPdf(document);
```

Use `TypstHtmlImporter` when you need to know what the import dropped:

```csharp
var importer = new TypstHtmlImporter(new TypstHtmlImportOptions
{
    AssetResolver = source => source == "logo.png" ? "assets/logo.png" : null,
});

var result = importer.Import(html);

foreach (var dropped in result.DroppedElements)
{
    // "iframe: 1", "caption: 2", …
}
```

## What is supported

| HTML | Becomes |
| --- | --- |
| `p` | paragraph |
| `h1`–`h6` | heading of that level |
| `ul`, `ol`, `li` (nested) | bullet or numbered list |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td` | table, with `colspan` and `rowspan` |
| `strong`, `b` | bold |
| `em`, `i` | italic |
| `code` | inline raw text |
| `pre` (with `code class="language-…"`) | fenced code block |
| `a href` | link |
| `img src` | figure, through the asset resolver |
| `blockquote` | quote |
| `br` | line break |
| `hr` | divider |
| `dl`, `dt`, `dd` | term list |
| `sup`, `sub` | superscript, subscript |

`div`, `span`, `section`, `article`, `figure` and the other structural wrappers are transparent:
their children are imported and they leave no output.

## What is not

Everything else is either unwrapped (its text survives) or dropped with its subtree, and counted in
`DroppedElements` — never silently mangled. `UnknownElements = TypstHtmlUnknownElementPolicy.Throw`
turns an unsupported element into a `TypstHtmlImportException` instead.

CSS is **not** honoured: no layout, no widths, no colours, no font sizes. `HonourInlineStyles =
true` reads exactly three declarations — `font-weight`, `font-style` and `text-decoration` — and
ignores the rest. A page whose appearance depends on CSS must be rendered by a browser.

Images are never fetched. Every `src` goes through `AssetResolver`; an unresolved source throws
`TypstHtmlImportException` at import time, not as a compiler error later.

## What the parser handles

It is hand-written, not a conforming HTML parser, and it handles the parts real pages depend on:
void elements, unclosed `p`/`li`/`td`, single-, double- and unquoted attributes, comments, a
doctype, and the named and numeric character references. Whitespace is collapsed outside `pre`.

Escaping is done by the markup writer, as everywhere in the model: text and attribute values with
`* _ # $ < > @ [ ]` or quotes become literal output, never Typst syntax.

The full contract — every element, every deliberate loss — is section 12 of
[04 — Document model](https://github.com/m3scalinum/text/blob/main/docs/specifications/04-document-model.md).

## Writing HTML

`ToHtml()` writes the model as a semantic HTML5 body fragment. It needs no compiler: it walks the
model, not a laid-out page, so the tags are the model's own structure — `h1`–`h6`, `p`, `ul`/`ol`,
`table` with `th` and `td`, `pre`/`code`, `blockquote`, `a`, `img`, `strong`/`em`.

```csharp
var result = document.ToHtml();

string html = result.Html;

foreach (var lost in result.Unrepresentable)
{
    // "raw: 2", "place: 1", …
}
```

Raw markup, placement, a bibliography or an outline have no HTML equivalent. They are listed with
their name and count instead of being approximated; `result.UnrepresentableCount` says whether the
export lost anything at all. Text and attribute values are escaped, and the output uses `\n` line
endings so it is byte-for-byte stable.

An image carries the asset path the importer resolved, or a `data:` URL if the model was given one —
export never fetches. A paragraph of body content written as `p` reads back the same, so
`TypstDocument.FromHtml(html).ToHtml()` is a fixed point for headings, paragraphs, emphasis, code,
lists, tables, links and quotes.

The full contract — the mapping tables, the escaping rules, the exact loss list and what round-trips
— is [27 — HTML export](https://github.com/m3scalinum/text/blob/main/docs/specifications/27-html-export.md).
