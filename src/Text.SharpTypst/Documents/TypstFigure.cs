using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A figure built by <see cref="TypstDocument.Figure"/>.</summary>
public sealed class TypstFigure
{
    private string? _image;
    private TypstLength? _width;
    private TypstLength? _height;
    private string? _fit;

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