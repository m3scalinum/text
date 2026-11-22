using System.Text.Json;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The builder's font-index settings reach the native side: a world either joins the process-wide
/// index or asks for a scan, and that decision is the whole reason two thousand compilations cost
/// one scan.
/// </summary>
public sealed class FontIndexBuilderTests
{
    private static JsonElement Fonts(TypstWorldBuilder builder, FakeTypstNative native)
    {
        using var world = builder.Build();

        using var document = JsonDocument.Parse(native.WorldConfigJson!);
        return document.RootElement.GetProperty("fonts").Clone();
    }

    [Fact]
    public void World_fonts_default_to_sharing_the_index_and_the_persistent_cache()
    {
        var native = new FakeTypstNative();

        var fonts = Fonts(new TypstWorldBuilder(native).AddSystemFonts(), native);

        Assert.True(fonts.GetProperty("system").GetBoolean());
        Assert.False(fonts.GetProperty("refresh").GetBoolean());
        Assert.True(fonts.GetProperty("cache").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fonts.GetProperty("cacheDir").ValueKind);
    }

    [Fact]
    public void A_refresh_scan_is_requested_for_either_source()
    {
        var native = new FakeTypstNative();
        var fonts = Fonts(
            new TypstWorldBuilder(native)
                .AddSystemFonts(FontScanMode.Refresh)
                .AddFontDirectory("/opt/fonts", FontScanMode.Refresh),
            native);

        Assert.True(fonts.GetProperty("refresh").GetBoolean());
        Assert.Single(fonts.GetProperty("directories").EnumerateArray());
    }

    [Fact]
    public void One_refresh_world_is_enough_to_rescan_everything()
    {
        var native = new FakeTypstNative();
        var fonts = Fonts(
            new TypstWorldBuilder(native)
                .AddSystemFonts(FontScanMode.Shared)
                .AddFontDirectory("/opt/fonts", FontScanMode.Refresh),
            native);

        Assert.True(fonts.GetProperty("refresh").GetBoolean());
    }

    [Fact]
    public void The_persistent_index_can_be_moved_or_turned_off()
    {
        var moved = new FakeTypstNative();
        var fonts = Fonts(
            new TypstWorldBuilder(moved)
                .AddFontDirectory("/opt/fonts")
                .WithFontIndexCache("/var/cache/fonts"),
            moved);

        Assert.True(fonts.GetProperty("cache").GetBoolean());
        Assert.Equal("/var/cache/fonts", fonts.GetProperty("cacheDir").GetString());

        var off = new FakeTypstNative();
        var disabled = Fonts(
            new TypstWorldBuilder(off).AddFontDirectory("/opt/fonts").WithoutFontIndexCache(),
            off);

        Assert.False(disabled.GetProperty("cache").GetBoolean());
        Assert.Equal(JsonValueKind.Null, disabled.GetProperty("cacheDir").ValueKind);
    }
}
