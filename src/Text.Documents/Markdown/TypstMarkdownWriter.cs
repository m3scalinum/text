using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>The constructs the Markdown exporter cannot write, as named in the export report.</summary>
internal static class MarkdownExportConstructs
{
    /// <summary>Typst source emitted verbatim, including the drawing shapes.</summary>
    public const string Raw = "raw";

    /// <summary>A Typst function call that has no Markdown equivalent, such as <c>#align</c>.</summary>
    public const string Call = "call";

    /// <summary>A table of contents.</summary>
    public const string Outline = "outline";

    /// <summary>A footnote.</summary>
    public const string Footnote = "footnote";

    /// <summary>A citation.</summary>
    public const string Cite = "cite";

    /// <summary>A metadata entry.</summary>
    public const string Metadata = "metadata";

    /// <summary>A label.</summary>
    public const string Label = "label";

    /// <summary>A reference to a label.</summary>
    public const string Reference = "reference";

    /// <summary>Filler text.</summary>
    public const string Lorem = "lorem";

    /// <summary>A bibliography.</summary>
    public const string Bibliography = "bibliography";

    /// <summary>A term list, which GFM has no syntax for.</summary>
    public const string TermList = "term-list";

    /// <summary>A cell that spans columns or rows, which a pipe table cannot express.</summary>
    public const string TableSpan = "table-span";

    /// <summary>A preamble line: <c>#let</c>, <c>#import</c>, <c>#set</c>, <c>#show</c>.</summary>
    public const string Preamble = "preamble";

    /// <summary>The page setup, which Markdown has no notion of.</summary>
    public const string PageSetup = "page-setup";

    /// <summary>The document metadata: title, author, keywords, date.</summary>
    public const string DocumentMetadata = "document-metadata";
}

/// <summary>
/// Writes the document model as Markdown, following the mapping of
/// <c>docs/specifications/06-markdown-and-text-export.md</c> §3 and the fidelity contract of §5.
/// </summary>
/// <remarks>
/// The walker runs over the nodes, not over a compilation: a document built in C# is exported
/// without paying for layout. Everything the model can express but Markdown cannot — raw Typst,
/// calls, footnotes, citations, tables of contents, shapes — is counted by construct instead of
/// being written, so the caller knows exactly what the file lost.
/// </remarks>
internal sealed class TypstMarkdownWriter
{
    /// <summary>
    /// The field table of each node type, built once. The model is write-only: its nodes keep their
    /// state in private fields so nothing outside the builders can mutate them, and the exporter
    /// reads those fields through this table rather than adding a reader to every node type.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, FieldInfo>> Fields = new();

    private readonly Dictionary<string, int> _dropped = new(StringComparer.Ordinal);

    /// <summary>Writes a whole document and returns its Markdown.</summary>
    /// <param name="body">The body nodes, in document order.</param>
    /// <param name="preambleCount">How many preamble lines the document carries.</param>
    /// <param name="hasPageSetup">Whether the document configures the page.</param>
    /// <param name="hasMetadata">Whether the document configures its metadata.</param>
    public string Write(
        IReadOnlyList<TypstNode> body,
        int preambleCount,
        bool hasPageSetup,
        bool hasMetadata)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (preambleCount > 0)
        {
            Drop(MarkdownExportConstructs.Preamble, preambleCount);
        }

        if (hasPageSetup)
        {
            Drop(MarkdownExportConstructs.PageSetup);
        }

        if (hasMetadata)
        {
            Drop(MarkdownExportConstructs.DocumentMetadata);
        }

        var blocks = new List<string>();
        AppendBlocks(body, blocks);

        var builder = new StringBuilder();

        foreach (var block in blocks)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(block);
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>The constructs that were not written, sorted by name.</summary>
    public IReadOnlyList<TypstMarkdownDroppedConstruct> Snapshot()
    {
        var constructs = new List<TypstMarkdownDroppedConstruct>(_dropped.Count);

        foreach (var pair in _dropped)
        {
            constructs.Add(new TypstMarkdownDroppedConstruct(pair.Key, pair.Value));
        }

        constructs.Sort(static (left, right) => string.CompareOrdinal(left.ConstructName, right.ConstructName));
        return constructs;
    }

    // ---- blocks --------------------------------------------------------------------------------

    private void AppendBlocks(IEnumerable<TypstNode> nodes, List<string> blocks)
    {
        var run = new StringBuilder();

        foreach (var node in nodes)
        {
            if (IsBlock(node))
            {
                Flush(run, blocks);

                var text = RenderBlock(node);

                if (text.Length > 0)
                {
                    blocks.Add(text);
                }

                continue;
            }

            AppendInline(node, run, table: false);
        }

        Flush(run, blocks);
    }

    private static void Flush(StringBuilder run, List<string> blocks)
    {
        var text = run.ToString().Trim();

        if (text.Length > 0)
        {
            blocks.Add(text);
        }

        run.Clear();
    }

    private string RenderBlock(TypstNode node)
    {
        switch (node)
        {
            case HeadingNode:
                return new string('#', ReadInt(node, "level")) + " " + RenderInline(ReadContent(node), table: false);

            case ParagraphNode or ContentNode:
                return RenderInline(ReadContent(node), table: false);

            case ListNode list:
                return RenderList(list, depth: 0);

            case QuoteNode:
                return RenderQuote(node);

            case TableNode:
                return RenderTable(ReadTable(node));

            case CodeBlockNode:
                return RenderCode(ReadString(node, "language"), ReadString(node, "code"));

            case FigureNode:
                return RenderFigure(ReadFigure(node));

            case MathNode:
                var expression = ReadString(node, "expression");
                return ReadBool(node, "block") ? "$$" + expression + "$$" : "$" + expression + "$";

            case SimpleCallNode:
                return RenderCall(ReadString(node, "call"));

            case CallNode or BareCallNode or WrapperNode:
                Drop(node switch
                {
                    WrapperNode => ReadString(node, "function"),
                    _ => MarkdownExportConstructs.Call,
                });

                return node is CallNode or WrapperNode ? RenderInline(ReadContent(node), table: false) : string.Empty;

            case TermListNode:
                Drop(MarkdownExportConstructs.TermList);
                return string.Empty;

            case OutlineNode:
                Drop(MarkdownExportConstructs.Outline);
                return string.Empty;

            case BibliographyNode:
                Drop(MarkdownExportConstructs.Bibliography);
                return string.Empty;

            case RawNode:
                Drop(MarkdownExportConstructs.Raw);
                return string.Empty;

            case LabelNode:
                Drop(MarkdownExportConstructs.Label);
                return string.Empty;

            case ReferenceNode:
                Drop(MarkdownExportConstructs.Reference);
                return string.Empty;

            default:
                // An inline node appended as its own block, which Add(TypstContent) allows.
                return RenderInline(new TypstContent().Node(node), table: false);
        }
    }

    private static bool IsBlock(TypstNode node)
        => node is HeadingNode
            or ParagraphNode
            or ListNode
            or QuoteNode
            or TableNode
            or CodeBlockNode
            or FigureNode
            or TermListNode
            or OutlineNode
            or BibliographyNode
            or RawNode
            or SimpleCallNode
            or BareCallNode
            or CallNode
            or ContentNode;

    private string RenderCall(string call)
    {
        // The model expresses a thematic break as #divider(), the call the importer emits.
        if (string.Equals(call, "#divider()", StringComparison.Ordinal))
        {
            return "---";
        }

        Drop(MarkdownExportConstructs.Call);
        return string.Empty;
    }

    private string RenderList(ListNode list, int depth)
    {
        var items = Read(list, "items") as IReadOnlyList<ListItem>;
        var ordered = ReadBool(list, "ordered");
        var loose = ReadBool(list, "loose");
        var builder = new StringBuilder();
        var first = true;

        foreach (var item in items ?? [])
        {
            if (!first && loose)
            {
                builder.Append('\n');
            }

            first = false;

            builder.Append(' ', depth * 2).Append(ordered ? "1. " : "- ");
            AppendInlineContent(item.Content, builder, table: false);
            builder.Append('\n');

            foreach (var child in item.Children)
            {
                if (child is ListNode nested)
                {
                    builder.Append(RenderList(nested, depth + 1));
                    continue;
                }

                var text = RenderBlock(child);

                if (text.Length > 0)
                {
                    AppendIndented(builder, text, (depth + 1) * 2);
                }
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendIndented(StringBuilder builder, string text, int indent)
    {
        foreach (var line in text.Split('\n'))
        {
            builder.Append(' ', indent);
            builder.Append(line);
            builder.Append('\n');
        }
    }

    private string RenderQuote(TypstNode node)
    {
        var blocks = new List<string>();
        AppendBlocks(NodesOf(ReadContent(node)), blocks);

        var inner = new StringBuilder();

        for (var index = 0; index < blocks.Count; index++)
        {
            if (index > 0)
            {
                inner.Append("\n\n");
            }

            inner.Append(blocks[index]);
        }

        if (inner.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var line in inner.ToString().Split('\n'))
        {
            builder.Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private string RenderTable(TypstTable table)
    {
        var header = table.Header;
        var columns = header.Count;

        if (columns == 0)
        {
            columns = table.ColumnWidths.Count;
        }

        foreach (var row in table.Rows)
        {
            columns = Math.Max(columns, row.Count);
        }

        if (columns == 0)
        {
            return string.Empty;
        }

        var alignments = new TypstAlignment?[columns];

        for (var index = 0; index < columns && index < header.Count; index++)
        {
            alignments[index] = header[index].Align;
        }

        var builder = new StringBuilder();

        builder.Append("| ");

        for (var index = 0; index < columns; index++)
        {
            if (index > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(index < header.Count ? RenderCell(header[index]) : string.Empty);
        }

        builder.Append(" |\n| ");

        for (var index = 0; index < columns; index++)
        {
            if (index > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(Delimiter(alignments[index]));
        }

        builder.Append(" |");

        foreach (var row in table.Rows)
        {
            builder.Append("\n| ");

            for (var index = 0; index < columns; index++)
            {
                if (index > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(index < row.Count ? RenderCell(row[index]) : string.Empty);
            }

            builder.Append(" |");
        }

        foreach (var cell in AllCells(table))
        {
            if (cell.Colspan > 1 || cell.Rowspan > 1)
            {
                Drop(MarkdownExportConstructs.TableSpan);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<TypstTableCell> AllCells(TypstTable table)
    {
        foreach (var cell in table.Header)
        {
            yield return cell;
        }

        foreach (var row in table.Rows)
        {
            foreach (var cell in row)
            {
                yield return cell;
            }
        }
    }

    private static string Delimiter(TypstAlignment? alignment) => alignment switch
    {
        TypstAlignment.Center => ":---:",
        TypstAlignment.Right => "---:",
        TypstAlignment.Left => ":---",
        _ => "---",
    };

    private string RenderCell(TypstTableCell cell)
    {
        var builder = new StringBuilder();
        AppendInlineContent(cell.Content, builder, table: true);
        return builder.ToString().Replace('\n', ' ').Trim();
    }

    private static string RenderCode(string language, string code)
    {
        // A fence longer than any backtick run in the code, so a block that contains ``` stays a
        // block instead of ending at the first one. Three is the shortest fence Markdown accepts.
        var fence = new string('`', Math.Max(3, LongestBacktickRun(code) + 1));
        return fence + language + "\n" + code.TrimEnd('\n') + "\n" + fence;
    }

    private string RenderFigure(TypstFigure figure)
    {
        var image = ReadString(figure, "image");
        var caption = new StringBuilder();

        if (figure.Caption is not null)
        {
            AppendPlainText(figure.Caption, caption);
        }

        if (figure.LabelName is not null)
        {
            Drop(MarkdownExportConstructs.Label);
        }

        if (image.Length == 0)
        {
            return caption.ToString().Trim();
        }

        return "![" + caption.ToString().Trim() + "](" + image + ")";
    }

    /// <summary>
    /// Collects the text of a run, dropping its formatting: the alt text of an image is the only
    /// place Markdown keeps a caption inline, and it carries no markup.
    /// </summary>
    private static void AppendPlainText(TypstContent content, StringBuilder builder)
    {
        foreach (var node in NodesOf(content))
        {
            switch (node)
            {
                case TextNode:
                    builder.Append(ReadString(node, "text"));
                    break;

                case InlineCodeNode:
                    builder.Append(ReadString(node, "code"));
                    break;

                case MathNode:
                    builder.Append(ReadString(node, "expression"));
                    break;

                case LineBreakNode:
                    builder.Append(' ');
                    break;

                case LinkNode:
                    var text = ReadContent(node);
                    AppendPlainText(text is null ? new TypstContent().Text(ReadString(node, "url")) : text, builder);
                    break;

                case RawNode or ReferenceNode or LabelNode or FootnoteNode or CiteNode or MetadataNode or LoremNode:
                    break;

                default:
                    var nested = ReadContent(node);

                    if (nested is not null)
                    {
                        AppendPlainText(nested, builder);
                    }

                    break;
            }
        }
    }

    // ---- inlines -------------------------------------------------------------------------------

    private string RenderInline(TypstContent? content, bool table)
    {
        var builder = new StringBuilder();
        AppendInlineContent(content, builder, table);
        return builder.ToString().Trim();
    }

    private void AppendInlineContent(TypstContent? content, StringBuilder builder, bool table)
    {
        if (content is null)
        {
            return;
        }

        foreach (var node in NodesOf(content))
        {
            AppendInline(node, builder, table);
        }
    }

    private void AppendInline(TypstNode node, StringBuilder builder, bool table)
    {
        switch (node)
        {
            case TextNode:
                AppendText(builder, ReadString(node, "text"), table);
                break;

            case BoldNode:
                builder.Append("**");
                AppendInlineContent(ReadContent(node), builder, table);
                builder.Append("**");
                break;

            case EmphasisNode:
                builder.Append('*');
                AppendInlineContent(ReadContent(node), builder, table);
                builder.Append('*');
                break;

            case InlineCodeNode:
                builder.Append(CodeSpan(ReadString(node, "code")));
                break;

            case MathNode:
                builder.Append(ReadBool(node, "block") ? "$$" : "$")
                    .Append(ReadString(node, "expression"))
                    .Append(ReadBool(node, "block") ? "$$" : "$");
                break;

            case LinkNode:
                AppendLink(node, builder, table);
                break;

            case LineBreakNode:
                builder.Append(table ? " " : "  \n");
                break;

            case WrapperNode:
                // A decoration Markdown has no syntax for: the construct is lost, its text is not.
                Drop(ReadString(node, "function"));
                AppendInlineContent(ReadContent(node), builder, table);
                break;

            case ReferenceNode:
                Drop(MarkdownExportConstructs.Reference);
                break;

            case LabelNode:
                Drop(MarkdownExportConstructs.Label);
                break;

            case FootnoteNode:
                Drop(MarkdownExportConstructs.Footnote);
                break;

            case CiteNode:
                Drop(MarkdownExportConstructs.Cite);
                break;

            case MetadataNode:
                Drop(MarkdownExportConstructs.Metadata);
                break;

            case LoremNode:
                Drop(MarkdownExportConstructs.Lorem);
                break;

            case RawNode:
                Drop(MarkdownExportConstructs.Raw);
                break;

            default:
                var text = RenderBlock(node);

                if (text.Length > 0)
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
                    {
                        builder.Append("\n\n");
                    }

                    builder.Append(text);
                }

                break;
        }
    }

    private void AppendLink(TypstNode node, StringBuilder builder, bool table)
    {
        var url = ReadString(node, "url");
        var content = ReadContent(node);
        var label = new StringBuilder();
        AppendInlineContent(content, label, table);

        if (content is null || label.Length == 0 || string.Equals(label.ToString(), url, StringComparison.Ordinal))
        {
            // A bare URL, which is how Markdown spells a link whose text is its destination.
            builder.Append(url);
            return;
        }

        builder.Append('[').Append(label).Append("](").Append(url).Append(')');
    }

    private static string CodeSpan(string code)
    {
        var fence = new string('`', LongestBacktickRun(code) + 1);
        var pad = code.Length > 0 && (code[0] is '`' or ' ' || code[^1] is '`' or ' ') ? " " : string.Empty;
        return fence + pad + code + pad + fence;
    }

    private static void AppendText(StringBuilder builder, string text, bool table)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            var lineStart = builder.Length == 0 || builder[builder.Length - 1] == '\n';

            switch (c)
            {
                case '\n':
                    builder.Append('\n');
                    break;

                case '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>':
                    builder.Append('\\').Append(c);
                    break;

                case '|' when table:
                    builder.Append("\\|");
                    break;

                case '#' when lineStart:
                    builder.Append("\\#");
                    break;

                case '-' or '+' when lineStart:
                    builder.Append('\\').Append(c);
                    break;

                default:
                    if (lineStart && char.IsAsciiDigit(c))
                    {
                        var end = index;

                        while (end < text.Length && char.IsAsciiDigit(text[end]))
                        {
                            end++;
                        }

                        // `1.` at the start of a line is an ordered list marker.
                        if (end < text.Length && text[end] == '.')
                        {
                            builder.Append(text, index, end - index).Append("\\.");
                            index = end;
                            break;
                        }
                    }

                    builder.Append(c);
                    break;
            }
        }
    }

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;

        foreach (var c in text)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    // ---- the model is write-only: read it through the field table ------------------------------

    private void Drop(string construct, int count = 1)
        => _dropped[construct] = _dropped.TryGetValue(construct, out var seen) ? seen + count : count;

    private static string ReadString(object instance, string name)
        => Read(instance, name) as string ?? string.Empty;

    private static int ReadInt(object instance, string name)
        => Read(instance, name) is int value ? value : 0;

    private static bool ReadBool(object instance, string name)
        => Read(instance, name) is true;

    private static TypstContent? ReadContent(object instance) => Read(instance, "content") as TypstContent;

    private static TypstTable ReadTable(object instance)
        => Read(instance, "table") as TypstTable ?? new TypstTable();

    private static TypstFigure ReadFigure(object instance)
        => Read(instance, "figure") as TypstFigure ?? new TypstFigure();

    private static IReadOnlyList<TypstNode> NodesOf(TypstContent? content)
        => content is null ? [] : Read(content, "nodes") as IReadOnlyList<TypstNode> ?? [];

    private static object? Read(object instance, string name)
    {
        var fields = Fields.GetOrAdd(instance.GetType(), Describe);

        return fields.TryGetValue(name, out var field) ? field.GetValue(instance) : null;
    }

    /// <summary>
    /// Maps each field of a node type to the name it was declared under, stripped of the decoration
    /// the compiler adds: <c>&lt;text&gt;P</c> for a primary-constructor parameter,
    /// <c>&lt;Caption&gt;k__BackingField</c> for an auto-property, <c>_items</c> for a field.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "The type is always a document node type of this assembly, and every one of them is constructed by the public builders, so none is trimmed.")]
    private static IReadOnlyDictionary<string, FieldInfo> Describe(Type type)
    {
        var fields = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var name = field.Name;

            if (name.StartsWith('<'))
            {
                var end = name.IndexOf('>');

                if (end > 1)
                {
                    name = name[1..end];
                }
            }
            else if (name.StartsWith('_'))
            {
                name = name[1..];
            }

            if (name.Length == 0)
            {
                continue;
            }

            name = char.ToLowerInvariant(name[0]) + name[1..];
            fields.TryAdd(name, field);
        }

        return fields;
    }
}
