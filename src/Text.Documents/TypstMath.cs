using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Documents;

/// <summary>The math functions, for use inside a <c>$ … $</c> expression.</summary>
public static class TypstMath
{
    /// <summary>A fraction.</summary>
    public static TypstExpression Frac(string numerator, string denominator)
        => Call("frac", numerator, denominator);

    /// <summary>A square root.</summary>
    public static TypstExpression Sqrt(string radicand) => Call("sqrt", radicand);

    /// <summary>An n-th root.</summary>
    public static TypstExpression Root(string radicand, string index)
        => Call("root", radicand, index);

    /// <summary>A binomial coefficient.</summary>
    public static TypstExpression Binom(string n, string k) => Call("binom", n, k);

    /// <summary>A column vector.</summary>
    public static TypstExpression Vec(params string[] rows) => Call("vec", rows);

    /// <summary>A matrix, one array per row.</summary>
    public static TypstExpression Mat(params string[][] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var body = string.Join(
            "; ",
            rows.Select(row =>
            {
                ArgumentNullException.ThrowIfNull(row);
                return string.Join(", ", row);
            }));

        return TypstExpression.Raw($"mat({body})");
    }

    /// <summary>A piecewise definition.</summary>
    public static TypstExpression Cases(params string[] branches) => Call("cases", branches);

    /// <summary>Scaled delimiters around an expression.</summary>
    public static TypstExpression Lr(string content) => Call("lr", content);

    /// <summary>An accent over a base.</summary>
    public static TypstExpression Accent(string baseExpression, string accent)
        => Call("accent", baseExpression, accent);

    /// <summary>Attachments above, below and beside a base.</summary>
    public static TypstExpression Attach(
        string baseExpression,
        string? top = null,
        string? bottom = null,
        string? topLeft = null,
        string? bottomLeft = null,
        string? topRight = null,
        string? bottomRight = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseExpression);

        var arguments = new List<string> { baseExpression };
        Add(arguments, "t", top);
        Add(arguments, "b", bottom);
        Add(arguments, "tl", topLeft);
        Add(arguments, "bl", bottomLeft);
        Add(arguments, "tr", topRight);
        Add(arguments, "br", bottomRight);

        return TypstExpression.Raw("attach(" + string.Join(", ", arguments) + ")");
    }

    /// <summary>A text operator, for example <c>lim</c>.</summary>
    public static TypstExpression Op(string text, bool limits = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return limits
            ? TypstExpression.Raw($"op({LinkNode.Quote(text)}, limits: #true)")
            : TypstExpression.Raw($"op({LinkNode.Quote(text)})");
    }

    /// <summary>Forces a math class on content.</summary>
    public static TypstExpression Class(string mathClass, string body)
    {
        if (mathClass is not ("normal" or "punctuation" or "opening" or "closing" or "fence"
            or "large" or "relation" or "unary" or "binary" or "vary"))
        {
            throw new ArgumentException($"'{mathClass}' is not a math class", nameof(mathClass));
        }

        return TypstExpression.Raw($"class({LinkNode.Quote(mathClass)}, {body})");
    }

    /// <summary>Strikes through an expression.</summary>
    public static TypstExpression Cancel(string body, string? cross = null)
        => cross is null ? Call("cancel", body) : Call("cancel", body, cross);

    /// <summary>Grouped primes.</summary>
    public static TypstExpression Primes(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return TypstExpression.Raw($"primes({count.ToString(CultureInfo.InvariantCulture)})");
    }

    /// <summary>Stretches a glyph.</summary>
    public static TypstExpression Stretch(string body) => Call("stretch", body);

    /// <summary>Forced display size.</summary>
    public static TypstExpression Display(string body, bool cramped = false)
        => Sized("display", body, cramped);

    /// <summary>Forced inline size.</summary>
    public static TypstExpression Inline(string body, bool cramped = false)
        => Sized("inline", body, cramped);

    /// <summary>Forced script size.</summary>
    public static TypstExpression Script(string body, bool cramped = true)
        => Sized("script", body, cramped);

    /// <summary>Forced second script size.</summary>
    public static TypstExpression Sscript(string body, bool cramped = true)
        => Sized("sscript", body, cramped);

    /// <summary>Upright letterforms.</summary>
    public static TypstExpression Upright(string body) => Call("upright", body);

    /// <summary>Italic letterforms.</summary>
    public static TypstExpression Italic(string body) => Call("italic", body);

    /// <summary>Bold letterforms.</summary>
    public static TypstExpression Bold(string body) => Call("bold", body);

    /// <summary>Serif typeface.</summary>
    public static TypstExpression Serif(string body) => Call("serif", body);

    /// <summary>Sans-serif typeface.</summary>
    public static TypstExpression Sans(string body) => Call("sans", body);

    /// <summary>Fraktur typeface.</summary>
    public static TypstExpression Frak(string body) => Call("frak", body);

    /// <summary>Monospace typeface.</summary>
    public static TypstExpression Mono(string body) => Call("mono", body);

    /// <summary>Blackboard bold typeface.</summary>
    public static TypstExpression Bb(string body) => Call("bb", body);

    /// <summary>Calligraphic typeface.</summary>
    public static TypstExpression Cal(string body) => Call("cal", body);

    /// <summary>Script (roundhand) typeface.</summary>
    public static TypstExpression Scr(string body) => Call("scr", body);

    /// <summary>A line under content.</summary>
    public static TypstExpression Underline(string body) => Call("underline", body);

    /// <summary>A line over content.</summary>
    public static TypstExpression Overline(string body) => Call("overline", body);

    /// <summary>A brace under content, with an optional annotation.</summary>
    public static TypstExpression Underbrace(string body, string? annotation = null)
        => Annotated("underbrace", body, annotation);

    /// <summary>A brace over content, with an optional annotation.</summary>
    public static TypstExpression Overbrace(string body, string? annotation = null)
        => Annotated("overbrace", body, annotation);

    /// <summary>A bracket under content, with an optional annotation.</summary>
    public static TypstExpression Underbracket(string body, string? annotation = null)
        => Annotated("underbracket", body, annotation);

    /// <summary>A bracket over content, with an optional annotation.</summary>
    public static TypstExpression Overbracket(string body, string? annotation = null)
        => Annotated("overbracket", body, annotation);

    /// <summary>A parenthesis under content, with an optional annotation.</summary>
    public static TypstExpression Underparen(string body, string? annotation = null)
        => Annotated("underparen", body, annotation);

    /// <summary>A parenthesis over content, with an optional annotation.</summary>
    public static TypstExpression Overparen(string body, string? annotation = null)
        => Annotated("overparen", body, annotation);

    /// <summary>A tortoise shell bracket under content, with an optional annotation.</summary>
    public static TypstExpression Undershell(string body, string? annotation = null)
        => Annotated("undershell", body, annotation);

    /// <summary>A tortoise shell bracket over content, with an optional annotation.</summary>
    public static TypstExpression Overshell(string body, string? annotation = null)
        => Annotated("overshell", body, annotation);

    private static TypstExpression Sized(string function, string body, bool cramped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return cramped
            ? TypstExpression.Raw($"{function}({body}, cramped: true)")
            : TypstExpression.Raw($"{function}({body})");
    }

    private static TypstExpression Annotated(string function, string body, string? annotation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return annotation is null
            ? TypstExpression.Raw($"{function}({body})")
            : TypstExpression.Raw($"{function}({body}, {annotation})");
    }

    private static TypstExpression Call(string function, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
        }

        return TypstExpression.Raw($"{function}({string.Join(", ", arguments)})");
    }

    private static void Add(List<string> arguments, string name, string? value)
    {
        if (value is not null)
        {
            arguments.Add($"{name}: {value}");
        }
    }
}
