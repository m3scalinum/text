using System;

namespace M3scalinum.Text.Xlsx;

/// <summary>Why a workbook could not be read.</summary>
/// <remarks>
/// The values are the status codes the native reader returns, so a host can log the number it was
/// given without a mapping table. Codes 1 to 3 and 9 to 10 are the ones
/// <c>docs/specifications/02-native-abi.md</c> defines.
/// </remarks>
public enum XlsxErrorCode
{
    /// <summary>The bytes are not a zip package, or one of its parts does not parse.</summary>
    MalformedPackage = 11,

    /// <summary>The package is a zip package but lacks a part an XLSX file must carry.</summary>
    MissingPart = 12,

    /// <summary>The workbook is larger than <see cref="XlsxOptions.MaxWorkbookBytes"/>, or a sheet is wider than <see cref="XlsxOptions.MaxCells"/>.</summary>
    Limit = 9,

    /// <summary>An argument was null, empty, or outside its range.</summary>
    InvalidArgument = 1,

    /// <summary>The native library reported a handle it does not own.</summary>
    InvalidHandle = 2,

    /// <summary>The native library caught a panic; the bug is in this package.</summary>
    Panic = 3,

    /// <summary>A part of the workbook is outside what this reader implements.</summary>
    Unsupported = 10,

    /// <summary>The native library failed in a way it does not classify.</summary>
    Internal = 13,
}
