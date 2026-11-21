namespace M3scalinum.Text.Typst.Internal;

internal sealed class ClockDto
{
    public string Kind { get; set; } = "fixed";

    public int Year { get; set; } = 2024;

    public int Month { get; set; } = 1;

    public int Day { get; set; } = 1;

    public int Hour { get; set; }

    public int Minute { get; set; }

    public int Second { get; set; }
}