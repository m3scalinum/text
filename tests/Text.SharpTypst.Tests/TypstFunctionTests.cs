using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TypstFunctionTests
{
    [Fact]
    public void Emits_a_definition_with_parameters()
    {
        var function = TypstFunction.Define("greet")
            .Parameter("name")
            .Body("[Hello, #name!]");

        Assert.Equal("#let greet(name) = [Hello, #name!]", function.ToTypst());
    }

    [Fact]
    public void Emits_a_rest_parameter()
    {
        var function = TypstFunction.Define("count")
            .Rest("args")
            .Body("args.pos().len()");

        Assert.Equal("#let count(..args) = args.pos().len()", function.ToTypst());
    }

    [Fact]
    public void Emits_a_definition_without_parameters()
    {
        var function = TypstFunction.Define("now").Body("datetime.today()");

        Assert.Equal("#let now() = datetime.today()", function.ToTypst());
    }

    [Fact]
    public void Emits_a_definition_with_a_default_body()
    {
        Assert.Equal("#let noop() = none", TypstFunction.Define("noop").ToTypst());
    }

    [Fact]
    public void Rejects_an_empty_name()
    {
        Assert.ThrowsAny<ArgumentException>(() => TypstFunction.Define(" "));
    }

    [Fact]
    public void Emits_a_function_value()
    {
        Assert.Equal("(x) => x + 1", TypstExpression.Function("x", "x + 1").ToTypst());
        Assert.Equal("() => 1", TypstExpression.Function(string.Empty, "1").ToTypst());
    }

    [Fact]
    public void Emits_the_arguments_value()
    {
        Assert.Equal("arguments", TypstExpression.Arguments.ToTypst());
    }

    [Fact]
    public void Emits_a_module_member()
    {
        Assert.Equal("m.greet", TypstExpression.Member("m", "greet").ToTypst());
    }

    [Fact]
    public void Emits_an_emoji()
    {
        Assert.Equal("emoji.face", TypstExpression.Emoji("face").ToTypst());
    }
}