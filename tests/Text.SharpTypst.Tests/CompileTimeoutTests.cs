using System;
using System.Diagnostics;
using System.Threading;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// A host that accepts markup from a customer has to bound how long it waits. Typst 0.15 has no
/// interrupt hook, so the bound is on the caller's wait: the work is abandoned, not stopped.
/// </summary>
public sealed class CompileTimeoutTests
{
    private static TypstWorld World(FakeTypstNative native)
        => new TypstWorldBuilder(native).AddSource("main.typ", "= Title").Build();

    [Fact]
    public void A_compile_that_outlives_its_timeout_throws()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(400) };
        using var world = World(native);

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.Throws<TypstTimeoutException>(
            () => new TypstCompiler(native).Compile(world, new TypstCompilationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(30),
            }));
        stopwatch.Stop();

        Assert.Equal(TypstErrorCode.Timeout, exception.Code);
        Assert.Equal(TimeSpan.FromMilliseconds(30), exception.Timeout);

        // The caller is released long before the work ends.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(300),
            $"the call took {stopwatch.ElapsedMilliseconds} ms, so it waited for the compile instead of the timeout");
    }

    [Fact]
    public void A_compile_that_finishes_in_time_returns_its_result()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(10) };
        using var world = World(native);

        using var result = new TypstCompiler(native).Compile(world, new TypstCompilationOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
        });

        Assert.NotNull(result);
    }

    [Fact]
    public void A_timed_compile_runs_over_a_clone_so_the_caller_may_dispose_its_world()
    {
        var native = new FakeTypstNative { CompileDelay = TimeSpan.FromMilliseconds(200) };

        // The world is disposed the moment the timeout fires, while the abandoned compile still
        // reads it. Without the clone that is a use-after-free.
        using (var world = World(native))
        {
            Assert.Throws<TypstTimeoutException>(() => new TypstCompiler(native).Compile(world, new TypstCompilationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(20),
            }));
        }

        Assert.Equal(1, native.CloneCount);

        // Let the abandoned compile finish before the test ends, so its thread does not outlive the
        // fixture and the disposal it performs is observed.
        Thread.Sleep(400);
    }

    [Fact]
    public void A_failure_is_not_wrapped_in_an_aggregate_exception()
    {
        // Assert.Throws<T> demands the exact type, so an AggregateException would fail here.
        var native = new FakeTypstNative
        {
            CompileFailure = new M3scalinum.Text.SharpTypst.Native.NativeCallException(
                M3scalinum.Text.SharpTypst.Native.NativeStatus.Compilation,
                """[{"severity":"error","message":"unknown variable","file":"main.typ","line":1,"column":1}]""",
                "compile failed"),
        };

        using var world = World(native);

        var exception = Assert.Throws<TypstCompilationException>(
            () => new TypstCompiler(native).Compile(world, new TypstCompilationOptions
            {
                Timeout = TimeSpan.FromSeconds(5),
            }));

        Assert.Equal(TypstErrorCode.Compilation, exception.Code);
    }

    [Fact]
    public void Without_a_timeout_the_compile_runs_inline_and_takes_no_clone()
    {
        var native = new FakeTypstNative();
        using var world = World(native);

        using var result = new TypstCompiler(native).Compile(world);

        Assert.NotNull(result);
        Assert.Equal(0, native.CloneCount);
    }
}
