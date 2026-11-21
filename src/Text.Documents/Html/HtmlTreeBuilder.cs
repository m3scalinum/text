using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

internal abstract class HtmlNode;

internal sealed class HtmlText(string text) : HtmlNode
{
    public string Text { get; } = text;
}

internal sealed class HtmlElement(string name) : HtmlNode
{
    private readonly List<HtmlAttribute> _attributes = [];
    private readonly List<HtmlNode> _children = [];

    /// <summary>The lower-cased tag name.</summary>
    public string Name { get; } = name;

    public IReadOnlyList<HtmlNode> Children => _children;

    public void Add(HtmlNode node) => _children.Add(node);

    public void AddAttribute(HtmlAttribute attribute)
    {
        foreach (var existing in _attributes)
        {
            if (string.Equals(existing.Name, attribute.Name, StringComparison.Ordinal))
            {
                // HTML keeps the first occurrence of a duplicated attribute.
                return;
            }
        }

        _attributes.Add(attribute);
    }

    public string? Attribute(string name)
    {
        foreach (var attribute in _attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                return attribute.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// Turns a token stream into a small tree, repairing the omissions HTML allows: void elements,
/// unclosed paragraphs and list items, missing table cells, and stray end tags.
/// </summary>
internal static class HtmlTreeBuilder
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Start tags that end an open paragraph, as browsers do.</summary>
    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "details", "div", "dl", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr",
        "main", "menu", "nav", "ol", "p", "pre", "section", "table", "ul",
    };

    private static readonly string[] ParagraphBoundaries =
        ["li", "td", "th", "table", "body", "html", "#document"];

    private static readonly string[] ListBoundaries = ["ul", "ol", "table", "body", "html", "#document"];

    private static readonly string[] CellBoundaries = ["tr", "table", "body", "html", "#document"];

    private static readonly string[] RowBoundaries = ["table", "body", "html", "#document"];

    private static readonly string[] SectionBoundaries = ["table", "body", "html", "#document"];

    private static readonly string[] TermBoundaries = ["dl", "body", "html", "#document"];

    public static HtmlElement Parse(string html)
    {
        var root = new HtmlElement("#document");
        var stack = new List<HtmlElement> { root };
        var tokenizer = new HtmlTokenizer(html);

        while (tokenizer.TryRead(out var token))
        {
            switch (token.Kind)
            {
                case HtmlTokenKind.Text:
                    stack[^1].Add(new HtmlText(token.Text));
                    break;

                case HtmlTokenKind.StartTag:
                    OpenElement(stack, token);
                    break;

                case HtmlTokenKind.EndTag:
                    CloseElement(stack, token.Name);
                    break;

                default:
                    break;
            }
        }

        return root;
    }

    private static void OpenElement(List<HtmlElement> stack, HtmlToken token)
    {
        var name = token.Name;

        if (name is not "p" && BlockTags.Contains(name))
        {
            CloseAny(stack, ["p"], ParagraphBoundaries);
        }

        switch (name)
        {
            case "p":
                CloseAny(stack, ["p"], ParagraphBoundaries);
                break;
            case "li":
                CloseAny(stack, ["li"], ListBoundaries);
                break;
            case "dt":
            case "dd":
                CloseAny(stack, ["dt", "dd"], TermBoundaries);
                break;
            case "tr":
                CloseAny(stack, ["td", "th"], CellBoundaries);
                CloseAny(stack, ["tr"], RowBoundaries);
                break;
            case "td":
            case "th":
                CloseAny(stack, ["td", "th"], CellBoundaries);
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                CloseAny(stack, ["td", "th"], CellBoundaries);
                CloseAny(stack, ["tr"], RowBoundaries);
                CloseAny(stack, ["thead", "tbody", "tfoot"], SectionBoundaries);
                break;
            case "option":
                CloseAny(stack, ["option"], ["select"]);
                break;
            default:
                break;
        }

        var element = new HtmlElement(name);

        foreach (var attribute in token.Attributes)
        {
            element.AddAttribute(attribute);
        }

        stack[^1].Add(element);

        if (!token.SelfClosing && !VoidElements.Contains(name))
        {
            stack.Add(element);
        }
    }

    private static void CloseElement(List<HtmlElement> stack, string name)
    {
        for (var index = stack.Count - 1; index >= 1; index--)
        {
            if (string.Equals(stack[index].Name, name, StringComparison.Ordinal))
            {
                stack.RemoveRange(index, stack.Count - index);
                return;
            }
        }

        // A stray end tag (`</p>` with no open paragraph) closes nothing.
    }

    /// <summary>Closes the innermost target found above every boundary, if there is one.</summary>
    private static void CloseAny(List<HtmlElement> stack, string[] targets, string[] boundaries)
    {
        for (var index = stack.Count - 1; index >= 1; index--)
        {
            var name = stack[index].Name;

            foreach (var target in targets)
            {
                if (string.Equals(name, target, StringComparison.Ordinal))
                {
                    stack.RemoveRange(index, stack.Count - index);
                    return;
                }
            }

            foreach (var boundary in boundaries)
            {
                if (string.Equals(name, boundary, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }
    }
}
