# Writing an EPUB

`M3scalinum.Text.Epub` turns a document into an EPUB 3 book: reflowable text with a table of
contents, not a picture of a page. It uses `System.IO.Compression` alone — no package reference and
no native code — so it runs wherever the rest of the suite runs.

```csharp
using M3scalinum.Text.Epub;

var book = new EpubBook(new EpubMetadata
{
    Identifier = "urn:uuid:5b2c1e0a-4a5c-4c8e-9d0f-6f5b0d2a7c11",
    Title = "A Field Guide",
    Language = "en",
    Author = "Ana Kovács",
});

book.AddChapter("First Steps", """
    <h1>First Steps</h1>
    <p>Hello &amp; welcome.<br>This line starts on its own.</p>
    <p><img src="images/cover.png" alt="The cover"/></p>
    """);
book.AddAsset("images/cover.png", "image/png", coverBytes);

File.WriteAllBytes("guide.epub", EpubWriter.Write(book));
```

`Write(book, stream)` does the same into a stream you own. `Identifier` and `Title` are required, and
so is at least one chapter: an empty book is not something a reader would open.

## From one HTML document

If the document is already HTML — for instance `TypstCompiler.CompileToHtml(markup)` — you do not
have to split it by hand:

```csharp
var book = EpubBook.FromHtml(html, metadata);
book.AddAsset("images/cover.png", "image/png", coverBytes);
byte[] epub = EpubWriter.Write(book);
```

Chapters break at the shallowest heading the document uses, so `h1` chapters with `h2` sections split
at the `h1` and keep their sections. Content before the first heading becomes a chapter under the
book's title. A heading stays at the head of its chapter, so the reader sees it when it jumps there.

The HTML is parsed and re-serialized, which is what repairs the things HTML tolerates and XML does
not: unclosed paragraphs, `<br>` instead of `<br/>`, `&nbsp;` and `&mdash;`, a stray `</p>`. Elements
that cannot live in a book's content — scripts, frames, form controls — are dropped, and elements
outside the XHTML vocabulary are unwrapped with their children kept.

## Images and other files

A file is added under the name the chapter references, as a relative path inside the book:

```csharp
book.AddAsset("images/cover.png", "image/png", pngBytes);
// <img src="images/cover.png" alt="…"/> in a chapter resolves to it
```

The media type can be left out when the extension is one this package knows (`.png`, `.jpg`, `.gif`,
`.svg`, `.webp`, `.woff2`, `.mp3`, `.mp4`, …). A name is a plain relative path: letters, digits, `-`,
`_`, `.` and `/` between segments. `..`, an absolute path, a backslash or a reserved name such as
`nav.xhtml` is refused when you add the file.

A reference the book does not back fails the write, naming the path. That is deliberate: an EPUB
with a broken image is one a reader renders wrong, and the failure belongs where the file is missing,
not in a reader's log.

## What you get

A container with the `mimetype` entry first and stored, `META-INF/container.xml`, a package document
that declares the metadata and the spine, a navigation document (`properties="nav"`), one XHTML
document per chapter, a stylesheet, and every asset. The specification —
[25-epub.md](https://github.com/m3scalinum/text/blob/main/docs/specifications/25-epub.md) — says why
each of those is a rule rather than a convention.

## What you do not get

- **No EPUB 2.** The package is 3.0 with a nav document and no NCX; that is what readers have
  supported for years, and a second table of contents would only be one more thing to keep in step.
- **No DRM or encryption.** A book is plain files; nothing is signed or locked.
- **No fixed layout.** That is a page image with coordinates, which is the format this one avoids.
- **No media overlays, scripting or SVG/MathML content.** Those elements are dropped or unwrapped.
- **No cover by convention.** Add the image as an asset and reference it; the writer does not guess
  which image is the cover or declare `properties="cover-image"`.

## Why it is written by hand

The `epub` crate is GPL-3.0, and this repository is Apache-2.0: linking it would relicense the
package and everything downstream. The format does not need a library — a ZIP with one stored entry,
an XML pointer and a package document — so it is built with `System.IO.Compression` and `System.Xml`,
and the tests read the result back with the same two, then open it in a real reader.
