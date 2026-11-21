using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// A compilation is CPU-bound: letting a hundred requests start a hundred of them at once makes
/// every one slower and the process unresponsive. The compiler caps how many run, and a request
/// that has to wait parks on the semaphore instead of occupying a thread to stand in line.
/// </summary>
public sealed class CompileConcurrencyTests
{
    private static TypstWorld World(FakeTypstNative native)
        => new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

    [Fact]
    public async Task A_single_slot_serialises_two_compilations()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(150) };
        var compiler = new TypstCompiler(native, maxConcurrency: 1);

        using var first = World(native);
        using var second = World(native);

        var stopwatch = Stopwatch.StartNew();
        var results = await Task.WhenAll(
            compiler.CompileAsync(first, cancellationToken: TestContext.Current.CancellationToken),
            compiler.CompileAsync(second, cancellationToken: TestContext.Current.CancellationToken));
        stopwatch.Stop();

        foreach (var result in results)
        {
            result.Dispose();
        }

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(280),
            $"two 150 ms compiles on one slot took {stopwatch.ElapsedMilliseconds} ms, so they overlapped");
    }

    [Fact]
    public async Task Two_slots_let_two_compilations_overlap()
    {
        // Both compilations must be inside the native call at the same time. The barrier proves it
        // directly; a duration threshold would also fail when the thread pool simply started the
        // second task late, which is a property of the runner, not of the gate.
        using var barrier = new Barrier(2);
        var native = new FakeTypstNative { CompileBarrier = barrier };
        var compiler = new TypstCompiler(native, maxConcurrency: 2);

        using var first = World(native);
        using var second = World(native);

        var results = await Task.WhenAll(
            compiler.CompileAsync(first, cancellationToken: TestContext.Current.CancellationToken),
            compiler.CompileAsync(second, cancellationToken: TestContext.Current.CancellationToken));

        foreach (var result in results)
        {
            result.Dispose();
        }
    }

    [Fact]
    public async Task A_slot_is_released_when_a_compilation_fails()
    {
        var native = new FakeTypstNative
        {
            CompileFailure = new M3scalinum.Text.SharpTypst.Native.NativeCallException(
                M3scalinum.Text.SharpTypst.Native.NativeStatus.Compilation,
                """[{"severity":"error","message":"unknown variable","file":"main.typ","line":1,"column":1}]""",
                "compile failed"),
        };

        var compiler = new TypstCompiler(native, maxConcurrency: 1);
        using var world = World(native);

        await Assert.ThrowsAsync<TypstCompilationException>(() =>
            compiler.CompileAsync(world, cancellationToken: TestContext.Current.CancellationToken));

        // The slot must be back: a second call would hang forever otherwise.
        native.CompileFailure = null;
        using var result = await compiler.CompileAsync(world, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task A_request_that_times_out_while_queued_never_compiles()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(300) };
        var compiler = new TypstCompiler(native, maxConcurrency: 1);

        using var first = World(native);
        using var second = World(native);

        var running = compiler.CompileAsync(first, cancellationToken: TestContext.Current.CancellationToken);

        // The second request waits for the slot and gives up before it gets one.
        var exception = await Assert.ThrowsAsync<TypstTimeoutException>(() =>
            compiler.CompileAsync(
                second,
                new TypstCompilationOptions { Timeout = TimeSpan.FromMilliseconds(50) },
                TestContext.Current.CancellationToken));

        Assert.Equal(TypstErrorCode.Timeout, exception.Code);

        using var result = await running;

        // Only the first request reached the compiler.
        Assert.Single(native.CompileOptions);
    }

    [Fact]
    public async Task A_cancelled_request_that_is_still_queued_leaves_without_compiling()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(300) };
        var compiler = new TypstCompiler(native, maxConcurrency: 1);

        using var first = World(native);
        using var second = World(native);

        var running = compiler.CompileAsync(first, cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            compiler.CompileAsync(second, cancellationToken: cancellation.Token));

        using var result = await running;

        Assert.Single(native.CompileOptions);
    }

    [Fact]
    public async Task A_zero_ceiling_removes_the_cap()
    {
        // Zero means no cap, so both compilations must be inside the native call at the same time.
        // The barrier proves it: a duration threshold would pass on a machine that started the
        // second task late and fail on a loaded one, neither of which says anything about the gate.
        using var barrier = new Barrier(2);
        var native = new FakeTypstNative { CompileBarrier = barrier };
        var compiler = new TypstCompiler(native, maxConcurrency: 0);

        using var first = World(native);
        using var second = World(native);

        var results = await Task.WhenAll(
            compiler.CompileAsync(first, cancellationToken: TestContext.Current.CancellationToken),
            compiler.CompileAsync(second, cancellationToken: TestContext.Current.CancellationToken));

        foreach (var result in results)
        {
            result.Dispose();
        }

        Assert.Equal(2, native.CompileCount);
    }
}
