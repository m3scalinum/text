using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Barcodes.Internal;

/// <summary>Mirror of the native <c>BtBuffer</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBuffer
{
    public IntPtr Pointer;

    public nuint Length;
}
