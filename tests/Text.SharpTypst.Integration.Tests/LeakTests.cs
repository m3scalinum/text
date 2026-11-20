using System;
using System.Diagnostics;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// A long run must not grow without bound. Every native allocation is owned by a handle that the
/// managed side disposes, so a leak shows up as a monotonic rise in managed memory or in the
/// process working set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LeakTests
{
    private const int Cycles = 10_000;

    /// <summary>Warm-up worlds excluded from the working-set measurement.</summary>
    private const int WarmupCycles = 5_000;

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
    }

    [Fact]
    public void Ten_thousand_worlds_reach_a_steady_working_set()
    {
        // The first worlds grow the process to its steady state: the runtime reserves heap arenas,
        // the CRT pools blocks, the GC commits segments. That ramp is not a leak, and its size is a
        // property of the allocator, not of this library — on Windows ARM64 it alone exceeds the
        // threshold the whole loop used to be held to. So the ramp is run first and excluded, and
        // what is measured is the growth after it: a leak keeps growing here, a pool does not.
        for (var index = 0; index < WarmupCycles; index++)
        {
            using var warm = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        }

        var before = WorkingSet();

        for (var index = 0; index < Cycles; index++)
        {
            using var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        }

        var growth = WorkingSet() - before;

        Assert.True(
            growth < 32 * 1024 * 1024,
            $"working set grew by {growth / 1024 / 1024} MiB over {Cycles} worlds after a {WarmupCycles}-world warm-up");
    }

    private static long WorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
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

        for (var index = 0; index < Cycles; index++)
        {
            using var clone = template.Clone();
        }

        var growth = ManagedBytes() - before;

        Assert.True(
            growth < 8 * 1024 * 1024,
            $"managed memory grew by {growth / 1024} KiB over {Cycles} clones");
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
