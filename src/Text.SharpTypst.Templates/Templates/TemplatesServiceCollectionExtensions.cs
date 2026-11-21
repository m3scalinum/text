using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>Registers the template pack in a <see cref="IServiceCollection"/>.</summary>
public static class TemplatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TemplateCatalog.Default"/> as a singleton, so a component that renders
    /// documents resolves the pack instead of building it.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    public static IServiceCollection AddTextTemplates(this IServiceCollection services)
        => services.AddTextTemplates(TemplateCatalog.Default);

    /// <summary>Registers a specific catalog as a singleton.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="catalog">The catalog to register, which may add layouts to the ones shipped.</param>
    /// <remarks>
    /// The registration uses <c>TryAdd</c>, so calling this twice is harmless and a host can replace
    /// the catalog before or after the call.
    /// </remarks>
    public static IServiceCollection AddTextTemplates(this IServiceCollection services, TemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        services.TryAddSingleton(catalog);
        return services;
    }
}
