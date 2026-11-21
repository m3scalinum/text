using System;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Typst.Native;

/// <summary>Owns a native <c>TnWorld</c>.</summary>
internal sealed class TypstWorldHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public TypstWorldHandle()
        : base(ownsHandle: true)
    {
    }

    public TypstWorldHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    public int SourceCount => IsInvalid ? 0 : (int)NativeMethods.WorldSourceCount(handle);

    public int FontCount => IsInvalid ? 0 : (int)NativeMethods.WorldFontCount(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.WorldFree(handle);
        return true;
    }
}