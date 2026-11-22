using System;

namespace M3scalinum.Text.Xlsx;

/// <summary>What <see cref="XlsxReader.Read(byte[])"/> should do beyond reading.</summary>
public sealed class XlsxOptions
{
    /// <summary>The largest workbook this reader will open, in bytes, unless overridden.</summary>
    public const long DefaultMaxWorkbookBytes = 64L * 1024 * 1024;

    /// <summary>The largest used region, in cells, this reader will shape, unless overridden.</summary>
    public const long DefaultMaxCells = 1_000_000;

    /// <summary>
    /// The largest workbook, in bytes, this reader will open. A workbook over it fails with
    /// <see cref="XlsxErrorCode.Limit"/> instead of being read into memory.
    /// </summary>
    public long MaxWorkbookBytes { get; init; } = DefaultMaxWorkbookBytes;

    /// <summary>
    /// The largest used region, in cells, this reader will shape. A sheet whose used region is
    /// wider or taller fails with <see cref="XlsxErrorCode.Limit"/> rather than becoming a table no
    /// renderer could lay out.
    /// </summary>
    public long MaxCells { get; init; } = DefaultMaxCells;

    /// <summary>Throws when the values are outside what the native reader accepts.</summary>
    internal void Validate()
    {
        if (MaxWorkbookBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxWorkbookBytes), MaxWorkbookBytes, "the limit is a positive byte count");
        }

        if (MaxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCells), MaxCells, "the limit is a positive cell count");
        }
    }
}
