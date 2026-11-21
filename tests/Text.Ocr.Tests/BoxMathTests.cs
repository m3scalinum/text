using System.Linq;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The box arithmetic the result mapping leans on: clipping to a page, the union of a
/// line's word boxes, and reading order.
/// </summary>
public sealed class BoxMathTests
{
    [Fact]
    public void A_box_inside_the_page_is_kept_unchanged()
        => Assert.Equal([10, 20, 30, 40], Boxes(BoxMath.Clip(new OcrBox(10, 20, 30, 40), 100, 100)));

    [Fact]
    public void A_box_that_starts_before_the_page_is_trimmed_at_the_origin()
        => Assert.Equal([0, 0, 10, 10], Boxes(BoxMath.Clip(new OcrBox(-5, -5, 15, 15), 100, 100)));

    [Fact]
    public void A_box_that_ends_past_the_page_is_trimmed_at_the_edge()
        => Assert.Equal([90, 90, 10, 10], Boxes(BoxMath.Clip(new OcrBox(90, 90, 50, 50), 100, 100)));

    [Fact]
    public void A_box_entirely_outside_the_page_becomes_empty_rather_than_negative()
        => Assert.Equal([100, 100, 0, 0], Boxes(BoxMath.Clip(new OcrBox(200, 200, 10, 10), 100, 100)));

    [Fact]
    public void A_page_with_no_size_clips_everything_to_nothing()
        => Assert.Equal([0, 0, 0, 0], Boxes(BoxMath.Clip(new OcrBox(10, 10, 10, 10), -1, 0)));

    [Fact]
    public void A_box_of_no_area_stays_of_no_area()
        => Assert.Equal([40, 40, 0, 0], Boxes(BoxMath.Clip(new OcrBox(40, 40, 0, 0), 100, 100)));

    [Fact]
    public void The_union_of_no_boxes_is_the_empty_box()
    {
        Assert.Equal([0, 0, 0, 0], Boxes(BoxMath.Union([])));
        Assert.Same(BoxMath.Empty, BoxMath.Union([]));
    }

    [Fact]
    public void The_union_of_boxes_is_the_smallest_box_that_holds_them_all()
        => Assert.Equal(
            [10, 20, 60, 40],
            Boxes(BoxMath.Union([new OcrBox(10, 20, 20, 10), new OcrBox(40, 20, 30, 40)])));

    [Fact]
    public void The_union_of_boxes_in_any_order_is_the_same_box()
        => Assert.Equal(
            [10, 20, 60, 40],
            Boxes(BoxMath.Union([new OcrBox(40, 20, 30, 40), new OcrBox(10, 20, 20, 10)])));

    [Fact]
    public void Words_come_back_left_to_right()
    {
        var words = new[]
        {
            new OcrWord("b", new OcrBox(50, 0, 10, 10), 1.0),
            new OcrWord("a", new OcrBox(10, 0, 10, 10), 1.0),
        };

        Assert.Equal(["a", "b"], BoxMath.InReadingOrder(words).Select(word => word.Text));
    }

    [Fact]
    public void Two_words_that_start_in_the_same_column_come_back_top_to_bottom()
    {
        var words = new[]
        {
            new OcrWord("below", new OcrBox(10, 30, 10, 10), 1.0),
            new OcrWord("above", new OcrBox(10, 5, 10, 10), 1.0),
        };

        Assert.Equal(["above", "below"], BoxMath.InReadingOrder(words).Select(word => word.Text));
    }

    [Fact]
    public void No_words_come_back_as_no_words()
        => Assert.Empty(BoxMath.InReadingOrder([]));

    private static int[] Boxes(OcrBox box) => [box.X, box.Y, box.Width, box.Height];
}
