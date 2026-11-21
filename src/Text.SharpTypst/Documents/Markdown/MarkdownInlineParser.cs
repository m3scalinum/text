using System;
using System.Text;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>
/// Parses inline Markdown into a <see cref="TypstContent"/>. Every character that survives the
/// parse enters the model as a <see cref="TextNode"/>, so the markup writer escapes it: the
/// importer never concatenates text into Typst source.
/// </summary>
internal sealed class MarkdownInlineParser(IMarkdownInlineContext context)
{
    private const int MaxDepth = 32;

    private readonly IMarkdownInlineContext _context = context;

    /// <summary>Parses one run of inline text. Line breaks are soft breaks unless they are hard.</summary>
    public TypstContent Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseRange(text, 0, text.Length, 0);
    }

    /// <summary>
    /// Recognises a paragraph that is exactly one inline image, which the converter turns into a
    /// figure rather than a paragraph holding the alt text.
    /// </summary>
    /// <param name="text">The raw paragraph text.</param>
    /// <param name="alt">The alt text, empty when there is none.</param>
    /// <param name="destination">The image destination, exactly as written.</param>
    public static bool TryParseStandaloneImage(string text, out string alt, out string destination)
    {
        alt = string.Empty;
        destination = string.Empty;

        var trimmed = text.Trim();

        if (trimmed.Length < 5 || !trimmed.StartsWith("![", StringComparison.Ordinal))
        {
            return false;
        }

        var close = FindClosingBracket(trimmed, 0, trimmed.Length);

        if (close < 2 || close + 1 >= trimmed.Length || trimmed[close + 1] != '(')
        {
            return false;
        }

        if (!TryInlineLink(trimmed, close + 1, trimmed.Length, out var parsed, out _, out var next))
        {
            return false;
        }

        if (next != trimmed.Length)
        {
            return false;
        }

        alt = trimmed[2..close];
        destination = parsed;
        return true;
    }

    private TypstContent ParseRange(string text, int start, int end, int depth)
    {
        var content = new TypstContent();

        if (depth > MaxDepth)
        {
            if (end > start)
            {
                content.Text(text[start..end]);
            }

            return content;
        }

        var buffer = new StringBuilder();
        var position = start;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            content.Text(buffer.ToString());
            buffer.Clear();
        }

        while (position < end)
        {
            var c = text[position];

            switch (c)
            {
                case '\\':
                    if (position + 1 < end && text[position + 1] == '\n')
                    {
                        Flush();
                        content.LineBreak();
                        position += 2;
                        break;
                    }

                    if (position + 1 < end && IsEscapable(text[position + 1]))
                    {
                        buffer.Append(text[position + 1]);
                        position += 2;
                        break;
                    }

                    buffer.Append('\\');
                    position++;
                    break;

                case '\n':
                    var trailing = TrailingSpaces(buffer);

                    if (trailing > 0)
                    {
                        buffer.Length -= trailing;
                    }

                    Flush();

                    if (trailing >= 2)
                    {
                        content.LineBreak();
                    }
                    else
                    {
                        content.Text(" ");
                    }

                    position++;

                    while (position < end && text[position] == ' ')
                    {
                        position++;
                    }

                    break;

                case '`':
                    position = ParseCodeSpan(text, position, end, buffer, content, Flush);
                    break;

                case '<':
                    position = ParseAngle(text, position, end, buffer, content, Flush);
                    break;

                case '&':
                    position = ParseEntity(text, position, end, buffer);
                    break;

                case '!':
                    if (position + 1 < end && text[position + 1] == '[')
                    {
                        position = ParseLink(text, position + 1, end, buffer, content, Flush, image: true, depth);
                        break;
                    }

                    buffer.Append('!');
                    position++;
                    break;

                case '[':
                    position = ParseLink(text, position, end, buffer, content, Flush, image: false, depth);
                    break;

                case '*' or '_' or '~':
                    position = ParseEmphasis(text, position, end, buffer, content, Flush, depth);
                    break;

                default:
                    buffer.Append(c);
                    position++;
                    break;
            }
        }

        Flush();
        return content;
    }

    private static int ParseCodeSpan(
        string text,
        int position,
        int end,
        StringBuilder buffer,
        TypstContent content,
        Action flush)
    {
        var run = RunLength(text, position, end, '`');
        var close = FindBacktickRun(text, position + run, end, run);

        if (close < 0)
        {
            buffer.Append('`', run);
            return position + run;
        }

        var code = text[(position + run)..close].Replace('\n', ' ');

        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && !string.IsNullOrWhiteSpace(code))
        {
            code = code[1..^1];
        }

        flush();
        content.Code(code);
        return close + run;
    }

    private int ParseAngle(
        string text,
        int position,
        int end,
        StringBuilder buffer,
        TypstContent content,
        Action flush)
    {
        var close = text.IndexOf('>', position + 1);

        if (close < 0 || close >= end || close - position > 2048)
        {
            buffer.Append('<');
            return position + 1;
        }

        var candidate = text[(position + 1)..close];

        if (candidate.Length > 0 && !candidate.Contains(' ', StringComparison.Ordinal) && !candidate.Contains('\n'))
        {
            if (TryAutolink(candidate, out var url, out var label))
            {
                flush();
                content.Node(new LinkNode(url, new TypstContent().Text(label)));
                return close + 1;
            }
        }

        if (LooksLikeInlineHtml(candidate))
        {
            flush();
            _context.Drop("html-inline");
            return close + 1;
        }

        buffer.Append('<');
        return position + 1;
    }

    private static bool TryAutolink(string candidate, out string url, out string label)
    {
        url = string.Empty;
        label = string.Empty;

        var colon = candidate.IndexOf(':', StringComparison.Ordinal);

        if (colon >= 2 && colon <= 32)
        {
            var scheme = candidate.AsSpan(0, colon);
            var valid = char.IsAsciiLetter(scheme[0]);

            foreach (var c in scheme)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.' or ':'))
                {
                    valid = false;
                    break;
                }
            }

            if (valid && colon + 1 < candidate.Length)
            {
                url = candidate;
                label = candidate;
                return true;
            }
        }

        var at = candidate.IndexOf('@', StringComparison.Ordinal);

        if (at > 0 && at < candidate.Length - 1 && !candidate.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var local = candidate[..at];
            var domain = candidate[(at + 1)..];

            if (domain.Contains('.', StringComparison.Ordinal)
                && !local.Contains('@', StringComparison.Ordinal)
                && !domain.StartsWith('.')
                && !domain.EndsWith('.')
                && !domain.Contains(' '))
            {
                url = "mailto:" + candidate;
                label = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeInlineHtml(string candidate)
    {
        if (candidate.Length == 0)
        {
            return false;
        }

        if (candidate.Length >= 3 && candidate[0] == '!' && candidate.StartsWith("!--", StringComparison.Ordinal))
        {
            return true;
        }

        var position = candidate[0] == '/' ? 1 : 0;

        if (position >= candidate.Length || !char.IsAsciiLetter(candidate[position]))
        {
            return false;
        }

        while (position < candidate.Length
            && (char.IsAsciiLetterOrDigit(candidate[position]) || candidate[position] is '-' or ':'))
        {
            position++;
        }

        return position == candidate.Length
            || candidate[position] is ' ' or '/' or '='
            || (candidate[position] == '"' && position > 0);
    }

    private static int ParseEntity(string text, int position, int end, StringBuilder buffer)
    {
        var semicolon = text.IndexOf(';', position + 1);

        if (semicolon < 0 || semicolon >= end || semicolon - position > 32)
        {
            buffer.Append('&');
            return position + 1;
        }

        var reference = text[position..(semicolon + 1)];
        var decoded = HtmlEntities.Decode(reference);

        if (string.Equals(decoded, reference, StringComparison.Ordinal))
        {
            buffer.Append('&');
            return position + 1;
        }

        buffer.Append(decoded);
        return semicolon + 1;
    }

    private int ParseEmphasis(
        string text,
        int position,
        int end,
        StringBuilder buffer,
        TypstContent content,
        Action flush,
        int depth)
    {
        var character = text[position];
        var run = RunLength(text, position, end, character);
        var canOpen = position + run < end && !IsSpace(text[position + run]);

        if (character == '_' && canOpen && position > 0
            && char.IsLetterOrDigit(text[position - 1]) && char.IsLetterOrDigit(text[position + run]))
        {
            canOpen = false;
        }

        if (character == '~' && run < 2)
        {
            canOpen = false;
        }

        var use = character == '~'
            ? 2
            : run >= 3
                ? 3
                : run >= 2
                    ? 2
                    : 1;

        if (!canOpen)
        {
            buffer.Append(character, run);
            return position + run;
        }

        var close = FindCloser(text, position + run, end, character, use);

        if (close < 0)
        {
            buffer.Append(character, run);
            return position + run;
        }

        flush();

        var inner = ParseRange(text, position + use, close, depth + 1);

        if (character == '~')
        {
            content.Strikethrough(inner);
        }
        else if (use == 3)
        {
            var strong = new TypstContent().Bold(inner);
            content.Emphasis(strong);
        }
        else if (use == 2)
        {
            content.Bold(inner);
        }
        else
        {
            content.Emphasis(inner);
        }

        return close + use;
    }

    private static int FindCloser(string text, int start, int end, char character, int use)
    {
        var position = start;

        while (position < end)
        {
            var c = text[position];

            if (c == '\\')
            {
                position += 2;
                continue;
            }

            if (c == '`')
            {
                var run = RunLength(text, position, end, '`');
                var close = FindBacktickRun(text, position + run, end, run);
                position = close < 0 ? position + run : close + run;
                continue;
            }

            if (c != character)
            {
                position++;
                continue;
            }

            var closerRun = RunLength(text, position, end, character);

            if (closerRun >= use && position > 0 && !IsSpace(text[position - 1]))
            {
                if (character != '_'
                    || !(position + closerRun < end
                        && char.IsLetterOrDigit(text[position - 1])
                        && char.IsLetterOrDigit(text[position + closerRun])))
                {
                    return position;
                }
            }

            position += closerRun;
        }

        return -1;
    }

    private int ParseLink(
        string text,
        int position,
        int end,
        StringBuilder buffer,
        TypstContent content,
        Action flush,
        bool image,
        int depth)
    {
        if (!image && position + 1 < end && text[position + 1] == '^')
        {
            return ParseFootnote(text, position, end, buffer, content, flush);
        }

        var close = FindClosingBracket(text, position, end);

        if (close < 0)
        {
            buffer.Append('[');
            return position + 1;
        }

        var label = text[(position + 1)..close];
        var after = close + 1;

        if (after < end && text[after] == '('
            && TryInlineLink(text, after, end, out var destination, out var title, out var next))
        {
            _ = title;
            return EmitLink(text, position + 1, close, destination, next, content, flush, image, depth);
        }

        if (after < end && text[after] == '[')
        {
            var referenceClose = text.IndexOf(']', after + 1);

            if (referenceClose > 0 && referenceClose < end)
            {
                var reference = text[(after + 1)..referenceClose];
                var key = MarkdownBlockParser.NormalizeLabel(reference.Length == 0 ? label : reference);

                if (_context.TryGetLink(key, out var definition))
                {
                    return EmitLink(text, position + 1, close, definition.Destination, referenceClose + 1, content, flush, image, depth);
                }
            }
        }

        var shortcut = MarkdownBlockParser.NormalizeLabel(label);

        if (_context.TryGetLink(shortcut, out var shortcutDefinition))
        {
            return EmitLink(text, position + 1, close, shortcutDefinition.Destination, close + 1, content, flush, image, depth);
        }

        buffer.Append('[');
        return position + 1;
    }

    private int EmitLink(
        string text,
        int labelStart,
        int labelEnd,
        string destination,
        int next,
        TypstContent content,
        Action flush,
        bool image,
        int depth)
    {
        var inner = ParseRange(text, labelStart, labelEnd, depth + 1);
        flush();

        if (image)
        {
            var asset = _context.ResolveImage(destination);
            _ = asset;

            inner.AppendTo(content);
        }
        else
        {
            content.Node(new LinkNode(destination, inner));
        }

        return next;
    }

    private int ParseFootnote(
        string text,
        int position,
        int end,
        StringBuilder buffer,
        TypstContent content,
        Action flush)
    {
        var close = text.IndexOf(']', position + 2);

        if (close < 0 || close >= end)
        {
            buffer.Append('[');
            return position + 1;
        }

        var label = text[(position + 2)..close];
        var footnote = _context.TakeFootnote(label);

        if (footnote is null)
        {
            _context.Drop("footnote-reference");
            buffer.Append(text[position..(close + 1)]);
            return close + 1;
        }

        flush();
        content.Node(new FootnoteNode(footnote));
        return close + 1;
    }

    private static bool TryInlineLink(string text, int open, int end, out string destination, out string? title, out int next)
    {
        destination = string.Empty;
        title = null;
        next = open;

        var position = open + 1;

        while (position < end && text[position] == ' ')
        {
            position++;
        }

        if (position >= end)
        {
            return false;
        }

        var builder = new StringBuilder();

        if (text[position] == '<')
        {
            position++;

            while (position < end && text[position] != '>')
            {
                if (text[position] == '\n')
                {
                    return false;
                }

                builder.Append(text[position]);
                position++;
            }

            if (position >= end)
            {
                return false;
            }

            position++;
        }
        else
        {
            var nesting = 0;

            while (position < end)
            {
                var c = text[position];

                if (c == '\\' && position + 1 < end)
                {
                    builder.Append(c).Append(text[position + 1]);
                    position += 2;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    break;
                }

                if (c == '(')
                {
                    nesting++;
                }
                else if (c == ')')
                {
                    if (nesting == 0)
                    {
                        break;
                    }

                    nesting--;
                }

                builder.Append(c);
                position++;
            }
        }

        destination = builder.ToString();

        while (position < end && text[position] == ' ')
        {
            position++;
        }

        if (position < end && text[position] is '"' or '\'' or '(')
        {
            var openQuote = text[position];
            var closeQuote = openQuote == '(' ? ')' : openQuote;
            var titleBuilder = new StringBuilder();
            position++;

            while (position < end && text[position] != closeQuote)
            {
                if (text[position] == '\\' && position + 1 < end)
                {
                    position++;
                }

                titleBuilder.Append(text[position]);
                position++;
            }

            if (position >= end)
            {
                return false;
            }

            title = titleBuilder.ToString();
            position++;
        }

        while (position < end && text[position] == ' ')
        {
            position++;
        }

        if (position >= end || text[position] != ')')
        {
            return false;
        }

        next = position + 1;
        return destination.Length > 0 || title is not null;
    }

    private static int FindClosingBracket(string text, int open, int end)
    {
        var depth = 0;
        var position = open;

        while (position < end)
        {
            var c = text[position];

            if (c == '\\')
            {
                position += 2;
                continue;
            }

            if (c == '`')
            {
                var run = RunLength(text, position, end, '`');
                var close = FindBacktickRun(text, position + run, end, run);
                position = close < 0 ? position + run : close + run;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;

                if (depth == 0)
                {
                    return position;
                }
            }

            position++;
        }

        return -1;
    }

    private static int RunLength(string text, int position, int end, char character)
    {
        var run = 0;

        while (position + run < end && text[position + run] == character)
        {
            run++;
        }

        return run;
    }

    private static int FindBacktickRun(string text, int start, int end, int length)
    {
        var position = start;

        while (position < end)
        {
            if (text[position] != '`')
            {
                position++;
                continue;
            }

            var run = RunLength(text, position, end, '`');

            if (run == length)
            {
                return position;
            }

            position += run;
        }

        return -1;
    }

    private static int TrailingSpaces(StringBuilder buffer)
    {
        var count = 0;

        for (var index = buffer.Length - 1; index >= 0 && buffer[index] == ' '; index--)
        {
            count++;
        }

        return count;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n';

    private static bool IsEscapable(char c)
        => c is >= '!' and <= '/' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';
}
