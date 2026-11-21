using System.Collections.Generic;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>A node of the parsed HTML tree the chapter writer serializes.</summary>
internal abstract class HtmlNode
{
}

/// <summary>An element, with its attributes in document order and its children.</summary>
internal sealed class HtmlElement : HtmlNode
{
    public HtmlElement(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<KeyValuePair<string, string>> Attributes { get; } = [];

    public List<HtmlNode> Children { get; } = [];
}

/// <summary>A run of text, already decoded.</summary>
internal sealed class HtmlText : HtmlNode
{
    public HtmlText(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
