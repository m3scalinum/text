using System;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// The native Typst library this process needs is not installed, and the message says which package
/// carries it.
/// </summary>
/// <remarks>
/// The managed package no longer bundles all seven native libraries (Spec 08 §6), so "no satellite
/// referenced" is a normal configuration mistake — a project that has the managed package and not
/// the one for its runtime. It is a subclass of <see cref="DllNotFoundException"/> so that a host
/// that already catches that type keeps working, and the exception names the package to add instead
/// of only the library name the runtime could not find.
/// </remarks>
public sealed class TypstNativeLibraryNotFoundException : DllNotFoundException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="message">The message, naming the missing package, the RID and the probed paths.</param>
    public TypstNativeLibraryNotFoundException(string message)
        : base(message)
    {
    }
}
