# 25 — EPUB 3 output

`M3scalinum.Text.Epub` packages a document as an EPUB 3 book: the format an e-reader opens, and the
one that carries structure rather than a photograph of a page. A document rendered by the suite can
be compiled to PDF; this package is the other delivery — reflowable text, a table of contents, and
images that scale with the screen.

It is written against `System.IO.Compression` alone. No package reference, no native code, no
license beyond the repository's own.

## 1. Scope

| In scope | Detail |
| --- | --- |
| A valid container | `mimetype` first and stored, `META-INF/container.xml`, an OPF 3.0 package, a navigation document, XHTML content documents, the stylesheet, every asset |
| Metadata | identifier, title, language, author, publisher, date, and the `dcterms:modified` the package requires |
| Chapters | an ordered list, each a title and an HTML fragment the writer turns into well-formed XHTML |
| Assets | images, fonts and other files by name, stored as they are and declared in the manifest |
| An HTML bridge | `EpubBook.FromHtml` splits one document into chapters at its headings |
| XHTML repair | unclosed tags, `<br>`, named entities and unknown elements are made well-formed or dropped |

| Out of scope | Why |
| --- | --- |
| EPUB 2 | The 3.0 package with a nav document is what readers have supported for a decade; an NCX would be a second table of contents to keep in step |
| DRM, encryption, signatures | A different contract with a different vendor; nothing in a container enforces it |
| Fixed layout | That is a page image with coordinates, the thing this format exists to avoid |
| Media overlays, scripting, MathML/SVG in content | The writer unwraps or drops what it cannot serialize as XHTML; a book that needs them needs a different pipeline |
| epubcheck conformance | The tests parse what they assert, and the package was opened by a real reader; epubcheck needs a Java runtime the build does not have |
| A cover | An `item` with `properties="cover-image"` is a reading-system convenience, not a validity rule, and choosing the cover is the caller's decision |

## 2. The API

```csharp
using M3scalinum.Text.Epub;

var book = new EpubBook(new EpubMetadata
{
    Identifier = "urn:uuid:5b2c1e0a-4a5c-4c8e-9d0f-6f5b0d2a7c11",
    Title = "A Field Guide",
    Language = "en",
    Author = "Ana Kovács",
    Publisher = "M3scalinum",
    Date = new DateOnly(2026, 11, 21),
});

book.AddChapter("First Steps", """
    <h1>First Steps</h1>
    <p>Hello &amp; welcome.<br>This line starts on its own.</p>
    <p><img src="images/cover.png" alt="The cover"/></p>
    """);
book.AddChapter("Second Thoughts", "<h1>Second Thoughts</h1><p>And the rest.</p>");
book.AddAsset("images/cover.png", "image/png", coverBytes);

byte[] epub = EpubWriter.Write(book);        // or EpubWriter.Write(book, stream)
```

| Entry point | Input |
| --- | --- |
| `EpubWriter.Write(EpubBook)` | the container's bytes |
| `EpubWriter.Write(EpubBook, Stream)` | the container, written to a stream the caller owns |
| `EpubBook.FromHtml(string, EpubMetadata)` | one HTML document, split into chapters |
| `EpubBook.AddChapter(string, string)` | a title and an HTML fragment |
| `EpubBook.AddAsset(string, ReadOnlyMemory<byte>)` | a file, media type taken from the extension |
| `EpubBook.AddAsset(string, string, ReadOnlyMemory<byte>)` | a file and an explicit media type |

`EpubMetadata` is a record with `Identifier` and `Title` required, `Language` defaulting to `en`, and
`Author`, `Publisher`, `Date` and `Modified` optional. `EpubException` is the base of
`EpubValidationException` (the book cannot be a valid container) and `EpubFormatException`.

## 3. The container

```
mimetype                       application/epub+zip, stored, first
META-INF/container.xml         points at OEBPS/content.opf
OEBPS/content.opf              package: metadata, manifest, spine
OEBPS/nav.xhtml                the navigation document, properties="nav"
OEBPS/book.css                 the stylesheet every content document links
OEBPS/ch001.xhtml              one content document per chapter, numbered from 1
OEBPS/images/cover.png         an asset, at the path its name gives
```

**Why flat.** Content documents and assets sit in one directory, so every href is a name the
manifest declares and no href needs `..` to reach a sibling. A path that climbs out of the package
is refused, not normalized: `..` is the shape of a traversal bug, and a container has no reason to
carry one.

**Why `mimetype` first and stored.** A reading system sniffs the twenty bytes at offset 38 before it
parses anything. That position holds only when `mimetype` is the first entry, has no extra field,
and is not deflated; `EpubWriterTests` asserts the local header, the name length, the zero extra-field
length and the media type at that exact offset, not merely that the entry is uncompressed somewhere.

### 3.1 The package document

```xml
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:uuid:…</dc:identifier>
    <dc:title>A Field Guide</dc:title>
    <dc:language>en</dc:language>
    <dc:creator>Ana Kovács</dc:creator>
    <meta property="dcterms:modified">2026-11-21T10:00:00Z</meta>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="css" href="book.css" media-type="text/css"/>
    <item id="ch001" href="ch001.xhtml" media-type="application/xhtml+xml"/>
    <item id="asset001" href="images/cover.png" media-type="image/png"/>
  </manifest>
  <spine><itemref idref="ch001"/></spine>
</package>
```

`unique-identifier` names the `dc:identifier` that identifies the publication; `dcterms:modified` is
required by EPUB 3 and is the one timestamp a package must carry. It also stamps every archive entry,
so the same book written twice is the same bytes.

### 3.2 What makes it valid

| Rule | How it is met |
| --- | --- |
| `mimetype` is the first entry, stored | written before any other entry, `CompressionLevel.NoCompression` |
| `META-INF/container.xml` names the package | one `rootfile`, `application/oebps-package+xml` |
| Every manifest href resolves | the writer checks each declared href against the entries it will write |
| Every chapter reference resolves | `img src`, `a href` and `link href` are collected while serializing and checked against the chapters and assets; an absolute URL is exempt |
| The nav is the manifest item with `properties="nav"` | one `item` carries it, and the navigation document is a `nav epub:type="toc"` |
| XHTML is well-formed XML in the XHTML namespace | every document is written through `XmlWriter` with `http://www.w3.org/1999/xhtml` |
| Identifiers are unique | manifest ids are generated from the chapter and asset index; an `id` already used in the book is dropped from the later document, and a malformed one is dropped |
| The spine points at manifest items | `itemref idref` values are the generated chapter ids, by construction |

### 3.3 Paths

An asset name is a relative path: segments of ASCII letters, digits, `-`, `_` and `.`, no `..`, no
`.` segment, no empty segment, no leading `/`, no backslash, no colon, no query or fragment. The
names the writer generates (`mimetype`, `META-INF/container.xml`, `content.opf`, `nav.xhtml`,
`book.css`, `chNNN.xhtml`) are reserved. A book carries at most 999 chapters, because the file names
are numbered with three digits.

These rules are also what make the reference check possible: the path in an `img src` is exactly the
name the asset was added under, so "does this reference resolve" is a set lookup, not a rewrite of
relative paths.

## 4. Building from HTML

`EpubBook.FromHtml(html, metadata)` parses one document and splits it at its headings:

- the split level is the shallowest heading level the document uses — a document written with `h1`
  chapters and `h2` sections splits at its `h1`;
- a heading starts a chapter and stays at the head of it, so a reader that jumps to the chapter sees
  its title;
- content before the first heading becomes a chapter under the book's title, which is where a preface
  or an abstract lives;
- only wrappers that carry no structure (`div`, `section`, `article`, `main`, `header`, `footer`) are
  unwrapped, so a list, a table and a paragraph keep their shape.

The markup is parsed and re-serialized, not passed through. That is what lets a document written as
HTML — unclosed paragraphs, `<br>` instead of `<br/>`, `&nbsp;` and `&mdash;`, a stray `</p>` —
become XHTML that an XML parser accepts. An element outside the XHTML vocabulary is unwrapped (its
children are kept); a script, a frame, a form control or an embedded object is dropped with its
subtree, because a book's content document cannot carry it meaningfully. Named references outside
the table this package knows are left as text, which is visible but well-formed.

### 4.1 Why there is no compiler reference

`M3scalinum.Text.SharpTypst` can export HTML, and a bridge could have taken a `TypstDocument` or a
`CompilationResult` and called it. This package takes a string instead, and keeps the dependency out:

- **The release trains differ.** The compiler wrapper's version tracks the embedded Typst compiler
  (`0.15.1.<revision>`); the suite packages track their own tag (`SuiteVersion`). A reference would
  bind EPUB releases to Typst releases: a compiler patch would force an EPUB repack, and an EPUB fix
  would wait for one.
- **The dependency is not needed.** The HTML the compiler exports is HTML; splitting it at headings
  and making it well-formed is the job either way. A `TypstDocument` overload would add the native
  assembly's lifetime and platform matrix to a package that today is pure managed IL.
- **The package stays testable without a compiler.** Every test builds its input as a string, so the
  suite runs on a machine with no native library, on every target framework.

A caller that has a compilation result passes its HTML:

```csharp
var html = TypstCompiler.CompileToHtml(markup);   // in the caller's project
var book = EpubBook.FromHtml(html, metadata);
```

If a typed overload is ever wanted, it belongs in a package that already references the compiler, not
in this one.

## 5. Failure modes

| Situation | Result |
| --- | --- |
| No identifier, no title, no language, no chapter | `EpubValidationException`, before anything is written |
| More than 999 chapters | `EpubValidationException` |
| Asset name with `..`, an absolute path, a backslash, a reserved name | `EpubValidationException` when it is added |
| Chapter references a file no asset backs | `EpubValidationException` at write time, naming the reference |
| Duplicate asset name | `EpubValidationException` when it is added |
| Malformed HTML | repaired, not refused: unclosed elements are closed, unknown references stay text |
| An `id` repeated in the book | the later one is dropped, so the package's ids stay unique |

## 6. Why it is hand-built

The `epub` crate is GPL-3.0. This repository is Apache-2.0, and a GPL dependency would relicense
everything that links it, so the crate is excluded. The format does not need it: an EPUB is a ZIP
with one stored entry, one XML pointer and one package document, and `System.IO.Compression` plus
`System.Xml` write all four. The writer and the small HTML reader it needs fit in one assembly with
no reference at all, which is less work than tracking a third-party serializer's defaults for
compression, entry order and namespace handling.

## 7. Verification

`tests/Text.Epub.Tests/EpubWriterTests.cs` reads every book back with `ZipArchive` and parses the
XML with `XDocument`, so it asserts what another implementation would see:

- entry order, the local header and the media type at offset 38;
- the container's `rootfile` and the package's `unique-identifier`, title, language, creator,
  publisher, date and `dcterms:modified`;
- every manifest href exists as an entry, every spine `itemref` names a manifest id, and the nav item
  carries `properties="nav"`;
- every XHTML document parses, in the XHTML namespace, with a head, a body and unique ids;
- the nav lists the chapters in reading order, by the titles they were given;
- `../escape.png` is refused as an asset name and as a chapter reference, and a missing asset is
  refused at write time;
- unclosed HTML and named entities become well-formed XHTML;
- the same book written twice is byte-identical.

Beyond the tests, the sample book built from an HTML document was opened by Calibre's EPUB reader
(`ebook-meta`, then `ebook-convert … .htmlz`): the title, author, publisher and date came back, all
three chapters were read in spine order with their text, lists and table intact, and the image was
extracted. The same bytes were checked with an independent script that resolved every manifest href,
every spine idref and every nav link against the archive's entry list.
