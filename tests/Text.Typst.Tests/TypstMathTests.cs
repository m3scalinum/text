using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstMathTests
{
    [Fact]
    public void Formats_math_functions()
    {
        Assert.Equal("frac(a, b)", TypstMath.Frac("a", "b").ToTypst());
        Assert.Equal("sqrt(x)", TypstMath.Sqrt("x").ToTypst());
        Assert.Equal("root(x, 3)", TypstMath.Root("x", "3").ToTypst());
        Assert.Equal("binom(n, k)", TypstMath.Binom("n", "k").ToTypst());
        Assert.Equal("vec(a, b)", TypstMath.Vec("a", "b").ToTypst());
        Assert.Equal("cases(a, b)", TypstMath.Cases("a", "b").ToTypst());
        Assert.Equal("lr((a))", TypstMath.Lr("(a)").ToTypst());
        Assert.Equal("op(\"lim\")", TypstMath.Op("lim").ToTypst());
        Assert.Equal("op(\"lim\", limits: #true)", TypstMath.Op("lim", limits: true).ToTypst());
        Assert.Equal("cancel(x)", TypstMath.Cancel("x").ToTypst());
        Assert.Equal("primes(2)", TypstMath.Primes(2).ToTypst());
        Assert.Equal("stretch(x)", TypstMath.Stretch("x").ToTypst());
        Assert.Equal("class(\"relation\", x)", TypstMath.Class("relation", "x").ToTypst());
        Assert.Equal("accent(x, hat)", TypstMath.Accent("x", "hat").ToTypst());
    }

    [Fact]
    public void Formats_size_styles()
    {
        Assert.Equal("display(x)", TypstMath.Display("x").ToTypst());
        Assert.Equal("display(x, cramped: true)", TypstMath.Display("x", cramped: true).ToTypst());
        Assert.Equal("inline(x)", TypstMath.Inline("x").ToTypst());
        Assert.Equal("script(x, cramped: true)", TypstMath.Script("x").ToTypst());
        Assert.Equal("sscript(x, cramped: true)", TypstMath.Sscript("x").ToTypst());
    }

    [Fact]
    public void Formats_letterform_styles()
    {
        Assert.Equal("upright(A)", TypstMath.Upright("A").ToTypst());
        Assert.Equal("italic(A)", TypstMath.Italic("A").ToTypst());
        Assert.Equal("bold(A)", TypstMath.Bold("A").ToTypst());
        Assert.Equal("serif(A)", TypstMath.Serif("A").ToTypst());
        Assert.Equal("sans(A)", TypstMath.Sans("A").ToTypst());
        Assert.Equal("frak(P)", TypstMath.Frak("P").ToTypst());
        Assert.Equal("mono(x)", TypstMath.Mono("x").ToTypst());
        Assert.Equal("bb(N)", TypstMath.Bb("N").ToTypst());
        Assert.Equal("cal(P)", TypstMath.Cal("P").ToTypst());
        Assert.Equal("scr(L)", TypstMath.Scr("L").ToTypst());
    }

    [Fact]
    public void Formats_under_and_over()
    {
        Assert.Equal("underline(x)", TypstMath.Underline("x").ToTypst());
        Assert.Equal("overline(x)", TypstMath.Overline("x").ToTypst());
        Assert.Equal("underbrace(x)", TypstMath.Underbrace("x").ToTypst());
        Assert.Equal("underbrace(x, n)", TypstMath.Underbrace("x", "n").ToTypst());
        Assert.Equal("overbrace(x, n)", TypstMath.Overbrace("x", "n").ToTypst());
        Assert.Equal("underbracket(x)", TypstMath.Underbracket("x").ToTypst());
        Assert.Equal("overbracket(x)", TypstMath.Overbracket("x").ToTypst());
        Assert.Equal("underparen(x)", TypstMath.Underparen("x").ToTypst());
        Assert.Equal("overparen(x)", TypstMath.Overparen("x").ToTypst());
        Assert.Equal("undershell(x)", TypstMath.Undershell("x").ToTypst());
        Assert.Equal("overshell(x)", TypstMath.Overshell("x").ToTypst());
    }

    [Fact]
    public void Rejects_an_unknown_math_class()
    {
        Assert.Throws<ArgumentException>(() => TypstMath.Class("bold", "x"));
    }

    [Fact]
    public void Formats_a_matrix_with_row_separators()
    {
        Assert.Equal("mat(1, 2; 3, 4)", TypstMath.Mat(["1", "2"], ["3", "4"]).ToTypst());
    }

    [Fact]
    public void Formats_attachments()
    {
        Assert.Equal("attach(x, t: 2)", TypstMath.Attach("x", top: "2").ToTypst());
        Assert.Equal(
            "attach(x, t: 2, b: 3)",
            TypstMath.Attach("x", top: "2", bottom: "3").ToTypst());
    }

    [Fact]
    public void Emits_a_display_equation_from_an_expression()
    {
        var markup = TypstDocument.Create()
            .Math(TypstMath.Frac("a", "b"))
            .ToMarkup();

        Assert.Contains("$ frac(a, b) $", markup, StringComparison.Ordinal);
    }
}