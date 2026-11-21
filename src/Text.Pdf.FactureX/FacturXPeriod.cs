using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>A period of service: the invoice period (<c>BG-14</c>) or a line period (<c>BG-26</c>).</summary>
public sealed class FacturXPeriod
{
    /// <summary>The first day of the period, <c>BT-73</c> / <c>BT-134</c>.</summary>
    public DateOnly? Start { get; init; }

    /// <summary>The last day of the period, <c>BT-74</c> / <c>BT-135</c>.</summary>
    public DateOnly? End { get; init; }
}
