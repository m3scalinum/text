using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.DependencyInjection;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Registration, configuration and validation of the DI integration.</summary>
[Trait("Category", "Integration")]
public sealed class DependencyInjectionTests
{
    private static ServiceProvider Build(Action<TypstOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSharpTypst(configure ?? (static _ => { }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_the_compiler_as_a_singleton()
    {
        using var provider = Build();

        Assert.Same(
            provider.GetRequiredService<TypstCompiler>(),
            provider.GetRequiredService<TypstCompiler>());
    }

    [Fact]
    public void Registers_the_builder_as_transient()
    {
        using var provider = Build();

        Assert.NotSame(
            provider.GetRequiredService<TypstWorldBuilder>(),
            provider.GetRequiredService<TypstWorldBuilder>());
    }

    [Fact]
    public void Registers_the_world_factory_as_a_singleton()
    {
        using var provider = Build();

        Assert.Same(
            provider.GetRequiredService<ITypstWorldFactory>(),
            provider.GetRequiredService<ITypstWorldFactory>());
    }

    [Fact]
    public void Applies_the_limits_to_every_builder()
    {
        using var provider = Build(options =>
        {
            options.Limits.MaxSourceBytes = 4096;
            options.Limits.MaxSources = 7;
        });

        var builder = provider.GetRequiredService<TypstWorldBuilder>();

        Assert.Equal(4096, builder.Limits.MaxSourceBytes);
        Assert.Equal(7, builder.Limits.MaxSources);
    }

    [Fact]
    public void Applies_the_package_settings_to_every_builder()
    {
        using var provider = Build(options =>
        {
            options.Packages.CacheDirectory = "/tmp/sharp-typst-cache";
            options.Packages.AllowDownload = true;
            options.Packages.Registry = "https://packages.example.com";
        });

        var builder = provider.GetRequiredService<TypstWorldBuilder>();

        Assert.Equal("/tmp/sharp-typst-cache", builder.Packages.CacheDirectory);
        Assert.True(builder.Packages.AllowDownload);
        Assert.Equal("https://packages.example.com", builder.Packages.Registry);
    }

    [Fact]
    public void Defaults_to_a_fixed_clock_and_no_downloads()
    {
        using var provider = Build();

        var builder = provider.GetRequiredService<TypstWorldBuilder>();

        Assert.False(builder.UsesSystemClock);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), builder.FixedClock);
        Assert.False(builder.Packages.AllowDownload);
    }

    [Fact]
    public void Honours_the_system_clock()
    {
        using var provider = Build(options => options.Clock.UseSystemClock = true);

        Assert.True(provider.GetRequiredService<TypstWorldBuilder>().UsesSystemClock);
    }

    [Fact]
    public void Honours_a_custom_fixed_instant()
    {
        var instant = new DateTimeOffset(2030, 6, 15, 12, 30, 0, TimeSpan.Zero);

        using var provider = Build(options => options.Clock.FixedInstant = instant);

        Assert.Equal(instant, provider.GetRequiredService<TypstWorldBuilder>().FixedClock);
    }

    [Fact]
    public void The_factory_returns_configured_builders()
    {
        using var provider = Build(options => options.Limits.MaxFonts = 3);

        var factory = provider.GetRequiredService<ITypstWorldFactory>();

        Assert.Equal(3, factory.Create().Limits.MaxFonts);
        Assert.NotSame(factory.Create(), factory.Create());
    }

    [Fact]
    public void Rejects_a_non_positive_limit()
    {
        using var provider = Build(options => options.Limits.MaxSourceBytes = 0);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TypstOptions>>().Value);

        Assert.Contains("MaxSourceBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_malformed_registry()
    {
        using var provider = Build(options => options.Packages.Registry = "not a url");

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TypstOptions>>().Value);

        Assert.Contains("not a url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_non_positive_ppi()
    {
        using var provider = Build(options => options.Png = options.Png with { Ppi = 0 });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TypstOptions>>().Value);
    }

    [Fact]
    public void Calling_it_twice_is_harmless()
    {
        var services = new ServiceCollection();
        services.AddSharpTypst();
        services.AddSharpTypst(options => options.Limits.MaxSources = 5);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(5, provider.GetRequiredService<TypstWorldBuilder>().Limits.MaxSources);
        Assert.Single(provider.GetServices<TypstCompiler>());
    }

    [Fact]
    public void A_host_can_replace_a_registration()
    {
        var services = new ServiceCollection();
        services.AddSharpTypst();
        services.AddSingleton<ITypstWorldFactory, CustomFactory>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomFactory>(provider.GetRequiredService<ITypstWorldFactory>());
    }

    [Fact]
    public void Compiles_a_document_through_the_container()
    {
        using var provider = Build(options => options.Compilation = new TypstCompilationOptions
        {
            Root = "report.typ",
        });

        var builder = provider.GetRequiredService<TypstWorldBuilder>();
        var compiler = provider.GetRequiredService<TypstCompiler>();
        var options = provider.GetRequiredService<IOptions<TypstOptions>>().Value;

        using var world = builder
            .AddSource("report.typ", "= Quarterly report\n\nAll good.")
            .Build();

        using var result = compiler.Compile(world, options.Compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
        Assert.NotEmpty(result.ExportPdf());
    }

    private sealed class CustomFactory : ITypstWorldFactory
    {
        public TypstWorldBuilder Create() => new();
    }
}
