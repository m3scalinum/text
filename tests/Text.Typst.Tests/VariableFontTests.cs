using System;
using System.Linq;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The font-face inventory: what a world reports about the variation axes of the faces it resolves,
/// exercised against the fake native layer.
/// </summary>
/// <remarks>
/// The inventory is how a caller decides whether <c>#set text(weight: 350)</c> means anything: a face
/// with a <c>wght</c> axis interpolates to 350, a static face has no axis and serves its nearest cut.
/// The payload below is the shape <c>tn_world_font_faces</c> writes — one object per face, camelCase
/// keys, axes sorted by tag — and the integration suite proves the native side writes it.
/// </remarks>
public sealed class VariableFontTests
{
    private const string Payload = """
        [
          {"family":"Bahnschrift","weight":300,"stretch":100,"style":"normal","variable":true,
           "axes":[{"tag":"wdth","min":75,"max":100,"default":100},{"tag":"wght","min":300,"max":700,"default":300}]},
          {"family":"Libertinus Serif","weight":400,"stretch":100,"style":"normal","variable":false,"axes":[]}
        ]
        """;

    [Fact]
    public void A_variable_face_reports_the_range_of_its_weight_axis()
    {
        var native = new FakeTypstNative { FontFacesJson = Payload };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();
        var face = world.Fonts.Single(candidate => candidate.Family == "Bahnschrift");

        Assert.True(face.IsVariable);
        var weight = Assert.IsType<TypstFontAxis>(face.Axis("wght"));
        Assert.Equal(300, weight.Min);
        Assert.Equal(700, weight.Max);
        Assert.Equal(300, weight.Default);
        Assert.Equal(["wdth", "wght"], face.Axes.Select(axis => axis.Tag));
    }

    [Fact]
    public void An_intermediate_weight_is_inside_the_range_and_a_value_below_it_is_not()
    {
        var native = new FakeTypstNative { FontFacesJson = Payload };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();
        var weight = world.Fonts.Single(face => face.Family == "Bahnschrift").Axis("wght")!;

        Assert.True(weight.Contains(350), "350 must be inside the advertised range");
        Assert.True(weight.Contains(300), "the minimum is inside the range");
        Assert.True(weight.Contains(700), "the maximum is inside the range");
        Assert.False(weight.Contains(250), "250 is below the minimum");
        Assert.False(weight.Contains(750), "750 is above the maximum");
    }

    [Fact]
    public void A_static_face_carries_no_axis()
    {
        var native = new FakeTypstNative { FontFacesJson = Payload };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();
        var face = world.Fonts.Single(candidate => candidate.Family == "Libertinus Serif");

        Assert.False(face.IsVariable);
        Assert.Empty(face.Axes);
        Assert.Null(face.Axis("wght"));
        Assert.Equal(400, face.Weight);
        Assert.Equal("normal", face.Style);
    }

    [Fact]
    public void A_payload_without_faces_yields_no_face()
    {
        var native = new FakeTypstNative { FontFacesJson = "null" };

        using var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        Assert.Empty(world.Fonts);
    }

    [Fact]
    public void A_disposed_world_rejects_the_face_list()
    {
        var native = new FakeTypstNative();
        var world = new TypstWorldBuilder(native).AddSource("main.typ", "= x").Build();

        world.Dispose();

        Assert.Throws<ObjectDisposedException>(() => world.Fonts);
    }
}
