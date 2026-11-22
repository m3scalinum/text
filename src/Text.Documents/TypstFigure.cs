using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>A figure built by <see cref="TypstDocument.Figure"/>.</summary>
public sealed class TypstFigure
{
    private string? _image;
    private TypstLength? _width;
    private TypstLength? _height;
    private string? _fit;
    private string? _alt;

    /// <summary>The alternative description, null when the figure has none.</summary>
    internal string? AltText => _alt;

    internal string ImageMarkup
    {
        get
        {
            if (_image is null)
            {
                return "[]";
            }

            var arguments = new List<string> { LinkNode.Quote(_image) };

            if (_width is not null)
            {
                arguments.Add($"width: {_width}");
            }

            if (_height is not null)
            {
                arguments.Add($"height: {_height}");
            }

            if (_fit is not null)
            {
                arguments.Add($"fit: {LinkNode.Quote(_fit)}");
            }

            return "image(" + string.Join(", ", arguments) + ")";
        }
    }

    internal TypstContent? Caption { get; private set; }

    internal string? LabelName { get; private set; }

    /// <summary>Sets the image, by virtual asset path.</summary>
    public TypstFigure Image(
        string path,
        TypstLength? width = null,
        TypstLength? height = null,
        string? fit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (fit is not null && fit is not ("cover" or "contain" or "stretch"))
        {
            throw new ArgumentException("fit is 'cover', 'contain' or 'stretch'", nameof(fit));
        }

        _image = path;
        _width = width;
        _height = height;
        _fit = fit;
        return this;
    }

    /// <summary>Sets the alternative description a screen reader reads instead of the image.</summary>
    /// <remarks>
    /// <para>
    /// Emitted as the <c>alt</c> argument of the <c>#figure(…)</c> call. Typst turns that into a
    /// <c>/Figure</c> structure element carrying <c>/Alt</c>, which is what
    /// <c>PdfConformance.CheckAccessibility</c> reads as <c>PDFUA.FIGURE_ALT_MISSING</c>; the text
    /// is also read by assistive technology in the other export targets.
    /// </para>
    /// <para>
    /// The description is prose, not markup: it is quoted and escaped, never parsed. A figure that
    /// is purely decorative should not carry one, but the model has no decorative marker yet.
    /// </para>
    /// </remarks>
    public TypstFigure Alt(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _alt = description;
        return this;
    }

    /// <summary>Sets the caption.</summary>
    public TypstFigure WithCaption(TypstContent caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        Caption = caption;
        return this;
    }

    /// <summary>Attaches a label, so the figure can be referenced.</summary>
    public TypstFigure WithLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        LabelName = label;
        return this;
    }
}