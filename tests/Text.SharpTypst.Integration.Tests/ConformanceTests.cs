using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Diagnostics;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// Compiles one document per reference category, each invoking every item of that category.
/// This is the proof that the whole reference is reachable through the shipped native library.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConformanceTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "M3scalinum.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("could not find the repository root from " + AppContext.BaseDirectory);
    }

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(Root, "tests", "fixtures", "conformance", name));

    /// <summary>A 1×1 transparent PNG.</summary>
    private static readonly byte[] Pixel =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static TypstWorldBuilder World(string fixture)
    {
        var builder = new TypstWorldBuilder().AddSource("main.typ", Fixture(fixture));

        builder.AddAsset("pixel.png", Pixel);
        builder.AddAsset("refs.bib", Encoding.UTF8.GetBytes(
            "@article{doe2026,\n  author = {Doe, Jane},\n  title = {A Paper},\n  journal = {Journal},\n  year = {2026}\n}\n"));

        builder.AddAsset("data.txt", Encoding.UTF8.GetBytes("hello"));
        builder.AddSource("lib.typ", "#let greet(name) = [Hello, #name!]");
        builder.AddAsset("data.json", Encoding.UTF8.GetBytes("""{"items": [1, 2]}"""));
        builder.AddAsset("data.yaml", Encoding.UTF8.GetBytes("name: x\n"));
        builder.AddAsset("data.toml", Encoding.UTF8.GetBytes("name = \"x\"\n"));
        builder.AddAsset("data.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        builder.AddAsset("data.xml", Encoding.UTF8.GetBytes("<root><a>1</a></root>"));

        // CBOR for {"a": 1}: map(1), text "a", int 1.
        builder.AddAsset("data.cbor", new byte[] { 0xA1, 0x61, 0x61, 0x01 });

        return builder;
    }

    public static TheoryData<string> Categories =>
    [
        "foundations.typ",
        "model.typ",
        "text.typ",
        "math.typ",
        "layout.typ",
        "visualize.typ",
        "introspection.typ",
        "symbols.typ",
        "data.typ",
        "pdf.typ",
    ];

    [Theory]
    [MemberData(nameof(Categories))]
    public void Compiles_every_item_of_the_category(string fixture)
    {
        using var world = World(fixture).Build();
        using var result = new TypstCompiler().Compile(world);

        var errors = new List<TypstDiagnostic>();

        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == TypstSeverity.Error)
            {
                errors.Add(diagnostic);
            }
        }

        Assert.True(errors.Count == 0, fixture + ": " + string.Join("; ", errors));
        Assert.True(result.PageCount > 0);
    }

    [Fact]
    public void Compiles_the_html_category_in_html_mode()
    {
        using var world = World("html.typ").Build();
        using var result = new TypstCompiler().CompileBoth(world);

        var html = result.Html.ExportHtmlString();

        Assert.Contains("box", html, StringComparison.Ordinal);
        Assert.Contains("A typed paragraph.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiles_the_bundle_category_and_collects_its_files()
    {
        using var world = World("bundle.typ").Build();
        var bundle = new TypstCompiler().CompileBundle(world);

        Assert.Contains("Bundled document", bundle.Html, StringComparison.Ordinal);
        Assert.Equal(3, bundle.Files.Count);

        var data = bundle.Files.Single(file => file.Path == "data.txt");
        Assert.Equal("hello from the bundle", Encoding.UTF8.GetString(data.Data));

        var meta = bundle.Files.Single(file => file.Path == "nested/meta.json");
        Assert.Equal("{\"count\": 2}", Encoding.UTF8.GetString(meta.Data));

        Assert.Contains(bundle.Files, file => file.Path == "index.html");
    }

    [Fact]
    public void Writes_a_bundle_to_a_directory()
    {
        using var world = World("bundle.typ").Build();
        var bundle = new TypstCompiler().CompileBundle(world);

        var directory = Path.Combine(Path.GetTempPath(), "sharp-typst-bundle-" + Guid.NewGuid().ToString("N"));

        try
        {
            bundle.WriteTo(directory);

            Assert.True(File.Exists(Path.Combine(directory, "index.html")));
            Assert.True(File.Exists(Path.Combine(directory, "data.txt")));
            Assert.True(File.Exists(Path.Combine(directory, "nested", "meta.json")));
            Assert.Equal("hello from the bundle", File.ReadAllText(Path.Combine(directory, "data.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
