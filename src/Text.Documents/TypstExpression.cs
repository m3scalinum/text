using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A Typst expression: a value the document can bind, pass or display. Expressions are emitted
/// verbatim, so they are the typed door to the parts of the standard library that are not
/// document-shaped.
/// </summary>
public sealed class TypstExpression
{
    private readonly string _code;

    private TypstExpression(string code) => _code = code;

    /// <summary>An expression written by hand.</summary>
    public static TypstExpression Raw(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new TypstExpression(code);
    }

    /// <summary><c>none</c>.</summary>
    public static TypstExpression None => new("none");

    /// <summary><c>auto</c>.</summary>
    public static TypstExpression Auto => new("auto");

    /// <summary>A boolean.</summary>
    public static TypstExpression Bool(bool value) => new(value ? "true" : "false");

    /// <summary>An integer.</summary>
    public static TypstExpression Number(long value)
        => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A float.</summary>
    public static TypstExpression Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "a number must be finite");
        }

        return new TypstExpression(value.ToString("0.#####", CultureInfo.InvariantCulture));
    }

    /// <summary>A string, quoted and escaped.</summary>
    public static TypstExpression Str(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new TypstExpression(LinkNode.Quote(value));
    }

    /// <summary>An array. A single element keeps its trailing comma, as Typst requires.</summary>
    public static TypstExpression Array(params TypstExpression[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Length switch
        {
            0 => new TypstExpression("()"),
            1 => new TypstExpression("(" + items[0].ToTypst() + ",)"),
            _ => new TypstExpression("(" + string.Join(", ", items.Select(item => item.ToTypst())) + ")"),
        };
    }

    /// <summary>A dictionary.</summary>
    public static TypstExpression Dictionary(params (string Key, TypstExpression Value)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var parts = new List<string>(entries.Length);
        foreach (var (key, value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            parts.Add($"{key}: {value.ToTypst()}");
        }

        return new TypstExpression("(" + string.Join(", ", parts) + ")");
    }

    /// <summary>A symbol from the <c>sym</c> module, for example <c>arrow.r</c>.</summary>
    public static TypstExpression Symbol(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstExpression("sym." + name);
    }

    /// <summary>An emoji from the <c>emoji</c> module, for example <c>face</c>.</summary>
    public static TypstExpression Emoji(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstExpression("emoji." + name);
    }

    /// <summary>The <c>arguments</c> value, available inside a function body.</summary>
    public static TypstExpression Arguments => new("arguments");

    /// <summary>A function value, for example <c>(x) =&gt; x + 1</c>.</summary>
    public static TypstExpression Function(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new TypstExpression($"({parameters}) => {body}");
    }

    /// <summary>A member of a module, for example <c>m.greet</c>.</summary>
    public static TypstExpression Member(string module, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstExpression($"{module}.{name}");
    }

    /// <summary>A call into the <c>calc</c> module, for example <c>pow(2, 8)</c>.</summary>
    public static TypstExpression Calc(string call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(call);
        return new TypstExpression("calc." + call);
    }

    /// <summary>Evaluates Typst code from a string.</summary>
    public static TypstExpression Eval(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new TypstExpression($"eval({LinkNode.Quote(code)})");
    }

    /// <summary>The textual representation of a value.</summary>
    public static TypstExpression Repr(TypstExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new TypstExpression($"repr({value.ToTypst()})");
    }

    /// <summary>The type of a value.</summary>
    public static TypstExpression TypeOf(TypstExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new TypstExpression($"type({value.ToTypst()})");
    }

    /// <summary>A version number.</summary>
    public static TypstExpression Version(int major, int minor, int patch)
        => new($"version({major}, {minor}, {patch})");

    /// <summary>A regular expression.</summary>
    public static TypstExpression Regex(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new TypstExpression($"regex({LinkNode.Quote(pattern)})");
    }

    /// <summary>A selector over an element, for example <c>heading</c> or <c>figure</c>.</summary>
    public static TypstExpression Selector(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return new TypstExpression($"selector({target})");
    }

    /// <summary>A duration, built from named components.</summary>
    public static TypstExpression Duration(
        long seconds = 0,
        long minutes = 0,
        long hours = 0,
        long days = 0,
        long weeks = 0)
    {
        var parts = new List<string>();

        if (seconds != 0)
        {
            parts.Add($"seconds: {seconds.ToString(CultureInfo.InvariantCulture)}");
        }

        if (minutes != 0)
        {
            parts.Add($"minutes: {minutes.ToString(CultureInfo.InvariantCulture)}");
        }

        if (hours != 0)
        {
            parts.Add($"hours: {hours.ToString(CultureInfo.InvariantCulture)}");
        }

        if (days != 0)
        {
            parts.Add($"days: {days.ToString(CultureInfo.InvariantCulture)}");
        }

        if (weeks != 0)
        {
            parts.Add($"weeks: {weeks.ToString(CultureInfo.InvariantCulture)}");
        }

        return new TypstExpression("duration(" + string.Join(", ", parts) + ")");
    }

    /// <summary>The bytes of a string, as a value.</summary>
    public static TypstExpression Bytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TypstExpression($"bytes({LinkNode.Quote(text)})");
    }

    /// <summary>A path value.</summary>
    public static TypstExpression Path(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TypstExpression($"path({LinkNode.Quote(path)})");
    }

    /// <summary>Aborts compilation with a message.</summary>
    public static TypstExpression Panic(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new TypstExpression($"panic({LinkNode.Quote(message)})");
    }

    /// <summary>Fails compilation when the condition is false.</summary>
    public static TypstExpression Assert(TypstExpression condition, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return message is null
            ? new TypstExpression($"assert({condition.ToTypst()})")
            : new TypstExpression($"assert({condition.ToTypst()}, message: {LinkNode.Quote(message)})");
    }

    /// <summary>A member of the <c>std</c> module.</summary>
    public static TypstExpression Std(string member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return new TypstExpression("std." + member);
    }

    /// <summary>A member of the <c>sys</c> module.</summary>
    public static TypstExpression Sys(string member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return new TypstExpression("sys." + member);
    }

    /// <summary>The compilation target.</summary>
    public static TypstExpression Target => new("target");

    /// <summary>A label, as a value.</summary>
    public static TypstExpression Label(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TypstExpression("<" + name + ">");
    }

    /// <summary>Content, as a value.</summary>
    public static TypstExpression Content(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var writer = new TypstMarkupWriter();

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }

        return new TypstExpression(writer.ToMarkup().Trim());
    }

    /// <summary>Emits the Typst code for this expression.</summary>
    public string ToTypst() => _code;

    /// <inheritdoc />
    public override string ToString() => _code;
}
