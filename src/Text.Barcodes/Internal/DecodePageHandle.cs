using System;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>Owns a native decode result for one image, which may hold several symbols.</summary>
internal sealed class DecodePageHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DecodePageHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    public int SymbolCount => IsInvalid ? 0 : (int)NativeMethods.DecodePageCount(handle);

    public int ImageWidth => IsInvalid ? 0 : (int)NativeMethods.DecodePageWidth(handle);

    public int ImageHeight => IsInvalid ? 0 : (int)NativeMethods.DecodePageHeight(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.DecodePageFree(handle);
        return true;
    }
}
