namespace M3scalinum.Text.SharpTypst;

/// <summary>Machine-readable reason for a <see cref="TypstException"/>.</summary>
public enum TypstErrorCode
{
    /// <summary>The compiler rejected the document.</summary>
    Compilation,

    /// <summary>A source, asset or root could not be found.</summary>
    File,

    /// <summary>Layout succeeded but the export failed.</summary>
    Export,

    /// <summary>A template manifest or parameter was invalid.</summary>
    Template,

    /// <summary>Native code panicked. The process is still alive.</summary>
    Panic,

    /// <summary>The native library does not match the managed assembly.</summary>
    AbiMismatch,

    /// <summary>An argument was rejected before reaching the compiler.</summary>
    InvalidArgument,

    /// <summary>A configured limit was exceeded.</summary>
    LimitExceeded,

    /// <summary>The requested feature is not implemented in this version.</summary>
    Unsupported,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The compilation did not finish within the time the caller allowed.</summary>
    Timeout,
}
