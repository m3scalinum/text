using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Compilation is reentrant: independent worlds compile in parallel without sharing state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConcurrencyTests
{
    [Fact]
    public void Sixteen_worlds_compile_in_parallel()
    {
        const int Count = 16;
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, Count, index =>
        {
            try
            {
                using var world = new TypstWorldBuilder()
                    .AddSource("main.typ", $"= Document {index}\n\nBody {index}.")
                    .Build();

                using var result = new TypstCompiler().Compile(world);

                if (result.Diagnostics.Count != 0)
                {
                    failures.Add($"{index}: unexpected diagnostics");
                }

                if (result.PageCount != 1)
                {
                    failures.Add($"{index}: expected one page, got {result.PageCount}");
                }

                if (result.ExportPdf().Length == 0)
                {
                    failures.Add($"{index}: empty PDF");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{index}: {exception.GetType().Name}: {exception.Message}");
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void A_shared_compiler_serves_parallel_worlds()
    {
        const int Count = 8;
        var compiler = new TypstCompiler();
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, Count, index =>
        {
            try
            {
                using var world = new TypstWorldBuilder()
                    .AddSource("main.typ", $"= Title {index}")
                    .Build();

                using var result = compiler.Compile(world);

                if (result.PageCount != 1)
                {
                    failures.Add($"{index}: expected one page");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{index}: {exception.GetType().Name}: {exception.Message}");
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void Diagnostics_do_not_cross_talk_between_parallel_compilations()
    {
        const int Count = 8;
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, Count, index =>
        {
            // Even indices fail, odd indices succeed: a shared diagnostic buffer would leak.
            var markup = index % 2 == 0 ? "#panic(\"boom\")" : $"= Fine {index}";

            try
            {
                using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
                using var result = new TypstCompiler().Compile(world);

                if (index % 2 == 0)
                {
                    failures.Add($"{index}: expected a compilation failure");
                }
            }
            catch (TypstCompilationException)
            {
                if (index % 2 != 0)
                {
                    failures.Add($"{index}: unexpected compilation failure");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{index}: {exception.GetType().Name}: {exception.Message}");
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void A_world_is_not_shared_between_compilations()
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", "= One").Build();
        var compiler = new TypstCompiler();

        using var first = compiler.Compile(world);
        using var second = compiler.Compile(world);

        Assert.Equal(first.PageCount, second.PageCount);
        Assert.Equal(first.ExportPdf().Length, second.ExportPdf().Length);
    }
}
