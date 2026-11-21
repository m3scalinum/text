using System;

namespace M3scalinum.Text.Charts.Internal;

/// <summary>The rectangle a chart draws in, and the two scales that map data onto it.</summary>
internal sealed class ChartPlot
{
    internal ChartPlot(double left, double top, double width, double height, TickScale x, TickScale y)
    {
        Left = left;
        Top = top;
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);
        XScale = x;
        YScale = y;
    }

    internal double Left { get; }

    internal double Top { get; }

    internal double Width { get; }

    internal double Height { get; }

    internal TickScale XScale { get; }

    internal TickScale YScale { get; }

    internal double Right => Left + Width;

    internal double Bottom => Top + Height;

    internal double CenterX => Left + (Width / 2);

    internal double CenterY => Top + (Height / 2);

    /// <summary>Maps a value on the x axis to a horizontal position.</summary>
    internal double X(double value)
    {
        var span = XScale.Max - XScale.Min;

        return span == 0 ? CenterX : Left + (((value - XScale.Min) / span) * Width);
    }

    /// <summary>Maps a value on the y axis to a vertical position; the y axis points up.</summary>
    internal double Y(double value)
    {
        var span = YScale.Max - YScale.Min;

        return span == 0 ? CenterY : Bottom - (((value - YScale.Min) / span) * Height);
    }

    /// <summary>The vertical position of the zero line, kept inside the plot when the axis does not span zero.</summary>
    internal double Baseline => Y(Math.Clamp(0, YScale.Min, YScale.Max));
}
