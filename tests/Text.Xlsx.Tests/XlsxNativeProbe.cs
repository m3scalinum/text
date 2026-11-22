using System;
using System.IO;
using M3scalinum.Text.Xlsx.Internal;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>Whether the native reader is loadable on this host.</summary>
/// <remarks>
/// The library is built per RID by the `native-extras` job and handed to the `extras` job through
/// <c>SHARPTEXTXLSX_NATIVE_PATH</c>. A checkout without a Rust build has no library, so the tests
/// that read a workbook skip themselves rather than fail on a missing file.
/// </remarks>
internal static class XlsxNativeProbe
{
    internal const string SkipReason =
        "sharp_text_xlsx is not loadable here: build native/text-xlsx and point " +
        "SHARPTEXTXLSX_NATIVE_PATH at the library to run the reader tests.";

    internal static bool IsAvailable
    {
        get
        {
            try
            {
                return XlsxNative.AbiVersion() == 1;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Whether the Typst compiler is loadable, which the PDF render test needs.</summary>
    internal static bool CompilerAvailable
        => Environment.GetEnvironmentVariable("SHARPTYPST_NATIVE_PATH") is { Length: > 0 } path
            && File.Exists(path);
}
