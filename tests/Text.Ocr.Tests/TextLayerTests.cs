using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The bridge from a recognised page to the positions a text layer draws: pixels at the top-left
/// corner become points at the bottom-left one, and what is left out is left out on purpose.
/// </summary>
public sealed class TextLayerTests
{
    [Fact]
    public void A_word_becomes_a_box_in_page_points()
    {
        // 72 DPI is the one resolution where a pixel is a point, so the arithmetic is visible: the
        // page is 612×792 pixels and the word sits 100 pixels from the top, 72 from the left.
        var page = Page(lines: [Line(Word("Bonjour", 72, 100, 60, 20))]);

        var word = Assert.Single(OcrTextLayer.Words(page));

        Assert.Equal(0, word.PageIndex);
        Assert.Equal("Bonjour", word.Text);
        Assert.Equal(72, word.Left);
        Assert.Equal(672, word.Bottom);
        Assert.Equal(132, word.Right);
        Assert.Equal(692, word.Top);
    }

    [Fact]
    public void A_box_at_a_higher_resolution_scales_back_to_points()
    {
        // At 144 DPI one point is two pixels, so every coordinate halves.
        var page = Page(
            dpi: 144,
            width: 1224,
            height: 1584,
            lines: [Line(Word("Bonjour", 144, 200, 120, 40))]);

        var word = Assert.Single(OcrTextLayer.Words(page));

        Assert.Equal(72, word.Left);
        Assert.Equal(672, word.Bottom);
        Assert.Equal(132, word.Right);
        Assert.Equal(692, word.Top);
    }

    [Fact]
    public void The_page_size_follows_the_resolution_it_was_read_at()
    {
        var page = Page(dpi: 300, width: 2480, height: 3508, lines: []);

        var (width, height) = OcrTextLayer.PageSize(page);

        Assert.Equal(595.2, width, 3);
        Assert.Equal(841.92, height, 3);
    }

    [Fact]
    public void Lines_and_words_keep_the_order_the_engine_returned()
    {
        var page = Page(
            lines:
            [
                Line(Word("premier", 72, 100, 60, 20), Word("mot", 140, 100, 30, 20)),
                Line(Word("seconde", 72, 140, 60, 20)),
            ]);

        var words = OcrTextLayer.Words(page);

        Assert.Equal(["premier", "mot", "seconde"], words.Select(word => word.Text).ToList());
    }

    [Fact]
    public void A_rejected_word_is_left_out()
    {
        var page = Page(
            lines:
            [
                Line(Word("gardé", 72, 100, 60, 20), Word("douté", 140, 100, 60, 20, rejected: true)),
            ]);

        var word = Assert.Single(OcrTextLayer.Words(page));

        Assert.Equal("gardé", word.Text);
    }

    [Fact]
    public void A_word_with_no_area_is_left_out()
    {
        var page = Page(lines: [Line(Word("plat", 72, 100, 60, 0))]);

        Assert.Empty(OcrTextLayer.Words(page));
    }

    [Fact]
    public void A_refused_page_has_no_words()
    {
        var refused = new OcrPageUnreadable(
            0,
            612,
            792,
            300,
            0.11,
            new OcrRefusal(OcrRefusalReason.MeanConfidence, 0.5, 0.11, 4));

        Assert.Empty(OcrTextLayer.Words(refused));
    }

    [Fact]
    public void Several_pages_carry_their_own_index()
    {
        var first = Page(pageIndex: 0, lines: [Line(Word("un", 10, 10, 20, 10))]);
        var second = Page(pageIndex: 3, lines: [Line(Word("trois", 10, 10, 30, 10))]);

        var words = OcrTextLayer.Words(new OcrPageOutcome[] { first, second });

        Assert.Equal(2, words.Count);
        Assert.Equal(0, words[0].PageIndex);
        Assert.Equal(3, words[1].PageIndex);
        Assert.Equal("trois", words[1].Text);
    }

    [Fact]
    public void A_page_with_no_line_yields_no_word()
    {
        Assert.Empty(OcrTextLayer.Words(Page(lines: [])));
    }

    [Fact]
    public void A_page_size_without_a_resolution_is_refused()
    {
        var page = Page(dpi: 0, lines: []);

        var exception = Assert.Throws<ArgumentException>(() => OcrTextLayer.PageSize(page));

        Assert.Contains("no resolution", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A recognised page built directly, so a test states the pixels it means.</summary>
    private static OcrPageResult Page(
        OcrLine[] lines,
        int dpi = 72,
        int pageIndex = 0,
        int width = 612,
        int height = 792)
        => new(
            pageIndex,
            string.Join("\n", lines.Select(line => line.Text)),
            lines,
            0.9,
            width,
            height,
            dpi);

    /// <summary>A line holding <paramref name="words"/>, boxed around them all.</summary>
    private static OcrLine Line(params OcrWord[] words)
    {
        var left = words.Min(word => word.Bounds.X);
        var top = words.Min(word => word.Bounds.Y);
        var right = words.Max(word => word.Bounds.X + word.Bounds.Width);
        var bottom = words.Max(word => word.Bounds.Y + word.Bounds.Height);

        return new OcrLine(
            string.Join(" ", words.Select(word => word.Text)),
            new OcrBox(left, top, right - left, bottom - top),
            0.9,
            words);
    }

    /// <summary>One recognised word: its text and its box in pixels from the top-left corner.</summary>
    private static OcrWord Word(
        string text,
        int x,
        int y,
        int width,
        int height,
        bool rejected = false)
        => new(text, new OcrBox(x, y, width, height), rejected ? 0.2 : 0.95, rejected);
}
