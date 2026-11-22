namespace M3scalinum.Text.Barcodes;

/// <summary>The page a <see cref="ShippingLabel"/> is laid out on.</summary>
/// <remarks>
/// Two formats, both portrait, both big enough for a GS1-128 SSCC symbol at its nominal module size
/// plus the address block a carrier needs. <see cref="Millimetres100x150"/> is the default: it is
/// the 4 × 6 in stock every thermal label printer is loaded with, and 100 mm is wide enough for a
/// twenty-character GS1-128 at 0.495 mm per module with the 10X quiet zone on both sides.
/// </remarks>
public enum ShippingLabelFormat
{
    /// <summary>100 × 150 mm, the 4 × 6 in label stock.</summary>
    Millimetres100x150 = 0,

    /// <summary>ISO A6, 105 × 148 mm: the same label on an office sheet.</summary>
    A6 = 1,
}

/// <summary>The millimetres a <see cref="ShippingLabelFormat"/> measures.</summary>
internal static class ShippingLabelFormats
{
    /// <summary>The width of the page, in millimetres.</summary>
    public static double WidthMillimetres(this ShippingLabelFormat format) => format switch
    {
        ShippingLabelFormat.Millimetres100x150 => 100,
        ShippingLabelFormat.A6 => 105,
        _ => throw new BarcodeException(
            BarcodeErrorCode.InvalidArgument, $"unknown label format '{format}'"),
    };

    /// <summary>The height of the page, in millimetres.</summary>
    public static double HeightMillimetres(this ShippingLabelFormat format) => format switch
    {
        ShippingLabelFormat.Millimetres100x150 => 150,
        ShippingLabelFormat.A6 => 148,
        _ => throw new BarcodeException(
            BarcodeErrorCode.InvalidArgument, $"unknown label format '{format}'"),
    };
}
