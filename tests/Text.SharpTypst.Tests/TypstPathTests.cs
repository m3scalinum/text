using System;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

public sealed class TypstPathTests
{
    private static TypstWorldBuilder NewBuilder() => new(new FakeTypstNative());

    [Theory]
    [InlineData("main.typ")]
    [InlineData("partials/header.typ")]
    [InlineData("a/b/c.typ")]
    public void Accepts_relative_forward_slash_paths(string path)
    {
        NewBuilder().AddSource(path, "Hello");
    }

    [Theory]
    [InlineData("partials\\header.typ")]
    [InlineData("/main.typ")]
    [InlineData("C:/main.typ")]
    [InlineData("../secrets.typ")]
    [InlineData("a//b.typ")]
    public void Rejects_paths_outside_the_virtual_root(string path)
    {
        var builder = NewBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddSource(path, "Hello"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_paths(string path)
    {
        var builder = NewBuilder();
        Assert.ThrowsAny<ArgumentException>(() => builder.AddSource(path, "Hello"));
    }
}
