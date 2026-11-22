using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// A fluent builder that emits Typst source. The model is write-only: it never parses Typst,
/// and anything it does not cover is one <see cref="Raw"/> call away.
/// </summary>
public sealed partial class TypstDocument
{
    private readonly List<TypstNode> _body = [];
    private readonly List<string> _preamble = [];
    private readonly Dictionary<TypstLabelKind, int> _labelOrdinals = [];
    private TypstMetadata? _metadata;
    private TypstPageSetup? _page;
    private TypstDirection? _direction;
    private TypstDocumentStyle? _style;

    private TypstDocument()
    {
    }

    /// <summary>Creates an empty document.</summary>
    public static TypstDocument Create() => new();

    /// <summary>
    /// Builds a document from an HTML page or fragment, keeping the supported subset and dropping
    /// the rest. Use <see cref="TypstHtmlImporter"/> when the dropped elements must be inspected.
    /// </summary>
    /// <param name="html">The HTML to import.</param>
    /// <param name="options">The import options; defaults are used when null.</param>
    public static TypstDocument FromHtml(string html, TypstHtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new TypstHtmlImporter(options).Import(html).Document;
    }

    /// <summary>
    /// Builds a document from a Markdown document or fragment, keeping the supported subset and
    /// dropping the rest. Use <see cref="MarkdownImporter"/> when the dropped constructs must be
    /// inspected.
    /// </summary>
    /// <param name="markdown">The Markdown to import.</param>
    /// <param name="options">The import options; defaults are used when null.</param>
    public static TypstDocument FromMarkdown(string markdown, TypstMarkdownImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new MarkdownImporter(options).Import(markdown).Document;
    }

    /// <summary>Configures <c>#set document(…)</c>.</summary>
    public TypstDocument Metadata(Action<TypstMetadata> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _metadata ??= new TypstMetadata();
        configure(_metadata);
        return this;
    }

    /// <summary>Configures <c>#set page(…)</c>.</summary>
    public TypstDocument Page(Action<TypstPageSetup> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _page ??= new TypstPageSetup();
        configure(_page);
        return this;
    }

    /// <summary>
    /// Applies a reusable <see cref="TypstDocumentStyle"/>: its page, body, heading, language, header
    /// and footer rules are written once, after the document's own <c>#set</c> rules and before the
    /// body. Applying the same style twice writes the same markup once.
    /// </summary>
    /// <remarks>
    /// The style only speaks where it is configured: a document that also set a page fill, a
    /// language or a header keeps them wherever the style stays silent, and a document with no style
    /// keeps exactly the markup it had.
    /// </remarks>
    public TypstDocument Apply(TypstDocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        return this;
    }

    /// <summary>Appends a node.</summary>
    public TypstDocument Add(TypstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _body.Add(node);
        return this;
    }

    /// <summary>Appends content as its own block: shapes, decorated runs, inline constructs.</summary>
    public TypstDocument Add(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new ContentNode(content));
        return this;
    }

    /// <summary>Appends a heading. Levels 1 to 6 map to <c>=</c> to <c>======</c>.</summary>
    public TypstDocument Heading(int level, TypstContent content)
    {
        if (level is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "a heading level is 1 to 6");
        }

        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new HeadingNode(level, content));
        return this;
    }

    /// <summary>Appends a paragraph.</summary>
    public TypstDocument Paragraph(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new ParagraphNode(content));
        return this;
    }

    /// <summary>Appends a paragraph built inline.</summary>
    public TypstDocument Paragraph(Action<TypstContent> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var content = new TypstContent();
        build(content);
        return Paragraph(content);
    }

    /// <summary>Appends a bullet list.</summary>
    public TypstDocument BulletList(params TypstContent[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _body.Add(new ListNode(items, ordered: false));
        return this;
    }

    /// <summary>Appends a numbered list.</summary>
    public TypstDocument NumberedList(params TypstContent[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _body.Add(new ListNode(items, ordered: true));
        return this;
    }

    /// <summary>Appends a term list.</summary>
    public TypstDocument TermList(params (string Term, TypstContent Description)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _body.Add(new TermListNode(items));
        return this;
    }

    /// <summary>Appends a table.</summary>
    public TypstDocument Table(Action<TypstTable> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var table = new TypstTable();
        configure(table);
        _body.Add(new TableNode(table));
        return this;
    }

    /// <summary>Appends a figure.</summary>
    /// <param name="configure">Configures the figure: image, alt text, caption, label.</param>
    /// <param name="scope">
    /// What the figure number counts from. <see cref="TypstNumberingScope.Document"/> (the default)
    /// is one sequence for the whole document; <see cref="TypstNumberingScope.Section"/> prints
    /// <c>section.figure</c> and restarts the figure count at each level-1 heading.
    /// </param>
    /// <remarks>
    /// A section-scoped figure turns heading numbering on, unless the document already numbers its
    /// headings, because the section prefix is Typst's <c>counter(heading)</c>. A figure that
    /// precedes the first section keeps a plain number.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is not a known scope.</exception>
    public TypstDocument Figure(
        Action<TypstFigure> configure,
        TypstNumberingScope scope = TypstNumberingScope.Document)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var figure = new TypstFigure();
        configure(figure);

        if (TypstFootnoteNumbering.IsSectionScoped(scope, nameof(scope)))
        {
            TypstFigureNumbering.EnableSection(_preamble);
        }

        _body.Add(new FigureNode(figure));
        return this;
    }

    /// <summary>Appends a fenced code block.</summary>
    public TypstDocument Code(string language, string code)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(code);
        _body.Add(new CodeBlockNode(language, code));
        return this;
    }

    /// <summary>Appends a display equation, written in Typst math syntax.</summary>
    public TypstDocument Math(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _body.Add(new ParagraphNode(new TypstContent().Node(new MathNode(expression, block: true))));
        return this;
    }

    /// <summary>Appends a display equation built with <see cref="TypstMath"/>.</summary>
    public TypstDocument Math(TypstExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Math(expression.ToTypst());
    }

    /// <summary>Appends a quote, block-level by default.</summary>
    public TypstDocument Quote(TypstContent content, bool block = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new QuoteNode(content, block));
        return this;
    }

    /// <summary>
    /// Appends a reference to the element labelled <paramref name="label"/>.
    /// </summary>
    /// <param name="label">The label of the target, as it was given to a label call.</param>
    /// <param name="supplement">
    /// The word printed before the number, overriding the target's own — <c>Figure</c>,
    /// <c>Table</c>, <c>Section</c>. Null keeps the target's supplement.
    /// </param>
    /// <remarks>
    /// <para>
    /// Emits <c>#ref(&lt;label&gt;)</c>, or <c>#ref(&lt;label&gt;, supplement: […])</c>. The model
    /// writes the reference and no number: Typst resolves it against the laid-out document, so the
    /// number and the page are whatever the compiled document says.
    /// </para>
    /// <para>
    /// A reference may name a target that comes later, and the same label may be referenced any
    /// number of times. A label that no element carries makes Typst fail the compilation
    /// (<c>label `x` does not exist in the document</c>); a target without numbering fails it too
    /// (<c>cannot reference heading without numbering</c>).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="label"/> or <paramref name="supplement"/> is blank.</exception>
    public TypstDocument Reference(string label, string? supplement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (supplement is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(supplement);
        }

        _body.Add(new ContentNode(new TypstContent().Node(new ReferenceNode(label, supplement))));
        return this;
    }

    /// <summary>
    /// Labels the element this call follows, so a reference can find it.
    /// </summary>
    /// <param name="name">The label to attach, e.g. <c>fig:revenue</c>.</param>
    /// <remarks>
    /// <para>
    /// The label lands on the last element added: a figure or a table is numbered as well as
    /// labelled (<c>figure(kind: image|table, supplement: […])</c>), a heading is numbered because
    /// the first section label turns <c>#set heading(numbering: "1.1")</c> on, and any other block
    /// just carries the label after it.
    /// </para>
    /// <para>
    /// A name the document already holds is emitted with <c>-2</c>, <c>-3</c>, … appended to the
    /// later occurrences, because Typst refuses a document in which a label occurs twice. The first
    /// occurrence keeps the name as written, and a reference to that name reaches it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public TypstDocument Label(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Attach(KindOfLastNode(), name);
    }

    /// <summary>
    /// Labels the element this call follows with a generated name, or with
    /// <paramref name="name"/> when one is given.
    /// </summary>
    /// <param name="kind">What the label sits on; it also selects the generated name's prefix.</param>
    /// <param name="name">The label to attach, or null to generate one.</param>
    /// <remarks>
    /// The generated names are <c>sec:1</c>, <c>fig:1</c> and <c>tbl:1</c> for the first label of
    /// each kind, then <c>sec:2</c>, <c>fig:2</c>, …, counted per kind in call order. They depend on
    /// nothing but the calls the document made, so the same document always produces the same
    /// names. Labelling a section turns <c>#set heading(numbering: "1.1")</c> on for the document —
    /// Typst refuses to reference a heading that is not numbered — unless the document already sets
    /// its own heading numbering.
    /// </remarks>
    public TypstDocument Label(TypstLabelKind kind, string? name = null)
        => Attach(kind, name ?? NextLabelName(kind));

    private TypstLabelKind? KindOfLastNode() => _body.Count == 0
        ? null
        : _body[^1] switch
        {
            HeadingNode => TypstLabelKind.Section,
            FigureNode => TypstLabelKind.Figure,
            TableNode => TypstLabelKind.Table,
            _ => null,
        };

    private TypstDocument Attach(TypstLabelKind? kind, string name)
    {
        if (kind == TypstLabelKind.Section)
        {
            EnableHeadingNumbering();
        }

        switch (_body.Count == 0 ? null : _body[^1])
        {
            case FigureNode figure when kind is null or TypstLabelKind.Figure:
                figure.Target.WithLabel(name);
                break;

            case TableNode table when kind is null or TypstLabelKind.Table:
                table.Target.WithLabel(name);
                break;

            default:
                _body.Add(new LabelNode(name));
                break;
        }

        return this;
    }

    private string NextLabelName(TypstLabelKind kind)
    {
        _labelOrdinals.TryGetValue(kind, out var ordinal);
        _labelOrdinals[kind] = ++ordinal;
        return TypstLabelRegistry.Prefix(kind) + ":" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    private void EnableHeadingNumbering()
    {
        if (_preamble.Exists(line => line.Contains("heading(numbering", StringComparison.Ordinal)))
        {
            return;
        }

        _preamble.Add("#set heading(numbering: \"1.1\")");
    }

    /// <summary>Appends a table of contents whose page numbers come from the compiled document.</summary>
    /// <param name="title">The heading printed above the entries, or null for none.</param>
    /// <param name="depth">How many heading levels the entries cover, 1 to 6.</param>
    /// <remarks>
    /// <para>
    /// Emits <c>#outline(title: […], depth: …)</c>. Typst fills it at layout time, in document
    /// order, with the final page number of every heading up to <paramref name="depth"/>; headings
    /// appear with the numbers <c>#set heading(numbering: …)</c> gave them. The model computes no
    /// entry and no page: nothing before the compilation knows either.
    /// </para>
    /// <para>
    /// A caller that needs the same tree on the host side reads
    /// <c>TypstEvaluation.Outline</c> from the compiled document: it lists the level, the title, the
    /// final page and the label of every heading, in the same order.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not 1 to 6.</exception>
    public TypstDocument Outline(TypstContent? title = null, int depth = 2)
    {
        if (depth is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "an outline depth is 1 to 6");
        }

        _body.Add(new OutlineNode(title, depth));
        return this;
    }

    /// <summary>Appends a page break.</summary>
    public TypstDocument PageBreak()
    {
        _body.Add(new SimpleCallNode("#pagebreak()"));
        return this;
    }

    /// <summary>Appends a column break.</summary>
    public TypstDocument ColumnBreak()
    {
        _body.Add(new SimpleCallNode("#colbreak()"));
        return this;
    }

    /// <summary>Appends a paragraph break.</summary>
    public TypstDocument ParagraphBreak()
    {
        _body.Add(new SimpleCallNode("#parbreak()"));
        return this;
    }

    /// <summary>Appends a footnote.</summary>
    /// <param name="content">The note's text.</param>
    /// <param name="scope">
    /// What the note number counts from. <see cref="TypstNumberingScope.Document"/> (the default) is
    /// one sequence for the whole document; <see cref="TypstNumberingScope.Section"/> restarts it at
    /// each level-1 heading.
    /// </param>
    /// <remarks>
    /// Typst numbers the note itself; a section scope only adds the rule that resets
    /// <c>counter(footnote)</c> at each level-1 heading. A document without headings numbers its
    /// notes 1, 2, 3 whichever scope it asks for. Mixing scopes in one document is not meaningful:
    /// the reset applies to the counter, so every note after a heading restarts, whatever scope its
    /// own call asked for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is not a known scope.</exception>
    public TypstDocument Footnote(
        TypstContent content,
        TypstNumberingScope scope = TypstNumberingScope.Document)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (TypstFootnoteNumbering.IsSectionScoped(scope, nameof(scope)))
        {
            TypstFootnoteNumbering.EnableSection(_preamble);
        }

        _body.Add(new ContentNode(new TypstContent().Footnote(content)));
        return this;
    }

    /// <summary>Appends a citation.</summary>
    public TypstDocument Cite(string key, bool prose = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _body.Add(new ContentNode(new TypstContent().Cite(key, prose)));
        return this;
    }

    /// <summary>Appends a metadata entry, readable by <c>query</c> from Typst code.</summary>
    public TypstDocument MetadataEntry(string key, string value, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _body.Add(new ContentNode(new TypstContent().Metadata(key, value, label)));
        return this;
    }

    /// <summary>Appends filler text.</summary>
    public TypstDocument Lorem(int words = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        _body.Add(new ContentNode(new TypstContent().Lorem(words)));
        return this;
    }

    /// <summary>Sets the text direction with <c>#set text(dir: …)</c>.</summary>
    public TypstDocument TextDirection(TypstDirection direction)
    {
        _direction = direction;
        return this;
    }

    /// <summary>Appends a thematic break.</summary>
    public TypstDocument Divider()
    {
        _body.Add(new SimpleCallNode("#divider()"));
        return this;
    }

    /// <summary>Appends a bibliography.</summary>
    public TypstDocument Bibliography(string path, string? style = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _body.Add(new BibliographyNode(path, style));
        return this;
    }

    /// <summary>Appends a block of Typst source, emitted untouched.</summary>
    public TypstDocument Raw(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        _body.Add(new RawNode(markup));
        return this;
    }

    /// <summary>
    /// Appends a context block, which unlocks the contextual functions: <c>counter</c>,
    /// <c>state</c>, <c>query</c>, <c>locate</c>, <c>here</c>, <c>measure</c>.
    /// </summary>
    public TypstDocument Context(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _body.Add(new SimpleCallNode("#context {" + code + "}"));
        return this;
    }

    /// <summary>Appends a context block built with <see cref="TypstContext"/>.</summary>
    public TypstDocument Context(Action<TypstContext> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var context = new TypstContext();
        build(context);
        return Context(context.ToCode());
    }

    /// <summary>Binds a name with <c>#let</c>, before the body.</summary>
    public TypstDocument Let(string name, TypstExpression value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _preamble.Add($"#let {name} = {value.ToTypst()}");
        return this;
    }

    /// <summary>Imports names from another Typst file.</summary>
    public TypstDocument Import(string path, params string[] names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(names);

        _preamble.Add(names.Length == 0
            ? $"#import {LinkNode.Quote(path)}"
            : $"#import {LinkNode.Quote(path)}: {string.Join(", ", names)}");

        return this;
    }

    /// <summary>Imports a file as a module, so its members are reachable as <c>alias.name</c>.</summary>
    public TypstDocument ImportAs(string path, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        _preamble.Add($"#import {LinkNode.Quote(path)} as {alias}");
        return this;
    }

    /// <summary>Defines a function, before the body.</summary>
    public TypstDocument Function(TypstFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _preamble.Add(function.ToTypst());
        return this;
    }

    /// <summary>Enables or disables smart quotes with <c>#set smartquote(…)</c>.</summary>
    public TypstDocument SmartQuotes(bool enabled = true)
    {
        _preamble.Add($"#set smartquote(enabled: {(enabled ? "true" : "false")})");
        return this;
    }

    /// <summary>Appends an asset, written into the bundle at the given path.</summary>
    public TypstDocument Asset(string path, string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);
        _body.Add(new SimpleCallNode($"#asset({LinkNode.Quote(path)}, {LinkNode.Quote(data)})"));
        return this;
    }

    /// <summary>Appends content repeated to fill the available space.</summary>
    public TypstDocument Repeat(TypstContent content, TypstLength? gap = null, bool justify = false)
    {
        ArgumentNullException.ThrowIfNull(content);

        var arguments = new List<string>();
        AddArgument(arguments, "gap", gap?.ToString());

        if (justify)
        {
            arguments.Add("justify: true");
        }

        var call = arguments.Count == 0 ? "#repeat" : "#repeat(" + string.Join(", ", arguments) + ")";
        _body.Add(new CallNode(call, content));
        return this;
    }

    /// <summary>Includes another Typst file.</summary>
    public TypstDocument Include(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _body.Add(new SimpleCallNode($"#include {LinkNode.Quote(path)}"));
        return this;
    }

    /// <summary>Adds a set rule, for example <c>text(size: 12pt)</c>.</summary>
    public TypstDocument Set(string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        _preamble.Add($"#set {rule}");
        return this;
    }

    /// <summary>Adds a show rule, for example <c>heading</c> and <c>it => text(..)[#it]</c>.</summary>
    public TypstDocument Show(string selector, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        _preamble.Add($"#show {selector}: {body}");
        return this;
    }

    /// <summary>Appends an expression as its own block.</summary>
    public TypstDocument Expression(TypstExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _body.Add(new SimpleCallNode("#(" + expression.ToTypst() + ")"));
        return this;
    }

    /// <summary>Appends content in columns.</summary>
    public TypstDocument Columns(int count, TypstContent content)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#columns({count})", content));
        return this;
    }

    /// <summary>Appends content in columns, with an explicit gutter.</summary>
    /// <remarks>
    /// This is the flow-level <c>#columns</c> container, for a block of columns inside a page. A whole
    /// document in columns is a page-level setting: <see cref="Page"/> with
    /// <see cref="TypstPageSetup.Columns(int, TypstLength)"/>, which also paginates.
    /// </remarks>
    public TypstDocument Columns(TypstColumnLayout layout, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#columns({layout.CallArguments})", content));
        return this;
    }

    /// <summary>
    /// Appends a margin note: the marker in the flow, and the note's text in the margin, level with
    /// the marker's line.
    /// </summary>
    /// <param name="note">The note: marker, text, offset and width.</param>
    /// <remarks>
    /// The note is placed in the margin of the page, not of the column: a note in a two-column body
    /// belongs to the page's outer margin, and its <see cref="TypstMarginNote.Offset"/> has to be
    /// measured from the marker against the column width the caller chose. See
    /// <see cref="TypstMarginNote"/> for what the placement cannot do: a note never breaks across
    /// pages and two notes closer than one note's height overlap.
    /// </remarks>
    public TypstDocument MarginNote(TypstMarginNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _body.Add(new TypstMarginNoteNode(note));
        return this;
    }

    /// <summary>Appends a margin note built from its parts.</summary>
    /// <param name="marker">The text printed in the flow at the anchor.</param>
    /// <param name="content">The note's text, placed in the margin.</param>
    /// <param name="offset">The distance from the marker to the note's left edge.</param>
    /// <param name="width">The width the note wraps at; null lets the text run on one line.</param>
    public TypstDocument MarginNote(
        string marker,
        TypstContent content,
        TypstLength offset,
        TypstLength? width = null)
        => MarginNote(new TypstMarginNote(marker, content, offset, width));

    /// <summary>Appends aligned content.</summary>
    public TypstDocument Align(TypstAlignment alignment, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#align({AlignmentName(alignment)})", content));
        return this;
    }

    /// <summary>Appends a box.</summary>
    public TypstDocument Box(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode("#box", content));
        return this;
    }

    /// <summary>Appends a block, optionally with a size, a fill and a stroke.</summary>
    public TypstDocument Block(
        TypstContent content,
        TypstLength? width = null,
        TypstLength? height = null,
        ITypstPaint? fill = null,
        TypstStroke? stroke = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var arguments = new List<string>();
        AddArgument(arguments, "width", width?.ToString());
        AddArgument(arguments, "height", height?.ToString());
        AddArgument(arguments, "fill", fill?.ToTypst());
        AddArgument(arguments, "stroke", stroke?.ToTypst());

        var call = arguments.Count == 0 ? "#block" : "#block(" + string.Join(", ", arguments) + ")";
        _body.Add(new CallNode(call, content));
        return this;
    }

    /// <summary>Appends a grid with a fixed number of columns.</summary>
    public TypstDocument Grid(int columns, params TypstContent[] cells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentNullException.ThrowIfNull(cells);

        var builder = new StringBuilder($"#grid(columns: {columns}, ");
        for (var index = 0; index < cells.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append('[').Append(EscapeInline(cells[index])).Append(']');
        }

        builder.Append(')');
        _body.Add(new SimpleCallNode(builder.ToString()));
        return this;
    }

    /// <summary>Appends a stack of content, optionally in a direction.</summary>
    public TypstDocument Stack(TypstContent content, TypstDirection? direction = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var call = direction is null ? "#stack" : $"#stack(dir: {DirectionName(direction.Value)})";
        _body.Add(new CallNode(call, content));
        return this;
    }

    /// <summary>Appends padded content.</summary>
    public TypstDocument Pad(TypstLength padding, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#pad({padding})", content));
        return this;
    }

    /// <summary>Appends content placed at an alignment, optionally offset.</summary>
    public TypstDocument Place(
        TypstAlignment alignment,
        TypstContent content,
        TypstLength? dx = null,
        TypstLength? dy = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var arguments = new List<string> { AlignmentName(alignment) };
        AddArgument(arguments, "dx", dx?.ToString());
        AddArgument(arguments, "dy", dy?.ToString());

        _body.Add(new CallNode("#place(" + string.Join(", ", arguments) + ")", content));
        return this;
    }

    /// <summary>Appends hidden content, which still occupies space.</summary>
    public TypstDocument Hide(TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode("#hide", content));
        return this;
    }

    /// <summary>Appends rotated content.</summary>
    public TypstDocument Rotate(double degrees, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#rotate({Number(degrees)}deg)", content));
        return this;
    }

    /// <summary>Appends rotated content.</summary>
    public TypstDocument Rotate(TypstAngle angle, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#rotate({angle})", content));
        return this;
    }

    /// <summary>Appends scaled content.</summary>
    public TypstDocument Scale(double factor, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#scale({Number(factor)}%)", content));
        return this;
    }

    /// <summary>Appends content scaled independently on each axis.</summary>
    public TypstDocument Scale(double x, double y, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#scale(x: {Number(x)}%, y: {Number(y)}%)", content));
        return this;
    }

    /// <summary>Appends content moved by an offset.</summary>
    public TypstDocument Move(TypstLength dx, TypstLength dy, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#move(dx: {dx}, dy: {dy})", content));
        return this;
    }

    /// <summary>Appends skewed content.</summary>
    public TypstDocument Skew(double ax, double ay, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new CallNode($"#skew(ax: {Number(ax)}deg, ay: {Number(ay)}deg)", content));
        return this;
    }

    /// <summary>Appends horizontal space.</summary>
    public TypstDocument HSpace(TypstLength length)
    {
        _body.Add(new BareCallNode($"#h({length})"));
        return this;
    }

    /// <summary>Appends vertical space.</summary>
    public TypstDocument VSpace(TypstLength length)
    {
        _body.Add(new BareCallNode($"#v({length})"));
        return this;
    }

    /// <summary>Emits the Typst source. Stable: the same document always produces the same string.</summary>
    public string ToMarkup()
    {
        var writer = new TypstMarkupWriter();
        WriteSetRules(writer);

        if (_style is not null)
        {
            foreach (var rule in _style.SetRules)
            {
                writer.WriteVerbatim(rule);
                writer.NewLine();
            }
        }

        foreach (var line in _preamble)
        {
            writer.WriteVerbatim(line);
            writer.NewLine();
        }

        foreach (var node in _body)
        {
            node.Write(writer);
        }

        return writer.ToMarkup();
    }

    private void WriteSetRules(TypstMarkupWriter writer)
    {
        if (_metadata is not null)
        {
            writer.WriteVerbatim("#set document(" + MetadataArguments(_metadata) + ")");
            writer.NewLine();
        }

        if (_page is not null)
        {
            writer.WriteVerbatim("#set page(" + string.Join(", ", PageArguments(_page)) + ")");
            writer.NewLine();

            if (_page.ColumnLayout?.GutterRule is { } gutterRule)
            {
                writer.WriteVerbatim(gutterRule);
                writer.NewLine();
            }
        }

        if (_metadata?.LanguageValue is { } language)
        {
            writer.WriteVerbatim($"#set text(lang: {LinkNode.Quote(language)})");
            writer.NewLine();
        }

        if (_direction is { } direction)
        {
            writer.WriteVerbatim($"#set text(dir: {DirectionName(direction)})");
            writer.NewLine();
        }
    }

    private static string MetadataArguments(TypstMetadata metadata)
    {
        var parts = new List<string>();

        if (metadata.TitleValue is { } title)
        {
            parts.Add("title: " + LinkNode.Quote(title));
        }

        if (metadata.AuthorValue is { } author)
        {
            parts.Add("author: " + LinkNode.Quote(author));
        }

        if (metadata.KeywordsValue.Count > 0)
        {
            parts.Add("keywords: (" + string.Join(", ", metadata.KeywordsValue.Select(LinkNode.Quote)) + ")");
        }

        if (metadata.DateValue is { } date)
        {
            parts.Add($"date: datetime(year: {date.Year}, month: {date.Month}, day: {date.Day})");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The <c>#set page</c> arguments a page setup declares, one string per argument, in the order
    /// Typst reads them. The header and footer can be left to a <see cref="TypstDocumentStyle"/>.
    /// </summary>
    internal static IReadOnlyList<string> PageArguments(
        TypstPageSetup page,
        bool includeHeader = true,
        bool includeFooter = true)
    {
        var parts = new List<string>();

        if (page.PaperName is { } paper)
        {
            parts.Add("paper: " + LinkNode.Quote(paper));
        }
        else if (page.CustomWidth is { } width && page.CustomHeight is { } height)
        {
            parts.Add($"width: {width}, height: {height}");
        }

        if (page.MarginAll is { } margin)
        {
            parts.Add("margin: " + margin);
        }
        else if (page.MarginTop is not null
            || page.MarginBottom is not null
            || page.MarginLeft is not null
            || page.MarginRight is not null)
        {
            var sides = new List<string>();

            if (page.MarginTop is { } top)
            {
                sides.Add("top: " + top);
            }

            if (page.MarginBottom is { } bottom)
            {
                sides.Add("bottom: " + bottom);
            }

            if (page.MarginLeft is { } left)
            {
                sides.Add("left: " + left);
            }

            if (page.MarginRight is { } right)
            {
                sides.Add("right: " + right);
            }

            parts.Add("margin: (" + string.Join(", ", sides) + ")");
        }

        if (page.Flipped)
        {
            parts.Add("flipped: true");
        }

        if (page.FillPaint is { } fill)
        {
            parts.Add("fill: " + fill.ToTypst());
        }

        if (page.ColumnLayout is { } columns)
        {
            parts.Add(columns.PageArgument);
        }

        if (page.NumberingFormat is { } numbering)
        {
            parts.Add("numbering: " + LinkNode.Quote(numbering));
        }

        if (includeHeader && page.Header is not null)
        {
            parts.Add("header: [" + EscapeInline(page.Header) + "]");
        }

        if (includeFooter && page.Footer is not null)
        {
            parts.Add("footer: [" + EscapeInline(page.Footer) + "]");
        }

        return parts;
    }

    /// <summary>Renders content to a string for embedding inside a <c>[…]</c> block.</summary>
    private static string EscapeInline(TypstContent content)
    {
        var writer = new TypstMarkupWriter();
        using (writer.ContentBlock())
        {
            content.Write(writer);
        }

        var markup = writer.ToMarkup().Trim();
        return markup.Length >= 2 ? markup[1..^1] : string.Empty;
    }

    private static string AlignmentName(TypstAlignment alignment) => alignment switch
    {
        TypstAlignment.Left => "left",
        TypstAlignment.Center => "center",
        TypstAlignment.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "unknown alignment"),
    };

    private static string DirectionName(TypstDirection direction) => direction switch
    {
        TypstDirection.LeftToRight => "ltr",
        TypstDirection.RightToLeft => "rtl",
        TypstDirection.TopToBottom => "ttb",
        TypstDirection.BottomToTop => "btt",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unknown direction"),
    };

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AddArgument(List<string> arguments, string name, string? value)
    {
        if (value is not null)
        {
            arguments.Add($"{name}: {value}");
        }
    }

    /// <summary>Sets the language Typst reads the document in, as <c>#set text(lang: …)</c>.</summary>
    /// <param name="language">The language tag, for example <see cref="TypstLanguageTag.Japanese"/>.</param>
    /// <remarks>
    /// <see cref="TypstMetadata.Language"/> writes the same rule through the document metadata; this
    /// one is the explicit form, for a document whose language is not part of its metadata, and it
    /// is written after every style rule, so it wins over a <see cref="TypstDocumentStyle"/> that
    /// set a language of its own.
    /// </remarks>
    public TypstDocument Language(TypstLanguageTag language)
        => Set($"text(lang: {LinkNode.Quote(language.Code)})");

    /// <summary>
    /// Appends a block whose paragraphs take <paramref name="direction"/> as their base direction.
    /// </summary>
    /// <param name="direction">The base direction of the block's paragraphs.</param>
    /// <param name="content">The block's content.</param>
    /// <remarks>
    /// <para>
    /// Emitted as <c>#block[#set text(dir: …) …]</c>. The rule encloses the paragraphs instead of
    /// sitting in one, because a paragraph resolves <c>dir</c> from its style chain and
    /// <c>#text(dir: …)</c> inside a left-to-right paragraph is an inline directional embedding, not
    /// a base direction. Spec 43 §4.2 carries the reasoning.
    /// </para>
    /// <para>
    /// A document that does not ask for a direction emits nothing: the compiler's default is left to
    /// right, as it was before this method existed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public TypstDocument Direction(TypstDirection direction, TypstContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _body.Add(new DirectedBlockNode(TypstDirectionToken.Of(direction), content));
        return this;
    }

    /// <summary>
    /// Appends a <see cref="TypstSection"/>: its content inside a content block, with the header,
    /// footer, watermark and page numbering it declares set for that block alone.
    /// </summary>
    /// <param name="section">The section, built with <see cref="TypstSection.Create"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    /// <remarks>
    /// The block is the whole mechanism: a <c>#set page</c> inside it applies to the pages the
    /// section's content lands on and stops applying where the block closes, so the document's own
    /// setup and its <see cref="TypstDocumentStyle"/> are untouched outside the section.
    /// </remarks>
    public TypstDocument Section(TypstSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        _body.Add(new SectionNode(section));
        return this;
    }

    /// <summary>
    /// Appends a section configured in place, and fills its content from the same call.
    /// </summary>
    /// <param name="configure">Sets the section's header, footer, watermark and numbering.</param>
    /// <param name="body">Writes the section's content; null leaves the section empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <c>document.Section(s => s.Footer("Partie I — page {page}"), b => b.Heading(1, "Contexte"))</c>
    /// is the same section as one built with <see cref="TypstSection.Create"/> and passed to
    /// <see cref="Section(TypstSection)"/>.
    /// </remarks>
    public TypstDocument Section(Action<TypstSection> configure, Action<TypstDocument>? body = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var section = TypstSection.Create();
        configure(section);
        body?.Invoke(section.Body);
        return Section(section);
    }
}
