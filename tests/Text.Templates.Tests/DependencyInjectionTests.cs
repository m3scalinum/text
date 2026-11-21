using System;
using Microsoft.Extensions.DependencyInjection;
using M3scalinum.Text.Templates;
using Xunit;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>The registration surface, exercised through a real container.</summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddTextTemplates_registers_the_default_catalog_as_a_singleton()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddTextTemplates());

        using var provider = services.BuildServiceProvider();

        Assert.Same(TemplateCatalog.Default, provider.GetRequiredService<TemplateCatalog>());
        Assert.Same(
            provider.GetRequiredService<TemplateCatalog>(),
            provider.GetRequiredService<TemplateCatalog>());
    }

    [Fact]
    public void AddTextTemplates_registers_a_catalog_that_extends_the_pack()
    {
        var catalog = new TemplateCatalog(TemplateCatalog.Default.Templates);
        var services = new ServiceCollection();

        services.AddTextTemplates(catalog);

        using var provider = services.BuildServiceProvider();

        Assert.Same(catalog, provider.GetRequiredService<TemplateCatalog>());
    }

    [Fact]
    public void Calling_AddTextTemplates_twice_keeps_the_first_registration()
    {
        var first = new TemplateCatalog(TemplateCatalog.Default.Templates);
        var second = new TemplateCatalog(TemplateCatalog.Default.Templates);
        var services = new ServiceCollection();

        services.AddTextTemplates(first);
        services.AddTextTemplates(second);

        using var provider = services.BuildServiceProvider();

        Assert.Same(first, provider.GetRequiredService<TemplateCatalog>());
    }

    [Fact]
    public void AddTextTemplates_rejects_a_null_container()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddTextTemplates());
    }

    [Fact]
    public void AddTextTemplates_rejects_a_null_catalog()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddTextTemplates(null!));
    }
}
