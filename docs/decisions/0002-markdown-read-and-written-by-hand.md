# 0002 — Markdown is read and written by hand, not through a Markdown crate

**Status:** accepted. **Area:** `M3scalinum.Text.Documents`, Markdown import and export.

## Decision

`M3scalinum.Text.Documents` parses and emits Markdown itself. It takes no dependency on
`pulldown-cmark`, `markdown-rs`, `comrak` or any other Markdown crate.

## Context

Three properties decide this, and none of them is about speed.

**The package has no native dependency.** `M3scalinum.Text.Documents` produces Typst markup and imports
Markdown and HTML with no library to load and no compiler present — that is why it ships as a package
of its own, separate from the compiler train. A Rust Markdown parser would make a managed-only package
carry a native asset for one format, on every runtime, in every package that references it.

**The result is a document model, not a token stream.** The importer's contract is a
`TypstDocument` plus a list of the constructs it could not represent, each named and counted, so a
lossy import is inspectable instead of silent
([06 §5](../specifications/06-markdown-and-text-export.md)). A parser library hands back events or an
AST in its own vocabulary; the mapping to `HeadingNode`, `TableNode`, `StyledTextNode` and the rest
would be written either way, and the library would add a second tree to walk rather than remove one.

**The exporter has to be a fixed point.** `FromMarkdown(md).ToMarkdown()` must return Markdown that
re-exports to itself, with escaping applied to text nodes and never to generated syntax, and with a
fence longer than the longest backtick run inside it. That is a property of this package's output, not
of a parser's.

The dependency policy records what was on the table:
[dependencies.md](../specifications/dependencies.md) lists `markdown-rs` 0.1.0 as **rejected** — a
non-standard licence and 675 downloads in ninety days — and `epub` 2.1.6 as rejected for GPL-3.0.
The alternative that would have to be argued against the policy, `pulldown-cmark`, would still leave
the mapping and the exporter to write — and it is not in the register, because this package does not
use it.

## Consequences

- Markdown support tracks the subset the specifications name, not CommonMark in full. Anything outside
  it is reported as a dropped construct with its count, and documented in the fidelity contract.
- Two writers exist for two surfaces: the model-level writer described in
  [06 §8](../specifications/06-markdown-and-text-export.md), and the compiler-level one that walks the
  HTML tree of a compiled document. They do not share code, because they do not share an input.
- A future crate that offers a lossless round trip into this model, a licence the policy allows and no
  native asset would reopen this decision. None is known today.
