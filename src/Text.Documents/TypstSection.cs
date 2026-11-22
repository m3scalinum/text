using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// One part of a document that carries its own running header, footer and watermark, and can hide
/// the page number of its pages — a cover, a contract's parts, a report's annexes.
/// </summary>
/// <remarks>
/// <para>
/// Typst has no section: it has a page setup that applies from the content where it is set. A
/// section therefore writes its own <c>#set page(…)</c> inside a content block, and Typst's scoping
/// ends it there — what the <see cref="TypstDocumentStyle"/> or the document declared before the
/// section is in force again after it. That is the primitive, and it is the one that survives a
/// page break: the header and the footer are re-drawn by the page setup on every page the section's
/// content lands on, not once at the top of the section.
/// </para>
/// <para>
/// A page that holds the last lines of the previous section and the first lines of this one uses the
/// previous section's setup: Typst picks the setup of the content that starts the page. A caller that
/// wants an exact boundary calls <see cref="StartsOnNewPage"/>.
/// </para>
/// <para>
/// The section only overrides what it declares. A section with a footer and no header keeps the
/// document's header; <see cref="NoHeader"/> removes it for the section's pages only.
/// </para>
/// </remarks>
public sealed class TypstSection
{
    private TypstSection()
    {
    }

    /// <summary>Starts a section.</summary>
    public static TypstSection Create() => new();

    /// <summary>
    /// The section's content, built with the document's own fluent API: every method a
    /// <see cref="TypstDocument"/> has works here, and what it writes stays inside the section.
    /// </summary>
    public TypstDocument Body { get; } = TypstDocument.Create();

    /// <summary>The running header the section declares, before placeholders are expanded.</summary>
    internal string? HeaderValue { get; private set; }

    /// <summary>Whether the section removes the inherited header.</summary>
    internal bool HeaderRemoved { get; private set; }

    /// <summary>The running footer the section declares, before placeholders are expanded.</summary>
    internal string? FooterValue { get; private set; }

    /// <summary>Whether the section removes the inherited footer.</summary>
    internal bool FooterRemoved { get; private set; }

    /// <summary>The watermark the section draws on its pages.</summary>
    internal TypstWatermark? WatermarkValue { get; private set; }

    /// <summary>Whether the section removes the inherited watermark.</summary>
    internal bool WatermarkRemoved { get; private set; }

    /// <summary>The page numbering format the section declares, for example <c>i</c> for preliminaries.</summary>
    internal string? NumberingFormat { get; private set; }

    /// <summary>Whether the section hides the page number entirely.</summary>
    internal bool NumberingHidden { get; private set; }

    /// <summary>Whether the section starts with a page break, so its setup begins a page.</summary>
    internal bool StartsOnNewPageValue { get; private set; }

    /// <summary>
    /// Sets the section's running header. The <c>{page}</c>, <c>{pages}</c> and <c>{title}</c>
    /// placeholders are the ones a <see cref="TypstDocumentStyle"/> accepts.
    /// </summary>
    /// <param name="text">The header text.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public TypstSection Header(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        HeaderValue = text;
        HeaderRemoved = false;
        return this;
    }

    /// <summary>Removes the header the document declared, on the section's pages only.</summary>
    public TypstSection NoHeader()
    {
        HeaderValue = null;
        HeaderRemoved = true;
        return this;
    }

    /// <summary>Sets the section's running footer, with the same placeholders as <see cref="Header"/>.</summary>
    /// <param name="text">The footer text.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public TypstSection Footer(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        FooterValue = text;
        FooterRemoved = false;
        return this;
    }

    /// <summary>Removes the footer the document declared, on the section's pages only.</summary>
    public TypstSection NoFooter()
    {
        FooterValue = null;
        FooterRemoved = true;
        return this;
    }

    /// <summary>
    /// Draws a watermark behind the section's text, diagonally and centred on every page.
    /// </summary>
    /// <param name="text">The word or phrase to draw.</param>
    /// <param name="configure">Adjusts the angle, size, colour and opacity.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public TypstSection Watermark(string text, Action<TypstWatermark>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var watermark = TypstWatermark.Create(text);
        configure?.Invoke(watermark);
        return Watermark(watermark);
    }

    /// <summary>Draws a prepared watermark behind the section's text.</summary>
    /// <param name="watermark">The watermark.</param>
    /// <exception cref="ArgumentNullException"><paramref name="watermark"/> is null.</exception>
    public TypstSection Watermark(TypstWatermark watermark)
    {
        ArgumentNullException.ThrowIfNull(watermark);
        WatermarkValue = watermark;
        WatermarkRemoved = false;
        return this;
    }

    /// <summary>Removes the watermark the document declared, on the section's pages only.</summary>
    public TypstSection NoWatermark()
    {
        WatermarkValue = null;
        WatermarkRemoved = true;
        return this;
    }

    /// <summary>
    /// Numbers the section's pages with another format, for example <c>i</c> for the preliminaries
    /// of a report whose body counts <c>1</c>, <c>2</c>, <c>3</c>.
    /// </summary>
    /// <param name="format">A Typst numbering pattern.</param>
    /// <exception cref="ArgumentException"><paramref name="format"/> is blank.</exception>
    public TypstSection PageNumbering(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        NumberingFormat = format;
        NumberingHidden = false;
        return this;
    }

    /// <summary>
    /// Hides the page number on the section's pages. The page counter keeps counting: the page after
    /// a cover numbered <c>none</c> is page two, and shows <c>2</c> when the document numbers pages.
    /// </summary>
    public TypstSection NoPageNumber()
    {
        NumberingFormat = null;
        NumberingHidden = true;
        return this;
    }

    /// <summary>
    /// Starts the section on a new page, so no page carries two sections' setups. The break belongs
    /// to the previous section, which is why it is written before the section's own rules.
    /// </summary>
    public TypstSection StartsOnNewPage(bool startsOnNewPage = true)
    {
        StartsOnNewPageValue = startsOnNewPage;
        return this;
    }

    /// <summary>
    /// The <c>#set page(…)</c> rule the section writes, or <see langword="null"/> when it overrides
    /// nothing.
    /// </summary>
    internal string? PageRule()
    {
        var arguments = new List<string>();

        if (HeaderRemoved)
        {
            arguments.Add("header: none");
        }
        else if (HeaderValue is { } header)
        {
            arguments.Add("header: " + TypstRunningText.Markup(header));
        }

        if (FooterRemoved)
        {
            arguments.Add("footer: none");
        }
        else if (FooterValue is { } footer)
        {
            arguments.Add("footer: " + TypstRunningText.Markup(footer));
        }

        if (WatermarkRemoved)
        {
            arguments.Add("background: none");
        }
        else if (WatermarkValue is { } watermark)
        {
            arguments.Add("background: " + watermark.ToPageBackground());
        }

        if (NumberingHidden)
        {
            arguments.Add("numbering: none");
        }
        else if (NumberingFormat is { } format)
        {
            arguments.Add("numbering: " + LinkNode.Quote(format));
        }

        return arguments.Count == 0 ? null : "#set page(" + string.Join(", ", arguments) + ")";
    }
}

/// <summary>
/// Turns a running header or footer into a Typst expression: literal text with the
/// <c>{page}</c>, <c>{pages}</c> and <c>{title}</c> placeholders, wrapped so the page counter can be
/// read where the running text is laid out.
/// </summary>
internal static class TypstRunningText
{
    /// <summary>Wraps a running text in a <c>context</c> block with its placeholders expanded.</summary>
    /// <param name="text">The running text.</param>
    internal static string Markup(string text)
    {
        var markup = new StringBuilder("context [ ");
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '{')
            {
                var end = text.IndexOf('}', index + 1);

                if (end > index && Placeholder(text[(index + 1)..end]) is { } replacement)
                {
                    markup.Append(replacement);
                    index = end + 1;
                    continue;
                }
            }

            markup.Append(Escape(text[index]));
            index++;
        }

        return markup.Append(" ]").ToString();
    }

    private static string? Placeholder(string name) => name switch
    {
        "page" => "#counter(page).display()",
        "pages" => "#counter(page).final().first()",
        "title" => "#document.title",
        _ => null,
    };

    /// <summary>Escapes one character of literal running text, so it reaches the page as written.</summary>
    private static string Escape(char character) => character switch
    {
        '\\' => "\\\\",
        '#' => "\\#",
        '$' => "\\$",
        '*' => "\\*",
        '_' => "\\_",
        '`' => "\\`",
        '<' => "\\<",
        '>' => "\\>",
        '@' => "\\@",
        '~' => "\\~",
        '[' => "\\[",
        ']' => "\\]",
        _ => character.ToString(),
    };
}

/// <summary>A section written as a content block, so its page setup ends where the section ends.</summary>
internal sealed class SectionNode(TypstSection section) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (section.StartsOnNewPageValue)
        {
            writer.BlankLine();
            writer.WriteVerbatim("#pagebreak()");
            writer.NewLine();
        }

        writer.BlankLine();
        writer.WriteVerbatim("#[");
        writer.NewLine();

        if (section.PageRule() is { } rule)
        {
            writer.WriteVerbatim(rule);
            writer.NewLine();
        }

        var body = section.Body.ToMarkup();

        if (body.Length > 0)
        {
            writer.WriteVerbatim(body);
        }

        writer.WriteVerbatim("]");
        writer.NewLine();
    }
}
