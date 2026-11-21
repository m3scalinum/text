namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>
/// Status codes returned by the native library. The numbering is the contract: it is the same in
/// <c>native/text-barcodes/src/ffi.rs</c> and in Spec 20 §4, and a value never changes meaning.
/// </summary>
internal enum BarcodeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    InvalidHandle = 2,
    Panic = 3,
    Encode = 4,
    Unsupported = 5,
    Decode = 6,
    AbiMismatch = 7,
    Limit = 8,
}
