using System;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Native;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// Holds the leak soaks apart from the rest of the suite: they count native handles, so another
/// class holding one would turn the assertion into noise.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LeakSoak
{
    public const string Name = "Leak soak";
}

/// <summary>
/// A long run must not grow without bound. Every native allocation is owned by a handle that the
/// managed side disposes, so a leak shows up either as a monotonic rise in managed memory or as a
/// native handle that is never released.
/// </summary>
[Collection(LeakSoak.Name)]
[Trait("Category", "Integration")]
public sealed class LeakTests
{
    private const int Cycles = 10_000;

    /// <summary>
    /// A compilation abandoned by a timeout keeps its world until its worker thread returns, so a
    /// leak is thousands while a straggler is one. The tolerance absorbs the straggler, not a leak.
    /// </summary>
    private const int HandleTolerance = 8;

    private static long ManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [Fact]
    public void Ten_thousand_compile_and_export_cycles_do_not_grow_managed_memory()
    {
        const string source = """
            = Leak probe

            A paragraph with *bold*, _emphasis_ and a #link("https://typst.app")[link].

            - one
            - two

            $ sum_(k=1)^n k = (n(n+1))/2 $
            """;

        // Warm up: the first cycles allocate the font book, the library and the caches.
        for (var index = 0; index < 50; index++)
        {
            using var warmWorld = new TypstWorldBuilder().AddSource("main.typ", source).Build();
            using var warmResult = new TypstCompiler().Compile(warmWorld);
            _ = warmResult.ExportPdf();
        }

        var before = ManagedBytes();
        var documentsBefore = TypstNative.LiveDocumentCount;

        for (var index = 0; index < Cycles; index++)
        {
            using var world = new TypstWorldBuilder().AddSource("main.typ", source).Build();
            using var result = new TypstCompiler().Compile(world);
            _ = result.ExportPdf();
        }

        var after = ManagedBytes();
        var growth = after - before;

        // 10 000 cycles of a ~1 KB document: anything above a few megabytes means a handle is
        // not being released. The threshold is generous on purpose — this is a leak detector,
        // not a memory benchmark.
        Assert.True(
            growth < 8 * 1024 * 1024,
            $"managed memory grew by {growth / 1024} KiB over {Cycles} cycles");
        Assert.InRange(
            Math.Abs(TypstNative.LiveDocumentCount - documentsBefore),
            0,
            HandleTolerance);
    }

    [Fact]
    public void Ten_thousand_page_text_reads_release_every_buffer()
    {
        const string source = """
            #set page(paper: "a4", margin: 2cm)

            ZEBRAALPHA

            #pagebreak()

            ZEBRABETA
            """;

        using var world = new TypstWorldBuilder().AddSource("main.typ", source).Build();
        using var result = new TypstCompiler().Compile(world);

        // Warm up: the first reads allocate the collector's strings.
        for (var index = 0; index < 50; index++)
        {
            _ = result.ExtractText(0);
        }

        var before = ManagedBytes();
        var documentsBefore = TypstNative.LiveDocumentCount;

        for (var index = 0; index < Cycles; index++)
        {
            _ = result.ExtractText(0);
            _ = result.ExtractText(1);
        }

        var growth = ManagedBytes() - before;

        // Every call returns an owned TnBuffer that the managed side copies and frees, so a missed
        // free is invisible to the managed heap and hidden by the allocator: what this asserts is
        // that neither copy is retained across 20 000 reads.
        Assert.True(
            growth < 8 * 1024 * 1024,
            $"managed memory grew by {growth / 1024} KiB over {Cycles} page-text reads");
        Assert.InRange(
            Math.Abs(TypstNative.LiveDocumentCount - documentsBefore),
            0,
            HandleTolerance);
    }

    [Fact]
    public void Ten_thousand_worlds_release_every_native_handle()
    {
        // Ask the native library how many worlds it holds, instead of reading the process working
        // set. The working set is process-wide and every platform retains freed native blocks
        // differently: this same loop was flat on x64 and grew by 492 MiB on a Windows runner and
        // by 78 MiB on linux-arm64, which describes the allocator, not this library. The handle
        // count is the property the library owns, and it is exact.
        var before = TypstNative.LiveWorldCount;

        using (var one = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build())
        {
            // Guard: a counter stuck at zero would make the assertion below pass for nothing.
            Assert.Equal(before + 1, TypstNative.LiveWorldCount);
        }

        for (var index = 0; index < Cycles; index++)
        {
            using var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        }

        Assert.InRange(
            Math.Abs(TypstNative.LiveWorldCount - before),
            0,
            HandleTolerance);
    }

    [Fact]
    public void A_failed_compilation_releases_its_handles()
    {
        var before = ManagedBytes();

        for (var index = 0; index < 1_000; index++)
        {
            using var world = new TypstWorldBuilder().AddSource("main.typ", "#let x = ").Build();

            Assert.Throws<TypstCompilationException>(() => new TypstCompiler().Compile(world));
        }

        var growth = ManagedBytes() - before;

        Assert.True(growth < 4 * 1024 * 1024, $"managed memory grew by {growth / 1024} KiB");
    }

    [Fact]
    public void Ten_thousand_clones_do_not_grow_managed_memory()
    {
        using var template = new TypstWorldBuilder()
            .AddSource("main.typ", "= Clone probe\n\n#lorem(20)")
            .Build();

        for (var index = 0; index < 50; index++)
        {
            using var warm = template.Clone();
        }

        var before = ManagedBytes();
        var worldsBefore = TypstNative.LiveWorldCount;

        for (var index = 0; index < Cycles; index++)
        {
            using var clone = template.Clone();
        }

        var growth = ManagedBytes() - before;

        Assert.True(
            growth < 8 * 1024 * 1024,
            $"managed memory grew by {growth / 1024} KiB over {Cycles} clones");
        Assert.InRange(
            Math.Abs(TypstNative.LiveWorldCount - worldsBefore),
            0,
            HandleTolerance);
    }

    [Fact]
    public void A_thousand_specialised_clones_do_not_grow_managed_memory()
    {
        using var template = new TypstWorldBuilder()
            .AddSource("main.typ", "= Clone probe")
            .Build();

        for (var index = 0; index < 50; index++)
        {
            using var warm = template.WithSource("main.typ", "= Warm");
        }

        var before = ManagedBytes();

        for (var index = 0; index < 1_000; index++)
        {
            using var clone = template.WithSource("main.typ", "= Specialised");
        }

        var growth = ManagedBytes() - before;

        Assert.True(
            growth < 4 * 1024 * 1024,
            $"managed memory grew by {growth / 1024} KiB over 1 000 specialised clones");
    }
}
