using System;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>Owns a native decode result.</summary>
internal sealed class DecodeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DecodeHandle()
        : base(ownsHandle: true)
    {
    }

    public DecodeHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.DecodeFree(handle);
        return true;
    }
}
