using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>A run of inline material: text, emphasis, links, math.</summary>
public sealed partial class TypstContent
{
    private readonly List<TypstNode> _nodes = [];

    /// <summary>An empty run.</summary>
    public static TypstContent Empty => new();

    internal bool IsEmpty => _nodes.Count == 0;

    /// <summary>Appends text, escaped for markup mode.</summary>
    public TypstContent Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _nodes.Add(new TextNode(text));
        return this;
    }

    /// <summary>Appends strong emphasis.</summary>
    public TypstContent Bold(string text) => Bold(From(text));

    /// <summary>Appends strong emphasis around a nested run.</summary>
    public TypstContent Bold(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new BoldNode(content));
        return this;
    }

    /// <summary>Appends emphasis.</summary>
    public TypstContent Emphasis(string text) => Emphasis(From(text));

    /// <summary>Appends emphasis around a nested run.</summary>
    public TypstContent Emphasis(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new EmphasisNode(content));
        return this;
    }

    /// <summary>Appends inline code.</summary>
    public TypstContent Code(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        _nodes.Add(new InlineCodeNode(code));
        return this;
    }

    /// <summary>Appends inline math, written in Typst math syntax.</summary>
    public TypstContent Math(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _nodes.Add(new MathNode(expression, block: false));
        return this;
    }

    /// <summary>Appends a link.</summary>
    public TypstContent Link(string url, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _nodes.Add(new LinkNode(url, text is null ? null : From(text)));
        return this;
    }

    /// <summary>Appends a reference to a label.</summary>
    /// <param name="label">The label of the target.</param>
    /// <param name="supplement">
    /// The word printed before the number, overriding the target's own; null keeps it.
    /// </param>
    public TypstContent Reference(string label, string? supplement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (supplement is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(supplement);
        }

        _nodes.Add(new ReferenceNode(label, supplement));
        return this;
    }

    /// <summary>Appends a label, attached to the preceding element.</summary>
    public TypstContent Label(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _nodes.Add(new LabelNode(name));
        return this;
    }

    /// <summary>Appends a line break.</summary>
    public TypstContent LineBreak()
    {
        _nodes.Add(new LineBreakNode());
        return this;
    }

    /// <summary>Appends the current page number.</summary>
    /// <remarks>
    /// Intended for a running header or footer. The value is only known after layout, so it is
    /// wrapped in <c>context</c>.
    /// </remarks>
    public TypstContent PageNumber()
    {
        _nodes.Add(new RawNode("#context counter(page).display()"));
        return this;
    }

    /// <summary>Appends the total number of pages.</summary>
    /// <remarks>
    /// Intended for a running header or footer. The value is only known after layout, so it is
    /// wrapped in <c>context</c>.
    /// </remarks>
    public TypstContent PageCount()
    {
        _nodes.Add(new RawNode("#context counter(page).final().first()"));
        return this;
    }

    /// <summary>Appends a nested node.</summary>
    public TypstContent Node(TypstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return this;
    }

    /// <summary>Wraps a string as content.</summary>
    public static implicit operator TypstContent(string text) => From(text);

    internal void Write(TypstMarkupWriter writer)
    {
        foreach (var node in _nodes)
        {
            node.Write(writer);
        }
    }

    /// <summary>Appends this run's nodes to another run, without wrapping them.</summary>
    internal void AppendTo(TypstContent target)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var node in _nodes)
        {
            target._nodes.Add(node);
        }
    }

    private static TypstContent From(string text) => new TypstContent().Text(text);
}
