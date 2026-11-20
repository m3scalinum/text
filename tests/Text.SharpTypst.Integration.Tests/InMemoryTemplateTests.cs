using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Templates;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>Templates loaded from memory, and bundles written as a zip.</summary>
[Trait("Category", "Integration")]
public sealed class InMemoryTemplateTests
{
    private const string Manifest = """
        name = "letter"
        version = "1.0.0"
        entry = "template.typ"

        [parameters]
        recipient = { type = "string", required = true }
        """;

    private const string Entry = """
        #let render(p) = [
          #set page(paper: "a4")

          = Letter

          Dear #p.recipient,
        ]
        """;

    private static Dictionary<string, byte[]> Files() => new(StringComparer.Ordinal)
    {
        ["template.typ"] = Encoding.UTF8.GetBytes(Entry),
        ["assets/logo.svg"] = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10"/></svg>"""),
    };

    [Fact]
    public void A_template_loads_from_memory()
    {
        var template = TypstTemplate.Load(Manifest, Files());

        Assert.Equal("letter", template.Manifest.Name);
        Assert.Equal("template.typ", template.Manifest.Entry);
    }

    [Fact]
    public void An_in_memory_template_renders()
    {
        var template = TypstTemplate.Load(Manifest, Files());

        var pdf = template.Render(new TypstCompiler(), new Dictionary<string, object?>
        {
            ["recipient"] = "Ada",
        });

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);
    }

    [Fact]
    public void A_missing_entry_file_is_rejected()
    {
        var files = Files();
        files.Remove("template.typ");

        Assert.Throws<TypstTemplateException>(() => TypstTemplate.Load(Manifest, files));
    }

    [Fact]
    public void An_entry_without_render_is_rejected()
    {
        var files = Files();
        files["template.typ"] = Encoding.UTF8.GetBytes("= Not a template");

        Assert.Throws<TypstTemplateException>(() => TypstTemplate.Load(Manifest, files));
    }

    [Fact]
    public void A_bundle_writes_to_a_zip()
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", """
                #document("index.html")[
                  #set document(title: "Bundle")
                  = Bundled
                ]

                #asset("data.txt", "hello")
                """)
            .Build();

        var bundle = new TypstCompiler().CompileBundle(world);

        var path = Path.Combine(Path.GetTempPath(), "sharp-typst-bundle-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            bundle.WriteToZip(path);

            Assert.True(File.Exists(path));

            using var archive = ZipFile.OpenRead(path);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();

            Assert.Contains("index.html", names);
            Assert.Contains("data.txt", names);

            var data = archive.GetEntry("data.txt")!;
            using var reader = new StreamReader(data.Open());
            Assert.Equal("hello", reader.ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
