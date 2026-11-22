namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>One row of the native capability report: a symbology and the floors a print enforces.</summary>
/// <param name="Name">The ABI name.</param>
/// <param name="CanEncode">Whether the engine has a writer for it.</param>
/// <param name="QuietZone">The quiet zone, in modules on each side, the specification requires.</param>
/// <param name="MinimumModuleMillimetres">The smallest printed module, in millimetres.</param>
internal readonly record struct SymbologyCapability(
    string Name,
    bool CanEncode,
    int QuietZone,
    double MinimumModuleMillimetres);
