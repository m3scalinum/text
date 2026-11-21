namespace M3scalinum.Text.Typst.Native;

/// <summary>Status codes returned by the native library (Spec 02 §2).</summary>
internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    InvalidHandle = 2,
    Panic = 3,
    Compilation = 4,
    File = 5,
    Export = 6,
    AbiMismatch = 7,
    Cancelled = 8,
    Limit = 9,
    Unsupported = 10,
}