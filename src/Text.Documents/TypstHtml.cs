using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>HTML-specific functions. They only take effect when exporting to HTML.</summary>
public static class TypstHtml
{
    /// <summary>Emits an HTML element with the given tag.</summary>
    public static TypstExpression Elem(string tag, TypstContent? body = null, params (string Name, string Value)[] attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(attributes);

        var arguments = new List<string> { LinkNode.Quote(tag) };

        if (attributes.Length > 0)
        {
            var pairs = new List<string>(attributes.Length);

            foreach (var (name, value) in attributes)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentNullException.ThrowIfNull(value);
                pairs.Add($"{name}: {LinkNode.Quote(value)}");
            }

            arguments.Add("attrs: (" + string.Join(", ", pairs) + ")");
        }

        if (body is not null)
        {
            return TypstExpression.Raw(
                "html.elem(" + string.Join(", ", arguments) + ")" + TypstExpression.Content(body).ToTypst());
        }

        return TypstExpression.Raw("html.elem(" + string.Join(", ", arguments) + ")");
    }

    /// <summary>Wraps content in an HTML frame.</summary>
    public static TypstExpression Frame(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return TypstExpression.Raw("html.frame" + TypstExpression.Content(content).ToTypst());
    }

    /// <summary>Emits a typed HTML element constructor, for example <c>html.p</c>.</summary>
    public static TypstExpression Typed(string tag, TypstContent? body = null, params (string Name, string Value)[] attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(attributes);

        var arguments = new List<string>();

        if (attributes.Length > 0)
        {
            var pairs = new List<string>(attributes.Length);

            foreach (var (name, value) in attributes)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArgumentNullException.ThrowIfNull(value);
                pairs.Add($"{name}: {LinkNode.Quote(value)}");
            }

            arguments.Add("attrs: (" + string.Join(", ", pairs) + ")");
        }

        var call = arguments.Count == 0
            ? "html." + tag
            : "html." + tag + "(" + string.Join(", ", arguments) + ")";

        return body is null
            ? TypstExpression.Raw(call)
            : TypstExpression.Raw(call + TypstExpression.Content(body).ToTypst());
    }
}