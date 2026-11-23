using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>One node of the compared tree: its kind, the text it carries, its properties and its children.</summary>
/// <remarks>
/// A snapshot is taken per document and thrown away: it holds no reference to the document, so a
/// change list cannot keep a whole model alive.
/// </remarks>
internal sealed class DocumentDiffNode
{
    private readonly List<KeyValuePair<string, string?>> _properties = [];

    internal DocumentDiffNode(string kind, string segment)
    {
        Kind = kind;
        Segment = segment;
    }

    /// <summary>The node's kind, such as <c>paragraph</c>, <c>text</c> or <c>cell</c>.</summary>
    internal string Kind { get; }

    /// <summary>The path segment this node adds to its parent's, such as <c>body[3]</c> or <c>row[1].cell[0]</c>.</summary>
    internal string Segment { get; }

    /// <summary>Whether the node carries a text payload, which a difference is reported as <c>TextChanged</c>.</summary>
    internal bool HasText { get; set; }

    /// <summary>The text payload, empty when <see cref="HasText"/> is false.</summary>
    internal string Text { get; set; } = string.Empty;

    /// <summary>The label the node carries, null when it carries none.</summary>
    internal string? Label { get; set; }

    /// <summary>The scalar properties of the node, sorted by name.</summary>
    internal IReadOnlyList<KeyValuePair<string, string?>> Properties => _properties;

    /// <summary>The child nodes, in document order.</summary>
    internal List<DocumentDiffNode> Children { get; } = [];

    /// <summary>
    /// The whole subtree as one string: two nodes with the same signature hold the same content, which
    /// is what lets a removal and an insertion in the same list be recognised as a move.
    /// </summary>
    internal string Signature { get; set; } = string.Empty;

    /// <summary>Adds a scalar property; an existing name keeps both values, which cannot happen today.</summary>
    internal void Property(string name, string? value)
        => _properties.Add(new KeyValuePair<string, string?>(name, value));

    /// <summary>Sorts the properties by name, so the comparison order does not depend on reflection.</summary>
    internal void SortProperties() => _properties.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

    /// <summary>Computes <see cref="Signature"/> bottom up, once every child is in place.</summary>
    internal string Seal()
    {
        var builder = new StringBuilder();
        builder.Append(Kind).Append('|').Append(HasText ? Text : string.Empty).Append('|');

        foreach (var pair in _properties)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
        }

        builder.Append('(');

        foreach (var child in Children)
        {
            builder.Append(child.Signature).Append(',');
        }

        builder.Append(')');
        Signature = builder.ToString();
        return Signature;
    }
}

/// <summary>
/// Flattens a <see cref="TypstDocument"/> into the tree a diff compares.
/// </summary>
/// <remarks>
/// The model is write-only: its nodes keep their state in private fields so nothing outside the
/// builders can mutate them, and this reader goes through a cached field table rather than adding a
/// reader to every node type — the same route <see cref="TypstMarkdownWriter"/> takes. A node type
/// this reader does not know is still compared, under the name its type gives: that is what keeps a
/// diff working when the model gains a node.
/// </remarks>
internal static class DocumentDiffSnapshot
{
    /// <summary>The field table of each node type, built once.</summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, FieldInfo>> Fields = new();

    /// <summary>The field, per node kind, that holds the text the kind carries.</summary>
    private static readonly Dictionary<string, string> TextFieldByKind = new(StringComparer.Ordinal)
    {
        ["text"] = "text",
        ["code"] = "code",
        ["code-block"] = "code",
        ["math"] = "expression",
        ["raw"] = "markup",
        ["label"] = "name",
        ["simple-call"] = "call",
        ["bare-call"] = "call",
    };

    /// <summary>Takes the snapshot of a document: its body, and nothing of its preamble.</summary>
    /// <param name="document">The document to read.</param>
    /// <remarks>
    /// The body is what a reader sees. Page setup, document metadata and the preamble
    /// (<c>#let</c>, <c>#set</c>, <c>#show</c>) are not part of the tree: they configure the layout
    /// rather than carry the words, and <c>docs/specifications/45-document-diff.md</c> §5 says what
    /// that leaves out.
    /// </remarks>
    internal static DocumentDiffNode Capture(TypstDocument document)
    {
        var root = new DocumentDiffNode("document", string.Empty);

        if (Read(document, "body") is IReadOnlyList<TypstNode> body)
        {
            AppendNodes(root, body, "body");
        }

        root.Seal();
        return root;
    }

    private static void AppendNodes(DocumentDiffNode parent, IReadOnlyList<TypstNode> nodes, string role)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            parent.Children.Add(Build(nodes[index], Segment(role, index)));
        }
    }

    private static void AppendContent(DocumentDiffNode parent, TypstContent? content)
    {
        if (content is not null && Read(content, "nodes") is IReadOnlyList<TypstNode> nodes)
        {
            AppendNodes(parent, nodes, "inline");
        }
    }

    private static DocumentDiffNode Build(object node, string segment)
    {
        var type = node.GetType();
        var kind = KindName(type);
        var result = new DocumentDiffNode(kind, segment);

        TextFieldByKind.TryGetValue(kind, out var textField);

        foreach (var pair in Explain(type))
        {
            var name = pair.Key;
            var value = pair.Value.GetValue(node);

            if (value is null)
            {
                continue;
            }

            if (name == textField && value is string text)
            {
                result.HasText = true;
                result.Text = text;
                continue;
            }

            switch (value)
            {
                case TypstContent content:
                    AppendContent(result, content);
                    break;

                case TypstTable table:
                    AppendTable(result, table);
                    break;

                case TypstFigure figure:
                    AppendFigure(result, figure);
                    break;

                case TypstSection section:
                    AppendSection(result, section);
                    break;

                case TypstTextStyle style:
                    AppendStyle(result, style);
                    break;

                case TypstNode child:
                    result.Children.Add(Build(child, Segment(name, 0)));
                    break;

                case IReadOnlyList<TypstNode> children:
                    AppendNodes(result, children, name);
                    break;

                case IReadOnlyList<ListItem> items:
                    AppendItems(result, items);
                    break;

                case IReadOnlyList<(string Term, TypstContent Description)> terms:
                    AppendTerms(result, terms);
                    break;

                case string literal:
                    result.Property(name, literal);
                    break;

                case bool flag:
                    result.Property(name, flag ? "true" : "false");
                    break;

                case int number:
                    result.Property(name, number.ToString(CultureInfo.InvariantCulture));
                    break;

                case double real:
                    result.Property(name, real.ToString("0.###", CultureInfo.InvariantCulture));
                    break;

                case Enum symbol:
                    result.Property(name, symbol.ToString());
                    break;

                case TypstLength length:
                    result.Property(name, length.ToString());
                    break;

                default:
                    // An object this reader does not understand contributes its presence, which keeps a
                    // document that gains or loses one visible, and nothing it holds.
                    result.Property(name, KindName(value.GetType()));
                    break;
            }
        }

        result.SortProperties();
        result.Seal();
        return result;
    }

    private static void AppendTable(DocumentDiffNode parent, TypstTable table)
    {
        parent.Property("columns", table.ColumnWidths.Count > 0
            ? string.Join(", ", table.ColumnWidths)
            : table.ColumnCount.ToString(CultureInfo.InvariantCulture));
        parent.Property("repeatsHeader", table.RepeatsHeader ? "true" : "false");

        if (table.LabelName is { } label)
        {
            parent.Label = label;
        }

        var header = table.Header;

        for (var column = 0; column < header.Count; column++)
        {
            parent.Children.Add(BuildCell(header[column], "header[" + column.ToString(CultureInfo.InvariantCulture) + "]"));
        }

        var rows = table.Rows;

        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < rows[row].Count; column++)
            {
                var segment = "row[" + row.ToString(CultureInfo.InvariantCulture)
                    + "].cell[" + column.ToString(CultureInfo.InvariantCulture) + "]";
                parent.Children.Add(BuildCell(rows[row][column], segment));
            }
        }

        if (table.Caption is { } caption)
        {
            var holder = new DocumentDiffNode("caption", "caption[0]");
            AppendContent(holder, caption);
            holder.Seal();
            parent.Children.Add(holder);
        }
    }

    private static DocumentDiffNode BuildCell(TypstTableCell cell, string segment)
    {
        var node = new DocumentDiffNode("cell", segment);
        node.Property("colspan", cell.Colspan.ToString(CultureInfo.InvariantCulture));
        node.Property("rowspan", cell.Rowspan.ToString(CultureInfo.InvariantCulture));

        if (cell.Align is { } align)
        {
            node.Property("align", align.ToString());
        }

        if (cell.Breakable is { } breakable)
        {
            node.Property("breakable", breakable ? "true" : "false");
        }

        if (cell.HeaderScope is { } scope)
        {
            node.Property("headerScope", scope.ToString());
        }

        AppendContent(node, cell.Content);
        node.SortProperties();
        node.Seal();
        return node;
    }

    private static void AppendFigure(DocumentDiffNode parent, TypstFigure figure)
    {
        if (Read(figure, "image") is string image)
        {
            parent.Property("image", image);
        }

        if (figure.AltText is { } alt)
        {
            parent.Property("alt", alt);
        }

        if (Read(figure, "width") is TypstLength width)
        {
            parent.Property("width", width.ToString());
        }

        if (Read(figure, "height") is TypstLength height)
        {
            parent.Property("height", height.ToString());
        }

        if (Read(figure, "fit") is string fit)
        {
            parent.Property("fit", fit);
        }

        if (figure.LabelName is { } label)
        {
            parent.Label = label;
        }

        if (figure.Caption is { } caption)
        {
            var holder = new DocumentDiffNode("caption", "caption[0]");
            AppendContent(holder, caption);
            holder.Seal();
            parent.Children.Add(holder);
        }
    }

    private static void AppendSection(DocumentDiffNode parent, TypstSection section)
    {
        if (section.HeaderValue is { } header)
        {
            parent.Property("header", header);
        }

        if (section.FooterValue is { } footer)
        {
            parent.Property("footer", footer);
        }

        if (section.NumberingFormat is { } numbering)
        {
            parent.Property("numbering", numbering);
        }

        parent.Property("startsOnNewPage", section.StartsOnNewPageValue ? "true" : "false");

        if (Read(section.Body, "body") is IReadOnlyList<TypstNode> body)
        {
            AppendNodes(parent, body, "section");
        }
    }

    private static void AppendStyle(DocumentDiffNode parent, TypstTextStyle style)
    {
        if (Read(style, "arguments") is not IReadOnlyList<string> arguments)
        {
            return;
        }

        foreach (var argument in arguments)
        {
            var separator = argument.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            parent.Property(
                argument[..separator].Trim(),
                argument[(separator + 1)..].Trim());
        }
    }

    private static void AppendItems(DocumentDiffNode parent, IReadOnlyList<ListItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var node = new DocumentDiffNode("item", Segment("item", index));
            AppendContent(node, items[index].Content);
            AppendNodes(node, items[index].Children, "nested");
            node.Seal();
            parent.Children.Add(node);
        }
    }

    private static void AppendTerms(
        DocumentDiffNode parent,
        IReadOnlyList<(string Term, TypstContent Description)> terms)
    {
        for (var index = 0; index < terms.Count; index++)
        {
            var term = new DocumentDiffNode("term", Segment("term", index))
            {
                HasText = true,
                Text = terms[index].Term,
            };
            term.Seal();
            parent.Children.Add(term);

            var description = new DocumentDiffNode("description", Segment("description", index));
            AppendContent(description, terms[index].Description);
            description.Seal();
            parent.Children.Add(description);
        }
    }

    private static string Segment(string role, int index)
        => role + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The kind of a node, from its type name: <c>ParagraphNode</c> is <c>paragraph</c>.</summary>
    private static string KindName(Type type)
    {
        var name = type.Name;

        if (name.EndsWith("Node", StringComparison.Ordinal))
        {
            name = name[..^"Node".Length];
        }

        var builder = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];

            if (char.IsUpper(c) && index > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.Length == 0 ? "node" : builder.ToString();
    }

    private static object? Read(object instance, string name)
        => Explain(instance.GetType()).TryGetValue(name, out var field) ? field.GetValue(instance) : null;

    /// <summary>The readable fields of a type, mapped to the names their declarations gave them.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "The type is always a document node type of this assembly, and every one of them is constructed by the public builders, so none is trimmed.")]
    private static IReadOnlyDictionary<string, FieldInfo> Explain(Type type)
        => Fields.GetOrAdd(type, Describe);

    /// <summary>
    /// Maps each field of a type to the name it was declared under, stripped of the decoration the
    /// compiler adds: <c>&lt;text&gt;P</c> for a primary-constructor parameter,
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
