using System;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Direction and language on a run: the two text properties a paragraph reads per run, as additions
/// to <see cref="TypstContent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Typst resolves <c>text(dir: …)</c> from the style chain of the paragraph for the base direction,
/// and per text item for the runs: a run whose direction differs from the paragraph's is wrapped in
/// an explicit directional embedding (<c>U+202A</c> / <c>U+202B</c> … <c>U+202C</c>) before the
/// bidirectional algorithm runs, so it takes part in the same reordering as its neighbours. That is
/// why a right-to-left run inside a left-to-right paragraph is <c>#text(dir: rtl)[…]</c> and not a
/// paragraph of its own.
/// </para>
/// <para>
/// The language works the same way for hyphenation: Typst reads <c>lang</c> per run, so a French
/// paragraph can quote a Japanese sentence and only the quote loses the French hyphenation
/// dictionary.
/// </para>
/// </remarks>
public sealed partial class TypstContent
{
    /// <summary>Appends a run whose direction differs from the paragraph's.</summary>
    /// <param name="direction">The direction of the run.</param>
    /// <param name="content">The run's content.</param>
    /// <remarks>
    /// Emitted as <c>#text(dir: …)[…]</c>. It sets the script of the run, not the base direction of
    /// the paragraph it sits in: to turn a whole paragraph around, wrap it in
    /// <see cref="TypstDocument.Direction"/>.
    /// </remarks>
    public TypstContent Direction(TypstDirection direction, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new InlineCallNode($"#text(dir: {TypstDirectionToken.Of(direction)})", content));
        return this;
    }

    /// <summary>Appends a run read in a given language, for its hyphenation and line breaking.</summary>
    /// <param name="language">The language of the run.</param>
    /// <param name="content">The run's content.</param>
    /// <remarks>Emitted as <c>#text(lang: …)[…]</c>, which overrides the document's language for
    /// this run only.</remarks>
    public TypstContent Language(TypstLanguageTag language, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new InlineCallNode($"#text(lang: {LinkNode.Quote(language.Code)})", content));
        return this;
    }
}

/// <summary>An inline Typst call that wraps content: <c>#text(dir: rtl)[…]</c>.</summary>
internal sealed class InlineCallNode(string call, TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim(call);

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }
    }
}

/// <summary>
/// A block whose paragraphs take a base direction, as <c>#block[#set text(dir: …) …]</c>.
/// </summary>
/// <remarks>
/// The set rule has to enclose the paragraph, not sit inside it: Typst's paragraph layouter reads
/// <c>dir</c> from its own style chain, and <c>block</c> has no <c>dir</c> argument in 0.15 to pass
/// it down. A content block <c>#[…]</c> would not do either — it is spliced into the surrounding
/// paragraph, so the rule inside it would become an inline embedding and leave the paragraph's base
/// direction <c>ltr</c>.
/// </remarks>
internal sealed class DirectedBlockNode(string direction, TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#block");

        using (writer.ContentBlock())
        {
            writer.WriteVerbatim($"#set text(dir: {direction})");
            writer.NewLine();
            content.Write(writer);
        }

        writer.NewLine();
    }
}

/// <summary>The Typst spelling of a <see cref="TypstDirection"/>, as <c>text(dir: …)</c> takes it.</summary>
/// <remarks>
/// Vertical directions are refused: <c>text(dir:)</c> is a horizontal property, and the compiler
/// rejects <c>ttb</c> and <c>btt</c> there with "text direction must be horizontal". A vertical run
/// is a rotated block, which is a different primitive.
/// </remarks>
internal static class TypstDirectionToken
{
    internal static string Of(TypstDirection direction) => direction switch
    {
        TypstDirection.LeftToRight => "ltr",
        TypstDirection.RightToLeft => "rtl",
        TypstDirection.TopToBottom or TypstDirection.BottomToTop => throw new ArgumentOutOfRangeException(
            nameof(direction),
            direction,
            "text direction is horizontal: the compiler refuses ttb and btt as a text property"),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unknown direction"),
    };
}
