# 42 — Bibliography, citations and the index: two lists a long document ends with

## 1. Scope

A technical manual ends with two lists the model could not produce: the works it cites, and the
terms it discusses. Both are now typed members of `M3scalinum.Text.Documents`:

| Member | Emits | Needs |
| --- | --- | --- |
| `TypstBibTexParser.Parse(bibtex)`, `TypstBibliography.Parse(bibtex)` | a bibliography resolved by citation key | nothing |
| `TypstDocument.Bibliography(TypstBibliography, style)` | the final list, one paragraph per entry | a bibliography |
| `TypstDocument.Citation(key, prose)` | a citation on its own line | a bibliography |
| `TypstDocument.CitationContent(key, prose)`, `TypstContent.Cite(document, key)` | the same citation inline, inside a paragraph | a bibliography |
| `TypstDocument.IndexMark(term, subterm?)` | `#context metadata(…)`, invisible | nothing |
| `TypstDocument.Index(title?)` | the sorted index with the pages the layout gave | nothing |

```csharp
var bibliography = TypstBibliography.Parse(File.ReadAllText("refs.bib"));
var document = TypstDocument.Create();

document
    .Heading(1, "Layout")
    .Paragraph(c => c.Text("A linear pass is enough ").Cite(document, "chen2022").Text("."))
    .Paragraph(c => c.Text("Counters are page-scoped ").Cite(document, "dupo2024").Text("."))
    .IndexMark("layout", "linear pass")
    .IndexMark("counter")
    .PageBreak()
    .Heading(1, "Bibliography")
    .Bibliography(bibliography, TypstBibliographyStyle.AuthorDate)
    .Index(new TypstContent().Text("Index"));
```

**In scope.** The BibTeX subset below; a citation key that resolves or throws; two styles over the
same entry; the final list in citation order; index marks anywhere in the flow and an index that
reads the real page counter.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| CSL, `.csl` files, field-level style rules | A CSL engine is a second format and a second implementation. The suite has no dependency on one, and the two styles here are the two a report asks for (§3). |
| `@string` macros, `#` concatenation, `crossref` | Reading them needs a macro expander and an entry-merging pass over the whole file. Each throws with a message naming the construct instead of being dropped (§2). |
| LaTeX or Typst markup inside a field (`\'e`, `\emph{…}`) | The value is text. A caller who needs formatted prose writes the entry himself; the model escapes what it prints so a stray `*` cannot change the page. |
| `#bibliography("refs.bib")` | See §4: the file is not in the compilation world, and Typst resolves the path through hayagriva. The list is built by the model instead. |
| Entry types beyond article, book and proceedings | They are read and listed, in a generic form (§3.4). A `@manual` does not print as a manual. |
| Truncating a long author list | Every author is printed. `et al.` is used in a citation, never in the list. |
| A range collapse (`45–56` to `45–56` or `45ff`) | Pages print as the field holds them, with `--` written as an en dash. |
| Index cross-references, grouped headings, subterm merging | §6. |

## 2. The BibTeX subset

The parser reads the source as a sequence of `@type{…}` blocks and ignores everything outside one —
a header comment, a `%` line, blank lines.

| Construct | Read? | Detail |
| --- | --- | --- |
| `@article`, `@book`, `@inproceedings` | yes | formatted specifically (§3.4) |
| any other `@type` | yes, structurally | formatted generically (§3.4) |
| `@comment`, `@preamble` | skipped | their body is skipped as a balanced brace group |
| `@string` | **no** | `TypstBibTexParseException`, offset of the `@` |
| a bare macro name as a value (`publisher = acme`) | **no** | exception naming the macro |
| `#` concatenation (`"Acme" # " Press"`) | **no** | exception naming the field |
| `{braced value}` | yes, nesting-aware | the braces are removed from the stored text |
| `"quoted value"` | yes | braces inside the quotes may contain a `"` |
| a bare number (`year = 2020`) | yes | kept as text |
| whitespace in a value | collapsed | runs of spaces, tabs and newlines become one space |
| `author = A and B` | yes | split on the whole word `and`; a name with a comma is `Family, Given`, otherwise the last word is the family name |
| `Family, Jr, Given` | partly | the third part is the given name and the `Jr` suffix is dropped |
| a repeated field in one entry | yes | the last value wins |
| two entries with the same key | **no** | exception naming the key |
| `pages = {45--56}` | yes | `--` prints as `–` in `TypstBibTexEntry.Pages` |

Fields are stored lowercased in `TypstBibTexEntry.Fields`, so `Keywords` and `keywords` name the same
field, and a field the model has no accessor for is still reachable through `Field(name)`.

**Decision — refuse what is not read (rejected alternative: skip it silently).** A `.bib` file whose
`@string` macro is ignored produces a bibliography that silently loses a publisher. The parser
raises `TypstBibTexParseException` with the offset instead; a caller who has such a file runs
`biber --tool` or expands the macros first.

## 3. The two styles

`TypstBibliographyStyle` has two values. The entry is the same text behind both; only the inline
form, the numbering and the order of the list differ.

### 3.1 Inline

| Style | One author | Two authors | Three or more | `prose: true` |
| --- | --- | --- | --- | --- |
| `Numeric` | `[1]` | `[1]` | `[1]` | `Chen et al. [1]` |
| `AuthorDate` | `(Dupont, 2024)` | `(Dupont and Durand, 2024)` | `(Chen et al., 2022)` | `Dupont (2024)` |

An entry with no `author` cites its key in the author position; an entry with no `year` prints
`n.d.`. The key is checked when the document is written: an unknown key raises
`TypstCitationException`, whose message names the key and every key that exists, and the model never
writes `[?]`. When `Bibliography(…)` was already called, `Citation` and `CitationContent` throw at
the call instead.

### 3.2 The list

| Style | Order | Numbering |
| --- | --- | --- |
| `Numeric` | order of first citation | `[1]`, `[2]`, … printed before the entry |
| `AuthorDate` | first author's family name, then year, then key — `StringComparer.InvariantCultureIgnoreCase` on the family name | none |

Only cited entries are listed. A document that cites nothing lists every entry in file order, which
is what makes `.Bibliography(bib)` useful on its own.

### 3.3 What an entry prints

| Style | Example |
| --- | --- |
| `Numeric` | `[1] J. Dupont, M. Durand. _A study of things_. Journal of Things 12(3), 45–56, 2024. doi: 10.1000/xyz.` |
| `AuthorDate` | `Dupont, J., Durand, M. (2024). _A study of things_. Journal of Things 12(3), 45–56. doi: 10.1000/xyz.` |

The title is emphasised with `_…_`; every other field is escaped prose, so a `*` or a `#` in a title
cannot change the page.

### 3.4 Per entry type

| Type | Numeric tail | Author-date tail |
| --- | --- | --- |
| `article` | `Journal 12(3), 45–56, 2024` | `Journal 12(3), 45–56` |
| `book` | `Acme Press, 2024` | `Acme Press` |
| `inproceedings` | `In Proceedings, 45–56, 2024` | `In Proceedings, 45–56` |
| any other | `Acme Press, 2024` | `Acme Press` |

A missing segment is dropped rather than printed empty: an entry with no publisher ends at the year.

## 4. Why the list is the model's, not `#bibliography`

Typst has the function this feature looks like: `#bibliography("refs.bib", style: "ieee")` lays out
the list, numbers it in citation order with `@key` citations and prints the numbers. It was rejected
for three reasons, measured against Typst 0.15.1:

1. **The file does not exist.** A document in this suite is a markup string and a world of sources the
   caller registered. `#bibliography` reads its path from the world, so the caller would have to
   register `refs.bib` beside the document and keep the two in step. The model already holds the
   parsed entries; emitting them costs nothing and needs no world.
2. **The style vocabulary is hayagriva's.** `style: "ieee"` is a CSL style looked up in the embedded
   hayagriva set. The two styles here are the model's, so a caller's citation is the string the test
   asserts, not whatever the CSL bundle prints.
3. **The entry text is not visible to the caller.** A CSL entry is laid out by the compiler; a
   bibliography the model builds is markup the caller can review, diff and assert.

The consequence is stated plainly: the numbering follows the document's citation order because the
model records that order, and `TypstDocument.Bibliography(string path, string? style)` — the raw
`#bibliography(…)` escape hatch — is unchanged and still available for a caller who has the file in
the world and wants hayagriva's styles.

## 5. The index

### 5.1 The mark

`IndexMark(term, subterm?)` emits one element per mark:

```typst
#context metadata((m3s-index: true, term: "Cache", sub: "eviction", page: counter(page).at(here()).first()))
```

Three facts make this work:

- `metadata` prints nothing (Typst 0.15.1, `introspection/metadata.rs`: "Exposes a value to the query
  system without producing visible content"), so a mark costs no line and no space.
- `here()` is the mark's location, and `counter(page).at(…)` is the page counter *at that location* —
  the number the reader sees on the page, not a count the model keeps.
- The `context` wrapper defers the page read to layout: the value is filled in when the page exists.

### 5.2 The list

`Index(title?)` emits one `context` block. It queries every `metadata` element, keeps the dictionaries
that carry `m3s-index: true` — unrelated metadata in the same document is ignored — then:

1. sorts the terms by their lowercase form and removes the adjacent duplicates;
2. prints a term's own page list when the term was marked without a subterm;
3. for each subterm, sorted the same way, prints it indented with its own page list.

Pages are sorted numerically and de-duplicated, so a page marked twice prints once.

### 5.3 The sort rule

Case is ignored by folding both sides to lowercase, and the fold is Typst's `lower`, a Unicode
lowercase mapping that consults no locale. That is the culture-invariant, case-insensitive order the
specification promises; it is not the collation of any language, so `Å` sorts after `Z` rather than
with `A`, and an accented term does not sit beside its unaccented twin. Two terms that differ only in
case stay two lines, adjacent.

### 5.4 What was measured

Both cases were compiled on the embedded compiler and are asserted in
`tests/Text.Typst.Integration.Tests/BibliographyAndIndexTests.cs`:

| Case | Result |
| --- | --- |
| 30 pages, one sentence each, one page break between them, marks on pages 2 (`Cache`), 5 (`Cache`/`eviction`), 12 (`buffer`), 30 (`Zebra`), index on page 31 | the index prints `buffer 12`, `Cache 2`, `eviction 5`, `Zebra 30` — in that order |
| a mark inside a `figure(placement: top)` whose 18 cm body does not fit on page 1 | the figure lands on page 2 and the index prints `Floating 2` |

So a mark in a floating element reports the page the float is laid out on, which is the page the
reader sees it on — there is no hole to document here. What is *not* reachable is the opposite case:
a mark cannot report a page it is not laid out on, and a caller who places a mark inside content that
Typst never lays out (a branch of a conditional that is not taken, an unreferenced `<label>` body)
gets no line at all.

## 6. What the index does not do

| Not done | Why, and what a caller does |
| --- | --- |
| `see` and `see also` cross-references | A reference between index entries is a second data model (a target term, a kind). The mark carries a term and a subterm; a caller who needs `see` writes the line as a `Raw` paragraph after the index and accepts that it is not sorted with the rest. |
| Grouping by initial letter (`A …`, `B …`) | The sort is the only grouping. A caller adds letter headings by querying the marks himself, or by writing the headings and splitting the index across several documents. |
| Merging a subterm into its term (`Cache, eviction` as `Cache eviction`) | The two levels are printed as they were marked; a caller marks `Cache eviction` as the term if that is the line he wants. |
| Merging terms that differ only in case or by an accent | Case-differing terms stay two lines (§5.3); an accent is a different string. |
| Page ranges (`3–7`) | Consecutive pages print one by one. Collapsing a run is an algorithm over the layout, and the index is built by Typst code the model emits, not by C#. |
| An index of figures or tables | Only `IndexMark` is queried. Figures are available through `TypstQuery` and `Outline`. |
| A `#outline`-style entry per page with leaders and dots | `par(hanging-indent: 1.2em)` is the whole layout: no dot leaders, no right-aligned numbers. |
| A mark inside a heading | It works and reports the heading's page, but the heading is also in the outline, so the term appears twice in the reader's navigation. |

## 7. The API

```csharp
public static class TypstBibTexParser
{
    public static IReadOnlyList<TypstBibTexEntry> Parse(string bibtex);
}

public sealed class TypstBibliography
{
    public static TypstBibliography Parse(string bibtex);
    public static TypstBibliography FromEntries(IEnumerable<TypstBibTexEntry> entries);
    public IReadOnlyList<TypstBibTexEntry> Entries { get; }
    public IReadOnlyList<string> Keys { get; }
    public bool Contains(string key);
    public TypstBibTexEntry this[string key] { get; }
    public TypstBibTexEntry Resolve(string key);
}

public sealed class TypstBibTexEntry
{
    public string Key { get; }
    public string EntryType { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public string? Title { get; }
    public IReadOnlyList<TypstBibAuthor> Authors { get; }
    public string? Year { get; }
    public string? Venue { get; }
    public string? Publisher { get; }
    public string? Volume { get; }
    public string? Number { get; }
    public string? Pages { get; }
    public string? Doi { get; }
    public string? Field(string name);
}

public sealed partial class TypstDocument
{
    public TypstDocument Bibliography(TypstBibliography bibliography, TypstBibliographyStyle style = TypstBibliographyStyle.Numeric);
    public TypstDocument Citation(string key, bool prose = false);
    public TypstContent CitationContent(string key, bool prose = false);
    public TypstDocument IndexMark(string term, string? subterm = null);
    public TypstDocument Index(TypstContent? title = null);
}

public static class TypstCitationContentExtensions
{
    public static TypstContent Cite(this TypstContent content, TypstDocument document, string key, bool prose = false);
}
```

`TypstBibTexParseException` carries the `Position` of the failure; `TypstCitationException` carries
the `Key` and the `AvailableKeys`.

## 8. Defaults and non-regression

- Everything here is additive. A document that calls none of these members emits the markup it emitted
  before: no preamble, no context block, no metadata.
- `TypstDocument.Cite(key, prose)` and `TypstContent.Cite(key, prose)` are untouched: they still emit
  Typst's own `@key` and `#cite(<key>)`, which need a `#bibliography(…)` in the document. The new
  members are `Citation` and the `Cite(document, key)` extension, named differently on purpose — a
  caller who wants hayagriva's machinery keeps the old pair.
- One document carries one bibliography: a second `Bibliography(…)` throws rather than printing two
  lists.
- A citation records its position when it is built, so an inline citation and a body citation number
  the same way. A `CitationContent` that is built and then discarded still holds its place; the
  documentation of the method says so.

## 9. Tests

Markup, `tests/Text.Typst.Tests/BibliographyTests.cs` and `IndexTests.cs`:

| Test | Invariant |
| --- | --- |
| `Reads_the_entries_of_a_bibtex_file` | keys, types, venue, volume, pages, publisher in file order |
| `Drops_the_braces_that_protect_case` | `{TeX}` reads `TeX` |
| `Reads_a_bare_number_as_a_year` | `year = 2020` |
| `Splits_authors_on_and_and_keeps_the_family_name` | `Dupont, Jean` gives family, given, initials |
| `Keeps_a_field_it_has_no_accessor_for` | `keywords` reaches `Field` |
| `Rejects_a_string_macro`, `Rejects_a_hash_concatenation`, `Rejects_a_macro_reference_in_a_field` | the three refusals of §2 |
| `Rejects_two_entries_with_the_same_key` | duplicate keys |
| `Numbers_five_citations_in_the_order_they_appear` | `[1]`…`[4]` by first citation |
| `Gives_the_same_number_to_a_repeated_citation` | one number per key |
| `Writes_an_author_date_citation_with_one_two_and_three_authors` | `(Dupont and Durand, 2024)`, `(Chen et al., 2022)`, prose form |
| `Sorts_an_author_date_bibliography_by_author_then_year` | Chen, Dupont, Smith, Zola order, no numbers |
| `Formats_a_book_and_a_proceedings_entry_differently` | the two tails of §3.4 |
| `Names_the_unknown_key_and_the_keys_that_exist` | `TypstCitationException` message and `AvailableKeys` |
| `Checks_the_key_immediately_when_the_bibliography_is_already_attached` | fail fast |
| `Refuses_a_citation_without_a_bibliography` | `InvalidOperationException` |
| `Refuses_a_second_bibliography` | one list per document |
| `Escapes_markup_characters_in_a_field` | `*`, `_`, `#` escaped |
| `Lists_every_entry_in_file_order_when_nothing_is_cited` | the no-citation case |
| `Writes_a_prose_numeric_citation_with_its_author` | `Chen et al. [1]` |
| `Writes_an_inline_citation_inside_a_paragraph` | the extension lands mid-sentence |
| `Builds_a_bibliography_from_entries_another_reader_produced` | `FromEntries`, duplicate rejection |
| `Writes_an_invisible_mark_carrying_the_term_and_the_page_counter` | the metadata dictionary |
| `Writes_a_subterm_and_escapes_it`, `Trims_the_term_and_the_subterm` | quoting and trimming |
| `Writes_an_index_that_queries_the_marks_and_sorts_them` | `query(metadata)`, sort, dedup, hanging indent |
| `Writes_the_index_title_as_a_heading`, `Writes_no_heading_when_the_index_has_no_title` | the optional title |
| `Rejects_a_blank_term`, `Rejects_a_blank_subterm` | `ArgumentException` |
| `The_mark_adds_no_visible_text` | nothing but metadata is written |

Compiled, `tests/Text.Typst.Integration.Tests/BibliographyAndIndexTests.cs` — the only proof of the
numbers:

| Test | What the compiler printed |
| --- | --- |
| `Renders_five_citations_and_a_numbered_bibliography` | `Layout is linear [1].` and five entries from `[1] W. Chen…` to `[5] E. Zola…`, in citation order, on one page |
| `Renders_an_author_date_bibliography_sorted_by_author` | `(Okafor, 2021)` inline; the list runs Chen, Dupont, Smith, Zola; no `[1]` |
| `Renders_a_thirty_page_manual_and_an_index_with_the_real_page_numbers` | 31 pages; `buffer 12`, `Cache 2`, `eviction 5`, `Zebra 30`; the marked pages hold only their own sentence |
| `Reports_the_page_a_mark_inside_a_floating_figure_lands_on` | the figure on page 2, the index line `Floating 2` |

## 10. What remains manual

| Case | What the caller does |
| --- | --- |
| A style other than the two | Build the list himself from `TypstBibliography.Entries` and `Raw`, or register the `.bib` in the world and call `Bibliography(path, style)` for hayagriva's CSL styles. |
| A `.bib` with `@string` macros | Expand them first (`biber --tool`), then parse. |
| Formatted text in a field (italics, LaTeX accents) | The field is text; a caller who needs rich text writes the entry as markup, or writes the entry himself. |
| A different page numbering in the printed footer | The index prints `counter(page)`, the same counter the footer prints, so a document that numbers pages `i, ii` in a preface indexes the roman number's value, not the printed glyph. `numbering` is a display style; a caller who wants the printed form is outside what `counter(page)` exposes to a query. |
| An index split across two columns | `TypstDocument.Columns(TypstColumnLayout, content)` around the index, or page-level columns; the index block is ordinary flow content. |
