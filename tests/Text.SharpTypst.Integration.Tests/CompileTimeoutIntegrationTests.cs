using System;
using System.Diagnostics;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// The timeout against the real compiler: the bound is on the caller's wait, and the world may be
/// disposed the moment the call gives up.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompileTimeoutIntegrationTests
{
    private const string Source = """
        = Bounded

        #lorem(200)
        """;

    /// <summary>
    /// A document whose layout takes seconds, so the timeout fires on any machine.
    /// </summary>
    /// <remarks>
    /// A trivial document is not enough: on a fast runner the compile finishes inside a millisecond,
    /// and the test then asserts a timeout that never had a reason to happen.
    /// </remarks>
    private const string Heavy = """
        #for i in range(4000) [#lorem(20)\ ]
        """;

    [Fact]
    public void A_compile_that_cannot_finish_in_time_is_abandoned()
    {
        using (var world = new TypstWorldBuilder().AddSource("main.typ", Heavy).Build())
        {
            var stopwatch = Stopwatch.StartNew();

            var exception = Assert.Throws<TypstTimeoutException>(() =>
                new TypstCompiler().Compile(world, new TypstCompilationOptions
                {
                    Timeout = TimeSpan.FromMilliseconds(50),
                }));

            stopwatch.Stop();

            Assert.Equal(TypstErrorCode.Timeout, exception.Code);

            // The caller is released at the timeout, not when the layout ends.
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"the call took {stopwatch.ElapsedMilliseconds} ms, so it waited for the compile");
        }

        // The world above was disposed while the abandoned compile was still reading it: without the
        // internal clone this is a use-after-free. Reaching this line without a crash is the check.
    }

    [Fact]
    public void A_generous_timeout_returns_the_document()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", Source).Build();

        var stopwatch = Stopwatch.StartNew();
        using var result = new TypstCompiler().Compile(world, new TypstCompilationOptions
        {
            Timeout = TimeSpan.FromMinutes(2),
        });
        stopwatch.Stop();

        Assert.True(result.PageCount > 0);

        // The timed path goes through a clone and a thread; it must not cost a visible delay.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"the timed compile took {stopwatch.ElapsedMilliseconds} ms");
    }
}
