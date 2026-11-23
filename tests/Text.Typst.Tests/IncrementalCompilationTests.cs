using System;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Native;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// <see cref="TypstCompiler.CompileChanged"/> compares page fingerprints against the previous
/// compilation recorded in a <see cref="TypstIncrementalState"/> and reports the pages that moved.
/// </summary>
/// <remarks>
/// The fingerprints themselves are the native library's job and are tested there against real
/// layouts; what is decided here is the comparison, the bookkeeping of the baseline and the shape
/// of "a page disappeared". A fake keeps the numbers readable: a digest is opaque, so a test states
/// which pages it means by writing them down.
/// </remarks>
public sealed class IncrementalCompilationTests
{
    private static TypstWorld World(FakeTypstNative native)
        => new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

    [Fact]
    public void The_first_compilation_reports_every_page_as_changed()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22,33]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using var changed = compiler.CompileChanged(world, state);

        Assert.True(changed.IsFirstCompile);
        Assert.Equal([0, 1, 2], changed.ChangedPages);
        Assert.Empty(changed.RemovedPages);
        Assert.True(state.HasBaseline);
        Assert.Equal(3, state.PageCount);
    }

    [Fact]
    public void Only_the_page_whose_fingerprint_moved_is_reported()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22,33]}""");
        native.PageFingerprintsJson.Add("""{"pages":[11,22,99]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.False(second.IsFirstCompile);
        Assert.Equal([2], second.ChangedPages);
        Assert.Empty(second.RemovedPages);
    }

    [Fact]
    public void An_unchanged_document_reports_no_page()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22,33]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.False(second.IsFirstCompile);
        Assert.Empty(second.ChangedPages);
        Assert.Empty(second.RemovedPages);
    }

    [Fact]
    public void A_shorter_document_reports_the_pages_that_disappeared()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22,33]}""");
        native.PageFingerprintsJson.Add("""{"pages":[11,22]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.Empty(second.ChangedPages);
        Assert.Equal([2], second.RemovedPages);
        Assert.Equal(2, state.PageCount);
    }

    [Fact]
    public void A_longer_document_reports_the_pages_it_gained_as_changed()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11]}""");
        native.PageFingerprintsJson.Add("""{"pages":[11,22]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.Equal([1], second.ChangedPages);
        Assert.Empty(second.RemovedPages);
    }

    [Fact]
    public void A_document_without_pages_reports_nothing_but_records_a_baseline()
    {
        // An HTML compilation: `tn_document_page_fingerprints` answers an empty array.
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using var changed = compiler.CompileChanged(
            world,
            state,
            new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Html });

        Assert.True(changed.IsFirstCompile);
        Assert.Empty(changed.ChangedPages);
        Assert.Empty(changed.RemovedPages);
        Assert.Equal(0, state.PageCount);
    }

    [Fact]
    public void Resetting_the_state_makes_the_next_comparison_the_first_one()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        state.Reset();
        Assert.False(state.HasBaseline);
        Assert.Equal(0, state.PageCount);

        using var third = compiler.CompileChanged(world, state);

        Assert.True(third.IsFirstCompile);
        Assert.Equal([0, 1], third.ChangedPages);
    }

    [Fact]
    public void A_failed_compilation_does_not_move_the_baseline()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11,22]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        var fingerprints = state.Fingerprints;
        native.CompileFailure = new NativeCallException(
            NativeStatus.Compilation,
            """{"code":"compilation","message":"not today","diagnostics":[]}""",
            "not today");

        Assert.Throws<TypstCompilationException>(() => compiler.CompileChanged(world, state));

        Assert.True(state.HasBaseline);
        Assert.Same(fingerprints, state.Fingerprints);
    }

    [Fact]
    public void Compiling_incrementally_edits_the_world_in_place_instead_of_cloning_it()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11]}""");

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= One").Build();
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        world.SetSource("main.typ", "= Two");

        using (compiler.CompileChanged(world, state))
        {
        }

        // The persistent world is the point: two compilations, no clone, no second world.
        Assert.Equal(1, native.CreateWorldCalls);
        Assert.Equal(0, native.CloneCount);
        Assert.Equal(2, native.CompileCount);
        Assert.Single(native.Sources);
        Assert.Equal("= Two", native.Sources[0].Text);
    }

    [Fact]
    public void A_fingerprint_uses_the_whole_unsigned_range()
    {
        // A digest is drawn from the whole u64 range: a decoder that read it as a signed 64-bit
        // number would turn half of them negative and report every page as changed, every time.
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[18446744073709551615,0]}""");

        using var world = World(native);
        var state = new TypstIncrementalState();
        var compiler = new TypstCompiler(native);

        using (compiler.CompileChanged(world, state))
        {
        }

        using var second = compiler.CompileChanged(world, state);

        Assert.Empty(second.ChangedPages);
        Assert.Equal([ulong.MaxValue, 0UL], state.Fingerprints);
    }

    [Fact]
    public void A_failure_to_read_the_fingerprints_releases_the_document()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsFailure = new NativeCallException(
            NativeStatus.InvalidHandle,
            """{"code":"invalid_handle","message":"gone","diagnostics":[]}""",
            "gone");

        using var world = World(native);
        var state = new TypstIncrementalState();

        Assert.Throws<NativeCallException>(
            () => new TypstCompiler(native).CompileChanged(world, state));

        // Nothing was recorded, so the next attempt is still a first compilation.
        Assert.False(state.HasBaseline);
    }

    [Fact]
    public void An_empty_fingerprint_payload_is_no_pages_rather_than_a_failure()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add(string.Empty);

        using var world = World(native);
        var state = new TypstIncrementalState();

        using var changed = new TypstCompiler(native).CompileChanged(world, state);

        Assert.True(changed.IsFirstCompile);
        Assert.Empty(changed.ChangedPages);
        Assert.Equal(0, state.PageCount);
    }

    [Fact]
    public void Clearing_the_cache_reaches_the_native_library()
    {
        var native = new FakeTypstNative();
        new TypstCompiler(native).ClearCache();

        Assert.Equal(1, native.ClearCacheCalls);
    }

    [Fact]
    public void A_null_state_is_rejected_before_anything_compiles()
    {
        var native = new FakeTypstNative();
        using var world = World(native);

        Assert.Throws<ArgumentNullException>(
            () => new TypstCompiler(native).CompileChanged(world, null!));

        Assert.Equal(0, native.CompileCount);
    }

    [Fact]
    public void The_changed_result_owns_the_document_and_disposes_it_once()
    {
        var native = new FakeTypstNative();
        native.PageFingerprintsJson.Add("""{"pages":[11]}""");

        using var world = World(native);
        var changed = new TypstCompiler(native).CompileChanged(world, new TypstIncrementalState());

        var document = changed.Document;
        changed.Dispose();

        // A second dispose is a no-op rather than a double free: the handle is closed once.
        changed.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.PageFingerprints());
    }
}
