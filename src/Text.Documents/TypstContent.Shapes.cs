using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>Shapes and text decorations, as content.</summary>
public sealed partial class TypstContent
{
    /// <summary>Appends uppercase text.</summary>
    public TypstContent Upper(string text) => Wrap("upper", From(text));

    /// <summary>Appends lowercase text.</summary>
    public TypstContent Lower(string text) => Wrap("lower", From(text));

    /// <summary>Appends small-caps text.</summary>
    public TypstContent SmallCaps(string text) => Wrap("smallcaps", From(text));

    /// <summary>Appends highlighted text.</summary>
    public TypstContent Highlight(string text) => Wrap("highlight", From(text));

    /// <summary>Appends underlined text.</summary>
    public TypstContent Underline(string text) => Underline(From(text));

    /// <summary>Appends underlined content.</summary>
    public TypstContent Underline(TypstContent content) => Wrap("underline", content);

    /// <summary>Appends overlined text.</summary>
    public TypstContent Overline(string text) => Wrap("overline", From(text));

    /// <summary>Appends struck-through text.</summary>
    public TypstContent Strikethrough(string text) => Strikethrough(From(text));

    /// <summary>Appends struck-through content.</summary>
    public TypstContent Strikethrough(TypstContent content) => Wrap("strike", content);

    /// <summary>Appends subscript text.</summary>
    public TypstContent Subscript(string text) => Subscript(From(text));

    /// <summary>Appends subscript content.</summary>
    public TypstContent Subscript(TypstContent content) => Wrap("sub", content);

    /// <summary>Appends superscript text.</summary>
    public TypstContent Superscript(string text) => Superscript(From(text));

    /// <summary>Appends superscript content.</summary>
    public TypstContent Superscript(TypstContent content) => Wrap("super", content);

    /// <summary>Appends filler text.</summary>
    public TypstContent Lorem(int words = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        _nodes.Add(new LoremNode(words));
        return this;
    }

    /// <summary>Appends a footnote.</summary>
    public TypstContent Footnote(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new FootnoteNode(content));
        return this;
    }

    /// <summary>Appends a citation.</summary>
    public TypstContent Cite(string key, bool prose = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _nodes.Add(new CiteNode(key, prose));
        return this;
    }

    /// <summary>Appends a metadata entry, readable by <c>query</c> from Typst code.</summary>
    public TypstContent Metadata(string key, string value, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _nodes.Add(new MetadataNode(key, value, label));
        return this;
    }

    /// <summary>Appends a line.</summary>
    public TypstContent Line(TypstLength? length = null, TypstAngle? angle = null, TypstStroke? stroke = null)
    {
        var arguments = new List<string>();
        Add(arguments, "length", length?.ToString());
        Add(arguments, "angle", angle?.ToString());
        Add(arguments, "stroke", stroke?.ToTypst());
        _nodes.Add(new RawNode("#line(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends a rectangle.</summary>
    public TypstContent Rect(
        TypstLength width,
        TypstLength height,
        ITypstPaint? fill = null,
        TypstStroke? stroke = null,
        TypstLength? radius = null)
    {
        var arguments = new List<string> { $"width: {width}", $"height: {height}" };
        Add(arguments, "fill", fill?.ToTypst());
        Add(arguments, "stroke", stroke?.ToTypst());
        Add(arguments, "radius", radius?.ToString());
        _nodes.Add(new RawNode("#rect(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends a square.</summary>
    public TypstContent Square(
        TypstLength size,
        ITypstPaint? fill = null,
        TypstStroke? stroke = null,
        TypstLength? radius = null)
    {
        var arguments = new List<string> { $"size: {size}" };
        Add(arguments, "fill", fill?.ToTypst());
        Add(arguments, "stroke", stroke?.ToTypst());
        Add(arguments, "radius", radius?.ToString());
        _nodes.Add(new RawNode("#square(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends a circle.</summary>
    public TypstContent Circle(TypstLength radius, ITypstPaint? fill = null, TypstStroke? stroke = null)
    {
        var arguments = new List<string> { $"radius: {radius}" };
        Add(arguments, "fill", fill?.ToTypst());
        Add(arguments, "stroke", stroke?.ToTypst());
        _nodes.Add(new RawNode("#circle(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends an ellipse.</summary>
    public TypstContent Ellipse(
        TypstLength width,
        TypstLength height,
        ITypstPaint? fill = null,
        TypstStroke? stroke = null)
    {
        var arguments = new List<string> { $"width: {width}", $"height: {height}" };
        Add(arguments, "fill", fill?.ToTypst());
        Add(arguments, "stroke", stroke?.ToTypst());
        _nodes.Add(new RawNode("#ellipse(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends a closed polygon through the given points.</summary>
    public TypstContent Polygon(
        ITypstPaint? fill,
        TypstStroke? stroke,
        params (TypstLength X, TypstLength Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 3)
        {
            throw new ArgumentException("a polygon needs at least three points", nameof(points));
        }

        var arguments = new List<string>();
        Add(arguments, "fill", fill?.ToTypst());
        Add(arguments, "stroke", stroke?.ToTypst());
        arguments.AddRange(Array.ConvertAll(points, point => $"({point.X}, {point.Y})"));

        _nodes.Add(new RawNode("#polygon(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    /// <summary>Appends an open curve through the given points.</summary>
    public TypstContent Curve(TypstStroke? stroke, params (TypstLength X, TypstLength Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
        {
            throw new ArgumentException("a curve needs at least two points", nameof(points));
        }

        var arguments = new List<string>();
        Add(arguments, "stroke", stroke?.ToTypst());
        arguments.Add($"curve.move(({points[0].X}, {points[0].Y}))");

        for (var index = 1; index < points.Length; index++)
        {
            arguments.Add($"curve.line(({points[index].X}, {points[index].Y}))");
        }

        _nodes.Add(new RawNode("#curve(" + string.Join(", ", arguments) + ")"));
        return this;
    }

    private TypstContent Wrap(string function, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _nodes.Add(new WrapperNode(function, content));
        return this;
    }

    private static void Add(List<string> arguments, string name, string? value)
    {
        if (value is not null)
        {
            arguments.Add($"{name}: {value}");
        }
    }
}
