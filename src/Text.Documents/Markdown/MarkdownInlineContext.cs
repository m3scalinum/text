using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>A resolved reference link definition: <c>[label]: destination "title"</c>.</summary>
internal readonly struct MarkdownLinkDefinition(string label, string destination, string? title)
{
    public string Label { get; } = label;

    public string Destination { get; } = destination;

    public string? Title { get; } = title;
}

/// <summary>What the inline pass needs from the document around it.</summary>
internal interface IMarkdownInlineContext
{
    /// <summary>Looks a reference link definition up by normalised label.</summary>
    bool TryGetLink(string label, out MarkdownLinkDefinition definition);

    /// <summary>
    /// Consumes the footnote with this label, returning its content once. Null when the label is
    /// undefined or has already been used, so a second reference cannot duplicate the footnote.
    /// </summary>
    TypstContent? TakeFootnote(string label);

    /// <summary>Resolves an image destination to a registered asset, or fails the import.</summary>
    string ResolveImage(string destination);

    /// <summary>Records one dropped construct.</summary>
    void Drop(string constructName);
}
