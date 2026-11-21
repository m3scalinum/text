using System;
using M3scalinum.Text.Epub;
using Xunit;

namespace M3scalinum.Text.Epub.Tests;

/// <summary>The failure types a caller catches, and the cause each keeps.</summary>
public sealed class EpubExceptionTests
{
    [Fact]
    public void A_validation_failure_is_an_epub_failure_with_a_cause_when_it_has_one()
    {
        var cause = new InvalidOperationException("the part was not written");

        var plain = new EpubValidationException("the id is empty");
        var wrapped = new EpubValidationException("the part was not written", cause);

        Assert.IsAssignableFrom<EpubException>(plain);
        Assert.Equal("the id is empty", plain.Message);
        Assert.Same(cause, wrapped.InnerException);
        Assert.Equal("the part was not written", wrapped.Message);
    }

    [Fact]
    public void A_format_failure_is_an_epub_failure_with_a_cause_when_it_has_one()
    {
        var cause = new FormatException("the fragment is not well formed");

        var plain = new EpubFormatException("the document is not HTML this reader can read");
        var wrapped = new EpubFormatException("the fragment is not well formed", cause);

        Assert.IsAssignableFrom<EpubException>(plain);
        Assert.Equal("the document is not HTML this reader can read", plain.Message);
        Assert.Same(cause, wrapped.InnerException);
        Assert.Equal("the fragment is not well formed", wrapped.Message);
    }

    [Fact]
    public void The_base_failure_keeps_its_cause_too()
    {
        var cause = new InvalidOperationException("underneath");

        var plain = new EpubException("failed");
        var wrapped = new EpubException("failed", cause);

        Assert.Equal("failed", plain.Message);
        Assert.Same(cause, wrapped.InnerException);
    }
}
