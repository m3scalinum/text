using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>
/// Splits Markdown into blocks. Line-based on purpose: no dependency, no allocation per character,
/// and predictable behaviour on malformed input, which is what a document importer needs.
/// </summary>
/// <remarks>
/// Reference link definitions and footnote definitions are collected before the blocks are parsed,
/// so a reference may appear before its definition, as CommonMark allows. The definition lines are
/// blanked out of the source in that pass, which keeps block boundaries intact.
/// </remarks>
internal sealed class MarkdownBlockParser
{
    /// <summary>HTML tags whose line starts an HTML block (CommonMark type 6).</summary>
    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption",
        "center", "col", "colgroup", "dd", "details", "dialog", "dir", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2",
        "h3", "h4", "h5", "h6", "head", "header", "hr", "html", "iframe", "legend", "li",
        "link", "main", "menu", "menuitem", "nav", "noframes", "ol", "optgroup", "option", "p",
        "param", "search", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead",
        "title", "tr", "track", "ul",
    };

    private readonly Dictionary<string, MarkdownLinkDefinition> _links = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _footnotes = new(StringComparer.Ordinal);

    /// <summary>The reference link definitions of the document, keyed by normalised label.</summary>
    public IReadOnlyDictionary<string, MarkdownLinkDefinition> Links => _links;

    /// <summary>The footnote definitions of the document, as raw lines, keyed by label.</summary>
    public IReadOnlyDictionary<string, List<string>> Footnotes => _footnotes;

    /// <summary>Parses a whole document.</summary>
    public List<MarkdownBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var lines = SplitLines(Normalize(markdown));
        CollectDefinitions(lines);
        return ParseBlocks(lines, 0, lines.Length);
    }

    /// <summary>Parses a fragment of lines that already belongs to a containing block.</summary>
    public List<MarkdownBlock> ParseNested(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var copy = new string[lines.Count];

        for (var index = 0; index < lines.Count; index++)
        {
            copy[index] = lines[index];
        }

        return ParseBlocks(copy, 0, copy.Length);
    }

    /// <summary>Normalises line endings, drops a byte-order mark and expands tabs to four columns.</summary>
    public static string Normalize(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        return text.Contains('\t') ? ExpandTabs(text) : text;
    }

    private static string ExpandTabs(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        var column = 0;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                builder.Append('\n');
                column = 0;
                continue;
            }

            if (c == '\t')
            {
                var width = 4 - (column % 4);
                builder.Append(' ', width);
                column += width;
                continue;
            }

            builder.Append(c);
            column++;
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string text) => text.Split('\n');

    // ---- definitions ---------------------------------------------------------------------------

    private void CollectDefinitions(string[] lines)
    {
        var index = 0;

        while (index < lines.Length)
        {
            if (TryFence(lines[index], out var fence))
            {
                index = SkipFence(lines, index, fence);
                continue;
            }

            if (IsBlank(lines[index]) || IndentOf(lines[index]) > 3)
            {
                index++;
                continue;
            }

            var startsBlock = index == 0 || IsBlank(lines[index - 1]);

            if (!startsBlock)
            {
                index++;
                continue;
            }

            if (TryFootnoteDefinition(lines, index, out var label, out var body, out var next))
            {
                _footnotes[label] = body;
                BlankOut(lines, index, next);
                index = next;
                continue;
            }

            if (TryLinkDefinition(lines, index, out var definition, out next))
            {
                _links[definition.Label] = definition;
                BlankOut(lines, index, next);
                index = next;
                continue;
            }

            index++;
        }
    }

    private static void BlankOut(string[] lines, int from, int to)
    {
        for (var index = from; index < to && index < lines.Length; index++)
        {
            lines[index] = string.Empty;
        }
    }

    private static bool TryLinkDefinition(string[] lines, int index, out MarkdownLinkDefinition definition, out int next)
    {
        definition = default;
        next = index;

        var line = lines[index];
        var indent = IndentOf(line);
        var span = line.AsSpan(indent);

        if (span.Length < 4 || span[0] != '[')
        {
            return false;
        }

        var close = span.IndexOf("]:", StringComparison.Ordinal);

        if (close < 2)
        {
            return false;
        }

        var label = span[1..close].ToString();

        if (label.Length == 0 || label[0] == '^')
        {
            return false;
        }

        var rest = span[(close + 2)..].Trim().ToString();
        next = index + 1;

        if (rest.Length == 0)
        {
            if (index + 1 >= lines.Length)
            {
                return false;
            }

            rest = lines[index + 1].Trim();
            next = index + 2;
        }

        string destination;

        if (rest.StartsWith('<'))
        {
            var gt = rest.IndexOf('>', StringComparison.Ordinal);

            if (gt < 0)
            {
                return false;
            }

            destination = rest[1..gt];
            rest = rest[(gt + 1)..].Trim();
        }
        else
        {
            var position = 0;
            var depth = 0;

            while (position < rest.Length && !char.IsWhiteSpace(rest[position]))
            {
                if (rest[position] == '(')
                {
                    depth++;
                }
                else if (rest[position] == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                position++;
            }

            destination = rest[..position];
            rest = rest[position..].Trim();
        }

        if (destination.Length == 0)
        {
            return false;
        }

        definition = new MarkdownLinkDefinition(NormalizeLabel(label), destination, ReadTitle(rest));
        return true;
    }

    private static string? ReadTitle(string rest)
    {
        if (rest.Length < 2)
        {
            return null;
        }

        var open = rest[0];

        if (open is not ('"' or '\'' or '('))
        {
            return null;
        }

        var close = open == '(' ? ')' : open;
        var end = rest.LastIndexOf(close);

        return end <= 0 ? null : rest[1..end];
    }

    private static bool TryFootnoteDefinition(string[] lines, int index, out string label, out List<string> body, out int next)
    {
        label = string.Empty;
        body = [];
        next = index;

        var line = lines[index];
        var indent = IndentOf(line);
        var span = line.AsSpan(indent);

        if (!span.StartsWith("[^", StringComparison.Ordinal))
        {
            return false;
        }

        var close = span.IndexOf("]:", StringComparison.Ordinal);

        if (close < 3)
        {
            return false;
        }

        label = span[2..close].ToString().Trim();

        if (label.Length == 0)
        {
            return false;
        }

        body.Add(span[(close + 2)..].ToString().TrimStart());

        var position = index + 1;

        while (position < lines.Length)
        {
            if (IsBlank(lines[position]))
            {
                body.Add(string.Empty);
                position++;
                continue;
            }

            if (IndentOf(lines[position]) >= 4)
            {
                body.Add(lines[position][4..]);
                position++;
                continue;
            }

            if (body.Count > 0 && body[^1].Length > 0 && !StartsNewBlock(lines[position]))
            {
                body.Add(lines[position].Trim());
                position++;
                continue;
            }

            break;
        }

        while (body.Count > 0 && body[^1].Length == 0)
        {
            body.RemoveAt(body.Count - 1);
        }

        next = position;
        return true;
    }

    /// <summary>Collapses a label's whitespace and trims it, as CommonMark does before matching.</summary>
    public static string NormalizeLabel(string label)
    {
        var builder = new StringBuilder(label.Length);
        var pendingSpace = false;

        foreach (var c in label.Trim())
        {
            if (c is ' ' or '\t' or '\n')
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

    // ---- blocks --------------------------------------------------------------------------------

    private List<MarkdownBlock> ParseBlocks(string[] lines, int start, int end)
    {
        var blocks = new List<MarkdownBlock>();
        var index = start;

        while (index < end)
        {
            if (IsBlank(lines[index]))
            {
                index++;
                continue;
            }

            if (TryFence(lines[index], out var fence))
            {
                blocks.Add(ParseFence(lines, ref index, end, fence));
                continue;
            }

            if (TryAtxHeading(lines[index], out var level, out var heading))
            {
                blocks.Add(new MarkdownHeading(level, heading));
                index++;
                continue;
            }

            if (IsThematicBreak(lines[index]))
            {
                blocks.Add(new MarkdownThematicBreak());
                index++;
                continue;
            }

            if (IsBlockQuoteStart(lines[index]))
            {
                blocks.Add(ParseBlockQuote(lines, ref index, end));
                continue;
            }

            if (IsHtmlBlockStart(lines[index]))
            {
                blocks.Add(ParseHtmlBlock(lines, ref index, end));
                continue;
            }

            if (TryListMarker(lines[index], out var marker))
            {
                blocks.Add(ParseList(lines, ref index, end, marker));
                continue;
            }

            if (IndentOf(lines[index]) >= 4)
            {
                blocks.Add(ParseIndentedCode(lines, ref index, end));
                continue;
            }

            if (IsTableStart(lines, index, end))
            {
                blocks.Add(ParseTable(lines, ref index, end));
                continue;
            }

            if (TrySetextHeading(lines, index, end, out var setextHeading))
            {
                blocks.Add(setextHeading);
                index += 2;
                continue;
            }

            blocks.Add(ParseParagraph(lines, ref index, end));
        }

        return blocks;
    }

    private static MarkdownCodeBlock ParseFence(string[] lines, ref int index, int end, Fence fence)
    {
        var code = new List<string>();
        var position = index + 1;

        while (position < end)
        {
            if (IsFenceClose(lines[position], fence))
            {
                position++;
                break;
            }

            var line = lines[position];
            var strip = Math.Min(fence.Indent, IndentOf(line));
            code.Add(line[strip..]);
            position++;
        }

        index = position;

        var language = FirstWord(fence.Info);

        while (code.Count > 0 && code[^1].Length == 0)
        {
            code.RemoveAt(code.Count - 1);
        }

        return new MarkdownCodeBlock(language, string.Join("\n", code));
    }

    private static int SkipFence(string[] lines, int index, Fence fence)
    {
        var position = index + 1;

        while (position < lines.Length)
        {
            if (IsFenceClose(lines[position], fence))
            {
                return position + 1;
            }

            position++;
        }

        return lines.Length;
    }

    private static bool IsFenceClose(string line, Fence fence)
    {
        var indent = IndentOf(line);

        if (indent > 3)
        {
            return false;
        }

        var span = line.AsSpan(indent);
        var run = 0;

        while (run < span.Length && span[run] == fence.Char)
        {
            run++;
        }

        return run >= fence.Length && span[run..].Trim().IsEmpty;
    }

    /// <summary>Reads the language out of an info string: its first word, or a <c>{.lang}</c> word.</summary>
    private static string FirstWord(string info)
    {
        var trimmed = info.Trim();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed[0] == '{')
        {
            var close = trimmed.IndexOf('}');

            if (close > 0)
            {
                var inside = trimmed[1..close];

                foreach (var part in inside.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length > 1 && part[0] == '.')
                    {
                        return part[1..];
                    }
                }
            }

            return string.Empty;
        }

        foreach (var part in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            return part;
        }

        return string.Empty;
    }

    private static bool TryAtxHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var indent = IndentOf(line);

        if (indent > 3)
        {
            return false;
        }

        var span = line.AsSpan(indent);
        var run = 0;

        while (run < span.Length && span[run] == '#' && run < 7)
        {
            run++;
        }

        if (run is 0 or > 6)
        {
            return false;
        }

        if (run < span.Length && span[run] is not (' ' or '\t'))
        {
            return false;
        }

        var rest = span[run..].Trim().ToString();

        // A closing run of hashes is decoration, unless the heading is only hashes.
        var trimmedEnd = rest.Length;

        while (trimmedEnd > 0 && rest[trimmedEnd - 1] == '#')
        {
            trimmedEnd--;
        }

        if (trimmedEnd < rest.Length && (trimmedEnd == 0 || rest[trimmedEnd - 1] is ' ' or '\t'))
        {
            rest = rest[..trimmedEnd].TrimEnd();
        }

        level = run;
        text = rest;
        return true;
    }

    private static bool IsThematicBreak(string line)
    {
        var indent = IndentOf(line);

        if (indent > 3)
        {
            return false;
        }

        char marker = '\0';
        var count = 0;

        foreach (var c in line.AsSpan(indent))
        {
            if (c == ' ' || c == '\t')
            {
                continue;
            }

            if (c is not ('*' or '-' or '_'))
            {
                return false;
            }

            if (marker == '\0')
            {
                marker = c;
            }
            else if (marker != c)
            {
                return false;
            }

            count++;
        }

        return count >= 3;
    }

    private static bool IsBlockQuoteStart(string line)
    {
        var indent = IndentOf(line);
        return indent <= 3 && indent < line.Length && line[indent] == '>';
    }

    private MarkdownQuote ParseBlockQuote(string[] lines, ref int index, int end)
    {
        var inner = new List<string>();
        var position = index;

        while (position < end)
        {
            if (IsBlockQuoteStart(lines[position]))
            {
                var indent = IndentOf(lines[position]);
                var rest = lines[position][(indent + 1)..];

                if (rest.StartsWith(' '))
                {
                    rest = rest[1..];
                }

                inner.Add(rest);
                position++;
                continue;
            }

            if (IsBlank(lines[position]))
            {
                // A blank line continues the quote only when another quoted line follows.
                var lookahead = position + 1;

                while (lookahead < end && IsBlank(lines[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < end && IsBlockQuoteStart(lines[lookahead]))
                {
                    inner.Add(string.Empty);
                    position++;
                    continue;
                }

                break;
            }

            if (inner.Count > 0 && inner[^1].Length > 0 && !StartsNewBlock(lines[position]))
            {
                inner.Add(lines[position].Trim());
                position++;
                continue;
            }

            break;
        }

        index = position;
        return new MarkdownQuote(ParseNested(inner));
    }

    private static MarkdownHtmlBlock ParseHtmlBlock(string[] lines, ref int index, int end)
    {
        var builder = new StringBuilder();
        var position = index;

        while (position < end && !IsBlank(lines[position]))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[position]);
            position++;
        }

        index = position;
        return new MarkdownHtmlBlock(builder.ToString());
    }

    private static MarkdownCodeBlock ParseIndentedCode(string[] lines, ref int index, int end)
    {
        var code = new List<string>();
        var position = index;

        while (position < end)
        {
            if (IsBlank(lines[position]))
            {
                code.Add(string.Empty);
                position++;
                continue;
            }

            if (IndentOf(lines[position]) < 4)
            {
                break;
            }

            code.Add(lines[position][4..]);
            position++;
        }

        index = position;

        while (code.Count > 0 && code[0].Length == 0)
        {
            code.RemoveAt(0);
        }

        while (code.Count > 0 && code[^1].Length == 0)
        {
            code.RemoveAt(code.Count - 1);
        }

        return new MarkdownCodeBlock(string.Empty, string.Join("\n", code));
    }

    private MarkdownList ParseList(string[] lines, ref int index, int end, ListMarker first)
    {
        var items = new List<MarkdownListItem>();
        var loose = false;
        var position = index;
        var blankBefore = false;

        while (position < end)
        {
            if (IsBlank(lines[position]))
            {
                blankBefore = items.Count > 0;
                position++;
                continue;
            }

            if (!TryListMarker(lines[position], out var marker)
                || marker.Ordered != first.Ordered
                || marker.Character != first.Character)
            {
                break;
            }

            if (blankBefore)
            {
                loose = true;
                blankBefore = false;
            }

            var itemLines = new List<string>();
            var contentStart = Math.Min(marker.ContentStart, lines[position].Length);
            itemLines.Add(lines[position][contentStart..]);
            position++;

            var blankInside = false;

            while (position < end)
            {
                if (IsBlank(lines[position]))
                {
                    itemLines.Add(string.Empty);
                    blankInside = true;
                    position++;
                    continue;
                }

                var indent = IndentOf(lines[position]);

                if (indent >= marker.ContentStart)
                {
                    if (blankInside)
                    {
                        loose = true;
                    }

                    itemLines.Add(lines[position][marker.ContentStart..]);
                    blankInside = false;
                    position++;
                    continue;
                }

                if (!blankInside && itemLines.Count > 0 && itemLines[^1].Length > 0 && !StartsNewBlock(lines[position]))
                {
                    // A lazy continuation of the item's paragraph.
                    itemLines.Add(lines[position].Trim());
                    position++;
                    continue;
                }

                break;
            }

            while (itemLines.Count > 0 && itemLines[^1].Length == 0)
            {
                itemLines.RemoveAt(itemLines.Count - 1);
            }

            items.Add(new MarkdownListItem(ParseNested(itemLines)));

            if (blankInside)
            {
                blankBefore = true;
            }
        }

        index = position;
        return new MarkdownList(first.Ordered, !loose, items);
    }

    private static MarkdownParagraph ParseParagraph(string[] lines, ref int index, int end)
    {
        var text = new StringBuilder();
        var position = index;
        var first = true;

        while (position < end)
        {
            if (IsBlank(lines[position]))
            {
                break;
            }

            if (!first && StartsNewBlock(lines[position]))
            {
                break;
            }

            if (!first)
            {
                text.Append('\n');
            }

            text.Append(lines[position].TrimStart(' '));
            position++;
            first = false;
        }

        index = position;
        return new MarkdownParagraph(text.ToString());
    }

    /// <summary>Recognises a setext heading: a paragraph line underlined by <c>=</c> or <c>-</c>.</summary>
    private static bool TrySetextHeading(string[] lines, int index, int end, out MarkdownHeading heading)
    {
        heading = null!;

        if (index + 1 >= end || !IsSetextUnderline(lines[index + 1], out var level))
        {
            return false;
        }

        heading = new MarkdownHeading(level, lines[index].Trim());
        return true;
    }

    private static bool IsSetextUnderline(string line, out int level)
    {
        level = 0;

        var indent = IndentOf(line);

        if (indent > 3)
        {
            return false;
        }

        var span = line.AsSpan(indent).TrimEnd();

        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '=')
        {
            level = 1;
        }
        else if (span[0] == '-')
        {
            level = 2;
        }
        else
        {
            return false;
        }

        foreach (var c in span)
        {
            if (c != span[0])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsNewBlock(string line)
    {
        if (IsBlank(line))
        {
            return true;
        }

        return TryFence(line, out _)
            || TryAtxHeading(line, out _, out _)
            || IsThematicBreak(line)
            || IsBlockQuoteStart(line)
            || IsHtmlBlockStart(line)
            || TryListMarker(line, out _)
            || IndentOf(line) >= 4;
    }

    // ---- tables --------------------------------------------------------------------------------

    private static bool IsTableStart(string[] lines, int index, int end)
    {
        if (index + 1 >= end)
        {
            return false;
        }

        if (!lines[index].Contains('|'))
        {
            return false;
        }

        if (IsBlank(lines[index + 1]))
        {
            return false;
        }

        var header = SplitCells(lines[index]);
        var alignments = ParseAlignments(lines[index + 1]);

        return alignments is not null && alignments.Count == header.Count;
    }

    private static MarkdownTable ParseTable(string[] lines, ref int index, int end)
    {
        var header = SplitCells(lines[index]);
        var alignments = ParseAlignments(lines[index + 1]) ?? [];
        var rows = new List<IReadOnlyList<string>>();
        var position = index + 2;

        while (position < end && !IsBlank(lines[position]) && lines[position].Contains('|'))
        {
            rows.Add(SplitCells(lines[position]));
            position++;
        }

        index = position;
        return new MarkdownTable(alignments, header, rows);
    }

    private static List<TypstAlignment?>? ParseAlignments(string line)
    {
        var span = line.AsSpan().Trim();

        if (span.Length == 0)
        {
            return null;
        }

        var trimmed = span;

        if (trimmed[0] == '|')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length > 0 && trimmed[^1] == '|')
        {
            trimmed = trimmed[..^1];
        }

        var parts = SplitCells(line);
        var alignments = new List<TypstAlignment?>(parts.Count);

        // SplitCells has already trimmed the pipes; re-derive the dashes from the raw parts.
        foreach (var part in parts)
        {
            var cell = part.Trim();
            var left = cell.StartsWith(':');
            var right = cell.EndsWith(':');
            var dashes = cell.Length - (left ? 1 : 0) - (right ? 1 : 0);

            if (dashes < 1)
            {
                return null;
            }

            foreach (var c in cell)
            {
                if (c is not ('-' or ':'))
                {
                    return null;
                }
            }

            alignments.Add(left && right
                ? TypstAlignment.Center
                : right
                    ? TypstAlignment.Right
                    : left
                        ? TypstAlignment.Left
                        : null);
        }

        return alignments.Count == 0 ? null : alignments;
    }

    /// <summary>Splits a table row on unescaped pipes, dropping the optional outer pipes.</summary>
    private static List<string> SplitCells(string line)
    {
        var span = line.AsSpan().Trim();

        if (span.Length > 0 && span[0] == '|')
        {
            span = span[1..];
        }

        if (span.Length > 0 && span[^1] == '|' && !EndsWithEscapedPipe(span))
        {
            span = span[..^1];
        }

        var cells = new List<string>();
        var builder = new StringBuilder();
        var escaped = false;

        foreach (var c in span)
        {
            if (escaped)
            {
                if (c != '|')
                {
                    builder.Append('\\');
                }

                builder.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '|')
            {
                cells.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        if (escaped)
        {
            builder.Append('\\');
        }

        cells.Add(builder.ToString().Trim());
        return cells;
    }

    private static bool EndsWithEscapedPipe(ReadOnlySpan<char> span)
    {
        var backslashes = 0;

        for (var index = span.Length - 2; index >= 0 && span[index] == '\\'; index--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    // ---- lexing helpers ------------------------------------------------------------------------

    private static bool IsBlank(string line) => line.AsSpan().Trim().IsEmpty;

    private static int IndentOf(string line)
    {
        var indent = 0;

        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }

    private readonly record struct Fence(char Char, int Length, int Indent, string Info);

    private static bool TryFence(string line, out Fence fence)
    {
        fence = default;

        var indent = IndentOf(line);

        if (indent > 3)
        {
            return false;
        }

        var span = line.AsSpan(indent);

        if (span.Length < 3 || span[0] is not ('`' or '~'))
        {
            return false;
        }

        var character = span[0];
        var run = 0;

        while (run < span.Length && span[run] == character)
        {
            run++;
        }

        if (run < 3)
        {
            return false;
        }

        var info = span[run..].ToString();

        if (character == '`' && info.Contains('`'))
        {
            return false;
        }

        fence = new Fence(character, run, indent, info.Trim());
        return true;
    }

    private readonly record struct ListMarker(bool Ordered, char Character, int Indent, int ContentStart);

    private static bool TryListMarker(string line, out ListMarker marker)
    {
        marker = default;

        var indent = IndentOf(line);

        if (indent > 3 || indent >= line.Length)
        {
            return false;
        }

        var character = line[indent];
        int width;
        bool ordered;
        char kind;

        if (character is '-' or '+' or '*')
        {
            width = 1;
            ordered = false;
            kind = character;
        }
        else if (char.IsAsciiDigit(character))
        {
            var position = indent;

            while (position < line.Length && char.IsAsciiDigit(line[position]) && position - indent < 10)
            {
                position++;
            }

            if (position == indent || position - indent > 9)
            {
                return false;
            }

            if (position >= line.Length || line[position] is not ('.' or ')'))
            {
                return false;
            }

            width = position - indent + 1;
            ordered = true;
            kind = line[position];
        }
        else
        {
            return false;
        }

        var after = indent + width;

        if (after < line.Length && line[after] != ' ')
        {
            return false;
        }

        var spaces = 0;
        var cursor = after;

        while (cursor < line.Length && line[cursor] == ' ')
        {
            spaces++;
            cursor++;
        }

        if (after < line.Length && spaces == 0)
        {
            return false;
        }

        var contentStart = spaces is >= 1 and <= 4 ? after + spaces : after + 1;
        marker = new ListMarker(ordered, kind, indent, contentStart);
        return true;
    }

    private static bool IsHtmlBlockStart(string line)
    {
        var indent = IndentOf(line);

        if (indent > 3 || indent >= line.Length || line[indent] != '<')
        {
            return false;
        }

        var span = line.AsSpan(indent);

        if (span.StartsWith("<!--", StringComparison.Ordinal)
            || span.StartsWith("<?", StringComparison.Ordinal)
            || span.StartsWith("<![CDATA[", StringComparison.Ordinal)
            || span.StartsWith("<!", StringComparison.Ordinal))
        {
            return true;
        }

        var position = 1;

        if (position < span.Length && span[position] == '/')
        {
            position++;
        }

        var start = position;

        while (position < span.Length
            && (char.IsAsciiLetterOrDigit(span[position]) || span[position] == '-'))
        {
            position++;
        }

        if (position == start || !char.IsAsciiLetter(span[start]))
        {
            return false;
        }

        return BlockTags.Contains(span[start..position].ToString().ToLowerInvariant());
    }
}
