using System;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

public sealed class TypstIntrospectionTests
{
    [Fact]
    public void Formats_counter_calls()
    {
        Assert.Equal("counter(page).display()", TypstCounter.Page.Display().ToTypst());
        Assert.Equal("counter(page).display(\"1 / 1\")", TypstCounter.Page.Display("1 / 1").ToTypst());
        Assert.Equal("counter(heading).get()", TypstCounter.Heading.Get().ToTypst());
        Assert.Equal("counter(page).step(2)", TypstCounter.Page.Step(2).ToTypst());
        Assert.Equal("counter(page).update(5)", TypstCounter.Page.Update(5).ToTypst());
        Assert.Equal("counter(page).final()", TypstCounter.Page.Final().ToTypst());
        Assert.Equal("counter(\"custom\")", TypstCounter.Of("custom").Get().ToTypst().Replace(".get()", string.Empty));
    }

    [Fact]
    public void Formats_state_calls()
    {
        var state = TypstState.Of("seen", TypstExpression.Bool(false));

        Assert.Equal("state(\"seen\", false).get()", state.Get().ToTypst());
        Assert.Equal("state(\"seen\", false).update(true)", state.Update(TypstExpression.Bool(true)).ToTypst());
        Assert.Equal("state(\"seen\", false).final()", state.Final().ToTypst());
    }

    [Fact]
    public void Formats_queries()
    {
        Assert.Equal("query(heading)", TypstQuery.Headings().ToTypst());
        Assert.Equal("query(heading.where(level: 1))", TypstQuery.Headings(1).ToTypst());
        Assert.Equal("query(figure)", TypstQuery.Figures().ToTypst());
        Assert.Equal("query(<fig:a>)", TypstQuery.Labels("fig:a").ToTypst());
    }

    [Fact]
    public void Rejects_a_heading_level_outside_one_to_six()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TypstQuery.Headings(7));
    }

    [Fact]
    public void Formats_locations()
    {
        Assert.Equal("here()", TypstLocations.Here.ToTypst());
        Assert.Equal("here().page()", TypstLocations.Page.ToTypst());
        Assert.Equal("locate(<intro>)", TypstLocations.Locate("intro").ToTypst());
    }

    [Fact]
    public void Builds_a_context_block()
    {
        var markup = TypstDocument.Create()
            .Context(context => context
                .Let("n", TypstCounter.Page.Get())
                .Statement(TypstCounter.Page.Display("1 / 1")))
            .ToMarkup();

        Assert.Contains(
            "#context {let n = counter(page).get(); counter(page).display(\"1 / 1\")}",
            markup,
            StringComparison.Ordinal);
    }
}