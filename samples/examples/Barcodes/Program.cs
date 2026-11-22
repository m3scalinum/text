using M3scalinum.Text.Barcodes;

// Encodes a QR Code and reads it back: both directions run inside the native library the package
// ships, so a package missing its runtime for this RID fails here with DllNotFoundException.

const string Payload = "M3SCALINUM BARCODES SAMPLE";

Console.WriteLine("M3scalinum.Text.Barcodes");
Console.WriteLine($"  symbologies  {string.Join(", ", Barcode.SupportedSymbologies)}");

var encoded = Barcode.Encode(BarcodeSymbology.QrCode, Payload);

Console.WriteLine($"  encoded      {encoded.Symbology} {encoded.ModuleWidth}x{encoded.ModuleHeight} modules,"
    + $" png {encoded.Png.Length} bytes, svg {encoded.Svg.Length} chars");

if (encoded.Png.Length == 0 || encoded.Svg.Length == 0)
{
    throw new InvalidOperationException("the encoder produced no image");
}

var decoded = Barcode.Decode(encoded.Png);

Console.WriteLine($"  decoded      success={decoded.Success} format={decoded.Format} text='{decoded.Text}'");

if (!decoded.Success || decoded.Text != Payload)
{
    throw new InvalidOperationException(
        $"the round trip lost the payload: success={decoded.Success} text='{decoded.Text}' error='{decoded.Error}'");
}

// A second symbology proves the matrix is not the QR path alone: a Code 128 payload the encoder
// must reject is handled by the same native library.
var code128 = Barcode.Encode(BarcodeSymbology.Code128, "FA-2026-0042");
var code128Back = Barcode.Decode(code128.Png);

Console.WriteLine($"  code128      {code128Back.Text}");

if (code128Back.Text != "FA-2026-0042")
{
    throw new InvalidOperationException($"the Code 128 round trip lost the payload: '{code128Back.Text}'");
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Barcodes: the native library loaded from the package and both directions ran.");
