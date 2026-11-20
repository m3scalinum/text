using System;
using System.Collections.Generic;
using M3scalinum.Text.SharpTypst.Internal;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TomlTests
{
    [Fact]
    public void Parses_scalars_tables_and_inline_tables()
    {
        const string text = """
            # a comment
            name = "invoice"
            version = "1.0.0"
            enabled = true
            count = 3
            ratio = 1.5
            authors = ["a", "b"]

            [parameters]
            title = { type = "string", required = true }
            """;

        var root = Toml.Parse(text);

        Assert.Equal("invoice", root["name"]);
        Assert.Equal(true, root["enabled"]);
        Assert.Equal(3L, root["count"]);
        Assert.Equal(1.5, root["ratio"]);
        Assert.Equal(["a", "b"], (List<object?>)root["authors"]!);

        var parameters = Assert.IsType<TomlTable>(root["parameters"]);
        var title = Assert.IsType<TomlTable>(parameters["title"]);
        Assert.Equal("string", title["type"]);
        Assert.Equal(true, title["required"]);
    }

    [Fact]
    public void Handles_escapes_and_hashes_inside_strings()
    {
        var root = Toml.Parse("""path = "a#b\"c\\d" """);

        Assert.Equal("a#b\"c\\d", root["path"]);
    }

    [Fact]
    public void Rejects_an_unterminated_string()
    {
        Assert.Throws<FormatException>(() => Toml.Parse("""name = "oops"""));
    }

    [Fact]
    public void Rejects_a_line_without_assignment()
    {
        Assert.Throws<FormatException>(() => Toml.Parse("just a line"));
    }
}