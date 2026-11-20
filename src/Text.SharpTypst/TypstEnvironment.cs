using System.Reflection;
using M3scalinum.Text.SharpTypst.Native;

namespace M3scalinum.Text.SharpTypst;

/// <summary>Versions of the two moving parts: this library and the bundled compiler.</summary>
public static class TypstEnvironment
{
    /// <summary>Version of the Typst compiler embedded in the native library, for example <c>0.15.1</c>.</summary>
    public static string TypstVersion => TypstNative.Instance.TypstVersion;

    /// <summary>
    /// Version of the M3scalinum.Text.SharpTypst assembly, for example <c>0.15.1.42 (Typst 0.15.1)</c>. The first
    /// three components are the Typst version; the fourth is the build revision.
    /// </summary>
    public static string SharpTypstVersion =>
        typeof(TypstEnvironment).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TypstEnvironment).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
