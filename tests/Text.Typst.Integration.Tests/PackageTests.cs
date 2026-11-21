using System;
using System.IO;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// Package resolution: <c>@namespace/name:version</c> reads from the data directory, then the
/// cache, then the registry — and never touches the network unless the host allows it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PackageTests
{
    /// <summary>Writes a minimal package into <paramref name="root"/> and returns the root.</summary>
    private static string CreatePackage(string root, string name = "testpkg", string version = "0.1.0")
    {
        var directory = Path.Combine(root, "preview", name, version);
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "typst.toml"), $"""
            [package]
            name = "{name}"
            version = "{version}"
            entrypoint = "lib.typ"
            authors = ["M3scalinum.Text.Typst tests"]
            license = "MIT"
            description = "A package used by the M3scalinum.Text.Typst test suite"
            """);

        File.WriteAllText(Path.Combine(directory, "lib.typ"), """
            #let hello() = [Hello from a package]
            """);

        return root;
    }

    private static string NewDirectory()
        => Path.Combine(Path.GetTempPath(), "sharp-typst-pkg-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolves_a_package_from_the_cache_directory()
    {
        var root = NewDirectory();

        try
        {
            CreatePackage(root);

            using var world = new TypstWorldBuilder()
                .WithPackages(packages => packages.CacheDirectory = root)
                .AddSource("main.typ", """
                    #import "@preview/testpkg:0.1.0": hello

                    #hello()
                    """)
                .Build();

            using var result = new TypstCompiler().Compile(world);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(1, result.PageCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolves_a_package_from_the_data_directory()
    {
        var root = NewDirectory();

        try
        {
            CreatePackage(root);

            using var world = new TypstWorldBuilder()
                .WithPackages(packages => packages.DataDirectory = root)
                .AddSource("main.typ", """
                    #import "@preview/testpkg:0.1.0": hello

                    #hello()
                    """)
                .Build();

            using var result = new TypstCompiler().Compile(world);

            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_package_fails_without_touching_the_network()
    {
        var root = NewDirectory();
        Directory.CreateDirectory(root);

        try
        {
            using var world = new TypstWorldBuilder()
                .WithPackages(packages => packages.CacheDirectory = root)
                .AddSource("main.typ", """
                    #import "@preview/does-not-exist:9.9.9": hello

                    #hello()
                    """)
                .Build();

            var exception = Assert.Throws<TypstCompilationException>(
                () => new TypstCompiler().Compile(world));

            Assert.Contains("does-not-exist", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Downloads_are_off_by_default()
    {
        var packages = new TypstPackages();

        Assert.False(packages.AllowDownload);
        Assert.Null(packages.CacheDirectory);
        Assert.Null(packages.DataDirectory);
        Assert.Null(packages.Registry);
    }

    [Fact]
    public void Rejects_a_registry_that_is_not_an_http_url()
    {
        var builder = new TypstWorldBuilder();

        Assert.Throws<ArgumentException>(
            () => builder.WithPackages(packages => packages.Registry = "not a url"));

        Assert.Throws<ArgumentException>(
            () => builder.WithPackages(packages => packages.Registry = "ftp://example.com"));
    }

    [Fact]
    public void Accepts_an_https_registry()
    {
        var builder = new TypstWorldBuilder()
            .WithPackages(packages =>
            {
                packages.Registry = "https://packages.example.com";
                packages.AllowDownload = true;
            });

        using var world = builder.AddSource("main.typ", "= Title").Build();

        Assert.Equal(1, world.SourceCount);
    }
}
