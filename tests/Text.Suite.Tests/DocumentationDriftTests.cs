using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.DependencyInjection;
using Xunit;

namespace M3scalinum.Text.Suite.Tests;

/// <summary>
/// Fails when the documentation names a type or a method that does not exist.
/// </summary>
/// <remarks>
/// The specifications are the contract; a snippet that calls <c>ExportHtml()</c> when the method is
/// <c>ExportHtmlString()</c> is a defect a reader only discovers by compiling. This test reads every
/// C# block in the docs and checks each identifier against the public surface.
/// </remarks>
public sealed class DocumentationDriftTests
{
    /// <summary>Types the docs describe on purpose even though they are internal.</summary>
    private static readonly HashSet<string> AllowedInternalTypes = new(StringComparer.Ordinal)
    {
        "TypstMarkupWriter",
    };

    /// <summary>Members that come from the BCL or from a package, not from M3scalinum.Text.Typst.</summary>
    private static readonly HashSet<string> AllowedForeignMembers = new(StringComparer.Ordinal)
    {
        // OpenTelemetry wiring, shown in 07-diagnostics.md and Performance.md.
        "AddMeter",
        "AddOpenTelemetry",
        "AddTypst",
        "AddSource",
        "Combine",
        "LoadPkcs12FromFile",
        "LogWarning",
        "ReadAllBytes",
        "ReadAllText",
        "WithMetrics",
        "WithTracing",
        "WriteAllBytes",
        "WriteLine",
    };

    [Fact]
    public void Every_Typst_type_named_in_the_documentation_exists()
    {
        var types = PublicTypeNames();
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (file, block) in CSharpBlocks())
        {
            foreach (Match match in Regex.Matches(block, @"\b(Typst\w+)\b"))
            {
                var name = match.Groups[1].Value;

                if (!types.Contains(name) && !AllowedInternalTypes.Contains(name))
                {
                    unknown.Add($"{name} ({file})");
                }
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_method_called_in_the_documentation_exists()
    {
        var members = PublicMemberNames();
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (file, block) in CSharpBlocks())
        {
            foreach (Match match in Regex.Matches(block, @"\.(\w+)\s*\("))
            {
                var name = match.Groups[1].Value;

                if (!members.Contains(name) && !AllowedForeignMembers.Contains(name))
                {
                    unknown.Add($"{name} ({file})");
                }
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_ABI_function_in_the_documentation_is_exported()
    {
        var root = RepositoryRoot();
        var lib = File.ReadAllText(Path.Combine(root, "native", "sharp-typst", "src", "lib.rs"));
        var abi = File.ReadAllText(Path.Combine(root, "docs", "specifications", "02-native-abi.md"));

        var exported = Regex.Matches(lib, @"extern ""C"" fn (tn_\w+)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var documented = Regex.Matches(abi, @"\b(tn_\w+)\s*\(")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = documented.Except(exported).OrderBy(name => name, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_documentation_is_found()
    {
        // Guards the two tests above: a wrong root would make them pass vacuously.
        var blocks = CSharpBlocks().ToList();

        Assert.True(blocks.Count > 20, $"only {blocks.Count} C# blocks found");
        Assert.Contains(blocks, block => block.File == "03-compilation.md");
        Assert.Contains(blocks, block => block.File == "04-document-model.md");
        Assert.Contains(blocks, block => block.File == "Home.md");
    }

    private static HashSet<string> PublicTypeNames()
        => Assemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> PublicMemberNames()
        => Assemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<Assembly> Assemblies()
    {
        yield return typeof(TypstCompiler).Assembly;
        yield return typeof(M3scalinum.Text.Documents.TypstDocument).Assembly;
        yield return typeof(TypstOptions).Assembly;
        yield return typeof(M3scalinum.Text.Pdf.PdfSigner).Assembly;
        yield return typeof(M3scalinum.Text.Templates.TemplateCatalog).Assembly;
        yield return typeof(M3scalinum.Text.Mcp.McpServer).Assembly;
        yield return typeof(M3scalinum.Text.Barcodes.Barcode).Assembly;
        yield return typeof(M3scalinum.Text.Ocr.OcrEngine).Assembly;
        yield return typeof(M3scalinum.Text.Docx.DocxReader).Assembly;
        yield return typeof(M3scalinum.Text.Xlsx.XlsxReader).Assembly;
    }

    private static IEnumerable<(string File, string Block)> CSharpBlocks()
    {
        var root = RepositoryRoot();

        foreach (var path in DocumentationFiles(root))
        {
            var text = File.ReadAllText(path);
            var file = Path.GetFileName(path);

            foreach (Match match in Regex.Matches(text, "```csharp\r?\n(.*?)```", RegexOptions.Singleline))
            {
                yield return (file, StripStringLiterals(match.Groups[1].Value));
            }
        }
    }

    private static IEnumerable<string> DocumentationFiles(string root)
    {
        var specifications = Path.Combine(root, "docs", "specifications");

        foreach (var path in Directory.EnumerateFiles(specifications, "*.md"))
        {
            yield return path;
        }

        var wiki = Path.Combine(root, "wiki");
        if (Directory.Exists(wiki))
        {
            foreach (var path in Directory.EnumerateFiles(wiki, "*.md"))
            {
                yield return path;
            }
        }

        var readme = Path.Combine(root, "README.md");
        if (File.Exists(readme))
        {
            yield return readme;
        }
    }

    /// <summary>
    /// Removes string literals, so Typst code embedded in a C# string is not mistaken for C#.
    /// </summary>
    private static string StripStringLiterals(string block)
    {
        var withoutRaw = Regex.Replace(block, "\"\"\".*?\"\"\"", "\"\"", RegexOptions.Singleline);
        return Regex.Replace(withoutRaw, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "specifications")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find docs/specifications above {AppContext.BaseDirectory}");
    }
}
