using System;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// An invisible index mark: a term, an optional subterm, and the page the mark is laid out on.
/// </summary>
/// <remarks>
/// The mark emits <c>#context metadata((…))</c> — a zero-size metadata element whose <c>page</c>
/// field is read from <c>counter(page).at(here())</c> at layout time. Nothing of it reaches the
/// page; the value is only visible to the query <see cref="TypstIndexNode"/> runs at the end of the
/// document.
/// </remarks>
internal sealed class TypstIndexMarkNode(string term, string? subterm) : TypstNode
{
    /// <summary>The dictionary key every index mark carries, so unrelated metadata is filtered out.</summary>
    internal const string Marker = "m3s-index";

    internal override void Write(TypstMarkupWriter writer)
    {
        var sub = subterm is null ? "none" : Quote(subterm);
        writer.WriteVerbatim("#context metadata((");
        writer.WriteVerbatim(Marker + ": true, term: " + Quote(term) + ", sub: " + sub);
        writer.WriteVerbatim(", page: counter(page).at(here()).first()))");
        writer.NewLine();
    }

    /// <summary>Writes a Typst string literal for <paramref name="text"/>.</summary>
    internal static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');

        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => string.Empty,
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}

/// <summary>The final alphabetical index, resolved from the marks the document placed.</summary>
/// <remarks>
/// <para>
/// The list is built at layout time, not by the model: a <c>context</c> block queries every
/// <c>metadata</c> element tagged with <see cref="TypstIndexMarkNode.Marker"/>, groups it by term
/// and then by subterm, de-duplicates and sorts the page numbers, and prints one paragraph per
/// line. Terms and subterms are sorted by their lowercase form, a Unicode lowercase fold that reads
/// the same in every language, so the order is culture-invariant and case is ignored.
/// </para>
/// <para>
/// A term that is only ever marked under a subterm prints no line of its own: it appears as the
/// indented line of the subterm. Terms that differ only in case stay two lines, adjacent.
/// </para>
/// </remarks>
internal sealed class TypstIndexNode(TypstContent? title) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (title is not null)
        {
            writer.BlankLine();
            writer.WriteVerbatim("= ");
            title.Write(writer);
            writer.NewLine();
        }

        writer.BlankLine();
        writer.WriteVerbatim(IndexCode);
        writer.NewLine();
    }

    /// <summary>
    /// The Typst source that renders the index. Kept as one block on purpose: it is the whole
    /// behaviour of the feature and is read as Typst, not as C# interpolation.
    /// </summary>
    internal const string IndexCode = """
        #context {
          let index-fold(it) = str(lower(it))
          let index-marks = query(metadata)
            .map(it => it.value)
            .filter(it => type(it) == dictionary and it.at("m3s-index", default: false))
          let index-line(term, values, depth) = {
            let pages = values.map(it => it.page).sorted().dedup()
            if pages.len() > 0 {
              pad(left: depth * 1.2em, par(hanging-indent: 1.2em)[#term #pages.map(str).join(", ")])
            }
          }
          for term in index-marks.map(it => it.term).sorted(key: index-fold).dedup() {
            let own = index-marks.filter(it => it.term == term)
            index-line(term, own.filter(it => it.sub == none), 0)
            for sub in own.filter(it => it.sub != none).map(it => it.sub).sorted(key: index-fold).dedup() {
              index-line(sub, own.filter(it => it.sub == sub), 1)
            }
          }
        }
        """;
}
