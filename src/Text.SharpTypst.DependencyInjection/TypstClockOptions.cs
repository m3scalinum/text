using System;

namespace M3scalinum.Text.SharpTypst.DependencyInjection;

/// <summary>
/// What the compiler reports as the current date.
/// </summary>
/// <remarks>
/// Fixed by default, so a document that prints <c>#datetime.today()</c> compiles to the same bytes
/// on every run. Set <see cref="UseSystemClock"/> when the document must show the real date.
/// </remarks>
public sealed class TypstClockOptions
{
    /// <summary>Whether the compiler follows the system clock instead of <see cref="FixedInstant"/>.</summary>
    public bool UseSystemClock { get; set; }

    /// <summary>The instant reported when <see cref="UseSystemClock"/> is <see langword="false"/>.</summary>
    public DateTimeOffset FixedInstant { get; set; } = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
