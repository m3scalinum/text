using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// A world is documented as safe to compile from several threads. Disposing it while a compile is
/// in flight must not free the native object under the call: the handle is pinned for the duration,
/// so the caller sees <see cref="ObjectDisposedException"/> or a result, never a crash.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConcurrentDisposeTests
{
    private const int Workers = 8;
    private const int Rounds = 20;

    [Fact]
    public async Task Disposing_a_world_while_it_compiles_never_crashes()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var world = new TypstWorldBuilder()
                .AddSource("main.typ", "= Title\n\n#lorem(200)")
                .Build();

            var compiler = new TypstCompiler();
            var outcomes = new ConcurrentBag<string>();
            var failures = new ConcurrentBag<Exception>();

            using var start = new Barrier(Workers + 1);

            var workers = Enumerable.Range(0, Workers)
                .Select(index => Task.Run(() =>
                {
                    start.SignalAndWait();

                    for (var iteration = 0; iteration < 20; iteration++)
                    {
                        try
                        {
                            using var result = compiler.Compile(world);
                            _ = result.ExportPdf();
                            outcomes.Add("ok");
                        }
                        catch (ObjectDisposedException)
                        {
                            outcomes.Add("disposed");
                            return;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                            return;
                        }
                    }
                }, TestContext.Current.CancellationToken))
                .ToArray();

            var disposer = Task.Run(() =>
            {
                start.SignalAndWait();
                Thread.SpinWait(20_000);
                world.Dispose();
            }, TestContext.Current.CancellationToken);

            await Task.WhenAll([.. workers, disposer]);

            Assert.Empty(failures.Select(exception => exception.ToString()));
            Assert.All(outcomes, outcome => Assert.Contains(outcome, new[] { "ok", "disposed" }));
        }
    }

    [Fact]
    public void A_disposed_world_reports_ObjectDisposedException_to_a_late_caller()
    {
        var world = new TypstWorldBuilder().AddSource("main.typ", "= Title").Build();
        var compiler = new TypstCompiler();

        world.Dispose();

        Assert.Throws<ObjectDisposedException>(() => compiler.Compile(world));
    }
}
