using System;
using M3scalinum.Text.SharpTypst.Documents;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>Introspection helpers that the earlier tests did not reach.</summary>
public sealed class IntrospectionCoverageTests
{
    [Fact]
    public void Named_counters_emit_their_constructor()
    {
        Assert.Equal("counter(heading).display()", TypstCounter.Heading.Display().ToTypst());
        Assert.Equal("counter(figure).display()", TypstCounter.Figure.Display().ToTypst());
        Assert.Equal("counter(footnote).display()", TypstCounter.Footnote.Display().ToTypst());
        Assert.Equal("counter(math.equation).display()", TypstCounter.Equation.Display().ToTypst());
    }

    [Fact]
    public void A_named_counter_quotes_its_name()
    {
        Assert.Equal("counter(\"my-counter\").get()", TypstCounter.Of("my-counter").Get().ToTypst());
    }

    [Fact]
    public void A_counter_display_accepts_a_numbering_pattern()
    {
        Assert.Equal("counter(page).display(\"1 / 1\")", TypstCounter.Page.Display("1 / 1").ToTypst());
    }

    [Fact]
    public void A_counter_reads_at_a_location()
    {
        Assert.Equal("counter(page).at(here())", TypstCounter.Page.At(TypstLocations.Here).ToTypst());
    }

    [Fact]
    public void A_state_reads_at_a_location()
    {
        var state = TypstState.Of("s", TypstExpression.Number(0));

        Assert.Equal("state(\"s\", 0).at(here())", state.At(TypstLocations.Here).ToTypst());
    }

    [Fact]
    public void A_context_emits_text_let_and_measure()
    {
        var markup = TypstDocument.Create()
            .Context(context => context
                .Text("hello")
                .Let("x", TypstExpression.Number(1))
                .Measure(TypstContent.Empty.Text("m")))
            .ToMarkup();

        Assert.Contains("\"hello\"", markup, StringComparison.Ordinal);
        Assert.Contains("let x = 1", markup, StringComparison.Ordinal);
        Assert.Contains("measure(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outline_accepts_a_title()
    {
        var markup = TypstDocument.Create()
            .Outline(TypstContent.Empty.Text("Contents"), depth: 3)
            .ToMarkup();

        Assert.Contains("title: [Contents]", markup, StringComparison.Ordinal);
        Assert.Contains("depth: 3", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outline_without_a_title_omits_it()
    {
        var markup = TypstDocument.Create().Outline().ToMarkup();

        Assert.DoesNotContain("title:", markup, StringComparison.Ordinal);
        Assert.Contains("depth: 2", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_html_element_emits_its_attributes()
    {
        var expression = TypstHtml.Typed(
            "a",
            TypstContent.Empty.Text("link"),
            ("href", "https://example.com"));

        Assert.Equal("html.a(attrs: (href: \"https://example.com\"))[link]", expression.ToTypst());
    }

    [Fact]
    public void A_typed_html_element_without_attributes_or_body_is_a_bare_call()
    {
        Assert.Equal("html.br", TypstHtml.Typed("br").ToTypst());
    }

    [Fact]
    public void A_typed_html_element_without_attributes_keeps_its_body()
    {
        Assert.Equal("html.p[text]", TypstHtml.Typed("p", TypstContent.Empty.Text("text")).ToTypst());
    }

    [Fact]
    public void An_html_frame_wraps_its_content()
    {
        Assert.Equal("html.frame[text]", TypstHtml.Frame(TypstContent.Empty.Text("text")).ToTypst());
    }
}
