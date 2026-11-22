using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The host font index: one metadata scan per process, shared by every world that asks for the
/// same sources, and a refresh that replaces it.
/// </summary>
/// <remarks>
/// The scan is not observable from the managed side — the ABI reports what a world can resolve,
/// not how it got there — so the tests assert the two things that are: a world that joins the
/// index costs a fraction of one that scans, and a font added after the scan is visible only to a
/// refreshing world. The exact count of scans is pinned on the Rust side, by
/// <c>world::tests::two_thousand_compilations_scan_the_fonts_once</c>.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class FontIndexTests
{
    private static string[] FindSystemFonts()
    {
        string[] candidates =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSerif.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\georgia.ttf",
        ];

        return [.. candidates.Where(File.Exists)];
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sharp-typst-font-index-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Two_thousand_worlds_cost_less_than_the_scan_they_share()
    {
        var fonts = FindSystemFonts();
        if (fonts.Length == 0)
        {
            return;
        }

        var directory = CreateDirectory();

        try
        {
            // The same face under many names: what matters is that the scan has real work to do,
            // not how many fonts this host happens to ship. The index does not deduplicate by
            // content, so N files are N faces to read.
            const int Faces = 120;
            var pattern = Path.GetExtension(fonts[0]);

            for (var i = 0; i < Faces; i++)
            {
                File.Copy(fonts[0], Path.Combine(directory, $"font-{i:D3}{pattern}"));
            }

            // The persistent index is off: this measures the process-wide share, not the cache.
            TypstWorld Build(FontScanMode mode) => new TypstWorldBuilder()
                .AddFontDirectory(directory, mode)
                .WithoutFontIndexCache()
                .AddSource("main.typ", "= Title")
                .Build();

            // Warm the P/Invoke path so the first call does not pay for the JIT of both sides.
            using (Build(FontScanMode.Shared))
            {
            }

            var scanning = Stopwatch.StartNew();
            using (Build(FontScanMode.Refresh))
            {
            }

            scanning.Stop();

            var scanned = scanning.Elapsed.TotalMilliseconds;

            Assert.True(scanned > 0.5, $"one scan of {Faces} faces took {scanned:F3} ms");

            const int Worlds = 2000;
            var shared = Stopwatch.StartNew();

            for (var i = 0; i < Worlds; i++)
            {
                using var world = Build(FontScanMode.Shared);
            }

            shared.Stop();

            var perWorld = shared.Elapsed.TotalMilliseconds / Worlds;

            Assert.True(
                perWorld * 5 < scanned,
                $"a world that joins the index took {perWorld:F3} ms, one that scanned {Faces} " +
                $"faces took {scanned:F3} ms: the index is not being shared");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_font_installed_after_the_scan_is_seen_by_a_refreshing_world()
    {
        var fonts = FindSystemFonts();
        if (fonts.Length < 2)
        {
            return;
        }

        var directory = CreateDirectory();

        try
        {
            File.Copy(fonts[0], Path.Combine(directory, Path.GetFileName(fonts[0])));

            int Count(FontScanMode mode)
            {
                using var world = new TypstWorldBuilder()
                    .AddFontDirectory(directory, mode)
                    .WithoutFontIndexCache()
                    .AddSource("main.typ", "= Title")
                    .Build();

                return world.FontCount;
            }

            var before = Count(FontScanMode.Shared);

            // A font dropped into the directory after the process scanned it.
            File.Copy(fonts[1], Path.Combine(directory, Path.GetFileName(fonts[1])));

            Assert.Equal(before, Count(FontScanMode.Shared));

            var refreshed = Count(FontScanMode.Refresh);

            Assert.True(
                refreshed > before,
                $"the refreshing world should see more than {before} fonts, got {refreshed}");

            // The refresh replaced the process index, so the next world sees it too.
            Assert.Equal(refreshed, Count(FontScanMode.Shared));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_document_compiles_from_a_shared_index()
    {
        var fonts = FindSystemFonts();
        if (fonts.Length == 0)
        {
            return;
        }

        var directory = CreateDirectory();

        try
        {
            File.Copy(fonts[0], Path.Combine(directory, Path.GetFileName(fonts[0])));

            using var world = new TypstWorldBuilder()
                .AddFontDirectory(directory)
                .WithoutFontIndexCache()
                .AddSource("main.typ", "= Title\n\nA paragraph laid out from an indexed face.")
                .Build();

            using var result = new TypstCompiler().Compile(world);

            Assert.Equal(1, result.PageCount);

            // The face is parsed on first use, not at scan time: the family the document used is
            // resolvable through the index.
            Assert.NotEmpty(world.FontFamilies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
