using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>Raised when a BibTeX source cannot be read.</summary>
/// <remarks>
/// The parser is deliberately small, so it refuses what it cannot read instead of guessing: a
/// <c>@string</c> macro, a <c>#</c> concatenation, a bare macro reference in a field and an
/// unterminated entry all raise this exception with the offset of the failure.
/// </remarks>
public sealed class TypstBibTexParseException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public TypstBibTexParseException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public TypstBibTexParseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public TypstBibTexParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The zero-based offset of the failure in the source, or -1 when it is not tied to one.</summary>
    public int Position { get; init; } = -1;
}

/// <summary>
/// Reads the BibTeX subset a citation needs: <c>@article</c>, <c>@book</c> and
/// <c>@inproceedings</c>, brace- or quote-delimited values, nested braces, and one entry per key.
/// </summary>
/// <remarks>
/// <para>
/// The parser reads a value as a single atom: <c>{…}</c>, <c>"…"</c> or a bare number. Braces are
/// nesting-aware and removed from the stored text; whitespace inside a value collapses to one
/// space. A <c>@comment</c> or <c>@preamble</c> block is skipped, and text outside an entry — a
/// header line, a stray <c>%</c> comment — is ignored.
/// </para>
/// <para>
/// It does not read <c>@string</c> macros, <c>#</c> concatenation, a bare macro name in a field, a
/// <c>crossref</c> that merges another entry, or the LaTeX commands inside a value: those throw
/// rather than being dropped. A repeated field keeps its last value.
/// </para>
/// </remarks>
public static class TypstBibTexParser
{
    /// <summary>Reads every entry of a BibTeX source, in file order.</summary>
    /// <param name="bibtex">The contents of a <c>.bib</c> file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bibtex"/> is null.</exception>
    /// <exception cref="TypstBibTexParseException">The source is malformed, or uses a construct the subset does not read.</exception>
    public static IReadOnlyList<TypstBibTexEntry> Parse(string bibtex)
    {
        ArgumentNullException.ThrowIfNull(bibtex);

        var entries = new List<TypstBibTexEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        while (true)
        {
            var at = bibtex.IndexOf('@', index);

            if (at < 0)
            {
                break;
            }

            index = at + 1;
            var typeStart = index;

            while (index < bibtex.Length && (char.IsAsciiLetterOrDigit(bibtex[index]) || bibtex[index] == '_'))
            {
                index++;
            }

            if (index == typeStart)
            {
                throw Error(bibtex, index, "expected an entry type after '@'");
            }

            var type = bibtex[typeStart..index].ToLowerInvariant();
            SkipWhitespace(bibtex, ref index);

            if (index >= bibtex.Length)
            {
                throw Error(bibtex, at, $"the @{type} entry is not closed");
            }

            var open = bibtex[index];

            if (open is not ('{' or '('))
            {
                throw Error(bibtex, index, $"expected '{{' or '(' after '@{type}'");
            }

            var close = open == '{' ? '}' : ')';
            index++;
            SkipWhitespace(bibtex, ref index);

            if (type is "comment" or "preamble")
            {
                index = SkipBody(bibtex, index, open, close);
                continue;
            }

            if (type == "string")
            {
                throw Error(
                    bibtex,
                    at,
                    "@string macros are not supported; write the value into each entry instead");
            }

            var keyStart = index;

            while (index < bibtex.Length && bibtex[index] != ',' && bibtex[index] != close)
            {
                index++;
            }

            if (index >= bibtex.Length)
            {
                throw Error(bibtex, keyStart, $"the @{type} entry is not closed");
            }

            var key = bibtex[keyStart..index].Trim();

            if (key.Length == 0)
            {
                throw Error(bibtex, keyStart, $"the @{type} entry has no citation key");
            }

            if (!seen.Add(key))
            {
                throw Error(bibtex, keyStart, $"the citation key '{key}' is defined twice");
            }

            if (bibtex[index] == close)
            {
                index++;
                entries.Add(new TypstBibTexEntry(key, type, new Dictionary<string, string>(StringComparer.Ordinal)));
                continue;
            }

            index++;
            entries.Add(new TypstBibTexEntry(key, type, ReadFields(bibtex, ref index, close, key)));
        }

        return entries;
    }

    /// <summary>Splits an <c>author</c> field on <c>and</c> and each part into a family and given name.</summary>
    /// <param name="value">The raw field value, or null.</param>
    internal static IReadOnlyList<TypstBibAuthor> SplitAuthors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var authors = new List<TypstBibAuthor>();
        var start = 0;
        var index = 0;

        while (index < value.Length)
        {
            if (char.IsWhiteSpace(value[index]) && IsAndAt(value, index))
            {
                AddAuthor(authors, value[start..index]);
                index += 4;
                start = index;
                continue;
            }

            index++;
        }

        AddAuthor(authors, value[start..]);
        return authors;
    }

    private static void AddAuthor(List<TypstBibAuthor> authors, string part)
    {
        var trimmed = part.Trim();

        if (trimmed.Length > 0)
        {
            authors.Add(SplitName(trimmed));
        }
    }

    /// <summary>Reduces given names to initials: <c>Jean-Pierre</c> becomes <c>J. P.</c>.</summary>
    internal static string Initials(string given)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            return string.Empty;
        }

        var initials = new List<string>();

        foreach (var part in given.Split([' ', '-', '\u2010', '\u2011'], StringSplitOptions.RemoveEmptyEntries))
        {
            initials.Add(char.ToUpperInvariant(part[0]) + ".");
        }

        return string.Join(' ', initials);
    }

    private static bool IsAndAt(string value, int index)
    {
        if (index + 4 > value.Length
            || value[index + 1] is not ('a' or 'A')
            || value[index + 2] is not ('n' or 'N')
            || value[index + 3] is not ('d' or 'D'))
        {
            return false;
        }

        // `and` must be a whole word: what follows it is whitespace or the end of the value.
        var after = index + 4;
        return after == value.Length || char.IsWhiteSpace(value[after]);
    }

    private static TypstBibAuthor SplitName(string part)
    {
        var comma = part.IndexOf(',', StringComparison.Ordinal);

        if (comma > 0)
        {
            var family = part[..comma].Trim();
            var rest = part[(comma + 1)..];
            var secondComma = rest.IndexOf(',', StringComparison.Ordinal);

            // `Family, Jr, Given` keeps the given names of the third part and drops the suffix.
            var given = (secondComma >= 0 ? rest[(secondComma + 1)..] : rest).Trim();
            return new TypstBibAuthor(family, given);
        }

        var space = part.LastIndexOfAny([' ', '\t']);

        return space > 0
            ? new TypstBibAuthor(part[(space + 1)..].Trim(), part[..space].Trim())
            : new TypstBibAuthor(part.Trim(), string.Empty);
    }

    private static Dictionary<string, string> ReadFields(string bibtex, ref int index, char close, string key)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        while (true)
        {
            SkipWhitespace(bibtex, ref index);

            if (index >= bibtex.Length)
            {
                throw Error(bibtex, index, $"the entry '{key}' is not closed");
            }

            if (bibtex[index] == close)
            {
                index++;
                return fields;
            }

            var nameStart = index;

            while (index < bibtex.Length
                   && (char.IsAsciiLetterOrDigit(bibtex[index]) || bibtex[index] is '_' or '-'))
            {
                index++;
            }

            var name = bibtex[nameStart..index].ToLowerInvariant();

            if (name.Length == 0)
            {
                throw Error(bibtex, index, $"expected a field name in the entry '{key}'");
            }

            SkipWhitespace(bibtex, ref index);

            if (index >= bibtex.Length || bibtex[index] != '=')
            {
                throw Error(bibtex, index, $"expected '=' after the field '{name}' of '{key}'");
            }

            index++;
            SkipWhitespace(bibtex, ref index);
            fields[name] = ReadAtom(bibtex, ref index, key, name);
            SkipWhitespace(bibtex, ref index);

            if (index < bibtex.Length && bibtex[index] == '#')
            {
                throw Error(
                    bibtex,
                    index,
                    $"the field '{name}' of '{key}' concatenates with '#', which the subset does not read");
            }

            if (index < bibtex.Length && bibtex[index] == ',')
            {
                index++;
                continue;
            }

            if (index < bibtex.Length && bibtex[index] == close)
            {
                index++;
                return fields;
            }

            throw Error(bibtex, index, $"expected ',' or '{close}' after the field '{name}' of '{key}'");
        }
    }

    private static string ReadAtom(string bibtex, ref int index, string key, string field)
    {
        if (index >= bibtex.Length)
        {
            throw Error(bibtex, index, $"the field '{field}' of '{key}' has no value");
        }

        var start = index;
        var builder = new StringBuilder();
        var pendingSpace = false;

        void Append(char c)
        {
            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        var first = bibtex[index];

        if (first is '{' or '"')
        {
            var depth = 0;
            index++;

            while (index < bibtex.Length)
            {
                var c = bibtex[index++];

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    if (depth == 0)
                    {
                        if (first == '{')
                        {
                            return builder.ToString();
                        }

                        throw Error(bibtex, index - 1, $"the field '{field}' of '{key}' has a stray '}}'");
                    }

                    depth--;
                    continue;
                }

                if (c == '"' && first == '"' && depth == 0)
                {
                    return builder.ToString();
                }

                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    continue;
                }

                Append(c);
            }

            throw Error(bibtex, start, $"the field '{field}' of '{key}' has an unclosed {first}");
        }

        if (char.IsAsciiDigit(first))
        {
            while (index < bibtex.Length && (char.IsAsciiDigit(bibtex[index]) || bibtex[index] == '.'))
            {
                builder.Append(bibtex[index++]);
            }

            return builder.ToString();
        }

        if (char.IsAsciiLetter(first))
        {
            var end = index;

            while (end < bibtex.Length
                   && (char.IsAsciiLetterOrDigit(bibtex[end]) || bibtex[end] is '_' or '-'))
            {
                end++;
            }

            throw Error(
                bibtex,
                start,
                $"the field '{field}' of '{key}' references the macro '{bibtex[start..end]}'; @string macros are not supported");
        }

        throw Error(bibtex, start, $"the field '{field}' of '{key}' has an unexpected '{first}'");
    }

    private static int SkipBody(string bibtex, int index, char open, char close)
    {
        var depth = 1;

        while (index < bibtex.Length && depth > 0)
        {
            var c = bibtex[index++];

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
            }
        }

        return index;
    }

    private static void SkipWhitespace(string bibtex, ref int index)
    {
        while (index < bibtex.Length && char.IsWhiteSpace(bibtex[index]))
        {
            index++;
        }
    }

    private static TypstBibTexParseException Error(string bibtex, int index, string message)
        => new($"{message} (offset {index}).")
        {
            Position = index,
        };
}
