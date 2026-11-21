namespace M3scalinum.Text.Barcodes;

/// <summary>
/// One point of the detected symbol, in pixels of the image that was handed to
/// <see cref="Barcode.Decode"/>.
/// </summary>
/// <remarks>
/// Which points a symbology reports is its detector's business: a QR Code reports its three finder
/// patterns and its bottom-right corner, a one-dimensional symbol the ends of the row that decoded.
/// The count and order are therefore not part of this package's contract.
/// </remarks>
/// <param name="X">Horizontal position, in pixels.</param>
/// <param name="Y">Vertical position, in pixels.</param>
public readonly record struct BarcodePoint(float X, float Y);
