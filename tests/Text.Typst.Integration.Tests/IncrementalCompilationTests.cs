using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Incremental compilation against the real native library: an edited document compiles against
/// the state Typst memoized for the previous one, and <see cref="TypstCompiler.CompileChanged"/>
/// reports the pages that actually moved.
/// </summary>
/// <remarks>
/// A page fingerprint is computed from the laid-out page, so only a real layout can prove that
/// editing a sentence does not mark the pages after it — the spans of every later element shift
/// with the edit, and a digest that included them would report the whole document as changed.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class IncrementalCompilationTests(ITestOutputHelper output)
{
    /// <summary>
    /// A three-page document. `first` is the sentence on page one, `second` the one on page two,
    /// so a test can edit either without changing the page count.
    /// </summary>
    private static string ThreePages(string first, string second) => $"""
        #set page(paper: "a4")
        #heading(level: 1)[First]
        {first}
        #pagebreak(weak: true)
        #heading(level: 1)[Second]
        {second}
        #pagebreak(weak: true)
        #heading(level: 1)[Third]
        #lorem(120)
        """;

    [Fact]
    public void Editing_the_first_page_reports_only_the_first_page()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", ThreePages("The original sentence.", "Second page body."))
            .Build();

        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler();

        using (var baseline = compiler.CompileChanged(world, state))
        {
            Assert.True(baseline.IsFirstCompile);
            Assert.Equal(3, baseline.Document.PageCount);
            Assert.Equal([0, 1, 2], baseline.ChangedPages);
        }

        world.SetSource("main.typ", ThreePages("The edited sentence.", "Second page body."));

        using var changed = compiler.CompileChanged(world, state);

        Assert.False(changed.IsFirstCompile);
        Assert.Equal([0], changed.ChangedPages);
        Assert.Empty(changed.RemovedPages);

        // The document is complete: every page is there, changed or not.
        Assert.Equal(3, changed.Document.PageCount);
    }

    [Fact]
    public void Editing_the_second_page_reports_only_the_second_page()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", ThreePages("The original sentence.", "Second page body."))
            .Build();

        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler();

        using (compiler.CompileChanged(world, state))
        {
        }

        world.SetSource("main.typ", ThreePages("The original sentence.", "Second page, rewritten."));

        using var changed = compiler.CompileChanged(world, state);

        Assert.Equal([1], changed.ChangedPages);
    }

    [Fact]
    public void Compiling_the_same_world_twice_reports_no_changed_page()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", ThreePages("Stable.", "Stable too."))
            .Build();

        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler();

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.False(second.IsFirstCompile);
        Assert.Empty(second.ChangedPages);
        Assert.Empty(second.RemovedPages);
    }

    [Fact]
    public void A_lost_page_is_reported_as_removed()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", ThreePages("One.", "Two."))
            .Build();

        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler();

        using (compiler.CompileChanged(world, state))
        {
        }

        // Two pages instead of three: the third one is gone, not changed.
        world.SetSource("main.typ", "#heading[Only]\n#pagebreak(weak: true)\n#lorem(120)");

        using var changed = compiler.CompileChanged(world, state);

        Assert.Equal(2, changed.Document.PageCount);
        Assert.Equal([2], changed.RemovedPages);
    }

    [Fact]
    public void An_html_compilation_has_no_pages_to_compare()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Heading\n\nBody.")
            .Build();

        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler();

        using var changed = compiler.CompileChanged(
            world,
            state,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.True(changed.IsFirstCompile);
        Assert.Empty(changed.ChangedPages);
        Assert.Equal(0, state.PageCount);
    }

    /// <summary>
    /// The measurement behind the item: two compilations of one document with one sentence changed
    /// cost less than two cold ones.
    /// </summary>
    /// <remarks>
    /// Cold is made honest by <see cref="TypstCompiler.ClearCache"/>, which drops every memoized
    /// result the process holds — the state a fresh process starts with — instead of relying on
    /// this test being the first to compile anything. Two rounds are run, each with one cold pass
    /// and two warm ones, and the minimum of each side is compared: on a machine that runs this
    /// suite in parallel, a single reading measures the machine more than the compiler.
    /// The document is seeded with a fresh marker so no other test in the run can warm it.
    /// </remarks>
    [Fact]
    public void Two_compilations_with_one_sentence_changed_cost_less_than_two_cold_ones()
    {
        var seed = Guid.NewGuid().ToString("N")[..8];
        var compiler = new TypstCompiler();
        var cold = new List<TimeSpan>();
        var warm = new List<TimeSpan>();
        var coldInside = new List<TimeSpan>();
        var warmInside = new List<TimeSpan>();

        for (var round = 0; round < 2; round++)
        {
            using var world = new TypstWorldBuilder()
                .AddSource("main.typ", Report(seed, round, edit: 0))
                .Build();

            var state = new TypstIncrementalState();

            compiler.ClearCache();

            var coldClock = Stopwatch.StartNew();

            using (var result = compiler.CompileChanged(world, state))
            {
                coldClock.Stop();
                cold.Add(coldClock.Elapsed);
                coldInside.Add(result.Document.Metrics.CompileDuration);
            }

            // One sentence, on page one, of an eight-page document. Two edits per round, so the
            // warm side has twice as many samples as the cold side: the reading that survives is
            // the one least disturbed by whatever else this run is doing.
            for (var edit = 1; edit <= 2; edit++)
            {
                world.SetSource("main.typ", Report(seed, round, edit));

                var warmClock = Stopwatch.StartNew();

                using (var result = compiler.CompileChanged(world, state))
                {
                    warmClock.Stop();
                    warm.Add(warmClock.Elapsed);
                    warmInside.Add(result.Document.Metrics.CompileDuration);
                }
            }
        }

        var coldMin = cold.Min();
        var warmMin = warm.Min();
        var coldInsideMin = coldInside.Min();
        var warmInsideMin = warmInside.Min();

        output.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "8 pages, one sentence changed: wall cold {0:0.0} ms / warm {1:0.0} ms ({2:0.0}x); "
                    + "inside Typst cold {3:0.0} ms / warm {4:0.0} ms ({5:0.0}x); "
                    + "cold passes {6}; warm passes {7}",
                coldMin.TotalMilliseconds,
                warmMin.TotalMilliseconds,
                coldMin.TotalMilliseconds / warmMin.TotalMilliseconds,
                coldInsideMin.TotalMilliseconds,
                warmInsideMin.TotalMilliseconds,
                coldInsideMin.TotalMilliseconds / warmInsideMin.TotalMilliseconds,
                string.Join(" / ", cold.Select(time => $"{time.TotalMilliseconds:0.0}")),
                string.Join(" / ", warm.Select(time => $"{time.TotalMilliseconds:0.0}"))));

        // The wall clock is what a caller pays, and it is the number the item asks for. The time
        // inside Typst is the signal without the managed overhead on top of it — marshalling, the
        // metrics and diagnostics round trip, and the fingerprint walk — and it is asserted too,
        // because a reuse that only moved work out of the compiler would pass the first test.
        Assert.True(
            warmMin < coldMin,
            $"a second compilation of an edited document must be cheaper than a cold one (cold {coldMin.TotalMilliseconds:0.0} ms, warm {warmMin.TotalMilliseconds:0.0} ms)");

        Assert.True(
            warmInsideMin < coldInsideMin,
            $"Typst itself must do less work on an edited document (cold {coldInsideMin.TotalMilliseconds:0.0} ms, warm {warmInsideMin.TotalMilliseconds:0.0} ms)");
    }

    /// <summary>
    /// An eight-page report whose first page carries the sentence the test edits.
    /// </summary>
    private static string Report(string seed, int round, int edit)
    {
        var markup = new System.Text.StringBuilder("#set page(paper: \"a4\")\n#set text(size: 10pt)\n");

        for (var page = 0; page < 8; page++)
        {
            if (page > 0)
            {
                markup.Append("#pagebreak(weak: true)\n");
            }

            markup.Append($"#heading(level: 1)[Section {page} {seed}-{round}]\n\n");

            for (var paragraph = 0; paragraph < 6; paragraph++)
            {
                markup.Append($"Paragraph {paragraph} of page {page}: #lorem({40 + paragraph}) ");

                if (page == 0 && paragraph == 2)
                {
                    markup.Append($"Revision {edit} of the sentence. ");
                }

                markup.Append("\n\n");
            }
        }

        return markup.ToString();
    }
}
