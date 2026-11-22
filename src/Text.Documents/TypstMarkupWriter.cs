using System;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Emits Typst markup with the escaping rules of Spec 04 §5, and enforces the invariants that
/// make the output reviewable: no double blank lines, no trailing whitespace, two-space
/// indentation, one trailing newline.
/// </summary>
internal sealed class TypstMarkupWriter
{
    private readonly StringBuilder _out = new();
    private int _indent;
    private int _contentDepth;
    private bool _atLineStart = true;

    /// <summary>Every label this document emitted, in document order, deduplicated as it goes.</summary>
    public TypstLabelRegistry Labels { get; } = new();

    /// <summary>Writes text, escaping every character that is special in markup mode.</summary>
    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];

            if (c == '\n')
            {
                NewLine();
                continue;
            }

            if (_atLineStart && c == ' ')
            {
                _out.Append(' ');
                continue;
            }

            if (_atLineStart && char.IsAsciiDigit(c))
            {
                // `1.` at the start of a line is an enumerated list marker, like `+`.
                var end = index;
                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }

                if (end < text.Length
                    && text[end] == '.'
                    && (end + 1 == text.Length || text[end + 1] is ' ' or '\t'))
                {
                    _out.Append(text, index, end - index).Append("\\.");
                    index = end;
                    _atLineStart = false;
                    continue;
                }
            }

            if (_atLineStart && c is '-' or '+' or '/' or '=')
            {
                _out.Append('\\').Append(c);
                _atLineStart = false;
                continue;
            }

            if (c == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
            {
                _out.Append("\\/");
                _atLineStart = false;
                continue;
            }

            if (_contentDepth > 0 && c is '[' or ']')
            {
                _out.Append('\\').Append(c);
                _atLineStart = false;
                continue;
            }

            if (c is '\\' or '#' or '$' or '*' or '_' or '`' or '<' or '>' or '@' or '~')
            {
                _out.Append('\\').Append(c);
                _atLineStart = false;
                continue;
            }

            _out.Append(c);
            _atLineStart = false;
        }
    }

    /// <summary>Writes text without escaping. The caller is writing Typst now.</summary>
    public void WriteVerbatim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _out.Append(text);
        _atLineStart = text.EndsWith('\n');
    }

    /// <summary>Starts a new line, applying the current indentation.</summary>
    public void NewLine()
    {
        _out.Append('\n');
        _atLineStart = true;

        if (_indent > 0)
        {
            _out.Append(' ', _indent * 2);
        }
    }

    /// <summary>Ensures exactly one blank line separates the next block from the previous one.</summary>
    public void BlankLine()
    {
        if (_out.Length == 0)
        {
            return;
        }

        if (!_out.ToString().EndsWith('\n'))
        {
            _out.Append('\n');
        }

        if (!_out.ToString().EndsWith("\n\n", StringComparison.Ordinal))
        {
            _out.Append('\n');
        }

        _atLineStart = true;

        if (_indent > 0)
        {
            _out.Append(' ', _indent * 2);
        }
    }

    /// <summary>Opens a content block: <c>[</c> … <c>]</c>, with brackets escaped inside.</summary>
    public IDisposable ContentBlock()
    {
        _out.Append('[');
        _contentDepth++;
        _atLineStart = false;
        return new Scope(() =>
        {
            _contentDepth--;
            _out.Append(']');
        });
    }

    /// <summary>Indents every line written until the scope is disposed.</summary>
    public IDisposable Indent()
    {
        _indent++;
        return new Scope(() => _indent--);
    }

    /// <summary>Returns the markup, with trailing whitespace removed and one final newline.</summary>
    public string ToMarkup()
    {
        var lines = _out.ToString().Split('\n');
        var builder = new StringBuilder(_out.Length);

        foreach (var line in lines)
        {
            builder.Append(line.TrimEnd()).Append('\n');
        }

        var text = builder.ToString().TrimEnd('\n');
        return text.Length == 0 ? string.Empty : text + "\n";
    }

    private sealed class Scope : IDisposable
    {
        private Action? _onDispose;

        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
