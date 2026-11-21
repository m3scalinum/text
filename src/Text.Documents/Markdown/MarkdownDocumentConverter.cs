using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Documents;

/// <summary>The constructs the Markdown importer can drop, as named in the fidelity contract.</summary>
internal static class MarkdownConstructs
{
    /// <summary>A raw HTML block, dropped with its text.</summary>
    public const string HtmlBlock = "html-block";

    /// <summary>A raw HTML tag inside a paragraph.</summary>
    public const string HtmlInline = "html-inline";

    /// <summary>A reference link definition that nothing refers to.</summary>
    public const string ReferenceDefinition = "reference-definition";

    /// <summary>A footnote reference with no definition, or a second reference to one footnote.</summary>
    public const string FootnoteReference = "footnote-reference";

    /// <summary>A footnote definition that nothing refers to.</summary>
    public const string FootnoteDefinition = "footnote-definition";

    /// <summary>A block inside a footnote definition that is not a paragraph.</summary>
    public const string FootnoteBlock = "footnote-block";

    /// <summary>A table cell beyond the column count the header declares.</summary>
    public const string TableCell = "table-cell";
}

/// <summary>Converts the Markdown blocks into document-model nodes.</summary>
internal sealed class MarkdownDocumentConverter : IMarkdownInlineContext
{
    private readonly TypstMarkdownImportOptions _options;
    private readonly MarkdownBlockParser _parser = new();
    private readonly MarkdownInlineParser _inline;
    private readonly Dictionary<string, int> _dropped = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedFootnotes = new(StringComparer.Ordinal);

    public MarkdownDocumentConverter(TypstMarkdownImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _inline = new MarkdownInlineParser(this);
    }

    public TypstMarkdownImportResult Convert(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var blocks = _parser.Parse(markdown);
        var document = TypstDocument.Create();

        foreach (var node in ConvertBlocks(blocks))
        {
            document.Add(node);
        }

        foreach (var label in _parser.Links.Keys)
        {
            if (!_usedLinks.Contains(label))
            {
                Drop(MarkdownConstructs.ReferenceDefinition);
            }
        }

        foreach (var label in _parser.Footnotes.Keys)
        {
            if (!_usedFootnotes.Contains(label))
            {
                Drop(MarkdownConstructs.FootnoteDefinition);
            }
        }

        return new TypstMarkdownImportResult(document, Snapshot());
    }

    // ---- IMarkdownInlineContext ----------------------------------------------------------------

    public bool TryGetLink(string label, out MarkdownLinkDefinition definition)
    {
        if (_parser.Links.TryGetValue(label, out definition))
        {
            _usedLinks.Add(label);
            return true;
        }

        definition = default;
        return false;
    }

    public TypstContent? TakeFootnote(string label)
    {
        if (!_parser.Footnotes.TryGetValue(label, out var lines) || !_usedFootnotes.Add(label))
        {
            return null;
        }

        var content = new TypstContent();
        var first = true;

        foreach (var block in _parser.ParseNested(lines))
        {
            if (block is not MarkdownParagraph paragraph)
            {
                Drop(MarkdownConstructs.FootnoteBlock);
                continue;
            }

            var parsed = _inline.Parse(paragraph.Text);

            if (!first && !parsed.IsEmpty)
            {
                content.LineBreak();
            }

            parsed.AppendTo(content);
            first = false;
        }

        return content;
    }

    public string ResolveImage(string destination)
    {
        var asset = _options.AssetResolver?.Invoke(destination);

        if (string.IsNullOrWhiteSpace(asset))
        {
            throw new TypstMarkdownImportException(
                $"the image destination '{destination}' does not resolve to a registered asset; " +
                "register it with the world builder and return its name from " +
                nameof(TypstMarkdownImportOptions) + "." + nameof(TypstMarkdownImportOptions.AssetResolver) + ".")
            {
                ConstructName = "image",
            };
        }

        return asset;
    }

    public void Drop(string constructName)
        => _dropped[constructName] = _dropped.TryGetValue(constructName, out var count) ? count + 1 : 1;

    // ---- blocks --------------------------------------------------------------------------------

    private List<TypstNode> ConvertBlocks(IReadOnlyList<MarkdownBlock> blocks)
    {
        var nodes = new List<TypstNode>(blocks.Count);

        foreach (var block in blocks)
        {
            ConvertBlock(block, nodes);
        }

        return nodes;
    }

    private void ConvertBlock(MarkdownBlock block, List<TypstNode> into)
    {
        switch (block)
        {
            case MarkdownParagraph paragraph:
                AddParagraph(paragraph, into);
                break;

            case MarkdownHeading heading:
                var headingContent = _inline.Parse(heading.Text);

                if (!headingContent.IsEmpty)
                {
                    into.Add(new HeadingNode(heading.Level, headingContent));
                }

                break;

            case MarkdownCodeBlock code:
                into.Add(new CodeBlockNode(code.Language, code.Code));
                break;

            case MarkdownThematicBreak:
                into.Add(new SimpleCallNode("#divider()"));
                break;

            case MarkdownQuote quote:
                into.Add(BuildQuote(quote));
                break;

            case MarkdownList list:
                into.Add(BuildList(list));
                break;

            case MarkdownTable table:
                into.Add(BuildTable(table));
                break;

            case MarkdownHtmlBlock html:
                if (_options.RawHtml == TypstMarkdownRawHtmlPolicy.Throw)
                {
                    throw new TypstMarkdownImportException(
                        "raw HTML is not part of the supported Markdown subset: " + Excerpt(html.Html))
                    {
                        ConstructName = MarkdownConstructs.HtmlBlock,
                    };
                }

                Drop(MarkdownConstructs.HtmlBlock);
                break;

            default:
                break;
        }
    }

    private void AddParagraph(MarkdownParagraph paragraph, List<TypstNode> into)
    {
        if (MarkdownInlineParser.TryParseStandaloneImage(paragraph.Text, out var alt, out var destination))
        {
            into.Add(BuildFigure(alt, destination));
            return;
        }

        var content = _inline.Parse(paragraph.Text);

        if (!content.IsEmpty)
        {
            into.Add(new ParagraphNode(content));
        }
    }

    private FigureNode BuildFigure(string alt, string destination)
    {
        var figure = new TypstFigure().Image(ResolveImage(destination));

        if (!string.IsNullOrWhiteSpace(alt))
        {
            figure.WithCaption(_inline.Parse(alt));
        }

        return new FigureNode(figure);
    }

    private QuoteNode BuildQuote(MarkdownQuote quote)
    {
        var content = new TypstContent();

        foreach (var node in ConvertBlocks(quote.Blocks))
        {
            content.Node(node);
        }

        return new QuoteNode(content, block: true);
    }

    private ListNode BuildList(MarkdownList list)
    {
        var items = new List<ListItem>(list.Items.Count);

        foreach (var item in list.Items)
        {
            var content = new TypstContent();
            var children = new List<TypstNode>();
            var first = true;

            foreach (var block in item.Blocks)
            {
                if (block is MarkdownParagraph paragraph)
                {
                    var parsed = _inline.Parse(paragraph.Text);

                    if (!first && !parsed.IsEmpty)
                    {
                        content.LineBreak();
                    }

                    parsed.AppendTo(content);
                    first = false;
                    continue;
                }

                ConvertBlock(block, children);
            }

            items.Add(new ListItem(content, children));
        }

        return new ListNode(items, list.Ordered, !list.Tight);
    }

    private TableNode BuildTable(MarkdownTable table)
    {
        var columns = Math.Max(1, table.Header.Count);
        var model = new TypstTable();
        var widths = new TypstLength[columns];

        for (var index = 0; index < columns; index++)
        {
            widths[index] = TypstLength.Fr(1);
        }

        model.Columns(widths);

        if (table.Header.Count > 0)
        {
            model.HeaderRow(BuildCells(table.Header, columns, table.Alignments));
        }

        foreach (var row in table.Rows)
        {
            model.Row(BuildCells(row, columns, table.Alignments));
        }

        return new TableNode(model);
    }

    private TypstTableCell[] BuildCells(
        IReadOnlyList<string> cells,
        int columns,
        IReadOnlyList<TypstAlignment?> alignments)
    {
        var result = new TypstTableCell[columns];

        for (var index = 0; index < columns; index++)
        {
            var text = index < cells.Count ? cells[index] : string.Empty;
            result[index] = new TypstTableCell(_inline.Parse(text))
            {
                Align = index < alignments.Count ? alignments[index] : null,
            };
        }

        for (var index = columns; index < cells.Count; index++)
        {
            Drop(MarkdownConstructs.TableCell);
        }

        return result;
    }

    private static string Excerpt(string html)
    {
        var trimmed = html.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
    }

    private List<TypstMarkdownDroppedConstruct> Snapshot()
    {
        var constructs = new List<TypstMarkdownDroppedConstruct>(_dropped.Count);

        foreach (var pair in _dropped)
        {
            constructs.Add(new TypstMarkdownDroppedConstruct(pair.Key, pair.Value));
        }

        constructs.Sort(static (left, right) => string.CompareOrdinal(left.ConstructName, right.ConstructName));
        return constructs;
    }
}
