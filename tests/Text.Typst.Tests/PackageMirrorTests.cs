using System;
using System.Collections.Generic;
using System.IO;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The managed half of a package mirror: the settings that describe it, and the preflight that
/// names a package the mirror does not hold before the compiler starts.
/// </summary>
public sealed class PackageMirrorTests
{
    private static string NewDirectory()
        => Path.Combine(Path.GetTempPath(), "sharp-typst-mirror-unit-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_mirror_is_refused_while_downloads_are_allowed()
    {
        var packages = new TypstPackages
        {
            MirrorDirectory = "/opt/typst-mirror",
            AllowDownload = true,
        };

        var exception = Assert.Throws<ArgumentException>(packages.Validate);

        Assert.Contains("cache-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_mirror_path_is_refused()
    {
        var packages = new TypstPackages { MirrorDirectory = "  " };

        Assert.Throws<ArgumentException>(packages.Validate);
    }

    [Fact]
    public void A_mirror_is_off_by_default()
    {
        var packages = new TypstPackages();

        Assert.Null(packages.MirrorDirectory);
        packages.Validate();
    }

    [Fact]
    public void The_preflight_reads_package_specs_out_of_markup()
    {
        const string markup = """
            #import "@preview/cetz:0.3.1": canvas
            #import "@preview/tablex:0.0.8" as tablex

            = A title

            #canvas({})
            """;

        Assert.Equal(
            ["@preview/cetz:0.3.1", "@preview/tablex:0.0.8"],
            [.. TypstPackageMirror.Specs(markup)]);
    }

    [Fact]
    public void The_preflight_ignores_text_that_is_not_a_versioned_spec()
    {
        const string markup = """
            #import "@preview/cetz": canvas
            #import "@preview/cetz:latest": canvas
            #let address = "someone@example.com/thing:1.2.3"
            """;

        Assert.Empty(TypstPackageMirror.Specs(markup));
    }

    [Fact]
    public void A_package_the_mirror_holds_passes_the_preflight()
    {
        var mirror = NewDirectory();
        Directory.CreateDirectory(Path.Combine(mirror, "preview", "cetz", "0.3.1"));

        try
        {
            TypstPackageMirror.EnsureSatisfied(
                mirror,
                ["""#import "@preview/cetz:0.3.1": canvas"""]);
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    [Fact]
    public void A_package_the_mirror_lacks_is_named_with_the_mirror()
    {
        var mirror = NewDirectory();
        Directory.CreateDirectory(mirror);

        try
        {
            var exception = Assert.Throws<TypstCompilationException>(
                () => TypstPackageMirror.EnsureSatisfied(
                    mirror,
                    ["""#import "@preview/cetz:0.3.1": canvas"""]));

            Assert.Contains("@preview/cetz:0.3.1", exception.Message, StringComparison.Ordinal);
            Assert.Contains(mirror, exception.Message, StringComparison.Ordinal);
            Assert.Contains("typst-mirror.py", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    [Fact]
    public void The_preflight_names_the_absent_package_and_not_the_present_one()
    {
        var mirror = NewDirectory();
        Directory.CreateDirectory(Path.Combine(mirror, "preview", "cetz", "0.3.1"));

        try
        {
            var exception = Assert.Throws<TypstCompilationException>(
                () => TypstPackageMirror.EnsureSatisfied(
                    mirror,
                    [
                        """#import "@preview/cetz:0.3.1": canvas""",
                        """#import "@preview/tablex:0.0.8": tablex""",
                    ]));

            Assert.Contains("@preview/tablex:0.0.8", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("@preview/cetz:0.3.1", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    [Fact]
    public void The_mirror_round_trips_through_the_native_payload()
    {
        // The DTO is what actually crosses the ABI: a rename here is a silent null in Rust, and the
        // native side would then never verify anything.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new M3scalinum.Text.Typst.Internal.WorldConfigDto
            {
                Packages = new M3scalinum.Text.Typst.Internal.PackagesDto
                {
                    Mirror = "/opt/typst-mirror",
                    Download = false,
                },
            },
            M3scalinum.Text.Typst.Internal.TypstJsonContext.Default.WorldConfigDto);

        Assert.Contains("\"mirror\":\"/opt/typst-mirror\"", json, StringComparison.Ordinal);
    }
}
