using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>Converts the repaired HTML tree into document-model nodes.</summary>
internal sealed class HtmlDocumentConverter
{
    /// <summary>Elements whose content is invisible in a paged document.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "head", "title", "meta", "link", "base", "param", "source", "track", "wbr",
        "input", "button", "select", "option", "optgroup", "textarea", "script", "style",
        "noscript", "template",
    };

    /// <summary>Elements dropped together with their subtree, because their text is not prose.</summary>
    private static readonly HashSet<string> DroppedSubtrees = new(StringComparer.Ordinal)
    {
        "iframe", "object", "embed", "video", "audio", "canvas", "svg", "math", "map",
    };

    /// <summary>Structural elements that contribute no output but keep their children.</summary>
    private static readonly HashSet<string> Transparent = new(StringComparer.Ordinal)
    {
        "html", "body", "div", "span", "section", "article", "header", "footer", "main", "nav",
        "aside", "figure", "figcaption", "form", "fieldset", "details", "summary", "address",
        "center", "hgroup", "menu", "picture",
    };

    private readonly TypstHtmlImportOptions _options;
    private readonly Dictionary<string, int> _dropped = new(StringComparer.Ordinal);

    public HtmlDocumentConverter(TypstHtmlImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public TypstHtmlImportResult Convert(HtmlElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var document = TypstDocument.Create();
        var blocks = new List<TypstNode>();

        VisitBlocks(FindBody(root), blocks);

        foreach (var block in blocks)
        {
            document.Add(block);
        }

        return new TypstHtmlImportResult(document, SnapshotDropped());
    }

    private static HtmlElement FindBody(HtmlElement root)
    {
        if (root.Name is "body" or "html")
        {
            return root;
        }

        foreach (var node in root.Children)
        {
            if (node is HtmlElement { Name: "html" } html)
            {
                foreach (var child in html.Children)
                {
                    if (child is HtmlElement { Name: "body" } body)
                    {
                        return body;
                    }
                }

                return html;
            }

            if (node is HtmlElement { Name: "body" } body2)
            {
                return body2;
            }
        }

        return root;
    }

    private void VisitBlocks(HtmlElement container, List<TypstNode> into)
    {
        var inline = new InlineBuilder();

        foreach (var node in container.Children)
        {
            if (node is HtmlText text)
            {
                inline.AppendText(text.Text);
                continue;
            }

            if (node is HtmlElement element)
            {
                VisitBlockElement(element, into, inline);
            }
        }

        inline.Flush(into);
    }

    private void VisitBlockElement(HtmlElement element, List<TypstNode> into, InlineBuilder inline)
    {
        switch (element.Name)
        {
            case "p":
                inline.Flush(into);
                AddParagraph(into, element);
                break;

            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                inline.Flush(into);
                AddHeading(into, element);
                break;

            case "ul":
            case "ol":
                inline.Flush(into);
                into.Add(BuildList(element));
                break;

            case "li":
                // A list item outside a list still has text worth keeping.
                inline.Flush(into);
                AddParagraph(into, element);
                break;

            case "table":
                inline.Flush(into);
                var table = BuildTable(element);

                if (table is not null)
                {
                    into.Add(table);
                }

                break;

            case "blockquote":
                inline.Flush(into);
                into.Add(BuildQuote(element));
                break;

            case "pre":
                inline.Flush(into);
                into.Add(BuildCode(element));
                break;

            case "img":
                inline.Flush(into);
                var figure = BuildFigure(element);

                if (figure is not null)
                {
                    into.Add(figure);
                }

                break;

            case "hr":
                inline.Flush(into);
                into.Add(new SimpleCallNode("#divider()"));
                break;

            case "dl":
                inline.Flush(into);
                var terms = BuildTermList(element);

                if (terms is not null)
                {
                    into.Add(terms);
                }

                break;

            case "br":
                inline.AppendNode(new LineBreakNode());
                break;

            case "strong":
            case "b":
            case "em":
            case "i":
            case "code":
            case "a":
            case "sup":
            case "sub":
                VisitInlineElement(element, inline);
                break;

            default:
                if (Ignored.Contains(element.Name))
                {
                    break;
                }

                if (DroppedSubtrees.Contains(element.Name))
                {
                    Drop(element.Name);
                    break;
                }

                if (Transparent.Contains(element.Name))
                {
                    inline.Flush(into);
                    VisitBlocks(element, into);
                    break;
                }

                HandleUnknown(element);
                VisitBlocks(element, into);
                break;
        }
    }

    private void AddParagraph(List<TypstNode> into, HtmlElement element)
    {
        var content = ConvertInlineChildren(element);

        if (!content.IsEmpty)
        {
            into.Add(new ParagraphNode(content));
        }
    }

    private void AddHeading(List<TypstNode> into, HtmlElement element)
    {
        var content = ConvertInlineChildren(element);

        if (!content.IsEmpty)
        {
            into.Add(new HeadingNode(element.Name[1] - '0', content));
        }
    }

    /// <summary>Converts the children of an inline element, then applies its own style.</summary>
    private TypstContent ConvertInline(HtmlElement element)
        => ApplyStyle(element, ConvertInlineChildren(element));

    /// <summary>Converts the children of an element without applying its own style.</summary>
    private TypstContent ConvertInlineChildren(HtmlElement element)
    {
        var inline = new InlineBuilder();

        foreach (var node in element.Children)
        {
            VisitInlineNode(node, inline);
        }

        return inline.Content;
    }

    private void VisitInlineNode(HtmlNode node, InlineBuilder inline)
    {
        if (node is HtmlText text)
        {
            inline.AppendText(text.Text);
            return;
        }

        if (node is HtmlElement element)
        {
            VisitInlineElement(element, inline);
        }
    }

    private void VisitInlineElement(HtmlElement element, InlineBuilder inline)
    {
        switch (element.Name)
        {
            case "strong":
            case "b":
                inline.AppendNode(new BoldNode(ConvertInline(element)));
                break;

            case "em":
            case "i":
                inline.AppendNode(new EmphasisNode(ConvertInline(element)));
                break;

            case "code":
                inline.AppendNode(new InlineCodeNode(Collapse(CollectText(element))));
                break;

            case "a":
                var href = element.Attribute("href");

                if (string.IsNullOrWhiteSpace(href))
                {
                    foreach (var child in element.Children)
                    {
                        VisitInlineNode(child, inline);
                    }
                }
                else
                {
                    inline.AppendNode(new LinkNode(href, ConvertInline(element)));
                }

                break;

            case "sup":
                inline.AppendNode(new WrapperNode("super", ConvertInline(element)));
                break;

            case "sub":
                inline.AppendNode(new WrapperNode("sub", ConvertInline(element)));
                break;

            case "br":
                inline.AppendNode(new LineBreakNode());
                break;

            case "hr":
                inline.AppendLineBreak();
                break;

            case "img":
                var source = element.Attribute("src");

                if (!string.IsNullOrWhiteSpace(source))
                {
                    ResolveAsset(source);
                }

                var alt = element.Attribute("alt");

                if (!string.IsNullOrWhiteSpace(alt))
                {
                    inline.AppendText(alt);
                }

                break;

            case "p":
            case "div":
            case "section":
            case "article":
            case "li":
            case "ul":
            case "ol":
            case "dl":
            case "dt":
            case "dd":
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
            case "table":
            case "thead":
            case "tbody":
            case "tfoot":
            case "tr":
            case "td":
            case "th":
            case "blockquote":
            case "pre":
            case "figure":
            case "figcaption":
                inline.AppendLineBreak();

                foreach (var child in element.Children)
                {
                    VisitInlineNode(child, inline);
                }

                break;

            default:
                if (Ignored.Contains(element.Name))
                {
                    break;
                }

                if (DroppedSubtrees.Contains(element.Name))
                {
                    Drop(element.Name);
                    break;
                }

                if (!Transparent.Contains(element.Name))
                {
                    HandleUnknown(element);
                }

                var nested = new InlineBuilder();

                foreach (var child in element.Children)
                {
                    VisitInlineNode(child, nested);
                }

                inline.AppendContent(ApplyStyle(element, nested.Content), nested.EndsWithSpace);
                break;
        }
    }

    private ListNode BuildList(HtmlElement list)
    {
        var ordered = string.Equals(list.Name, "ol", StringComparison.Ordinal);
        var items = new List<ListItem>();

        foreach (var node in list.Children)
        {
            if (node is HtmlText text)
            {
                // Whitespace between items is formatting, not content.
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    items.Add(new ListItem(InlineBuilder.FromText(text.Text)));
                }

                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            switch (element.Name)
            {
                case "li":
                    items.Add(BuildListItem(element));
                    break;

                case "ul":
                case "ol":
                    if (items.Count == 0)
                    {
                        items.Add(new ListItem(new TypstContent()));
                    }

                    items[^1].Children.Add(BuildList(element));
                    break;

                default:
                    if (Ignored.Contains(element.Name))
                    {
                        break;
                    }

                    HandleUnknown(element);
                    break;
            }
        }

        return new ListNode(items, ordered);
    }

    private ListItem BuildListItem(HtmlElement item)
    {
        var inline = new InlineBuilder();
        var children = new List<TypstNode>();

        foreach (var node in item.Children)
        {
            if (node is HtmlText text)
            {
                inline.AppendText(text.Text);
                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            switch (element.Name)
            {
                case "ul":
                case "ol":
                    children.Add(BuildList(element));
                    break;

                case "p":
                case "div":
                case "section":
                    inline.AppendLineBreak();

                    foreach (var child in element.Children)
                    {
                        VisitInlineNode(child, inline);
                    }

                    break;

                case "pre":
                    children.Add(BuildCode(element));
                    break;

                case "table":
                    var table = BuildTable(element);

                    if (table is not null)
                    {
                        children.Add(table);
                    }

                    break;

                case "blockquote":
                    children.Add(BuildQuote(element));
                    break;

                default:
                    VisitInlineElement(element, inline);
                    break;
            }
        }

        return new ListItem(inline.Content, children);
    }

    private QuoteNode BuildQuote(HtmlElement blockquote)
    {
        var inner = new List<TypstNode>();
        VisitBlocks(blockquote, inner);

        var content = new TypstContent();

        foreach (var node in inner)
        {
            content.Node(node);
        }

        return new QuoteNode(content, block: true);
    }

    private static CodeBlockNode BuildCode(HtmlElement pre)
    {
        var code = CollectCode(pre);
        var language = FindLanguage(pre) ?? string.Empty;
        return new CodeBlockNode(language, code);
    }

    private FigureNode? BuildFigure(HtmlElement image)
    {
        var source = image.Attribute("src");

        if (string.IsNullOrWhiteSpace(source))
        {
            Drop("img");
            return null;
        }

        return new FigureNode(new TypstFigure().Image(ResolveAsset(source)));
    }

    private string ResolveAsset(string source)
    {
        var asset = _options.AssetResolver?.Invoke(source);

        if (string.IsNullOrWhiteSpace(asset))
        {
            throw new TypstHtmlImportException(
                $"the img source '{source}' does not resolve to a registered asset; " +
                "register it with the world builder and return its name from " +
                nameof(TypstHtmlImportOptions) + "." + nameof(TypstHtmlImportOptions.AssetResolver) + ".")
            {
                ElementName = "img",
            };
        }

        return asset;
    }

    private TermListNode? BuildTermList(HtmlElement list)
    {
        var items = new List<(string Term, TypstContent Description)>();
        string? term = null;

        foreach (var node in list.Children)
        {
            if (node is HtmlText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Text) && term is not null)
                {
                    term += " " + Collapse(text.Text);
                }

                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            switch (element.Name)
            {
                case "dt":
                    term = Collapse(CollectText(element));
                    break;

                case "dd":
                    items.Add((term ?? string.Empty, ConvertInlineChildren(element)));
                    term = null;
                    break;

                default:
                    if (Ignored.Contains(element.Name))
                    {
                        break;
                    }

                    HandleUnknown(element);
                    break;
            }
        }

        return items.Count == 0 ? null : new TermListNode(items);
    }

    private TableNode? BuildTable(HtmlElement table)
    {
        var rows = new List<(bool Header, List<TypstTableCell> Cells)>();
        CollectRows(table, rows, header: false);

        if (rows.Count == 0)
        {
            return null;
        }

        var columns = 0;

        foreach (var (_, cells) in rows)
        {
            var width = 0;

            foreach (var cell in cells)
            {
                width += cell.Colspan;
            }

            if (width > columns)
            {
                columns = width;
            }
        }

        if (columns == 0)
        {
            return null;
        }

        var model = new TypstTable();
        var widths = new TypstLength[columns];

        for (var index = 0; index < columns; index++)
        {
            widths[index] = TypstLength.Fr(1);
        }

        model.Columns(widths);

        var headerSet = false;

        foreach (var (header, cells) in rows)
        {
            if (cells.Count == 0)
            {
                continue;
            }

            if (header && !headerSet)
            {
                model.HeaderRow(cells.ToArray());
                headerSet = true;
            }
            else
            {
                model.Row(cells.ToArray());
            }
        }

        return new TableNode(model);
    }

    private void CollectRows(HtmlElement container, List<(bool Header, List<TypstTableCell> Cells)> rows, bool header)
    {
        foreach (var node in container.Children)
        {
            if (node is HtmlText)
            {
                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            switch (element.Name)
            {
                case "tr":
                    rows.Add((header, ParseRow(element)));
                    break;

                case "thead":
                    CollectRows(element, rows, header: true);
                    break;

                case "tbody":
                case "tfoot":
                    CollectRows(element, rows, header: false);
                    break;

                case "caption":
                case "colgroup":
                case "col":
                    Drop(element.Name);
                    break;

                default:
                    if (Ignored.Contains(element.Name))
                    {
                        break;
                    }

                    HandleUnknown(element);
                    break;
            }
        }
    }

    private List<TypstTableCell> ParseRow(HtmlElement row)
    {
        var cells = new List<TypstTableCell>();

        foreach (var node in row.Children)
        {
            if (node is HtmlText)
            {
                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            if (element.Name is "td" or "th")
            {
                cells.Add(new TypstTableCell(
                    ConvertInlineChildren(element),
                    Span(element, "colspan"),
                    Span(element, "rowspan")));
                continue;
            }

            if (Ignored.Contains(element.Name))
            {
                continue;
            }

            HandleUnknown(element);
        }

        return cells;
    }

    private void HandleUnknown(HtmlElement element)
    {
        if (_options.UnknownElements == TypstHtmlUnknownElementPolicy.Throw)
        {
            throw new TypstHtmlImportException(
                $"the element <{element.Name}> is not part of the supported HTML subset.")
            {
                ElementName = element.Name,
            };
        }

        Drop(element.Name);
    }

    private void Drop(string name)
    {
        _dropped[name] = _dropped.TryGetValue(name, out var count) ? count + 1 : 1;
    }

    private List<TypstHtmlDroppedElement> SnapshotDropped()
    {
        var elements = new List<TypstHtmlDroppedElement>(_dropped.Count);

        foreach (var pair in _dropped)
        {
            elements.Add(new TypstHtmlDroppedElement(pair.Key, pair.Value));
        }

        elements.Sort(static (left, right) => string.CompareOrdinal(left.ElementName, right.ElementName));
        return elements;
    }

    /// <summary>Reads the raw text of a subtree, following <c>br</c> as a newline.</summary>
    private static string CollectText(HtmlElement element)
    {
        var builder = new StringBuilder();
        AppendText(element, builder);
        return builder.ToString();
    }

    private static string CollectCode(HtmlElement pre)
    {
        var builder = new StringBuilder();
        AppendText(pre, builder);

        var code = builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        if (code.StartsWith('\n'))
        {
            code = code[1..];
        }

        return code.TrimEnd('\n');
    }

    private static void AppendText(HtmlElement element, StringBuilder builder)
    {
        foreach (var node in element.Children)
        {
            if (node is HtmlText text)
            {
                builder.Append(text.Text);
                continue;
            }

            if (node is not HtmlElement child)
            {
                continue;
            }

            switch (child.Name)
            {
                case "br":
                    builder.Append('\n');
                    break;

                case "script":
                case "style":
                case "template":
                    break;

                default:
                    AppendText(child, builder);
                    break;
            }
        }
    }

    /// <summary>Finds the language in <c>code class="language-rust"</c>, if there is one.</summary>
    private static string? FindLanguage(HtmlElement element)
    {
        if (element.Name == "code")
        {
            var language = LanguageFromClass(element.Attribute("class"));

            if (language is not null)
            {
                return language;
            }
        }

        foreach (var node in element.Children)
        {
            if (node is HtmlElement child)
            {
                var language = FindLanguage(child);

                if (language is not null)
                {
                    return language;
                }
            }
        }

        return null;
    }

    private static string? LanguageFromClass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
            {
                return token[9..];
            }

            if (token.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
            {
                return token[5..];
            }
        }

        return null;
    }

    private static int Span(HtmlElement cell, string name)
    {
        var value = cell.Attribute(name);

        if (value is null
            || !int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var span)
            || span < 1
            || span > 1000)
        {
            return 1;
        }

        return span;
    }

    private TypstContent ApplyStyle(HtmlElement element, TypstContent content)
    {
        if (!_options.HonourInlineStyles)
        {
            return content;
        }

        var style = element.Attribute("style");

        if (string.IsNullOrWhiteSpace(style))
        {
            return content;
        }

        var bold = false;
        var italic = false;
        var underline = false;
        var strike = false;

        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                continue;
            }

            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim().ToLowerInvariant();

            switch (name)
            {
                case "font-weight":
                    bold = value is "bold" or "bolder"
                        || (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var weight)
                            && weight >= 600);
                    break;

                case "font-style":
                    italic = value is "italic" or "oblique";
                    break;

                case "text-decoration":
                    underline |= value.Contains("underline", StringComparison.Ordinal);
                    strike |= value.Contains("line-through", StringComparison.Ordinal);
                    break;

                default:
                    break;
            }
        }

        var result = content;

        if (underline)
        {
            result = TypstContent.Empty.Underline(result);
        }

        if (strike)
        {
            result = TypstContent.Empty.Strikethrough(result);
        }

        if (bold)
        {
            result = TypstContent.Empty.Bold(result);
        }

        if (italic)
        {
            result = TypstContent.Empty.Emphasis(result);
        }

        return result;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (IsCollapsible(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsCollapsible(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>Accumulates inline material with HTML's whitespace collapsing.</summary>
    private sealed class InlineBuilder
    {
        private TypstContent _content = new();
        private bool _hasContent;
        private bool _pendingSpace;
        private bool _lastWasBreak;

        public TypstContent Content => _content;

        public bool IsEmpty => !_hasContent;

        public bool EndsWithSpace => _pendingSpace;

        public static TypstContent FromText(string text)
        {
            var builder = new InlineBuilder();
            builder.AppendText(text);
            return builder.Content;
        }

        public void AppendText(string text)
        {
            var index = 0;

            while (index < text.Length)
            {
                if (IsCollapsible(text[index]))
                {
                    _pendingSpace = _hasContent;
                    index++;
                    continue;
                }

                var start = index;

                while (index < text.Length && !IsCollapsible(text[index]))
                {
                    index++;
                }

                if (_pendingSpace && _hasContent)
                {
                    _content.Text(" ");
                }

                _pendingSpace = false;
                _content.Text(text[start..index]);
                _hasContent = true;
                _lastWasBreak = false;
            }
        }

        public void AppendNode(TypstNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (_pendingSpace && _hasContent)
            {
                _content.Text(" ");
            }

            _pendingSpace = false;
            _content.Node(node);
            _hasContent = true;
            _lastWasBreak = false;
        }

        public void AppendContent(TypstContent content, bool trailingSpace = false)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (content.IsEmpty)
            {
                return;
            }

            if (_pendingSpace && _hasContent)
            {
                _content.Text(" ");
            }

            _pendingSpace = trailingSpace;
            content.AppendTo(_content);
            _hasContent = true;
            _lastWasBreak = false;
        }

        public void AppendLineBreak()
        {
            if (!_hasContent || _lastWasBreak)
            {
                return;
            }

            _content.Node(new LineBreakNode());
            _pendingSpace = false;
            _lastWasBreak = true;
        }

        public void Flush(List<TypstNode> into)
        {
            ArgumentNullException.ThrowIfNull(into);

            if (_hasContent && !_content.IsEmpty)
            {
                into.Add(new ParagraphNode(_content));
            }

            _content = new TypstContent();
            _hasContent = false;
            _pendingSpace = false;
            _lastWasBreak = false;
        }
    }
}
