using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>One entry of a list: its inline text and any nested blocks, usually a sublist.</summary>
internal sealed class ListItem(TypstContent content, List<TypstNode>? children = null)
{
    public TypstContent Content { get; } = content;

    public List<TypstNode> Children { get; } = children ?? [];
}

internal sealed class ListNode : TypstNode
{
    private static readonly string[] Indents =
    [
        string.Empty,
        "  ",
        "    ",
        "      ",
        "        ",
        "          ",
        "            ",
    ];

    private readonly IReadOnlyList<ListItem> _items;
    private readonly bool _ordered;

    public ListNode(IReadOnlyList<TypstContent> items, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(items);

        _ordered = ordered;
        var entries = new ListItem[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            entries[index] = new ListItem(items[index]);
        }

        _items = entries;
    }

    public ListNode(IReadOnlyList<ListItem> items, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        _ordered = ordered;
    }

    internal override void Write(TypstMarkupWriter writer)
    {
        if (_items.Count == 0)
        {
            return;
        }

        writer.BlankLine();
        WriteItems(writer, _items, _ordered, 0);
    }

    private static void WriteItems(
        TypstMarkupWriter writer,
        IReadOnlyList<ListItem> items,
        bool ordered,
        int depth)
    {
        foreach (var item in items)
        {
            writer.WriteVerbatim(Indent(depth));
            writer.WriteVerbatim(ordered ? "+ " : "- ");
            item.Content.Write(writer);
            writer.NewLine();

            foreach (var child in item.Children)
            {
                if (child is ListNode nested)
                {
                    WriteItems(writer, nested._items, nested._ordered, depth + 1);
                }
                else
                {
                    child.Write(writer);
                }
            }
        }
    }

    private static string Indent(int depth)
        => depth < Indents.Length ? Indents[depth] : new string(' ', depth * 2);
}
