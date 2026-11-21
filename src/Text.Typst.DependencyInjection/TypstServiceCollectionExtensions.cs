using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using M3scalinum.Text.Typst.Compilation;

namespace M3scalinum.Text.Typst.DependencyInjection;

/// <summary>Registers M3scalinum.Text.Typst in a <see cref="IServiceCollection"/>.</summary>
public static class TypstServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TypstCompiler"/> (singleton), <see cref="TypstWorldBuilder"/>
    /// (transient, pre-configured) and <see cref="ITypstWorldFactory"/> (singleton) with default
    /// options.
    /// </summary>
    public static IServiceCollection AddTypst(this IServiceCollection services)
        => services.AddTypst(static _ => { });

    /// <summary>
    /// Registers <see cref="TypstCompiler"/> (singleton), <see cref="TypstWorldBuilder"/>
    /// (transient, pre-configured) and <see cref="ITypstWorldFactory"/> (singleton).
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">Adjusts the options every builder is created from.</param>
    /// <remarks>
    /// Every registration uses <c>TryAdd</c>, so calling this twice is harmless and a host can
    /// replace any of the three services before or after the call.
    /// </remarks>
    public static IServiceCollection AddTypst(
        this IServiceCollection services,
        Action<TypstOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TypstOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TypstOptions>, TypstOptionsValidator>());

        services.TryAddSingleton<TypstCompiler>();
        services.TryAddSingleton<ITypstWorldFactory, TypstWorldFactory>();

        services.TryAddTransient(static provider => TypstWorldBuilderFactory.Create(
            provider.GetRequiredService<IOptions<TypstOptions>>().Value));

        return services;
    }
}
