using System;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// A clone shares what the original already paid for — the library, the font book and the fonts —
/// instead of building them again.
/// </summary>
/// <remarks>
/// The property is not observable through the managed API: a clone that copied the font book would
/// still compile the same document with the same fonts, just far more slowly, so comparing elapsed
/// time measures the runner rather than the clone. What the managed layer decides is whether the
/// clone is taken from the original's handle (<c>tn_world_clone</c>, the call that shares the three
/// pieces through <c>Arc</c>) or built from scratch, and that is asserted here by reference.
/// </remarks>
public sealed class WorldSharingTests
{
    private static ReadOnlySpan<byte> FontBytes => [0x00, 0x01, 0x00, 0x00];

    [Fact]
    public void A_clone_is_taken_from_its_original_and_parses_no_font_again()
    {
        var native = new FakeTypstNative();
        using var template = new TypstWorldBuilder(native)
            .AddSource("main.typ", "= x")
            .AddFont(FontBytes)
            .Build();

        Assert.Equal(1, native.CreateWorldCalls);
        Assert.Single(native.Fonts);

        using var clone = template.Clone();

        // One native call, taken on the original's handle: the fonts and the sources the original
        // holds are reachable from the clone without being registered a second time.
        Assert.Equal(1, native.CloneCount);
        Assert.Same(template.Handle, native.ClonedFrom);

        // A clone that rebuilt the world instead of sharing it would show here.
        Assert.Equal(1, native.CreateWorldCalls);
        Assert.Single(native.Fonts);
    }

    [Fact]
    public void A_specialised_clone_is_also_taken_from_its_original()
    {
        var native = new FakeTypstNative();
        using var template = new TypstWorldBuilder(native)
            .AddSource("main.typ", "= x")
            .AddFont(FontBytes)
            .Build();

        using var specialised = template.WithSource("main.typ", "= y");

        Assert.Equal(1, native.CloneCount);
        Assert.Same(template.Handle, native.ClonedFrom);
        Assert.Equal(1, native.CreateWorldCalls);
        Assert.Single(native.Fonts);
    }
}
