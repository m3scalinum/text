using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// CJK and bidirectional text, compiled by the real compiler and measured in the layout: where the
/// lines end, in what order the glyphs of a mixed run sit, and which characters a line may start
/// with.
/// </summary>
/// <remarks>
/// <para>
/// The proof is geometry, not a screenshot: the SVG export places every glyph at an absolute
/// <c>x</c>/<c>y</c> in points with the origin at the page's top-left, and the tests read those
/// placements back. A line that is justified ends on the same right edge as its neighbours; a
/// paragraph whose base direction is right to left has its last neutral punctuation at the leftmost
/// <c>x</c> of the line; digits and a Latin word keep their left-to-right order inside an Arabic
/// sentence.
/// </para>
/// <para>
/// <see cref="CompilationResult.ExtractTextPages"/> is not used as a bidi oracle. The layout hands
/// its text back in visual order, with a combining mark carried on a baseline of its own, so the page
/// text of an Arabic paragraph reads reversed and breaks a word across "lines" that are one line on
/// the page. It is used here only where the layout is left to right and the point is the words.
/// </para>
/// <para>
/// The scripts need a face the embedded set does not carry: Typst ships Libertinus Serif, New
/// Computer Modern, its math face and DejaVu Sans Mono, and none of them draws Japanese or Arabic.
/// Each test that needs one asks the host's font book, and skips with the reason when it finds
/// nothing — a document that renders tofu must not pass as a document that rendered.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CjkAndBidiIntegrationTests
{
    private const string Japanese =
        "東京は日本の首都です。人口は約千四百万人で、日本で最も大きな都市であり、経済と文化の中心地でもあります。"
        + "春には桜が咲き、多くの人が公園を訪れます。秋には紅葉が美しく、観光客が世界中から集まります。";

    private const string JapaneseWithPunctuation =
        "彼は「Typst 0.15」を使って、日本語の文書を作りました。結果は、良好でした。さらに、検討を続けます。"
        + "結論は、明確です。作業は、順調です。評価は、高いです。計画は、堅実です。予算は、妥当です。"
        + "日程は、厳しいです。品質は、安定しています。納期は、守ります。";

    private const string Arabic = "العدد 42 يساوي XLII حرفًا.";

    private const string Hebrew = "שלום עולם 2026.";

    private const string CitingArabic = "Le mot arabe العدد 42 signifie « le nombre ».";

    /// <summary>The characters no line may begin with, from the kinsoku rules UAX #14 encodes.</summary>
    private static readonly char[] ClosingPunctuation = ['。', '、', '，', '．', '！', '？', '」', '』', '）', '．'];

    private static readonly string[] JapaneseFaces =
    [
        "MS Gothic", "MS PGothic", "Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Mincho", "Yu Mincho",
        "Noto Sans CJK JP", "Noto Sans JP", "Noto Serif CJK JP", "IPAGothic", "TakaoGothic",
        "Hiragino Sans", "Source Han Sans JP", "Source Han Sans",
    ];

    private static readonly string[] RightToLeftFaces =
    [
        "Arial", "Segoe UI", "Tahoma", "Noto Sans Arabic", "Noto Naskh Arabic", "Amiri", "Geeza Pro",
        "David", "Noto Sans Hebrew", "DejaVu Sans", "FreeSerif", "Liberation Serif", "Times New Roman",
    ];

    private static readonly Lazy<IReadOnlyList<string>> InstalledFamilies = new(() =>
    {
        using var world = new TypstWorldBuilder()
            .AddSystemFonts()
            .AddSource("main.typ", "x")
            .Build();

        return world.FontFamilies;
    });

    // ---- CJK ------------------------------------------------------------------------------

    [Fact]
    public void A_japanese_paragraph_justifies_and_no_line_starts_with_closing_punctuation()
    {
        var family = RequireFace(JapaneseFaces, "a Japanese");

        using var justified = Compile(JapaneseDocument(family, justify: true, Japanese));
        using var ragged = Compile(JapaneseDocument(family, justify: false, Japanese));

        var lines = Lines(justified);
        Assert.True(lines.Length >= 3, "expected three lines or more, got " + lines.Length);

        foreach (var line in lines)
        {
            Assert.DoesNotContain(line[0], ClosingPunctuation);
        }

        // Justification fills the measure: every full line ends on the same right edge, at the text
        // width of an A5 page with 1.5 cm margins (377.01pt) less the advance of the last glyph.
        var justifiedEdges = RightEdges(justified);
        var raggedEdges = RightEdges(ragged);

        Assert.True(justifiedEdges.Count >= 2, "expected two lines at least");
        Assert.True(
            Math.Abs(justifiedEdges[0] - 367.01) < 0.5,
            $"first justified line ends at {justifiedEdges[0]:0.##}, expected 367.01");
        Assert.True(
            Math.Abs(justifiedEdges[1] - justifiedEdges[0]) < 0.05,
            $"justified lines must share their right edge: {justifiedEdges[0]:0.##} and {justifiedEdges[1]:0.##}");

        // The same paragraph, not justified, stops short of the measure and breaks elsewhere.
        Assert.True(
            raggedEdges[0] < 363,
            $"an unjustified line should stop before the measure, it ends at {raggedEdges[0]:0.##}");
        Assert.NotEqual(string.Join('\n', lines), string.Join('\n', Lines(ragged)));
    }

    [Fact]
    public void Full_width_punctuation_survives_justification_without_an_inserted_space()
    {
        var family = RequireFace(JapaneseFaces, "a Japanese");

        using var result = Compile(JapaneseDocument(family, justify: true, JapaneseWithPunctuation));

        // The measure forces four lines, and the punctuation is never moved to the next one: 。 and 、
        // stay on the line they close, which is what UAX #14 asks for and what a Western line breaker
        // — which has no rule for them — would not do.
        var lines = Lines(result);
        Assert.True(lines.Length >= 3, "expected three lines or more, got " + lines.Length);

        foreach (var line in lines)
        {
            Assert.DoesNotContain(line[0], ClosingPunctuation);
        }

        // And the text is the text: no space was inserted at a full-width mark, and none was eaten at
        // the Latin boundaries either.
        Assert.Equal(JapaneseWithPunctuation, string.Concat(lines));
        Assert.Contains("「Typst 0.15」", string.Concat(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void The_language_tag_selects_the_hyphenation_dictionary()
    {
        var english = Compile(LatinDocument(TypstLanguageTag.English));
        var japanese = Compile(LatinDocument(TypstLanguageTag.Japanese));

        using (english)
        using (japanese)
        {
            var englishLines = Lines(english);
            var japaneseLines = Lines(japanese);

            // English hyphenates: "therefore" is broken at a syllable boundary, so one line ends on
            // "there" and the next one starts on "fore". Japanese has no dictionary at all, so the
            // word moves to the next line whole.
            Assert.DoesNotContain(englishLines, line => line.Contains("therefore", StringComparison.Ordinal));
            Assert.Contains(englishLines, line => line.Contains("fore study", StringComparison.Ordinal));

            Assert.Contains(japaneseLines, line => line.Contains("therefore study", StringComparison.Ordinal));
            Assert.NotEqual(string.Join('\n', englishLines), string.Join('\n', japaneseLines));
        }
    }

    [Fact]
    public void Cjk_latin_spacing_inserts_a_quarter_em_at_each_boundary()
    {
        var family = RequireFace(JapaneseFaces, "a Japanese");

        using var auto = Compile(JapaneseDocument(family, justify: false, "彼はTypstを使って文書を作りました。"));
        using var none = Compile(JapaneseDocument(
            family,
            justify: false,
            "彼はTypstを使って文書を作りました。",
            "text(cjk-latin-spacing: none)"));

        // Two boundaries — は|T and t|を — at 0.25em each, on a 10pt face: 5pt of extra measure.
        var difference = RightEdges(auto)[0] - RightEdges(none)[0];

        Assert.True(
            Math.Abs(difference - 5) < 0.5,
            $"expected 5pt between the two variants, measured {difference:0.##}");

        // Both render the sentence as written: the spacing is inserted by the layout, not the text.
        Assert.Equal("彼はTypstを使って文書を作りました。", string.Concat(Lines(auto)));
    }

    // ---- Right to left --------------------------------------------------------------------

    [Fact]
    public void An_arabic_paragraph_keeps_its_digits_and_its_latin_word_the_right_way_round()
    {
        var family = RequireFace(RightToLeftFaces, "an Arabic");

        using var declared = Compile(ParagraphDocument(family, Arabic, TypstLanguageTag.Arabic));
        using var undeclared = Compile(ParagraphDocument(family, Arabic, language: null));

        var line = MainLine(declared);

        // The sentence opens on العدد — five letters — and the number 42 follows it, so with a
        // right-to-left base the word is the rightmost run and the digits sit to its left, each run
        // keeping its own direction inside the island.
        var firstWord = line.Where(run => run.Count == 5).OrderByDescending(run => run.Max(g => g.X)).First();
        var digits = line.Where(run => run.Count == 2).OrderByDescending(run => run.Max(g => g.X)).First();
        var latin = line.Where(run => run.Count == 4).Single();
        var middleWord = line.Where(run => run.Count == 5).OrderBy(run => run.Min(g => g.X)).First();

        Assert.True(
            firstWord.Max(g => g.X) > 370,
            $"the first word must end at the right margin, it ends at {firstWord.Max(g => g.X):0.##}pt");

        Assert.True(
            digits.Max(g => g.X) < firstWord.Min(g => g.X),
            "the digits must sit left of the Arabic word they follow");

        Assert.True(
            digits[0].X < digits[1].X,
            $"42 must read left to right, glyphs at {digits[0].X:0.##} and {digits[1].X:0.##}");

        Assert.True(
            latin[0].X < latin[^1].X,
            $"XLII must read left to right, glyphs from {latin[0].X:0.##} to {latin[^1].X:0.##}");

        Assert.True(
            latin.Max(g => g.X) < middleWord.Min(g => g.X),
            "XLII must sit left of the word it follows in the right-to-left flow");

        // The same paragraph with no language and no direction: Typst resolves `dir: auto` from the
        // language, and the default language is left to right, so the line is laid out from the left
        // margin instead of the right one. The words come out in the same order — one right-to-left
        // island inside an LTR paragraph reads the same way — but the paragraph is not turned around.
        Assert.True(
            LeftEdge(undeclared) < 100,
            $"without a language the paragraph must start at the left margin, it starts at {LeftEdge(undeclared):0.##}pt");
        Assert.True(
            LeftEdge(declared) > 250,
            $"with an Arabic language the paragraph must start at the right margin, it starts at {LeftEdge(declared):0.##}pt");
    }

    [Fact]
    public void A_hebrew_paragraph_runs_from_the_right_and_ends_its_period_on_the_left()
    {
        var family = RequireFace(RightToLeftFaces, "a Hebrew");

        using var result = Compile(ParagraphDocument(family, Hebrew, TypstLanguageTag.Hebrew));

        var line = MainLine(result);

        // "שלום עולם 2026." with a right-to-left base: the first word is rightmost, the digits keep
        // their left-to-right order, and the full stop — a neutral at the end of an RTL paragraph —
        // resolves to the leftmost position of the line.
        var period = line.Single(run => run.Count == 1)[0];
        var digits = line.Single(run => run.Count == 4);
        var words = line.Single(run => run.Count == 8);

        Assert.True(period.X < digits.Min(g => g.X), "the period must be the leftmost glyph");
        Assert.True(digits.Max(g => g.X) < words.Min(g => g.X), "the digits must sit left of the Hebrew words");
        Assert.True(digits[0].X < digits[^1].X, "2026 must read left to right");
    }

    [Fact]
    public void A_right_to_left_block_resolves_the_trailing_neutrals_the_other_way()
    {
        var family = RequireFace(RightToLeftFaces, "an Arabic");

        using var leftToRight = Compile(ParagraphDocument(family, CitingArabic, TypstLanguageTag.French));
        using var rightToLeft = Compile(BlockDocument(family, CitingArabic));

        var leftToRightText = SingleLine(leftToRight);
        var rightToLeftText = SingleLine(rightToLeft);

        // A French sentence citing Arabic, left to right: the guillemets and the full stop stay in
        // French order, and the Arabic words are drawn reversed, as Arabic is.
        Assert.Contains("Le mot arabe", leftToRightText, StringComparison.Ordinal);
        Assert.Contains("ددعلا", leftToRightText, StringComparison.Ordinal);
        Assert.Contains("nombre ».", leftToRightText, StringComparison.Ordinal);

        // The same sentence with a right-to-left base direction: the paragraph's trailing neutrals
        // now resolve to the right-to-left context, so the full stop moves inside the closing
        // guillemet. Nothing in the source changed.
        Assert.Contains("nombre.»", rightToLeftText, StringComparison.Ordinal);
        Assert.DoesNotContain("nombre ».", rightToLeftText, StringComparison.Ordinal);
    }

    // ---- Documents ------------------------------------------------------------------------

    private static TypstDocument JapaneseDocument(string family, bool justify, string text, string? extraRule = null)
    {
        var document = TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create()
                .Page(page => page.Paper("a5").Margin(TypstLength.Cm(1.5)))
                .Body(body => body.Font(family).Size(TypstLength.Pt(10)).Justify(justify))
                .Language(TypstLanguageTag.Japanese)
                .Build())
            .Paragraph(text);

        return extraRule is null ? document : document.Set(extraRule);
    }

    private static TypstDocument ParagraphDocument(string family, string text, TypstLanguageTag? language)
    {
        var style = TypstDocumentStyle.Create()
            .Page(page => page.Paper("a5").Margin(TypstLength.Cm(1.5)))
            .Body(body => body.Font(family).Size(TypstLength.Pt(10)));

        if (language is { } tag)
        {
            style = style.Language(tag);
        }

        return TypstDocument.Create().Apply(style.Build()).Paragraph(text);
    }

    private static TypstDocument BlockDocument(string family, string text)
        => TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create()
                .Page(page => page.Paper("a5").Margin(TypstLength.Cm(1.5)))
                .Body(body => body.Font(family).Size(TypstLength.Pt(10)))
                .Language(TypstLanguageTag.French)
                .Build())
            .Direction(TypstDirection.RightToLeft, new TypstContent().Text(text));

    private static TypstDocument LatinDocument(TypstLanguageTag language)
        => TypstDocument.Create()
            .Apply(TypstDocumentStyle.Create()
                .Page(page => page.Size(TypstLength.Cm(8), TypstLength.Cm(6)).Margin(TypstLength.Cm(1)))
                .Body(body => body.Font("Libertinus Serif").Size(TypstLength.Pt(10)).Justify())
                .Language(language)
                .Build())
            .Paragraph(
                "The internationalization of the document model deserves consideration and therefore study.");

    // ---- Measurements ---------------------------------------------------------------------

    private static CompilationResult Compile(TypstDocument document)
    {
        using var world = new TypstWorldBuilder()
            .AddSystemFonts()
            .AddSource("main.typ", document.ToMarkup())
            .Build();

        return new TypstCompiler().Compile(world, new TypstCompilationOptions());
    }

    private static string[] Lines(CompilationResult result)
        => result.ExtractTextPages()[0]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string SingleLine(CompilationResult result)
        => string.Join(' ', Lines(result).Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries))));

    /// <summary>The right edge of every line, in points from the page's left edge.</summary>
    private static List<double> RightEdges(CompilationResult result)
        => Glyphs(result)
            .GroupBy(glyph => Math.Round(glyph.Y, 1))
            .OrderBy(group => group.Key)
            .Select(group => group.Max(glyph => glyph.X))
            .ToList();

    /// <summary>The leftmost glyph of the line that carries the most glyphs.</summary>
    private static double LeftEdge(CompilationResult result)
        => MainLine(result).SelectMany(run => run).Min(glyph => glyph.X);

    /// <summary>The glyph runs of the line that carries the most glyphs, in document order.</summary>
    private static List<List<Glyph>> MainLine(CompilationResult result)
        => Runs(result)
            .GroupBy(run => Math.Round(run[0].Y, 1))
            .OrderByDescending(group => group.Sum(run => run.Count))
            .First()
            .ToList();

    private static List<List<Glyph>> Runs(CompilationResult result)
        => Runs(Encoding.UTF8.GetString(result.ExportSvg()));

    private static IEnumerable<Glyph> Glyphs(CompilationResult result)
        => Runs(result).SelectMany(run => run);

    /// <summary>
    /// Reads the glyph placements of an SVG page: one group per run, one <c>use</c> per glyph, each
    /// at an absolute position in points once the group's own translation is applied.
    /// </summary>
    private static List<List<Glyph>> Runs(string svg)
    {
        var runs = new List<List<Glyph>>();
        var cursor = 0;

        while (true)
        {
            var start = svg.IndexOf("<g transform=", cursor, StringComparison.Ordinal);

            if (start < 0)
            {
                return runs;
            }

            var open = svg.IndexOf('>', start);
            var close = svg.IndexOf("</g>", open, StringComparison.Ordinal);
            var header = svg[start..open];

            if (close < 0 || !header.Contains("matrix(1 0 0 -1", StringComparison.Ordinal))
            {
                cursor = open + 1;
                continue;
            }

            var matrix = header[(header.IndexOf("matrix(", StringComparison.Ordinal) + 7)..];
            var numbers = matrix[..matrix.IndexOf(')')].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var translateX = double.Parse(numbers[^2], CultureInfo.InvariantCulture);
            var translateY = double.Parse(numbers[^1], CultureInfo.InvariantCulture);

            var run = new List<Glyph>();
            var body = svg[open..close];
            var position = 0;

            while (true)
            {
                var use = body.IndexOf("<use ", position, StringComparison.Ordinal);

                if (use < 0)
                {
                    break;
                }

                var end = body.IndexOf("/>", use, StringComparison.Ordinal);
                var element = body[use..end];
                run.Add(new Glyph(
                    translateX + Attribute(element, "x"),
                    translateY + Attribute(element, "y")));
                position = end + 2;
            }

            if (run.Count > 0)
            {
                runs.Add(run);
            }

            cursor = close + 4;
        }
    }

    private static double Attribute(string element, string name)
    {
        var marker = " " + name + "=" + '"';
        var start = element.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            return 0;
        }

        start += marker.Length;
        return double.Parse(element[start..element.IndexOf('"', start)], CultureInfo.InvariantCulture);
    }

    private static string RequireFace(IReadOnlyList<string> candidates, string script)
    {
        var family = candidates.FirstOrDefault(candidate =>
            InstalledFamilies.Value.Contains(candidate, StringComparer.OrdinalIgnoreCase));

        Assert.SkipWhen(
            family is null,
            $"no {script} face on this host: install one of {string.Join(", ", candidates)}, "
            + "or pass its file through TypstWorldBuilder.AddFont, before this render can be believed");

        return family!;
    }

    /// <summary>One glyph placement, in points from the page's top-left corner.</summary>
    private readonly record struct Glyph(double X, double Y);
}
