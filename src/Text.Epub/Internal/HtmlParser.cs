using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>
/// A tolerant HTML reader: it takes what a document actually contains — unclosed paragraphs,
/// <c>&lt;br&gt;</c>, a stray <c>&lt;/p&gt;</c>, an attribute without a value — and produces the tree
/// the XHTML writer serializes. It is not a general HTML parser and does not try to be: it exists so
/// that a chapter's markup can be made well-formed XML without a dependency.
/// </summary>
/// <remarks>
/// The rules are the ones that change the shape of a document: an element that starts a block closes
/// an open paragraph, a list item closes the previous item, and a closing tag pops to the nearest
/// matching element, dropping the elements it passes. Everything else is kept as it is read.
/// </remarks>
internal sealed class HtmlParser
{
    /// <summary>Elements that never have content.</summary>
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param",
        "source", "track", "wbr",
    };

    /// <summary>Elements whose content is not markup and which are dropped whole.</summary>
    private static readonly HashSet<string> RawTextElements = new(StringComparer.Ordinal)
    {
        "script", "style",
    };

    /// <summary>An element that starts a block and therefore ends an open paragraph.</summary>
    private static readonly HashSet<string> BlockStarters = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "details", "div", "dl", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr",
        "main", "nav", "ol", "p", "pre", "section", "table", "ul",
    };

    private readonly string _html;
    private readonly HtmlElement _root = new("#document");
    private readonly List<HtmlElement> _stack = [];
    private int _index;

    private HtmlParser(string html)
    {
        _html = html;
    }

    /// <summary>Parses a document, or a fragment of one, into a synthetic root element.</summary>
    public static HtmlElement Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var parser = new HtmlParser(html);
        parser._stack.Add(parser._root);
        parser.Run();

        return parser._root;
    }

    /// <summary>Finds the element whose content is the body, or the root when the input is a fragment.</summary>
    public static HtmlElement Body(HtmlElement root)
    {
        foreach (var node in root.Children)
        {
            if (node is HtmlElement { Name: "body" } body)
            {
                return body;
            }

            if (node is HtmlElement { Name: "html" } html)
            {
                foreach (var child in html.Children)
                {
                    if (child is HtmlElement { Name: "body" } nested)
                    {
                        return nested;
                    }
                }
            }
        }

        return root;
    }

    private HtmlElement Current => _stack[^1];

    private void Run()
    {
        while (_index < _html.Length)
        {
            var next = _html.IndexOf('<', _index);
            if (next < 0)
            {
                AddText(_html[_index..]);
                _index = _html.Length;
                return;
            }

            if (next > _index)
            {
                AddText(_html[_index..next]);
            }

            _index = next;

            if (StartsWith("<!--"))
            {
                SkipComment();
                continue;
            }

            if (StartsWith("<!"))
            {
                SkipTo('>');
                continue;
            }

            if (StartsWith("<?"))
            {
                SkipTo('>');
                continue;
            }

            if (StartsWith("</"))
            {
                ReadEndTag();
                continue;
            }

            ReadStartTag();
        }
    }

    private bool StartsWith(string value)
        => string.CompareOrdinal(_html, _index, value, 0, value.Length) == 0;

    private void SkipComment()
    {
        var end = _html.IndexOf("-->", _index + 4, StringComparison.Ordinal);
        _index = end < 0 ? _html.Length : end + 3;
    }

    private void SkipTo(char terminator)
    {
        var end = _html.IndexOf(terminator, _index + 1);
        _index = end < 0 ? _html.Length : end + 1;
    }

    private void AddText(string raw)
    {
        if (raw.Length == 0)
        {
            return;
        }

        var text = HtmlEntities.Decode(raw);
        if (text.Length == 0)
        {
            return;
        }

        if (Current.Children.Count > 0 && Current.Children[^1] is HtmlText previous)
        {
            Current.Children[^1] = new HtmlText(previous.Text + text);
            return;
        }

        Current.Children.Add(new HtmlText(text));
    }

    private void ReadEndTag()
    {
        var end = _html.IndexOf('>', _index + 2);
        if (end < 0)
        {
            _index = _html.Length;
            return;
        }

        var name = _html[(_index + 2)..end].Trim().ToLowerInvariant();
        _index = end + 1;

        if (name.Length == 0)
        {
            return;
        }

        for (var i = _stack.Count - 1; i > 0; i--)
        {
            if (string.Equals(_stack[i].Name, name, StringComparison.Ordinal))
            {
                _stack.RemoveRange(i, _stack.Count - i);
                return;
            }
        }

        // A closing tag that matches nothing open is ignored, as browsers do.
    }

    private void ReadStartTag()
    {
        var cursor = _index + 1;
        var nameStart = cursor;
        while (cursor < _html.Length && IsNameChar(_html[cursor]))
        {
            cursor++;
        }

        if (cursor == nameStart)
        {
            // A lone '<' that starts no tag is text.
            AddText("<");
            _index++;
            return;
        }

        var name = _html[nameStart..cursor].ToLowerInvariant();
        var attributes = new List<KeyValuePair<string, string>>();
        var selfClosing = false;

        while (cursor < _html.Length)
        {
            while (cursor < _html.Length && char.IsWhiteSpace(_html[cursor]))
            {
                cursor++;
            }

            if (cursor >= _html.Length)
            {
                break;
            }

            if (_html[cursor] == '>')
            {
                cursor++;
                break;
            }

            if (_html[cursor] == '/' && cursor + 1 < _html.Length && _html[cursor + 1] == '>')
            {
                selfClosing = true;
                cursor += 2;
                break;
            }

            var attributeStart = cursor;
            while (cursor < _html.Length && !char.IsWhiteSpace(_html[cursor]) && _html[cursor] is not ('=' or '>' or '/'))
            {
                cursor++;
            }

            if (cursor == attributeStart)
            {
                cursor++;
                continue;
            }

            var attribute = _html[attributeStart..cursor].ToLowerInvariant();
            while (cursor < _html.Length && char.IsWhiteSpace(_html[cursor]))
            {
                cursor++;
            }

            var value = string.Empty;
            if (cursor < _html.Length && _html[cursor] == '=')
            {
                cursor++;
                while (cursor < _html.Length && char.IsWhiteSpace(_html[cursor]))
                {
                    cursor++;
                }

                if (cursor < _html.Length && (_html[cursor] == '"' || _html[cursor] == '\''))
                {
                    var quote = _html[cursor];
                    cursor++;
                    var valueStart = cursor;
                    while (cursor < _html.Length && _html[cursor] != quote)
                    {
                        cursor++;
                    }

                    value = _html[valueStart..cursor];
                    if (cursor < _html.Length)
                    {
                        cursor++;
                    }
                }
                else
                {
                    var valueStart = cursor;
                    while (cursor < _html.Length && !char.IsWhiteSpace(_html[cursor]) && _html[cursor] != '>')
                    {
                        cursor++;
                    }

                    value = _html[valueStart..cursor];
                }
            }

            attributes.Add(new KeyValuePair<string, string>(attribute, HtmlEntities.Decode(value)));
        }

        _index = cursor;

        if (RawTextElements.Contains(name))
        {
            SkipRawText(name);
            return;
        }

        var element = new HtmlElement(name);
        element.Attributes.AddRange(attributes);

        if (name == "p" || BlockStarters.Contains(name))
        {
            CloseOpenParagraph();
        }

        ImpliedEnd(name);

        Current.Children.Add(element);

        if (!selfClosing && !VoidElements.Contains(name))
        {
            _stack.Add(element);
        }
    }

    private void SkipRawText(string name)
    {
        var closing = _html.IndexOf("</" + name, _index, StringComparison.OrdinalIgnoreCase);
        if (closing < 0)
        {
            _index = _html.Length;
            return;
        }

        var end = _html.IndexOf('>', closing);
        _index = end < 0 ? _html.Length : end + 1;
    }

    private void CloseOpenParagraph()
    {
        var index = _stack.Count - 1;
        while (index > 0 && _stack[index].Name != "p")
        {
            index--;
        }

        if (index > 0)
        {
            _stack.RemoveRange(index, _stack.Count - index);
        }
    }

    private void ImpliedEnd(string name)
    {
        switch (name)
        {
            case "li":
                PopWhile("li");
                break;

            case "dt":
            case "dd":
                PopWhile("dt", "dd");
                break;

            case "tr":
                PopWhile("tr");
                break;

            case "td":
            case "th":
                PopWhile("td", "th");
                break;

            case "option":
                PopWhile("option");
                break;

            default:
                break;
        }
    }

    /// <summary>Pops while the innermost element is one the new element ends implicitly.</summary>
    private void PopWhile(params string[] names)
    {
        while (_stack.Count > 1)
        {
            var top = Current.Name;
            var found = false;
            foreach (var name in names)
            {
                if (top == name)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            _stack.RemoveAt(_stack.Count - 1);
        }
    }

    private static bool IsNameChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.';
}
