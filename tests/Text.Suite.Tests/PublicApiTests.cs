using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.DependencyInjection;
using Xunit;

namespace M3scalinum.Text.Suite.Tests;

/// <summary>Shape of the public surface, checked by reflection.</summary>
public sealed class PublicApiTests
{
    private static IEnumerable<Assembly> Assemblies()
    {
        yield return typeof(TypstCompiler).Assembly;
        yield return typeof(TypstOptions).Assembly;
        yield return typeof(M3scalinum.Text.Pdf.PdfSigner).Assembly;
        yield return typeof(M3scalinum.Text.Pdf.FactureX.FacturXInvoice).Assembly;
        yield return typeof(M3scalinum.Text.SharpTypst.Templates.TemplateCatalog).Assembly;
        yield return typeof(M3scalinum.Text.Mcp.McpServer).Assembly;
        yield return typeof(M3scalinum.Text.Charts.Chart).Assembly;
        yield return typeof(M3scalinum.Text.Epub.EpubWriter).Assembly;
    }

    [Fact]
    public void No_two_public_types_share_a_name_across_namespaces()
    {
        // A user with both `using` directives would get CS0104 on the bare name.
        var duplicates = Assemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Select(type => type.Namespace).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(type => type.Namespace))}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_public_type_lives_in_the_suite_namespace()
    {
        var strays = Assemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace is null
                || !type.Namespace.StartsWith("M3scalinum.Text", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(strays);
    }

    [Fact]
    public void Every_public_async_method_ends_with_Async()
    {
        var offenders = Assemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => typeof(System.Threading.Tasks.Task).IsAssignableFrom(method.ReturnType))
            .Where(method => !method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }
}
