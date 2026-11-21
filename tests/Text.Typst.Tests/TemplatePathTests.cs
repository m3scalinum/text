using System;
using System.IO;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The manifest is a file on disk, and a template can come from a package or a directory the caller
/// does not control. Its <c>entry</c> must not read outside the template.
/// </summary>
/// <remarks>
/// Each case plants a real file outside the template directory, so the test fails for the right
/// reason: without the check, <c>File.Exists</c> finds it and the load succeeds.
/// </remarks>
public sealed class TemplatePathTests
{
    private static (string Root, string Template) Plant(string entry, string outsideName)
    {
        var root = Path.Combine(Path.GetTempPath(), "sharp-typst-tpl-" + Guid.NewGuid().ToString("N"));
        var template = Path.Combine(root, "template");

        Directory.CreateDirectory(template);

        // A valid template that lives outside the template directory.
        File.WriteAllText(Path.Combine(root, outsideName), "#let render(p) = [escaped]");

        File.WriteAllText(
            Path.Combine(template, "template.toml"),
            $"""
            name = "probe"
            version = "1.0.0"
            entry = "{entry}"
            """);

        return (root, template);
    }

    [Theory]
    [InlineData("../outside.typ", "outside.typ")]
    [InlineData("nested/../../outside.typ", "outside.typ")]
    // A backslash cannot reach the check through a TOML manifest: the parser rejects `\o` as an
    // invalid escape before the entry is ever resolved. The check still handles it, for a manifest
    // built in memory.
    public void Load_rejects_an_entry_that_escapes_the_template_directory(string entry, string outsideName)
    {
        var (root, template) = Plant(entry, outsideName);

        try
        {
            var exception = Assert.Throws<TypstTemplateException>(() => TypstTemplate.Load(template));

            Assert.Contains("relative path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:/windows/win.ini")]
    public void Load_rejects_an_absolute_entry(string entry)
    {
        var (root, template) = Plant(entry, "outside.typ");

        try
        {
            var exception = Assert.Throws<TypstTemplateException>(() => TypstTemplate.Load(template));

            Assert.Contains("relative path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_accepts_a_nested_entry_inside_the_template_directory()
    {
        var (root, template) = Plant("layout/template.typ", "outside.typ");

        try
        {
            Directory.CreateDirectory(Path.Combine(template, "layout"));
            File.WriteAllText(
                Path.Combine(template, "layout", "template.typ"),
                "#let render(p) = [ok]");

            var loaded = TypstTemplate.Load(template);

            Assert.Equal("probe", loaded.Manifest.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
