using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Walks the document model and emits an HTML5 body fragment. The mapping is the inverse of
/// <see cref="HtmlDocumentConverter"/>, with the same semantic tags; anything the model can hold
/// but HTML cannot express is counted instead of guessed.
/// </summary>
internal sealed class TypstHtmlWriter
{
    private readonly StringBuilder _out = new();
    private readonly SortedDictionary<string, int> _unrepresentable = new(StringComparer.Ordinal);

    private TypstHtmlWriter()
    {
    }

    public static TypstHtmlExportResult Write(TypstDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var writer = new TypstHtmlWriter();

        foreach (var node in document.HtmlBody)
        {
            writer.WriteBlock(node);
        }

        return new TypstHtmlExportResult(writer._out.ToString(), writer.Snapshot());
    }

    private List<TypstHtmlDroppedElement> Snapshot()
    {
        var elements = new List<TypstHtmlDroppedElement>(_unrepresentable.Count);

        foreach (var pair in _unrepresentable)
        {
            elements.Add(new TypstHtmlDroppedElement(pair.Key, pair.Value));
        }

        return elements;
    }

    private void WriteBlock(TypstNode node)
    {
        switch (node)
        {
            case HeadingNode heading:
                WriteHeading(heading);
                break;

            case ParagraphNode paragraph:
                WriteParagraph(paragraph);
                break;

            case ContentNode content:
                WriteContentBlocks(Captured.Content(content, "content"));
                break;

            case ListNode list:
                WriteList(list);
                break;

            case TableNode table:
                WriteTable(table);
                break;

            case FigureNode figure:
                WriteFigure(figure);
                break;

            case CodeBlockNode code:
                WriteCodeBlock(code);
                break;

            case QuoteNode quote:
                WriteQuote(quote);
                break;

            case TermListNode terms:
                WriteTermList(terms);
                break;

            case SimpleCallNode call:
                WriteSimpleCall(call);
                break;

            default:
                Drop(node);
                break;
        }
    }

    private void WriteHeading(HeadingNode heading)
    {
        var level = Captured.Int(heading, "level");
        var content = Captured.Content(heading, "content");

        if (content.IsEmpty)
        {
            return;
        }

        _out.Append("<h").Append(level.ToString(CultureInfo.InvariantCulture)).Append('>');
        WriteInline(content);
        _out.Append("</h").Append(level.ToString(CultureInfo.InvariantCulture)).Append(">\n");
    }

    private void WriteParagraph(ParagraphNode paragraph)
    {
        var content = Captured.Content(paragraph, "content");

        if (content.IsEmpty)
        {
            return;
        }

        if (IsOnlyBlockMath(content, out var expression))
        {
            _out.Append("<div class=\"math\">").Append(EscapeText(expression)).Append("</div>\n");
            return;
        }

        _out.Append("<p>");
        WriteInline(content);
        _out.Append("</p>\n");
    }

    private static bool IsOnlyBlockMath(TypstContent content, out string expression)
    {
        if (content.HtmlNodes.Count == 1
            && content.HtmlNodes[0] is MathNode math
            && Captured.Bool(math, "block"))
        {
            expression = Captured.Text(math, "expression");
            return true;
        }

        expression = string.Empty;
        return false;
    }

    /// <summary>Writes a run of mixed content, wrapping the inline runs in paragraphs.</summary>
    private void WriteContentBlocks(TypstContent content)
    {
        if (content.IsEmpty)
        {
            return;
        }

        var pending = new List<TypstNode>();

        foreach (var node in content.HtmlNodes)
        {
            if (IsBlock(node))
            {
                FlushParagraph(pending);
                WriteBlock(node);
                continue;
            }

            pending.Add(node);
        }

        FlushParagraph(pending);
    }

    private void FlushParagraph(List<TypstNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        _out.Append("<p>");

        foreach (var node in nodes)
        {
            WriteInlineNode(node);
        }

        _out.Append("</p>\n");
        nodes.Clear();
    }

    private static bool IsBlock(TypstNode node)
        => node is HeadingNode
            or ParagraphNode
            or ListNode
            or TableNode
            or FigureNode
            or CodeBlockNode
            or QuoteNode
            or TermListNode
            or OutlineNode
            or BibliographyNode
            or RawNode
            or CallNode
            or SimpleCallNode
            or BareCallNode;

    private void WriteList(ListNode list)
    {
        var items = (IReadOnlyList<ListItem>)Captured.Value(list, "items")!;
        var ordered = Captured.Bool(list, "ordered");

        if (items.Count == 0)
        {
            return;
        }

        var (open, close) = ordered ? ("<ol>\n", "</ol>\n") : ("<ul>\n", "</ul>\n");
        _out.Append(open);

        foreach (var item in items)
        {
            _out.Append("<li>");
            WriteInline(item.Content);

            if (item.Children.Count > 0)
            {
                _out.Append('\n');

                foreach (var child in item.Children)
                {
                    WriteBlock(child);
                }
            }

            _out.Append("</li>\n");
        }

        _out.Append(close);
    }

    private void WriteTable(TableNode node)
    {
        var table = (TypstTable)Captured.Value(node, "table")!;

        if (table.Header.Count == 0 && table.Rows.Count == 0)
        {
            return;
        }

        _out.Append("<table>\n");

        if (table.Header.Count > 0)
        {
            _out.Append("<thead>\n");
            WriteRow(table.Header, header: true);
            _out.Append("</thead>\n");
        }

        if (table.Rows.Count > 0)
        {
            _out.Append("<tbody>\n");

            foreach (var row in table.Rows)
            {
                WriteRow(row, header: false);
            }

            _out.Append("</tbody>\n");
        }

        _out.Append("</table>\n");
    }

    private void WriteRow(IReadOnlyList<TypstTableCell> cells, bool header)
    {
        var tag = header ? "th" : "td";

        _out.Append("<tr>");

        foreach (var cell in cells)
        {
            _out.Append('<').Append(tag);

            if (cell.Colspan > 1)
            {
                _out.Append(" colspan=\"").Append(cell.Colspan.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            if (cell.Rowspan > 1)
            {
                _out.Append(" rowspan=\"").Append(cell.Rowspan.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            if (cell.Align is { } alignment)
            {
                _out.Append(" align=\"").Append(AlignmentName(alignment)).Append('"');
            }

            _out.Append('>');
            WriteInline(cell.Content);
            _out.Append("</").Append(tag).Append('>');
        }

        _out.Append("</tr>\n");
    }

    private static string AlignmentName(TypstAlignment alignment) => alignment switch
    {
        TypstAlignment.Center => "center",
        TypstAlignment.Right => "right",
        _ => "left",
    };

    private void WriteFigure(FigureNode node)
    {
        var figure = (TypstFigure)Captured.Value(node, "figure")!;
        var image = Captured.Value(figure, "image") as string;

        _out.Append("<figure");

        if (figure.LabelName is { } label)
        {
            _out.Append(" id=\"").Append(EscapeAttribute(label)).Append('"');
        }

        _out.Append(">\n");

        if (image is not null)
        {
            _out.Append("<img src=\"").Append(EscapeAttribute(image)).Append("\" alt=\"\" />\n");
        }

        if (figure.Caption is { } caption && !caption.IsEmpty)
        {
            _out.Append("<figcaption>");
            WriteInline(caption);
            _out.Append("</figcaption>\n");
        }

        _out.Append("</figure>\n");
    }

    private void WriteCodeBlock(CodeBlockNode node)
    {
        var language = Captured.Text(node, "language");
        var code = Captured.Text(node, "code");

        _out.Append("<pre><code");

        if (language.Length > 0)
        {
            _out.Append(" class=\"language-").Append(EscapeAttribute(language)).Append('"');
        }

        _out.Append('>').Append(EscapeText(code)).Append("</code></pre>\n");
    }

    private void WriteQuote(QuoteNode node)
    {
        var content = Captured.Content(node, "content");
        var block = Captured.Bool(node, "block");

        if (!block)
        {
            _out.Append("<q>");
            WriteInline(content);
            _out.Append("</q>\n");
            return;
        }

        _out.Append("<blockquote>\n");
        WriteContentBlocks(content);
        _out.Append("</blockquote>\n");
    }

    private void WriteTermList(TermListNode node)
    {
        var items = (IReadOnlyList<(string Term, TypstContent Description)>)Captured.Value(node, "items")!;

        if (items.Count == 0)
        {
            return;
        }

        _out.Append("<dl>\n");

        foreach (var (term, description) in items)
        {
            _out.Append("<dt>").Append(EscapeText(term)).Append("</dt>\n");
            _out.Append("<dd>");
            WriteInline(description);
            _out.Append("</dd>\n");
        }

        _out.Append("</dl>\n");
    }

    private void WriteSimpleCall(SimpleCallNode node)
    {
        var call = Captured.Text(node, "call");

        if (call.Contains("#divider(", StringComparison.Ordinal))
        {
            _out.Append("<hr />\n");
            return;
        }

        if (call.Contains("#parbreak(", StringComparison.Ordinal))
        {
            // A paragraph break is what the block boundaries already are; it leaves no element.
            return;
        }

        Drop(node, CalleeName(call));
    }

    private void WriteInline(TypstContent content)
    {
        foreach (var node in content.HtmlNodes)
        {
            WriteInlineNode(node);
        }
    }

    private void WriteInlineNode(TypstNode node)
    {
        switch (node)
        {
            case TextNode text:
                _out.Append(EscapeText(Captured.Text(text, "text")));
                break;

            case BoldNode bold:
                _out.Append("<strong>");
                WriteInline(Captured.Content(bold, "content"));
                _out.Append("</strong>");
                break;

            case EmphasisNode emphasis:
                _out.Append("<em>");
                WriteInline(Captured.Content(emphasis, "content"));
                _out.Append("</em>");
                break;

            case InlineCodeNode code:
                _out.Append("<code>").Append(EscapeText(Captured.Text(code, "code"))).Append("</code>");
                break;

            case MathNode math:
                _out.Append("<span class=\"math\">")
                    .Append(EscapeText(Captured.Text(math, "expression")))
                    .Append("</span>");
                break;

            case LinkNode link:
                WriteLink(link);
                break;

            case LineBreakNode:
                _out.Append("<br />");
                break;

            case LabelNode label:
                _out.Append("<span id=\"").Append(EscapeAttribute(Captured.Text(label, "name"))).Append("\"></span>");
                break;

            case ReferenceNode reference:
                var target = Captured.Text(reference, "label");
                _out.Append("<a href=\"#").Append(EscapeAttribute(target)).Append("\">@")
                    .Append(EscapeText(target)).Append("</a>");
                break;

            case FootnoteNode footnote:
                _out.Append("<span class=\"footnote\">");
                WriteInline(Captured.Content(footnote, "content"));
                _out.Append("</span>");
                break;

            case CiteNode cite:
                var key = Captured.Text(cite, "key");
                _out.Append("<cite data-key=\"").Append(EscapeAttribute(key)).Append("\">@")
                    .Append(EscapeText(key)).Append("</cite>");
                break;

            case StyledTextNode styled:
                _out.Append("<span>").Append(EscapeText(Captured.Text(styled, "text"))).Append("</span>");
                break;

            case WrapperNode wrapper:
                WriteWrapper(wrapper);
                break;

            default:
                Drop(node);
                break;
        }
    }

    private void WriteLink(LinkNode link)
    {
        var url = Captured.Text(link, "url");
        var content = Captured.Value(link, "content") as TypstContent;

        _out.Append("<a href=\"").Append(EscapeAttribute(url)).Append("\">");

        if (content is null || content.IsEmpty)
        {
            _out.Append(EscapeText(url));
        }
        else
        {
            WriteInline(content);
        }

        _out.Append("</a>");
    }

    private void WriteWrapper(WrapperNode wrapper)
    {
        var function = Captured.Text(wrapper, "function");
        var content = Captured.Content(wrapper, "content");

        var tag = function switch
        {
            "super" => "sup",
            "sub" => "sub",
            "underline" => "u",
            "strike" => "s",
            _ => null,
        };

        if (tag is not null)
        {
            _out.Append('<').Append(tag).Append('>');
            WriteInline(content);
            _out.Append("</").Append(tag).Append('>');
            return;
        }

        if (function is "upper" or "lower" or "smallcaps" or "highlight")
        {
            _out.Append("<span class=\"").Append(function).Append("\">");
            WriteInline(content);
            _out.Append("</span>");
            return;
        }

        Drop(wrapper, function);
    }

    /// <summary>Counts a node HTML cannot express, once, without visiting its subtree.</summary>
    private void Drop(TypstNode node, string? name = null)
    {
        var element = name ?? NameOf(node);
        _unrepresentable[element] = _unrepresentable.TryGetValue(element, out var count) ? count + 1 : 1;
    }

    private static string NameOf(TypstNode node) => node switch
    {
        RawNode => "raw",
        OutlineNode => "outline",
        BibliographyNode => "bibliography",
        MetadataNode => "metadata",
        LoremNode => "lorem",
        CallNode call => CalleeName(Captured.Text(call, "call")),
        SimpleCallNode call => CalleeName(Captured.Text(call, "call")),
        BareCallNode call => CalleeName(Captured.Text(call, "call")),
        WrapperNode wrapper => Captured.Text(wrapper, "function"),
        _ => node.GetType().Name.Replace("Node", string.Empty, StringComparison.Ordinal).ToLowerInvariant(),
    };

    /// <summary>Reads the callee of a Typst call, for example <c>place</c> from <c>#place(…)</c>.</summary>
    private static string CalleeName(string call)
    {
        var start = call.IndexOf('#', StringComparison.Ordinal);

        if (start < 0)
        {
            return "call";
        }

        var index = start + 1;

        if (index < call.Length && call[index] == '(')
        {
            return "expression";
        }

        var name = new StringBuilder();

        while (index < call.Length && char.IsLetter(call[index]))
        {
            name.Append(call[index]);
            index++;
        }

        return name.Length == 0 ? "call" : name.ToString().ToLowerInvariant();
    }

    private static string EscapeText(string text) => Escape(text, attribute: false);

    private static string EscapeAttribute(string text) => Escape(text, attribute: true);

    private static string Escape(string text, bool attribute)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new StringBuilder(normalized.Length + 16);

        foreach (var c in normalized)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;

                case '<':
                    builder.Append("&lt;");
                    break;

                case '>':
                    builder.Append("&gt;");
                    break;

                case '"' when attribute:
                    builder.Append("&quot;");
                    break;

                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads the arguments the nodes capture. Nodes are immutable and only emit Typst source, so
    /// their constructor arguments live in compiler-generated fields; this is the one place that
    /// knows those field names.
    /// </summary>
    private static class Captured
    {
        private static readonly ConcurrentDictionary<(Type Type, string Name), FieldInfo?> Fields = new();

        public static object? Value<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T node, string name)
            where T : class
        {
            var key = (typeof(T), name);

            var field = Fields.GetOrAdd(key, _ => Find(typeof(T), name))
                ?? throw new InvalidOperationException(
                    $"the {typeof(T).Name} node has no captured argument '{name}'; " +
                    "the HTML exporter and the document model have drifted apart.");

            return field.GetValue(node);
        }

        public static string Text<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T node, string name)
            where T : class
            => Value(node, name) as string ?? string.Empty;

        public static TypstContent Content<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T node, string name)
            where T : class
            => Value(node, name) as TypstContent ?? TypstContent.Empty;

        public static int Int<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T node, string name)
            where T : class
            => Value(node, name) is int value ? value : 0;

        public static bool Bool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] T>(T node, string name)
            where T : class
            => Value(node, name) is true;

        private static FieldInfo? Find(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] Type type,
            string name)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (string.Equals(Normalize(field.Name), name, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }

        /// <summary>
        /// Reduces a captured field name to its parameter name: <c>&lt;level&gt;P</c> and
        /// <c>_items</c> become <c>level</c> and <c>items</c>.
        /// </summary>
        private static string Normalize(string fieldName)
        {
            var name = fieldName;
            var start = name.IndexOf('<', StringComparison.Ordinal);

            if (start >= 0)
            {
                var end = name.IndexOf('>', start + 1);
                return end > start ? name[(start + 1)..end] : name;
            }

            return name.StartsWith('_') ? name[1..] : name;
        }
    }
}
