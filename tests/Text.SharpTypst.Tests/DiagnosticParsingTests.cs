using System.Linq;
using M3scalinum.Text.SharpTypst.Diagnostics;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class DiagnosticParsingTests
{
    private const string Payload = """
        {
          "code": "compilation",
          "message": "1 error(s), first: unknown variable: `totl` at main.typ:12:8",
          "diagnostics": [
            {
              "severity": "error",
              "message": "unknown variable: `totl`",
              "hints": ["did you mean `total`?"],
              "trace": ["main.typ:12:8 in render"],
              "file": "main.typ",
              "line": 12,
              "column": 8,
              "endLine": 12,
              "endColumn": 13,
              "isInSource": true
            },
            {
              "severity": "warning",
              "message": "unknown font family: Inter",
              "hints": [],
              "trace": [],
              "file": null,
              "line": null,
              "column": null,
              "endLine": null,
              "endColumn": null,
              "isInSource": false
            }
          ]
        }
        """;

    [Fact]
    public void Maps_every_field_of_a_diagnostic()
    {
        var diagnostics = NativeErrorTranslator.ParseDiagnostics(Payload);

        Assert.Equal(2, diagnostics.Count);

        var error = diagnostics[0];
        Assert.Equal(TypstSeverity.Error, error.Severity);
        Assert.Equal("unknown variable: `totl`", error.Message);
        Assert.Equal(new[] { "did you mean `total`?" }, error.Hints);
        Assert.Equal(new[] { "main.typ:12:8 in render" }, error.Trace);
        Assert.NotNull(error.Location);
        Assert.Equal("main.typ", error.Location!.File);
        Assert.Equal(12, error.Location.Line);
        Assert.Equal(8, error.Location.Column);
        Assert.Equal(13, error.Location.EndColumn);
        Assert.True(error.Location.IsInSource);
    }

    [Fact]
    public void Keeps_a_diagnostic_without_span()
    {
        var warning = NativeErrorTranslator.ParseDiagnostics(Payload)[1];

        Assert.Equal(TypstSeverity.Warning, warning.Severity);
        Assert.Null(warning.Location);
        Assert.Empty(warning.Hints);
    }

    [Fact]
    public void Formats_a_single_line()
    {
        var error = NativeErrorTranslator.ParseDiagnostics(Payload)[0];

        Assert.Equal("main.typ:12:8: error: unknown variable: `totl`", error.ToString());
    }

    [Fact]
    public void Formats_hints_on_following_lines()
    {
        var error = NativeErrorTranslator.ParseDiagnostics(Payload)[0];

        var text = error.ToDisplayString(includeHints: true);

        Assert.Contains("hint: did you mean `total`?", text);
    }

    [Fact]
    public void Returns_empty_for_an_empty_payload()
    {
        Assert.Empty(NativeErrorTranslator.ParseDiagnostics(string.Empty));
        Assert.Empty(NativeErrorTranslator.ParseDiagnostics("{}"));
    }

    [Fact]
    public void Survives_a_malformed_payload()
    {
        Assert.Empty(NativeErrorTranslator.ParseDiagnostics("{ not json"));
    }

    [Fact]
    public void Unknown_severity_is_a_hint()
    {
        const string json = """
            { "diagnostics": [ { "severity": "note", "message": "m" } ] }
            """;

        var diagnostic = NativeErrorTranslator.ParseDiagnostics(json).Single();

        Assert.Equal(TypstSeverity.Hint, diagnostic.Severity);
    }
}
