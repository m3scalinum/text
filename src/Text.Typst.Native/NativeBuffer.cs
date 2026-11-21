using System;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Typst.Native;

/// <summary>Mirror of the native <c>TnBuffer</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBuffer
{
    public IntPtr Pointer;
    public nuint Length;
}