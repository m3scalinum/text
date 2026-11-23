# 45 — Comparing two documents

## 1. Scope

Comparing two versions of an invoice or a contract means comparing two PDFs today: two files whose
text is already broken into lines, whose pages are already cut, and whose numbers are already laid
out. What a reader calls "the price changed" is then a difference of glyph positions on a page, and a
sentence that grew by one word reads as a change on every line after it.

The document model is the right place to compare. Two `TypstDocument` values are the same tree of
nodes before the compiler has decided anything: no line break, no page, no hyphenation. A diff over
that tree reports what a person edited, not what the layout did with it. It also runs without the
native library, because it never compiles — §4.

**In scope.**

- `TypstDocumentDiff.Between(TypstDocument, TypstDocument)` — the list of changes between two bodies.
- `TypstDocumentChange` and its five kinds: `TypstNodeAdded`, `TypstNodeRemoved`, `TypstTextChanged`,
  `TypstStyleChanged`, `TypstNodeMoved`.
- `TypstDocumentPath` — where in the tree each change is, by indices and by label.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Rebuilding a model from a PDF and diffing that | A page is not a tree. Spec [18](18-pdf-text.md) extracts what a page draws; the paragraphs, the table and the caption are gone, and no reconstruction puts them back with certainty. Comparing what the caller built is exact; comparing what a PDF kept is a guess. |
| Diffing the layout: line breaks, page numbers, the width of a glyph | Those are the compiler's answers, not the model's. §5. |
| Merging, patching or a three-way diff | A diff is an answer, not an edit. Writing one document from two is a different feature with a different failure mode, and a contract that merges wrongly is worse than one that cannot merge. |
| Comparing the preamble, the page setup or the document metadata | They configure the layout rather than carry the words. §5. |
| A similarity score, a percentage of change | A number that says "72% the same" invites a threshold, and a threshold on a contract is a decision this suite does not own. The caller counts the entries it cares about. |

## 2. The pairing algorithm

The tree is compared level by level: for each pair of nodes that correspond, its children are
aligned, then each aligned pair is compared and its own children are aligned, down to the leaves. The
only interesting decision is how two sibling lists are paired, and it is taken once.

**Decision — a weighted longest common subsequence over the siblings, keyed on the node kind,
recursing into each pair it produces.**

Siblings are the children of one node in order: the blocks of the body, the runs of a paragraph, the
cells of a table row. Two lists are aligned with a longest common subsequence (LCS), where two nodes
may correspond only if they have the same kind — `paragraph` with `paragraph`, `text` with `text`. A
node of the earlier list that no node of the later list corresponds to is a removal; the converse is
an addition.

### 2.1 Why not by index

Pairing the *i*-th child with the *i*-th child is the shortest code and the wrong answer. Insert one
paragraph at the top of a contract and every paragraph after it moves by one: the diff reports one
addition and N-1 changes of text, for a single edit. The item this document was written for is
precisely the case where index pairing fails — the diff must survive an insertion, not assume one
never happens.

Index pairing is also not cheaper in any way that matters: it is O(min(n, m)) against O(n·m) of the
LCS, and §6 shows what O(n·m) costs on a thousand nodes.

### 2.2 Why not by key

A node that carries a `Label` could be paired with the node of the later document that carries the
same label, and that is an exact identity — better than LCS when it applies. It applies to few
nodes: `TypstDocument.Label` is called by a caller who wants a reference, so most paragraphs, runs
and cells carry nothing. A key pass would cover a handful of nodes and leave the rest to something
else, which means two algorithms and one more place to disagree.

It is also wrong where a label is exactly what changed. Renaming `tbl:prix` to `tbl:montants` is an
edit a reader would want reported; under key pairing it is a removal and an addition, two entries for
one rename, and the label's own content never appears as a change.

Labels are therefore carried in the *path* (§3.3), where they help the caller locate a change, and
not in the pairing, where they would hide one.

### 2.3 The weight

An unweighted LCS on kinds alone is ambiguous when a list holds several nodes of one kind, which is
always: a contract is a list of paragraphs. Inserting a paragraph in the middle of three would align
paragraphs 1→1, 2→3, 3→(nothing) as readily as 1→1, 2→2, 3→3 plus one addition, and the first
answer reports two text changes for one insertion.

So a pair scores 2 when the two nodes are identical in their whole subtree, and 1 when they merely
share a kind. The LCS maximises the total, which makes an exact match always worth more than two
re-pairings, and the ambiguity disappears: the two paragraphs that did not change stay aligned, and
the inserted one is the only addition.

Identity is a signature computed once per node when the snapshot is taken: kind, text, properties and
the signatures of the children, concatenated. Two nodes with the same signature hold the same
content, which is also what §2.4 needs.

### 2.4 Moves

`TypstNodeMoved` is reported when a node disappeared from one list and appeared in the same list at
another index with an identical signature. The detection runs after the alignment, over the leftover
removals and additions only, and each removal is consumed by at most one move.

This finds a reordered paragraph, a table row that went up, a section that swapped with its
neighbour. It does not find a node that moved *and* changed: such a node is a removal and an
addition, which is honest — the diff cannot know that the new sentence is the old one edited if
nothing in the text says so.

A move is reported with two paths, `From` and `Path`, so a caller can say "this clause moved from
page 2 to page 7" without reading the whole list.

### 2.5 What the recursion compares

For a pair produced by the alignment, in this order:

1. the text payload, when both nodes carry one — `TypstTextChanged`;
2. every other scalar property, by name — `TypstStyleChanged`, one entry per property;
3. the children, by a recursive alignment.

Text and properties are separated because they answer different questions. A price is text: its
change is what the reader edited. A font size is a property: its change is how the document presents
the same words. Reporting a size change as a text change would lose the old and the new value, and
reporting a price change as a property change would lose that it is a *sentence* that changed.

## 3. A change

### 3.1 The five kinds

| Type | `Kind` | Answers |
| --- | --- | --- |
| `TypstNodeAdded` | `NodeAdded` | A node exists in the later document and not in the earlier one. Carries the node's kind. |
| `TypstNodeRemoved` | `NodeRemoved` | The converse. Carries the node's kind. |
| `TypstTextChanged` | `TextChanged` | A node kept its place and its text payload changed. Carries `OldText` and `NewText`. |
| `TypstStyleChanged` | `StyleChanged` | A node kept its place and one of its other attributes changed. Carries `Property`, `OldValue` and `NewValue`, either value null when the attribute is absent on that side. |
| `TypstNodeMoved` | `NodeMoved` | A node is in both documents with the same content and a different place. Carries `From` and `Path`. |

The entries are five sealed types over one abstract `TypstDocumentChange`, not one record with five
nullable fields. A caller that handles `TypstTextChanged` and `TypstStyleChanged` then cannot read an
`OldText` that is not there: the type names what the entry holds. `Kind` exists beside the types so a
caller can switch on a value — over a wire, or in a log — without pattern matching.

### 3.2 An entry, with a real example

Two invoices that differ by one price:

```csharp
var before = TypstDocument.Create()
    .Heading(1, "Facture 2026-0042")
    .Paragraph("Client : ACME Industrie")
    .Table(t => t
        .HeaderRow("Désignation", "Montant")
        .Row("Conseil", "1 200,00 €")
        .Row("Formation", "800,00 €"))
    .Paragraph("Total : 12,00 €");

var after = TypstDocument.Create()
    .Heading(1, "Facture 2026-0042")
    .Paragraph("Client : ACME Industrie")
    .Table(t => t
        .HeaderRow("Désignation", "Montant")
        .Row("Conseil", "1 200,00 €")
        .Row("Formation", "800,00 €"))
    .Paragraph("Total : 13,00 €");

foreach (var change in TypstDocumentDiff.Between(before, after))
{
    Console.WriteLine(change);
}
```

```text
text at body[3].inline[0]: "Total : 12,00 €" -> "Total : 13,00 €"
```

One entry, for one edit. That is the whole contract of this document: a diff that answered three
entries for one changed digit — a removal of the paragraph, an addition of its replacement, and a
move — would be a text comparison wearing a tree's clothes.

Read as a value, the entry is:

| Field | Value |
| --- | --- |
| type | `TypstTextChanged` |
| `Kind` | `TypstDocumentChangeKind.TextChanged` |
| `Path.Text` | `body[3].inline[0]` |
| `Path.Label` | null — the paragraph is not labelled |
| `OldText` | `Total : 12,00 €` |
| `NewText` | `Total : 13,00 €` |

The four kinds are told apart by type, and a caller that wants to act on each writes:

```csharp
foreach (var change in TypstDocumentDiff.Between(before, after))
{
    switch (change)
    {
        case TypstTextChanged text:
            Console.WriteLine("text " + text.Path.Text + ": " + text.OldText + " -> " + text.NewText);
            break;

        case TypstStyleChanged style:
            Console.WriteLine("style " + style.Path.Text + ": " + style.Property);
            break;

        case TypstNodeAdded added:
            Console.WriteLine("added " + added.Node + " at " + added.Path.Text);
            break;

        case TypstNodeMoved moved:
            Console.WriteLine("moved from " + moved.From.Text + " to " + moved.Path.Text);
            break;
    }
}
```

### 3.3 The path

`TypstDocumentPath` is a list of segments from the body down to the node, joined by dots:

| Path | Locates |
| --- | --- |
| `body[3]` | the fourth block of the body |
| `body[3].inline[0]` | the first run inside that block |
| `body[2].row[1].cell[1].inline[0]` | the first run of the second cell of the second body row of the third block |
| `body[0].caption[0].inline[0]` | the first run of the caption of the first block |
| `body[0].section[2].inline[0]` | the first run of the third block of a section's body |

A segment carries every index its step needed, so a table cell is located by its row and its column
in one segment and a caller never has to know how the table stores its rows. `Segments` exposes the
same path split, for a caller that walks rather than prints.

`Label` carries the nearest label in scope: the label a figure or a table was given, or the name of a
label node inside the flow. It is null when nothing around the node is labelled. A change inside a
labelled table therefore reads `body[0].row[1].cell[1]` with `Label` set to `tbl:prices`, which is
what a caller who named its tables can report against.

A path is a locator, not a key: two documents that differ elsewhere produce different indices for
the same paragraph, and that is the point — the path describes *where the change is now*. For the
removals it describes where the node *was*.

### 3.4 Text or style

The text payload of a node is the string its kind carries: the words of a `text` node, the code of an
inline or block `code`, the expression of a `math`, the source of a `raw` node, the name of a `label`.
Everything else is a property, compared by name: `level` on a heading, `size` on styled text, `url`
on a link, `colspan` on a cell, `image` and `alt` on a figure, `columns` on a table.

Properties are strings in the change, formatted by the model's own invariant formatting
(`10pt`, `true`, `Left`), so a diff is comparable across machines and cultures.

## 4. The diff compiles nothing

The comparison runs over the model, so it needs no compiler, no native library, no font and no
asset. That is not a detail of the implementation, it is the reason the feature is usable:

- it runs in a process that has no native asset — a web request, a job that has no `sharp-typst`;
- it runs on a document whose assets are absent: a figure pointing at `assets/revenue.png` is
  compared, and the file never has to exist;
- it is fast enough to run on every save, since it costs no layout (§6);
- it cannot fail on a document the compiler would refuse. A document with a broken label reference
  is still a tree, and two versions of it still differ in a way worth reporting.

Both documents are read, never written: the model is write-only and the diff takes two finished
values.

## 5. What the diff does not see

The diff compares the body's tree, and only that. Two documents identical here can still print
differently, and two that read differently can compare identical. Both directions matter.

**Not compared, because they are the compiler's answers:**

| Not seen | Decided by |
| --- | --- |
| Line breaks, justification, hyphenation | the layout, from the width and the font |
| Page breaks, page numbers, "page 3 of 7" | the layout, from the flow |
| Where a table row breaks across pages | the layout, from the row's height |
| The number a `#ref` prints, the entries of an outline or an index | counters resolved at layout time |
| Missing fonts, a font that falls back | the font book of the compilation |
| Syntax errors, unresolved labels, an image that does not exist | the compilation, which the diff never runs |

**Not compared, because the model keeps them outside the body:**

| Not seen | Where it lives |
| --- | --- |
| Page size, margins, columns, page fill | `TypstDocument.Page`, `TypstDocumentStyle` |
| Language, numbering patterns, running headers and footers of a style | `TypstDocumentStyle` |
| Title, author, keywords, date | `TypstDocument.Metadata` |
| `#let`, `#import`, `#set`, `#show` lines | the preamble |

A document whose margin changed from 2 cm to 3 cm therefore compares empty. This is a scope
decision, not an oversight: the body is what a reader reads, and a margin is not a change to the
contract. A caller that must report layout changes compares the two compiled documents, or the two
option values, itself.

**Not seen, because a node holds it in a shape the reader flattens:**

| Not seen | Why |
| --- | --- |
| A node type the reader does not know, beyond its presence | it is compared as its type name and nothing it holds; a node added to the model is diffed when this reader learns it |
| The bytes of a `Raw` call | compared as text, so `#v(1em)` and `#v(1.0em)` differ, and that is the honest answer — the model does not parse Typst |
| The content of a bibliography or a citation context | the entries are resolved when the document is written, not stored as nodes |

## 6. Cost

The alignment is O(n·m) in time and memory for two sibling lists of n and m nodes: the score table is
an `int[n+1, m+1]`. Total cost is the sum over every pair of lists compared, so a document of N nodes
shaped as B blocks of I runs costs O(B² + B·I²) — quadratic in the width of each list, not in N.

Measured on the test host (Release, x64), comparing two documents and reporting every change:

| Document | Shape | Time |
| --- | --- | --- |
| 1 000 nodes, 200 changes | 600 body blocks, 3 runs each | 31 ms cold, 37 ms warm |
| 1 001 nodes, 1 change | one flat body of 1 000 paragraphs (10⁶ cells) | 79 ms |

The flat thousand-paragraph body is the worst shape the model allows and still answers in under a
tenth of a second; a real invoice is a few dozen blocks. The cold run includes building the field
table by reflection, once per process.

Memory is the score table of the largest list pair: 4 MB for two lists of 1 000 nodes, less for every
realistic document. Nothing is cached between calls and no snapshot outlives the call, so a service
that diffs on demand holds neither document.

Two ways to make it cheaper were rejected: capping the table (Hirschberg's linear-memory variant)
buys memory at the cost of a slower run and more code, for a table that fits in a few megabytes; and
matching only the first differing node, then trusting the rest of the list, is what index pairing
does, and §2.1 says why it is wrong.

## 7. Tests

`tests/Text.Typst.Tests/DocumentDiffTests.cs` holds one test per promise made above:

| Test | Proves |
| --- | --- |
| `Two_documents_that_differ_by_one_price_produce_exactly_one_change` | the item's acceptance case: one `TypstTextChanged`, with its path, and no noise |
| `An_inserted_paragraph_is_one_addition_and_not_a_shift_of_every_paragraph_after_it` | LCS over siblings: an insertion is one `NodeAdded` |
| `A_removed_paragraph_is_one_removal` | the converse |
| `A_paragraph_that_kept_its_text_and_changed_its_place_is_one_move` | `NodeMoved`, with `From` and `Path` |
| `A_changed_font_size_is_a_style_change_with_the_old_and_the_new_value` | `StyleChanged` names `size`, old `10pt`, new `12pt` |
| `A_changed_heading_level_is_a_style_change_named_after_its_property` | the same for a structural property |
| `A_changed_table_cell_is_one_change_located_in_that_cell` | the path reaches `body[0].row[1].cell[1].inline[0]` |
| `A_change_inside_a_labelled_table_carries_the_label_in_its_path` | `Path.Label` |
| `A_change_in_a_caption_is_located_in_the_caption` | the caption is part of the tree |
| `A_section_is_compared_through_its_body_and_its_running_text` | a section's body is diffed, not skipped |
| `The_diff_reads_a_document_that_the_compiler_could_not_compile` | no compilation: a missing asset does not stop the diff |
| `Two_identical_documents_produce_no_change` | the empty list, and the same document compared with itself |
| `A_document_compared_with_null_is_refused` | the argument contract |

A regression that turns one price change into three entries fails the first test, which is the point
of writing it as the item states it.
