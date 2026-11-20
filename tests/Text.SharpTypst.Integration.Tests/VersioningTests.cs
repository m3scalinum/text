using System;
using System.Reflection;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// The package version tracks the embedded compiler. This is the check that keeps the two from
/// drifting: bumping Typst without bumping <c>TypstVersion</c> fails here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VersioningTests
{
    [Fact]
    public void The_assembly_version_starts_with_the_embedded_compiler_version()
    {
        var assembly = typeof(TypstEnvironment).Assembly.GetName().Version!;
        var typst = TypstEnvironment.TypstVersion;

        Assert.Equal(typst, $"{assembly.Major}.{assembly.Minor}.{assembly.Build}");
    }

    [Fact]
    public void The_informational_version_names_both_versions()
    {
        var informational = typeof(TypstEnvironment).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.NotNull(informational);
        Assert.StartsWith(TypstEnvironment.TypstVersion, informational, StringComparison.Ordinal);
        Assert.Contains("Typst " + TypstEnvironment.TypstVersion, informational, StringComparison.Ordinal);
    }

    [Fact]
    public void The_assembly_version_does_not_carry_the_build_revision()
    {
        // A rebuild must not change the assembly identity, or every consumer needs a binding
        // redirect for a patch-level change.
        var assembly = typeof(TypstEnvironment).Assembly.GetName().Version!;

        Assert.Equal(0, assembly.Revision);
    }

    [Fact]
    public void The_public_surface_reports_the_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(TypstEnvironment.SharpTypstVersion));
        Assert.Matches(@"^\d+\.\d+\.\d+", TypstEnvironment.SharpTypstVersion);
    }
}
