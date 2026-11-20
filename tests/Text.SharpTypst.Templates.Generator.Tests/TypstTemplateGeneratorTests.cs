using System;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Templates.Generator.Tests;

/// <summary>What the generator emits for a manifest, and what it rejects.</summary>
public sealed class TypstTemplateGeneratorTests
{
    private const string ManifestPath = "/project/templates/invoice/template.toml";

    private const string Manifest = """
        name = "invoice"
        version = "1.0.0"
        entry = "template.typ"

        [parameters]
        title = { type = "string", required = true }
        number = { type = "string", required = true }
        issued = { type = "date", required = true }
        currency = { type = "string", default = "EUR" }
        items = { type = "array", of = "object", required = true }
        total = { type = "string", required = true }
        notes = { type = "string", default = "" }
        line_count = { type = "number", default = 3 }
        """;

    private const string Source = """
        using M3scalinum.Text.SharpTypst.Templates;

        namespace Demo;

        [TypstTemplate("templates/invoice")]
        public partial record InvoiceOptions;
        """;

    private static GeneratorResult Run(string source = Source, string manifest = Manifest, string path = ManifestPath)
        => GeneratorHarness.Run(source, (path, manifest));

    [Fact]
    public void Generates_one_property_per_parameter()
    {
        var result = Run();

        Assert.Empty(result.Errors);
        Assert.Single(result.Generated);

        var generated = result.Single;

        Assert.Contains("partial record InvoiceOptions", generated, StringComparison.Ordinal);
        Assert.Contains("public required string Title { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required string Number { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required global::System.DateOnly Issued { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public string Currency { get; init; } = \"EUR\";", generated, StringComparison.Ordinal);
        Assert.Contains("public string Notes { get; init; } = \"\";", generated, StringComparison.Ordinal);
        Assert.Contains("public double LineCount { get; init; } = 3;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Maps_an_array_of_objects()
    {
        var generated = Run().Single;

        Assert.Contains(
            "public required global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.IReadOnlyDictionary<string, object?>> Items { get; init; }",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Maps_an_array_of_strings()
    {
        var manifest = """
            name = "t"
            version = "1.0.0"
            entry = "template.typ"

            [parameters]
            tags = { type = "array", of = "string", required = true }
            """;

        var generated = Run(manifest: manifest).Single;

        Assert.Contains(
            "public required global::System.Collections.Generic.IReadOnlyList<string> Tags { get; init; }",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Maps_every_declared_type()
    {
        var manifest = """
            name = "t"
            version = "1.0.0"
            entry = "template.typ"

            [parameters]
            a = { type = "string", required = true }
            b = { type = "number", required = true }
            c = { type = "boolean", required = true }
            d = { type = "date", required = true }
            e = { type = "asset", required = true }
            f = { type = "object", required = true }
            """;

        var generated = Run(manifest: manifest).Single;

        Assert.Contains("public required string A { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required double B { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required bool C { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required global::System.DateOnly D { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains("public required string E { get; init; }", generated, StringComparison.Ordinal);
        Assert.Contains(
            "public required global::System.Collections.Generic.IReadOnlyDictionary<string, object?> F { get; init; }",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Maps_parameters_to_their_manifest_names()
    {
        var generated = Run().Single;

        Assert.Contains("[\"title\"] = Title,", generated, StringComparison.Ordinal);
        Assert.Contains("[\"line_count\"] = LineCount,", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Collections.Generic.IReadOnlyDictionary<string, object?> ToParameters()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Works_for_a_class_as_well_as_a_record()
    {
        var source = """
            using M3scalinum.Text.SharpTypst.Templates;

            namespace Demo;

            [TypstTemplate("templates/invoice")]
            public partial class InvoiceOptions;
            """;

        var result = Run(source);

        Assert.Empty(result.Errors);
        Assert.Contains("partial class InvoiceOptions", result.Single, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_missing_manifest()
    {
        var result = Run(path: "/project/templates/other/template.toml");

        Assert.Contains("SHT001", result.DiagnosticIds);
        Assert.Empty(result.Generated);
    }

    [Fact]
    public void Reports_a_type_that_is_not_partial()
    {
        var source = """
            using M3scalinum.Text.SharpTypst.Templates;

            namespace Demo;

            [TypstTemplate("templates/invoice")]
            public record InvoiceOptions;
            """;

        var result = Run(source);

        Assert.Contains("SHT003", result.DiagnosticIds);
        Assert.Empty(result.Generated);
    }

    [Fact]
    public void Reports_a_primary_constructor()
    {
        var source = """
            using M3scalinum.Text.SharpTypst.Templates;

            namespace Demo;

            [TypstTemplate("templates/invoice")]
            public partial record InvoiceOptions(string Title);
            """;

        var result = Run(source);

        Assert.Contains("SHT004", result.DiagnosticIds);
        Assert.Empty(result.Generated);
    }

    [Fact]
    public void Reports_an_unknown_parameter_type()
    {
        var manifest = """
            name = "t"
            version = "1.0.0"
            entry = "template.typ"

            [parameters]
            weird = { type = "quantum", required = true }
            """;

        var result = Run(manifest: manifest);

        Assert.Contains("SHT005", result.DiagnosticIds);
        Assert.Contains("public required object? Weird { get; init; }", result.Single, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_malformed_parameter_entry()
    {
        var manifest = """
            name = "t"
            version = "1.0.0"
            entry = "template.typ"

            [parameters]
            broken = "not a table"
            """;

        var result = Run(manifest: manifest);

        Assert.Contains("SHT002", result.DiagnosticIds);
    }

    [Fact]
    public void Ignores_parameters_outside_the_parameters_table()
    {
        var manifest = """
            name = "t"
            version = "1.0.0"
            entry = "template.typ"
            title = { type = "string", required = true }

            [parameters]
            real = { type = "string", required = true }
            """;

        var generated = Run(manifest: manifest).Single;

        Assert.Contains("Real", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Title", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Handles_comments_and_blank_lines()
    {
        var manifest = """
            # a comment
            name = "t"
            version = "1.0.0"
            entry = "template.typ"

            [parameters]
            # another comment
            value = { type = "string", required = true }  # trailing
            """;

        var result = Run(manifest: manifest);

        Assert.Empty(result.Errors);
        Assert.Contains("public required string Value { get; init; }", result.Single, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_code_compiles()
    {
        var result = Run();

        Assert.Empty(result.Errors);
    }
}
