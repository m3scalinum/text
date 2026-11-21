using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

internal enum HtmlTokenKind
{
    Text,
    StartTag,
    EndTag,
}

internal readonly struct HtmlAttribute(string name, string value)
{
    public string Name { get; } = name;

    public string Value { get; } = value;
}

internal readonly struct HtmlToken
{
    private HtmlToken(HtmlTokenKind kind, string name, string text, HtmlAttribute[] attributes, bool selfClosing)
    {
        Kind = kind;
        Name = name;
        Text = text;
        Attributes = attributes;
        SelfClosing = selfClosing;
    }

    public HtmlTokenKind Kind { get; }

    /// <summary>The lower-cased tag name; empty for text.</summary>
    public string Name { get; }

    /// <summary>The decoded text; empty for tags.</summary>
    public string Text { get; }

    public HtmlAttribute[] Attributes { get; }

    public bool SelfClosing { get; }

    public static HtmlToken ForText(string text) => new(HtmlTokenKind.Text, string.Empty, text, [], false);

    public static HtmlToken ForStart(string name, HtmlAttribute[] attributes, bool selfClosing)
        => new(HtmlTokenKind.StartTag, name, string.Empty, attributes, selfClosing);

    public static HtmlToken ForEnd(string name)
        => new(HtmlTokenKind.EndTag, name, string.Empty, [], false);
}

/// <summary>
/// A single-pass, allocation-conscious HTML tokenizer. It is not a conforming HTML parser: it
/// knows tags, attributes, comments, raw-text elements and character references, and it leaves the
/// tree repair to <see cref="HtmlTreeBuilder"/>.
/// </summary>
internal sealed class HtmlTokenizer
{
    private readonly string _html;
    private int _index;
    private string? _rawTextElement;

    public HtmlTokenizer(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        _html = html;
    }

    /// <summary>Reads the next token, or returns <c>false</c> at the end of the input.</summary>
    public bool TryRead(out HtmlToken token)
    {
        token = default;

        SkipRawText();

        while (_index < _html.Length)
        {
            var next = _html[_index];

            if (next != '<')
            {
                var end = _html.IndexOf('<', _index);
                end = end < 0 ? _html.Length : end;
                token = HtmlToken.ForText(HtmlEntities.Decode(_html.AsSpan(_index, end - _index)));
                _index = end;
                return true;
            }

            if (StartsWith("<!--"))
            {
                SkipComment();
                continue;
            }

            if (StartsWith("<![CDATA["))
            {
                var end = _html.IndexOf("]]>", _index + 9, StringComparison.Ordinal);
                var textEnd = end < 0 ? _html.Length : end;
                token = HtmlToken.ForText(_html[(_index + 9)..textEnd]);
                _index = end < 0 ? _html.Length : end + 3;
                return true;
            }

            if (StartsWith("<!"))
            {
                SkipTo('>');
                continue;
            }

            if (StartsWith("</"))
            {
                if (TryReadEndTag(out token))
                {
                    return true;
                }

                _index += 2;
                continue;
            }

            if (_index + 1 < _html.Length && IsNameStart(_html[_index + 1]))
            {
                token = ReadStartTag();
                RememberRawTextElement(token);
                return true;
            }

            // A lone `<` is text.
            _index++;
            token = HtmlToken.ForText("<");
            return true;
        }

        return false;
    }

    private void SkipRawText()
    {
        if (_rawTextElement is null)
        {
            return;
        }

        var closer = "</" + _rawTextElement;
        var index = _html.IndexOf(closer, _index, StringComparison.OrdinalIgnoreCase);
        _index = index < 0 ? _html.Length : index;
        _rawTextElement = null;
    }

    private void RememberRawTextElement(HtmlToken token)
    {
        if (token.Name is "script" or "style" or "textarea")
        {
            _rawTextElement = token.Name;
            SkipRawText();
        }
    }

    private HtmlToken ReadStartTag()
    {
        _index++;
        var nameStart = _index;

        while (_index < _html.Length && IsNameChar(_html[_index]))
        {
            _index++;
        }

        var name = _html[nameStart.._index].ToLowerInvariant();
        List<HtmlAttribute>? attributes = null;

        while (_index < _html.Length)
        {
            SkipWhitespace();

            if (_index >= _html.Length)
            {
                break;
            }

            var c = _html[_index];

            if (c == '>')
            {
                _index++;
                return HtmlToken.ForStart(name, attributes?.ToArray() ?? [], selfClosing: false);
            }

            if (c == '/')
            {
                if (_index + 1 < _html.Length && _html[_index + 1] == '>')
                {
                    _index += 2;
                    return HtmlToken.ForStart(name, attributes?.ToArray() ?? [], selfClosing: true);
                }

                _index++;
                continue;
            }

            var attribute = ReadAttribute();

            if (attribute is { } value)
            {
                (attributes ??= []).Add(value);
            }
        }

        return HtmlToken.ForStart(name, attributes?.ToArray() ?? [], selfClosing: false);
    }

    private HtmlAttribute? ReadAttribute()
    {
        var nameStart = _index;

        while (_index < _html.Length)
        {
            var c = _html[_index];

            if (char.IsWhiteSpace(c) || c is '=' or '>' or '/')
            {
                break;
            }

            _index++;
        }

        if (_index == nameStart)
        {
            _index++;
            return null;
        }

        var name = _html[nameStart.._index].ToLowerInvariant();
        SkipWhitespace();

        if (_index >= _html.Length || _html[_index] != '=')
        {
            return new HtmlAttribute(name, string.Empty);
        }

        _index++;
        SkipWhitespace();

        if (_index >= _html.Length)
        {
            return new HtmlAttribute(name, string.Empty);
        }

        var quote = _html[_index];

        if (quote is '"' or '\'')
        {
            _index++;
            var valueStart = _index;

            while (_index < _html.Length && _html[_index] != quote)
            {
                _index++;
            }

            var value = HtmlEntities.Decode(_html.AsSpan(valueStart, _index - valueStart));

            if (_index < _html.Length)
            {
                _index++;
            }

            return new HtmlAttribute(name, value);
        }

        var plainStart = _index;

        while (_index < _html.Length && !char.IsWhiteSpace(_html[_index]) && _html[_index] != '>')
        {
            _index++;
        }

        return new HtmlAttribute(name, HtmlEntities.Decode(_html.AsSpan(plainStart, _index - plainStart)));
    }

    private bool TryReadEndTag(out HtmlToken token)
    {
        token = default;
        var index = _index + 2;
        var nameStart = index;

        while (index < _html.Length && IsNameChar(_html[index]))
        {
            index++;
        }

        if (index == nameStart)
        {
            return false;
        }

        token = HtmlToken.ForEnd(_html[nameStart..index].ToLowerInvariant());

        var close = _html.IndexOf('>', index);
        _index = close < 0 ? _html.Length : close + 1;
        return true;
    }

    private void SkipComment()
    {
        var end = _html.IndexOf("-->", _index + 4, StringComparison.Ordinal);
        _index = end < 0 ? _html.Length : end + 3;
    }

    private void SkipTo(char value)
    {
        var end = _html.IndexOf(value, _index);
        _index = end < 0 ? _html.Length : end + 1;
    }

    private void SkipWhitespace()
    {
        while (_index < _html.Length && char.IsWhiteSpace(_html[_index]))
        {
            _index++;
        }
    }

    private bool StartsWith(string value)
        => _html.AsSpan(_index).StartsWith(value, StringComparison.Ordinal);

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c);

    private static bool IsNameChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.';
}
