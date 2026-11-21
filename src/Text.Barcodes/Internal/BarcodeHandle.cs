using System;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>Owns a native encoded barcode.</summary>
internal sealed class BarcodeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public BarcodeHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    public int ModuleWidth => IsInvalid ? 0 : (int)NativeMethods.BarcodeWidth(handle);

    public int ModuleHeight => IsInvalid ? 0 : (int)NativeMethods.BarcodeHeight(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.BarcodeFree(handle);
        return true;
    }
}
