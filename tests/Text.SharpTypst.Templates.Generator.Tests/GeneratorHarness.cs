using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using M3scalinum.Text.SharpTypst.Templates;

namespace M3scalinum.Text.SharpTypst.Templates.Generator.Tests;

/// <summary>Runs the generator in memory and returns what it produced.</summary>
internal static class GeneratorHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    public static GeneratorResult Run(string source, params (string Path, string Text)[] additionalFiles)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
                generators: [new TypstTemplateGenerator().AsSourceGenerator()],
                additionalTexts: additionalFiles.Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text)))
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = output.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(tree => tree.ToString())
            .ToList();

        var errors = output.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        return new GeneratorResult(generated, diagnostics, errors);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        builder.Add(MetadataReference.CreateFromFile(typeof(ITypstTemplateOptions).Assembly.Location));

        return builder.ToImmutable();
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            => SourceText.From(text);
    }
}