using System;

namespace M3scalinum.Text.Documents;

/// <summary>Page setup, configured through <see cref="TypstDocument.Page"/>.</summary>
public sealed class TypstPageSetup
{
    internal string? PaperName { get; private set; }

    internal TypstLength? CustomWidth { get; private set; }

    internal TypstLength? CustomHeight { get; private set; }

    internal TypstLength? MarginAll { get; private set; }

    internal TypstLength? MarginTop { get; private set; }

    internal TypstLength? MarginBottom { get; private set; }

    internal TypstLength? MarginLeft { get; private set; }

    internal TypstLength? MarginRight { get; private set; }

    internal bool Flipped { get; private set; }

    internal ITypstPaint? FillPaint { get; private set; }

    internal int? ColumnCount { get; private set; }

    internal string? NumberingFormat { get; private set; }

    internal TypstContent? Header { get; private set; }

    internal TypstContent? Footer { get; private set; }

    /// <summary>Sets a named paper size: <c>a4</c>, <c>a5</c>, <c>letter</c>, <c>legal</c>.</summary>
    public TypstPageSetup Paper(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        PaperName = name;
        CustomWidth = null;
        CustomHeight = null;
        return this;
    }

    /// <summary>Sets a custom page size.</summary>
    public TypstPageSetup Size(TypstLength width, TypstLength height)
    {
        CustomWidth = width;
        CustomHeight = height;
        PaperName = null;
        return this;
    }

    /// <summary>Sets the same margin on every side.</summary>
    public TypstPageSetup Margin(TypstLength margin)
    {
        MarginAll = margin;
        MarginTop = null;
        MarginBottom = null;
        MarginLeft = null;
        MarginRight = null;
        return this;
    }

    /// <summary>Sets each margin independently, in CSS order.</summary>
    /// <param name="top">Top margin.</param>
    /// <param name="right">Right margin.</param>
    /// <param name="bottom">Bottom margin.</param>
    /// <param name="left">Left margin.</param>
    public TypstPageSetup Margin(
        TypstLength top,
        TypstLength right,
        TypstLength bottom,
        TypstLength left)
    {
        MarginAll = null;
        MarginTop = top;
        MarginBottom = bottom;
        MarginLeft = left;
        MarginRight = right;
        return this;
    }

    /// <summary>Sets the left and right margins together.</summary>
    public TypstPageSetup MarginX(TypstLength horizontal)
    {
        MarginAll = null;
        MarginLeft = horizontal;
        MarginRight = horizontal;
        return this;
    }

    /// <summary>Sets the top and bottom margins together.</summary>
    public TypstPageSetup MarginY(TypstLength vertical)
    {
        MarginAll = null;
        MarginTop = vertical;
        MarginBottom = vertical;
        return this;
    }

    /// <summary>Swaps the page width and height.</summary>
    public TypstPageSetup Landscape(bool landscape = true)
    {
        Flipped = landscape;
        return this;
    }

    /// <summary>Sets the page background.</summary>
    public TypstPageSetup Fill(ITypstPaint paint)
    {
        ArgumentNullException.ThrowIfNull(paint);
        FillPaint = paint;
        return this;
    }

    /// <summary>Sets the number of columns the page body is laid out in.</summary>
    public TypstPageSetup Columns(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ColumnCount = count;
        return this;
    }

    /// <summary>Sets the page numbering format, for example <c>1 / 1</c>. <see langword="null"/> disables it.</summary>
    public TypstPageSetup Numbering(string? format)
    {
        NumberingFormat = format;
        return this;
    }

    /// <summary>Sets the running header.</summary>
    public TypstPageSetup WithHeader(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Header = content;
        return this;
    }

    /// <summary>Sets the running footer.</summary>
    public TypstPageSetup WithFooter(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Footer = content;
        return this;
    }
}