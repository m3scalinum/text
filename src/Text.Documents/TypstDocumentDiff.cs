using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>What kind of change one entry of a document diff reports.</summary>
public enum TypstDocumentChangeKind
{
    /// <summary>A node exists in the later document and not in the earlier one.</summary>
    NodeAdded,

    /// <summary>A node exists in the earlier document and not in the later one.</summary>
    NodeRemoved,

    /// <summary>A node kept its place and its text payload changed.</summary>
    TextChanged,

    /// <summary>A node kept its place and one of its other attributes changed.</summary>
    StyleChanged,

    /// <summary>A node appears in both documents, at a different place, with the same content.</summary>
    NodeMoved,
}

/// <summary>Where in the document tree a change happened.</summary>
/// <remarks>
/// <para>
/// A path is a dot-separated list of segments from the document body down to the node:
/// <c>body[3]</c> is the fourth block, <c>body[3].inline[1]</c> the second run inside it,
/// <c>body[2].row[1].cell[0].inline[0]</c> the first run of the first cell of the second body row of
/// the third block. A segment holds every index the step needed, so a table cell is located by its
/// row and its column in one segment.
/// </para>
/// <para>
/// The path is what makes a change usable: without it a caller knows that a price changed but not
/// which one. <see cref="Label"/> adds the nearest label in scope, when the document carries one, so
/// a change inside a labelled figure or table can be reported against that name.
/// </para>
/// </remarks>
public sealed class TypstDocumentPath
{
    internal TypstDocumentPath(string text, IReadOnlyList<string> segments, string? label)
    {
        Text = text;
        Segments = segments;
        Label = label;
    }

    /// <summary>The path as one string, such as <c>body[3].inline[1]</c>.</summary>
    public string Text { get; }

    /// <summary>The path split into its segments, outermost first.</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>The label in scope at the node, or null when the document labels nothing around it.</summary>
    public string? Label { get; }

    /// <summary>Writes the path as its <see cref="Text"/>.</summary>
    public override string ToString() => Text;
}

/// <summary>One entry of a <see cref="TypstDocumentDiff"/>: a change, and where it happened.</summary>
/// <remarks>
/// The entries are typed rather than a single record with nullable fields, so a caller that handles
/// three kinds out of five cannot read an old text that is not there: the compiler names the kind.
/// </remarks>
public abstract class TypstDocumentChange
{
    internal TypstDocumentChange(TypstDocumentPath path) => Path = path;

    /// <summary>Where the change is, in the later document for a node that exists in both.</summary>
    public TypstDocumentPath Path { get; }

    /// <summary>The kind of this entry, so a caller can switch without pattern matching.</summary>
    public abstract TypstDocumentChangeKind Kind { get; }
}

/// <summary>A node the later document holds and the earlier one did not.</summary>
public sealed class TypstNodeAdded : TypstDocumentChange
{
    internal TypstNodeAdded(TypstDocumentPath path, string node)
        : base(path) => Node = node;

    /// <summary>The kind of the added node, such as <c>paragraph</c> or <c>cell</c>.</summary>
    public string Node { get; }

    /// <summary>Reports this entry as an addition.</summary>
    public override TypstDocumentChangeKind Kind => TypstDocumentChangeKind.NodeAdded;

    /// <summary>Writes the entry as one line, for a log or a test failure.</summary>
    public override string ToString() => "added " + Node + " at " + Path.Text;
}

/// <summary>A node the earlier document held and the later one does not.</summary>
public sealed class TypstNodeRemoved : TypstDocumentChange
{
    internal TypstNodeRemoved(TypstDocumentPath path, string node)
        : base(path) => Node = node;

    /// <summary>The kind of the removed node, such as <c>paragraph</c> or <c>cell</c>.</summary>
    public string Node { get; }

    /// <summary>Reports this entry as a removal.</summary>
    public override TypstDocumentChangeKind Kind => TypstDocumentChangeKind.NodeRemoved;

    /// <summary>Writes the entry as one line, for a log or a test failure.</summary>
    public override string ToString() => "removed " + Node + " at " + Path.Text;
}

/// <summary>The text payload of a node changed: its words, its code, its label or its math.</summary>
public sealed class TypstTextChanged : TypstDocumentChange
{
    internal TypstTextChanged(TypstDocumentPath path, string oldText, string newText)
        : base(path)
    {
        OldText = oldText;
        NewText = newText;
    }

    /// <summary>The text as the earlier document had it.</summary>
    public string OldText { get; }

    /// <summary>The text as the later document has it.</summary>
    public string NewText { get; }

    /// <summary>Reports this entry as a text change.</summary>
    public override TypstDocumentChangeKind Kind => TypstDocumentChangeKind.TextChanged;

    /// <summary>Writes the entry as one line, for a log or a test failure.</summary>
    public override string ToString()
        => "text at " + Path.Text + ": \"" + OldText + "\" -> \"" + NewText + "\"";
}

/// <summary>One attribute of a node changed: a font size, a level, a column span, a URL, a label.</summary>
/// <remarks>
/// A value the node did not carry is reported as null, so "the heading gained a level" and "the
/// heading's level went from 2 to 3" are told apart.
/// </remarks>
public sealed class TypstStyleChanged : TypstDocumentChange
{
    internal TypstStyleChanged(TypstDocumentPath path, string property, string? oldValue, string? newValue)
        : base(path)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>The attribute that changed, such as <c>size</c>, <c>level</c> or <c>url</c>.</summary>
    public string Property { get; }

    /// <summary>The value the earlier document had, or null when the attribute was absent.</summary>
    public string? OldValue { get; }

    /// <summary>The value the later document has, or null when the attribute is gone.</summary>
    public string? NewValue { get; }

    /// <summary>Reports this entry as a style change.</summary>
    public override TypstDocumentChangeKind Kind => TypstDocumentChangeKind.StyleChanged;

    /// <summary>Writes the entry as one line, for a log or a test failure.</summary>
    public override string ToString()
        => "style at " + Path.Text + ": " + Property + " "
            + (OldValue ?? "(absent)") + " -> " + (NewValue ?? "(absent)");
}

/// <summary>A node appears in both documents with the same content, at a different place.</summary>
public sealed class TypstNodeMoved : TypstDocumentChange
{
    internal TypstNodeMoved(TypstDocumentPath path, TypstDocumentPath from, string node)
        : base(path)
    {
        From = from;
        Node = node;
    }

    /// <summary>Where the node was in the earlier document.</summary>
    public TypstDocumentPath From { get; }

    /// <summary>The kind of the node that moved, such as <c>paragraph</c>.</summary>
    public string Node { get; }

    /// <summary>Reports this entry as a move.</summary>
    public override TypstDocumentChangeKind Kind => TypstDocumentChangeKind.NodeMoved;

    /// <summary>Writes the entry as one line, for a log or a test failure.</summary>
    public override string ToString() => "moved " + Node + " from " + From.Text + " to " + Path.Text;
}

/// <summary>
/// Compares two <see cref="TypstDocument"/> values and reports what changed between them.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is semantic: it runs over the document model, node by node, and never over Typst
/// source or over a compiled PDF. Nothing is compiled, so no native library is loaded and no font is
/// resolved — a document can be diffed in a process that has no compiler at all, which is what makes
/// this usable in a service that compares two versions of an invoice without rendering either.
/// </para>
/// <para>
/// Siblings are aligned with a longest-common-subsequence pass keyed on the node kind: an inserted
/// paragraph is one addition instead of a shift of every paragraph after it, and a paragraph whose
/// price changed keeps its place and is reported as one <see cref="TypstTextChanged"/>.
/// <c>docs/specifications/45-document-diff.md</c> §2 gives the algorithm, the alternatives that were
/// rejected and the cost.
/// </para>
/// </remarks>
public static class TypstDocumentDiff
{
    /// <summary>Compares two documents and returns every change, in the order of the later one.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <returns>
    /// The changes, outermost-first and in document order; an empty list when the two bodies hold the
    /// same tree. The list is empty for two identical documents, whatever their page setup.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="after"/> is null.</exception>
    public static IReadOnlyList<TypstDocumentChange> Between(TypstDocument before, TypstDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<TypstDocumentChange>();
        var path = new DiffPath();

        Compare(DocumentDiffSnapshot.Capture(before), DocumentDiffSnapshot.Capture(after), path, changes);

        return changes;
    }

    private static void Compare(
        DocumentDiffNode before,
        DocumentDiffNode after,
        DiffPath path,
        List<TypstDocumentChange> changes)
    {
        path.Push(after);

        try
        {
            if (before.HasText && after.HasText && !string.Equals(before.Text, after.Text, StringComparison.Ordinal))
            {
                changes.Add(new TypstTextChanged(path.ToPath(), before.Text, after.Text));
            }

            CompareProperties(before, after, path, changes);
            CompareChildren(before, after, path, changes);
        }
        finally
        {
            path.Pop();
        }
    }

    private static void CompareProperties(
        DocumentDiffNode before,
        DocumentDiffNode after,
        DiffPath path,
        List<TypstDocumentChange> changes)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var pair in before.Properties)
        {
            names.Add(pair.Key);
        }

        foreach (var pair in after.Properties)
        {
            names.Add(pair.Key);
        }

        foreach (var name in names)
        {
            var oldValue = Value(before, name);
            var newValue = Value(after, name);

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new TypstStyleChanged(path.ToPath(), name, oldValue, newValue));
            }
        }
    }

    private static string? Value(DocumentDiffNode node, string name)
    {
        foreach (var pair in node.Properties)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static void CompareChildren(
        DocumentDiffNode before,
        DocumentDiffNode after,
        DiffPath path,
        List<TypstDocumentChange> changes)
    {
        var alignment = Align(before.Children, after.Children);
        var backward = new int[after.Children.Count];
        var removed = new List<int>();

        Array.Fill(backward, -1);

        for (var index = 0; index < alignment.Length; index++)
        {
            if (alignment[index] >= 0)
            {
                backward[alignment[index]] = index;
            }
            else
            {
                removed.Add(index);
            }
        }

        var consumed = new bool[before.Children.Count];
        var movedFrom = new Dictionary<int, int>();

        // A removal and an addition that hold the same subtree are one moved node.
        foreach (var target in Indices(backward, matched: false))
        {
            foreach (var source in removed)
            {
                if (consumed[source])
                {
                    continue;
                }

                if (string.Equals(
                    before.Children[source].Signature,
                    after.Children[target].Signature,
                    StringComparison.Ordinal))
                {
                    consumed[source] = true;
                    movedFrom[target] = source;
                    break;
                }
            }
        }

        for (var index = 0; index < after.Children.Count; index++)
        {
            if (backward[index] >= 0)
            {
                Compare(before.Children[backward[index]], after.Children[index], path, changes);
                continue;
            }

            if (movedFrom.TryGetValue(index, out var from))
            {
                changes.Add(new TypstNodeMoved(
                    path.PathTo(after.Children[index]),
                    path.PathTo(before.Children[from]),
                    after.Children[index].Kind));
                continue;
            }

            changes.Add(new TypstNodeAdded(path.PathTo(after.Children[index]), after.Children[index].Kind));
        }

        foreach (var source in removed)
        {
            if (!consumed[source])
            {
                changes.Add(new TypstNodeRemoved(path.PathTo(before.Children[source]), before.Children[source].Kind));
            }
        }
    }

    private static IEnumerable<int> Indices(int[] backward, bool matched)
    {
        for (var index = 0; index < backward.Length; index++)
        {
            if ((backward[index] >= 0) == matched)
            {
                yield return index;
            }
        }
    }

    /// <summary>
    /// Aligns two sibling lists: the result holds, for each node of the earlier list, the index of the
    /// node of the later list it corresponds to, or -1.
    /// </summary>
    /// <remarks>
    /// A weighted longest common subsequence. Two nodes of the same kind score 1, and two nodes of the
    /// same kind whose whole subtree is identical score 2, so an exact match always wins over a
    /// re-pairing: inserting a paragraph between two others leaves the two others aligned instead of
    /// pairing each with its neighbour and reporting a text change for a move.
    /// </remarks>
    private static int[] Align(List<DocumentDiffNode> before, List<DocumentDiffNode> after)
    {
        var alignment = new int[before.Count];

        Array.Fill(alignment, -1);

        if (before.Count == 0 || after.Count == 0)
        {
            return alignment;
        }

        var score = new int[before.Count + 1, after.Count + 1];

        for (var row = 1; row <= before.Count; row++)
        {
            for (var column = 1; column <= after.Count; column++)
            {
                var best = Math.Max(score[row - 1, column], score[row, column - 1]);
                var weight = Weight(before[row - 1], after[column - 1]);

                if (weight > 0)
                {
                    best = Math.Max(best, score[row - 1, column - 1] + weight);
                }

                score[row, column] = best;
            }
        }

        var left = before.Count;
        var right = after.Count;

        while (left > 0 && right > 0)
        {
            var weight = Weight(before[left - 1], after[right - 1]);

            if (weight > 0 && score[left, right] == score[left - 1, right - 1] + weight)
            {
                alignment[left - 1] = right - 1;
                left--;
                right--;
            }
            else if (score[left - 1, right] >= score[left, right - 1])
            {
                left--;
            }
            else
            {
                right--;
            }
        }

        return alignment;
    }

    private static int Weight(DocumentDiffNode before, DocumentDiffNode after)
    {
        if (!string.Equals(before.Kind, after.Kind, StringComparison.Ordinal))
        {
            return 0;
        }

        return string.Equals(before.Signature, after.Signature, StringComparison.Ordinal) ? 2 : 1;
    }

    /// <summary>The path built so far, and the label in scope at each level.</summary>
    private sealed class DiffPath
    {
        private readonly List<string> _segments = [];
        private readonly List<string?> _labels = [];

        internal void Push(DocumentDiffNode node)
        {
            _segments.Add(node.Segment);
            _labels.Add(node.Label ?? CurrentLabel);
        }

        internal void Pop()
        {
            _segments.RemoveAt(_segments.Count - 1);
            _labels.RemoveAt(_labels.Count - 1);
        }

        /// <summary>The path of a child of the node currently pushed.</summary>
        internal TypstDocumentPath PathTo(DocumentDiffNode child)
        {
            Push(child);

            try
            {
                return ToPath();
            }
            finally
            {
                Pop();
            }
        }

        internal TypstDocumentPath ToPath()
        {
            var segments = new List<string>(_segments.Count);

            foreach (var segment in _segments)
            {
                if (segment.Length > 0)
                {
                    segments.Add(segment);
                }
            }

            return new TypstDocumentPath(string.Join(".", segments), segments, CurrentLabel);
        }

        private string? CurrentLabel => _labels.Count == 0 ? null : _labels[^1];
    }
}
