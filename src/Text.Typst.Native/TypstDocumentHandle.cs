using System;
using Microsoft.Win32.SafeHandles;

namespace M3scalinum.Text.Typst.Native;

/// <summary>Owns a native <c>TnDocument</c>.</summary>
internal sealed class TypstDocumentHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public TypstDocumentHandle()
        : base(ownsHandle: true)
    {
    }

    public TypstDocumentHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    public int PageCount => IsInvalid ? 0 : (int)NativeMethods.DocumentPageCount(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.DocumentFree(handle);
        return true;
    }
}