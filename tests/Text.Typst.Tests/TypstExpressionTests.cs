using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstExpressionTests
{
    [Fact]
    public void Formats_scalars()
    {
        Assert.Equal("none", TypstExpression.None.ToTypst());
        Assert.Equal("auto", TypstExpression.Auto.ToTypst());
        Assert.Equal("true", TypstExpression.Bool(true).ToTypst());
        Assert.Equal("false", TypstExpression.Bool(false).ToTypst());
        Assert.Equal("42", TypstExpression.Number(42).ToTypst());
        Assert.Equal("1.5", TypstExpression.Number(1.5).ToTypst());
        Assert.Equal("\"a\\\"b\"", TypstExpression.Str("a\"b").ToTypst());
    }

    [Fact]
    public void Formats_arrays_with_the_trailing_comma_rule()
    {
        Assert.Equal("()", TypstExpression.Array().ToTypst());
        Assert.Equal("(1,)", TypstExpression.Array(TypstExpression.Number(1)).ToTypst());
        Assert.Equal(
            "(1, 2)",
            TypstExpression.Array(TypstExpression.Number(1), TypstExpression.Number(2)).ToTypst());
    }

    [Fact]
    public void Formats_a_dictionary()
    {
        var dictionary = TypstExpression.Dictionary(
            ("name", TypstExpression.Str("x")),
            ("count", TypstExpression.Number(2)));

        Assert.Equal("(name: \"x\", count: 2)", dictionary.ToTypst());
    }

    [Fact]
    public void Formats_module_members()
    {
        Assert.Equal("sym.arrow.r", TypstExpression.Symbol("arrow.r").ToTypst());
        Assert.Equal("calc.pow(2, 8)", TypstExpression.Calc("pow(2, 8)").ToTypst());
        Assert.Equal("std.x", TypstExpression.Std("x").ToTypst());
        Assert.Equal("sys.x", TypstExpression.Sys("x").ToTypst());
        Assert.Equal("target", TypstExpression.Target.ToTypst());
    }

    [Fact]
    public void Formats_the_foundation_functions()
    {
        Assert.Equal("eval(\"1 + 1\")", TypstExpression.Eval("1 + 1").ToTypst());
        Assert.Equal("repr(x)", TypstExpression.Repr(TypstExpression.Raw("x")).ToTypst());
        Assert.Equal("type(x)", TypstExpression.TypeOf(TypstExpression.Raw("x")).ToTypst());
        Assert.Equal("version(0, 15, 1)", TypstExpression.Version(0, 15, 1).ToTypst());
        Assert.Equal("regex(\"a+\")", TypstExpression.Regex("a+").ToTypst());
        Assert.Equal("selector(heading)", TypstExpression.Selector("heading").ToTypst());
        Assert.Equal("duration(seconds: 5)", TypstExpression.Duration(seconds: 5).ToTypst());
        Assert.Equal("bytes(\"hello\")", TypstExpression.Bytes("hello").ToTypst());
        Assert.Equal("path(\"a/b\")", TypstExpression.Path("a/b").ToTypst());
        Assert.Equal("panic(\"boom\")", TypstExpression.Panic("boom").ToTypst());
        Assert.Equal("<fig:a>", TypstExpression.Label("fig:a").ToTypst());
    }

    [Fact]
    public void Formats_assert_with_and_without_a_message()
    {
        var condition = TypstExpression.Raw("x > 0");

        Assert.Equal("assert(x > 0)", TypstExpression.Assert(condition).ToTypst());
        Assert.Equal("assert(x > 0, message: \"positive\")", TypstExpression.Assert(condition, "positive").ToTypst());
    }

    [Fact]
    public void Formats_a_duration_from_named_components()
    {
        Assert.Equal("duration(hours: 12, days: 3)", TypstExpression.Duration(days: 3, hours: 12).ToTypst());
    }

    [Fact]
    public void Formats_content_as_a_value()
    {
        var content = new TypstContent().Text("hello");

        Assert.Equal("[hello]", TypstExpression.Content(content).ToTypst());
    }
}