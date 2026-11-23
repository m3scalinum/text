using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// A package mirror end to end: a world that resolves only from it, a manifest that refuses a
/// tampered file with the package and the file named, and a package the mirror does not hold that
/// fails with the package and the mirror named.
/// </summary>
/// <remarks>
/// Offline is proved by configuration rather than by trust: the worlds here point
/// <see cref="TypstPackages.Registry"/> at an address nothing listens on, so any network call would
/// fail the compile. A mirror and downloads are mutually exclusive by validation, which is the
/// other half of the same guarantee.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PackageMirrorTests
{
    /// <summary>A registry nothing answers on: a compile that touched it would fail.</summary>
    private const string UnreachableRegistry = "http://127.0.0.1:9";

    private const string TestPackage = "@preview/testpkg:0.1.0";
    private const string OtherPackage = "@preview/other:1.0.0";

    /// <summary>A mirror on disk, plus the packages it holds, removed when the test ends.</summary>
    private sealed class Mirror : IDisposable
    {
        private readonly Dictionary<string, List<(string Path, byte[] Bytes)>> _packages = [];

        internal Mirror()
        {
            Root = Path.Combine(Path.GetTempPath(), "sharp-typst-mirror-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        /// <summary>Writes a package's files, as Typst's own cache layout would hold them.</summary>
        internal Mirror Add(string spec, params (string Path, string Text)[] files)
        {
            var (namespaceName, name, version) = Split(spec);
            var directory = Path.Combine(Root, namespaceName, name, version);
            Directory.CreateDirectory(directory);

            if (!_packages.TryGetValue(spec, out var package))
            {
                package = [];
                _packages[spec] = package;
            }

            foreach (var (path, text) in files)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var target = Path.Combine(directory, path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                package.Add((path, bytes));
            }

            return this;
        }

        /// <summary>Writes <c>mirror.json</c> from what the mirror holds right now.</summary>
        internal Mirror WriteManifest()
        {
            var packages = new Dictionary<string, List<object>>(StringComparer.Ordinal);

            foreach (var (spec, files) in _packages)
            {
                var entries = new List<object>();

                foreach (var (path, bytes) in files)
                {
                    entries.Add(new
                    {
                        path,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                        size = bytes.Length,
                    });
                }

                packages[spec] = entries;
            }

            File.WriteAllText(
                Path.Combine(Root, "mirror.json"),
                JsonSerializer.Serialize(new { format = 1, packages }));

            return this;
        }

        /// <summary>Overwrites a file without touching the manifest: the tamper under test.</summary>
        internal void Tamper(string spec, string path, string text)
        {
            var (namespaceName, name, version) = Split(spec);
            File.WriteAllText(Path.Combine(Root, namespaceName, name, version, path), text);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static (string Namespace, string Name, string Version) Split(string spec)
        {
            var withoutNamespace = spec.TrimStart('@');
            var slash = withoutNamespace.IndexOf('/');
            var colon = withoutNamespace.IndexOf(':', slash + 1);

            return (
                withoutNamespace[..slash],
                withoutNamespace[(slash + 1)..colon],
                withoutNamespace[(colon + 1)..]);
        }
    }

    private static Mirror TwoPackages()
        => new Mirror()
            .Add(
                TestPackage,
                ("typst.toml", """
                    [package]
                    name = "testpkg"
                    version = "0.1.0"
                    entrypoint = "lib.typ"
                    authors = ["M3scalinum.Text.Typst tests"]
                    license = "MIT"
                    description = "A package used by the M3scalinum.Text.Typst test suite"
                    """),
                ("lib.typ", "#let hello() = [Hello from a package]\n"))
            .Add(OtherPackage, ("lib.typ", "#let other() = [Other]\n"))
            .WriteManifest();

    private static TypstWorldBuilder BuilderWith(string mirror)
        => new TypstWorldBuilder().WithPackages(packages =>
        {
            packages.MirrorDirectory = mirror;
            packages.Registry = UnreachableRegistry;
        });

    [Fact]
    public void A_document_importing_a_mirrored_package_compiles_offline()
    {
        using var mirror = TwoPackages();

        using var world = BuilderWith(mirror.Root)
            .AddSource("main.typ", $"""
                #import "{TestPackage}": hello

                #hello()
                """)
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void A_tampered_file_is_refused_with_the_package_the_file_and_the_mirror_named()
    {
        using var mirror = TwoPackages();

        // The same length, so the size check passes and the digest is the only thing that can
        // catch the edit: an attacker who keeps the length is the case worth testing.
        mirror.Tamper(TestPackage, "lib.typ", "#let hello() = [Tampered, same lengt]\n");

        var exception = Assert.Throws<TypstCompilationException>(
            () => BuilderWith(mirror.Root)
                .AddSource("main.typ", $"""
                    #import "{TestPackage}": hello

                    #hello()
                    """)
                .Build());

        Assert.Contains(TestPackage, exception.Message, StringComparison.Ordinal);
        Assert.Contains("lib.typ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(mirror.Root, exception.Message, StringComparison.Ordinal);
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_the_manifest_does_not_list_is_refused()
    {
        using var mirror = TwoPackages();
        File.WriteAllText(Path.Combine(mirror.Root, "preview", "testpkg", "0.1.0", "extra.typ"), "= extra\n");

        var exception = Assert.Throws<TypstCompilationException>(
            () => BuilderWith(mirror.Root).AddSource("main.typ", "= Title").Build());

        Assert.Contains(TestPackage, exception.Message, StringComparison.Ordinal);
        Assert.Contains("extra.typ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_the_mirror_lacks_fails_with_the_package_and_the_mirror_named()
    {
        using var mirror = TwoPackages();

        var exception = Assert.Throws<TypstCompilationException>(
            () => BuilderWith(mirror.Root)
                .AddSource("main.typ", """
                    #import "@preview/absent:9.9.9": hello

                    #hello()
                    """)
                .Build());

        Assert.Contains("@preview/absent:9.9.9", exception.Message, StringComparison.Ordinal);
        Assert.Contains(mirror.Root, exception.Message, StringComparison.Ordinal);
        Assert.Contains("typst-mirror.py", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The behaviour every existing host relies on: no mirror, and a missing package still fails
    /// with the compiler's own diagnostic, which names the package and nothing else.
    /// </summary>
    [Fact]
    public void Without_a_mirror_the_failure_is_the_one_it_always_was()
    {
        var cache = Path.Combine(Path.GetTempPath(), "sharp-typst-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);

        try
        {
            using var world = new TypstWorldBuilder()
                .WithPackages(packages =>
                {
                    packages.CacheDirectory = cache;
                    packages.Registry = UnreachableRegistry;
                })
                .AddSource("main.typ", """
                    #import "@preview/absent:9.9.9": hello

                    #hello()
                    """)
                .Build();

            var exception = Assert.Throws<TypstCompilationException>(
                () => new TypstCompiler().Compile(world));

            Assert.Contains("package not found", exception.Message, StringComparison.Ordinal);
            Assert.Contains("@preview/absent:9.9.9", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(cache, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void A_mirror_and_downloads_are_refused_together()
    {
        using var mirror = TwoPackages();

        var exception = Assert.Throws<ArgumentException>(
            () => new TypstWorldBuilder().WithPackages(packages =>
            {
                packages.MirrorDirectory = mirror.Root;
                packages.AllowDownload = true;
            }));

        Assert.Contains("cache-only", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mirror with no manifest is refused rather than treated as an empty one: the caller asked
    /// for verified packages, and an unlisted directory cannot promise that.
    /// </summary>
    [Fact]
    public void A_mirror_without_a_manifest_is_refused()
    {
        using var mirror = new Mirror().Add(TestPackage, ("lib.typ", "#let hello() = 1\n"));

        var exception = Assert.Throws<TypstCompilationException>(
            () => BuilderWith(mirror.Root).AddSource("main.typ", "= Title").Build());

        Assert.Contains("mirror.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains(mirror.Root, exception.Message, StringComparison.Ordinal);
    }
}
