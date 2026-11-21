using M3scalinum.Text.Barcodes.Internal;

namespace M3scalinum.Text.Barcodes;

/// <summary>Versions of the two moving parts: this assembly and the barcode engine it carries.</summary>
public static class BarcodeEnvironment
{
    /// <summary>
    /// The engine and its version, for example <c>rxing 0.9.4</c>. Useful in a bug report: the
    /// symbol that does not decode usually does not decode in one engine version only.
    /// </summary>
    public static string EngineVersion => BarcodeNative.Instance.EngineVersion;

    /// <summary>The version of this assembly, for example <c>0.1.0</c>.</summary>
    public static string BarcodesVersion =>
        typeof(BarcodeEnvironment).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
