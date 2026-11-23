using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Variable fonts: what <c>#set text(weight: …)</c> actually does to the layout, measured on the
/// glyph placements of the SVG export.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is narrow and geometric: a face with a <c>wght</c> axis renders an
/// intermediate weight as an interpolated instance of its own outlines, and a static face cannot.
/// The proof is that the same line, in the same family at the same size, occupies a different width
/// at 400 and at 350 — and that a static family occupies exactly the same width at both, because the
/// compiler serves it the 400 cut.
/// </para>
/// <para>
/// The variable face comes from the host, because the fonts embedded in the compiler are static and
/// cannot show the axis. The test asks <see cref="TypstWorld.Fonts"/> for the range first and skips
/// with a reason when the host has no variable face: a document that fell back to a static cut must
/// not pass as one that interpolated.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class VariableFontTests
{
    /// <summary>Font files that carry a <c>wght</c> axis, most common host first.</summary>
    private static readonly string[] VariableFontCandidates =
    [
        @"C:\Windows\Fonts\Bahnschrift.ttf",
        @"C:\Windows\Fonts\SitkaVF.ttf",
        @"C:\Windows\Fonts\SegUIVar.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans[wdth,wght].ttf",
        "/usr/share/fonts/variable/Inter[opsz,wght].ttf",
        "/usr/share/fonts/truetype/roboto/Roboto[wdth,wght].ttf",
        "/System/Library/Fonts/SFNS.ttf",
    ];

    /// <summary>A line short enough to stay on one line, with glyphs whose advances differ by weight.</summary>
    private const string Line = "Handgloves wrapping quays 0123456789";

    /// <summary>The embedded families, which are static and must keep behaving as they did.</summary>
    private const string StaticFamily = "Libertinus Serif";

    [Fact]
    public void The_inventory_covers_every_face_the_world_counts()
    {
        // No host font needed: the embedded faces are enough to prove the inventory answers at all,
        // and this is the test that runs on a machine with no variable font installed.
        using var world = new TypstWorldBuilder().AddSource("main.typ", Source(StaticFamily, 400)).Build();

        Assert.Equal(world.FontCount, world.Fonts.Count);
        Assert.Contains(world.Fonts, face => face.Family == StaticFamily && !face.IsVariable);
        Assert.All(world.Fonts, face => Assert.InRange(face.Weight, 100, 900));
    }

    [Fact]
    public void A_variable_face_reports_a_weight_axis()
    {
        using var host = new HostFont(FindVariableFont());
        using var world = host.Builder.AddSource("main.typ", Source(host.Family, 400)).Build();
        var face = world.Fonts.Single(candidate => candidate.Family == host.Family);

        Assert.True(face.IsVariable, $"{host.Family} must be reported as a variable face");

        var weight = face.Axis("wght");
        Assert.NotNull(weight);
        Assert.True(weight!.Max > weight.Min, $"{host.Family} must advertise a range, got {weight}");
    }

    [Fact]
    public void An_intermediate_weight_draws_different_outlines()
    {
        using var host = new HostFont(FindVariableFont());
        var axis = host.WeightAxis;

        Assert.SkipWhen(
            axis.Min > 350 || axis.Max < 700,
            $"{host.Family}'s weight axis runs {axis.Min}-{axis.Max}, which cannot show 350 and 700");

        var lighter = host.Outlines(350);
        var regular = host.Outlines(400);
        var heavier = host.Outlines(700);

        Assert.NotEqual(regular, lighter);
        Assert.NotEqual(regular, heavier);
        Assert.NotEqual(lighter, heavier);
    }

    [Fact]
    public void An_intermediate_weight_is_not_the_nearer_end_of_the_range()
    {
        using var host = new HostFont(FindVariableFont());
        var axis = host.WeightAxis;

        Assert.SkipWhen(
            axis.Min > 300 || axis.Max < 400,
            $"{host.Family}'s weight axis runs {axis.Min}-{axis.Max}, which cannot show 300 and 400");

        // 350 is one step from 300 and five from 400. If the compiler snapped to a shipped cut, the
        // outline would be an exact copy of one of them; interpolating makes it a third shape.
        var minimum = host.Outlines((int)Math.Round(axis.Min));
        var regular = host.Outlines(400);
        var between = host.Outlines(350);

        Assert.NotEqual(minimum, between);
        Assert.NotEqual(regular, between);
    }

    [Fact]
    public void A_weight_above_the_axis_range_draws_the_top_of_the_range()
    {
        using var host = new HostFont(FindVariableFont());
        var axis = host.WeightAxis;

        Assert.SkipWhen(axis.Max >= 900, $"{host.Family}'s axis reaches {axis.Max}, so 900 is inside it");

        var ceiling = host.Outlines((int)Math.Round(axis.Max));
        var beyond = host.Outlines(900);

        Assert.Equal(ceiling, beyond);
    }

    [Fact]
    public void A_static_family_ignores_an_intermediate_weight_and_still_bolds()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", Source(StaticFamily, 400))
            .Build();

        Assert.DoesNotContain(world.Fonts, face => face.IsVariable);

        var regular = Svg(StaticFamily, 400);
        var intermediate = Svg(StaticFamily, 350);
        var bold = Svg(StaticFamily, 700);

        // The same cut is served for both weights, so the layout and the outlines are identical...
        Assert.Equal(MeasureWidth(regular), MeasureWidth(intermediate), 6);
        Assert.Equal(OutlineDigest(regular), OutlineDigest(intermediate));

        // ...and the bold cut is still reachable, which is what the family could do before.
        Assert.True(
            MeasureWidth(bold) > MeasureWidth(regular) + 0.5,
            $"the bold cut must still differ: {MeasureWidth(bold):0.###}pt against {MeasureWidth(regular):0.###}pt");
        Assert.NotEqual(OutlineDigest(regular), OutlineDigest(bold));
    }

    [Fact]
    public void A_quoted_weight_is_not_a_weight()
    {
        // `weight:` takes an integer or one of the nine names. A numeric string is neither, and the
        // model's TypstTextStyle.Weight quotes its argument, so this is the shape a caller reaches
        // by accident. The compiler refuses it instead of silently ignoring it — which is the
        // behaviour worth pinning, because a silent fallback would render at 400 and look correct.
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "#set text(weight: \"350\")\n\nHello")
            .Build();

        Assert.Throws<TypstCompilationException>(() => new TypstCompiler().Compile(world));
    }

    // ---- Fixtures and measurements ---------------------------------------------------------

    private static string FindVariableFont()
    {
        var file = VariableFontCandidates.FirstOrDefault(File.Exists);

        Assert.SkipWhen(
            file is null,
            "no variable font on this host: install one of " + string.Join(", ", VariableFontCandidates)
            + ", or point the test at a file with a wght axis, before this render can be believed");

        return file!;
    }

    /// <summary>The SVG export of the paragraph, in the given family and weight.</summary>
    private static string Svg(string family, int weight)
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Source(family, weight)).Build();
        using var result = new TypstCompiler().Compile(world);

        return Encoding.UTF8.GetString(result.ExportSvg());
    }

    /// <summary>The width of the single line the paragraph produces, in points.</summary>
    private static double MeasureWidth(string svg)
    {
        var glyphs = Glyphs(svg);

        Assert.NotEmpty(glyphs);

        return glyphs.Max(glyph => glyph.X) - glyphs.Min(glyph => glyph.X);
    }

    /// <summary>
    /// A digest of the glyph outlines the page defines — the <c>d</c> attribute of every path the
    /// export writes, in the order it writes them.
    /// </summary>
    /// <remarks>
    /// This is the measure that answers "did the weight reach the outlines". A variable face at 350 is
    /// an interpolated instance, so its paths differ from the 400 instance's; a static family served
    /// at 350 is the 400 cut itself, so its paths are byte-identical. The advances are deliberately
    /// not the measure: a design can keep them constant across the weight axis, and Bahnschrift does —
    /// the line occupies the same width at every weight while the stems thicken.
    /// </remarks>
    private static string OutlineDigest(string svg)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Paths(svg))));

        return Convert.ToHexString(digest)[..16];
    }

    /// <summary>The <c>d</c> attributes of the outlines the page defines.</summary>
    private static List<string> Paths(string svg)
    {
        var paths = new List<string>();
        var cursor = 0;

        while (true)
        {
            var start = svg.IndexOf("<path ", cursor, StringComparison.Ordinal);

            if (start < 0)
            {
                return paths;
            }

            var end = svg.IndexOf("/>", start, StringComparison.Ordinal);
            var attribute = svg.IndexOf(" d=\"", start, StringComparison.Ordinal);

            if (attribute >= 0 && attribute < end)
            {
                var begin = attribute + 4;
                paths.Add(svg[begin..svg.IndexOf('"', begin)]);
            }

            cursor = end + 2;
        }
    }

    private static string Source(string family, int weight)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"""
            #set page(paper: "a5", margin: 1.5cm)
            #set par(justify: false)
            #set text(font: "{family}", size: 10pt, weight: {weight})

            {Line}
            """);

    /// <summary>
    /// A copy of one variable font in a directory of its own, so the world a test builds resolves that
    /// file and nothing else of the host's, and the temporary tree is removed afterwards.
    /// </summary>
    private sealed class HostFont : IDisposable
    {
        private readonly string _directory;
        private TypstFontFace? _face;

        public HostFont(string file)
        {
            Origin = file;
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "sharp-typst-variable-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            System.IO.File.Copy(file, System.IO.Path.Combine(_directory, System.IO.Path.GetFileName(file)));
        }

        /// <summary>The file the copy came from, for a skip message.</summary>
        public string Origin { get; }

        public TypstWorldBuilder Builder => new TypstWorldBuilder().AddFontDirectory(_directory);

        /// <summary>
        /// The family the file reports, read from the world rather than from the file name: the name a
        /// family answers to is the font's own.
        /// </summary>
        public string Family => Face.Family;

        public TypstFontAxis WeightAxis => Face.Axis("wght")!;

        /// <summary>The width of the line, in points — invariant across the axis on some designs.</summary>
        public double Width(int weight) => MeasureWidth(SvgAt(weight));

        /// <summary>A digest of the outlines the page draws at this weight.</summary>
        public string Outlines(int weight) => OutlineDigest(SvgAt(weight));

        private string SvgAt(int weight)
        {
            using var world = Builder.AddSource("main.typ", Source(Family, weight)).Build();
            using var result = new TypstCompiler().Compile(world);

            return Encoding.UTF8.GetString(result.ExportSvg());
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A handle the compiler still holds: the temporary tree goes with the next reboot.
            }
        }

        private TypstFontFace Face
        {
            get
            {
                if (_face is not null)
                {
                    return _face;
                }

                using var world = Builder.AddSource("main.typ", "x").Build();
                var face = world.Fonts.FirstOrDefault(
                    candidate => candidate.IsVariable && candidate.Axis("wght") is not null);

                Assert.SkipWhen(face is null, $"{System.IO.Path.GetFileName(Origin)} exposes no wght axis");

                return _face = face!;
            }
        }
    }

    /// <summary>
    /// Reads the glyph placements of an SVG page: one group per run, one <c>use</c> per glyph, each
    /// at an absolute position in points once the group's translation is applied. Only the leading
    /// dimension is needed here, and the origin of the page is a constant offset.
    /// </summary>
    private static List<(double X, double Y)> Glyphs(string svg)
    {
        var glyphs = new List<(double X, double Y)>();
        var cursor = 0;

        while (true)
        {
            var start = svg.IndexOf("<g transform=", cursor, StringComparison.Ordinal);

            if (start < 0)
            {
                return glyphs;
            }

            var open = svg.IndexOf('>', start);
            var close = svg.IndexOf("</g>", open, StringComparison.Ordinal);
            var header = svg[start..open];

            if (close < 0)
            {
                return glyphs;
            }

            var translated = header.Contains("matrix(1 0 0 -1", StringComparison.Ordinal);

            if (!translated)
            {
                cursor = open + 1;
                continue;
            }

            var matrix = header[(header.IndexOf("matrix(", StringComparison.Ordinal) + 7)..];
            var numbers = matrix[..matrix.IndexOf(')')].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var translateX = double.Parse(numbers[^2], CultureInfo.InvariantCulture);
            var translateY = double.Parse(numbers[^1], CultureInfo.InvariantCulture);

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
                glyphs.Add((translateX + Attribute(element, "x"), translateY + Attribute(element, "y")));
                position = end + 2;
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
}
