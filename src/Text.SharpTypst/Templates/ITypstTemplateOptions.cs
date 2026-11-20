using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// Implemented by the options record of a typed template. The mapping is explicit so that
/// trimming and Native AOT keep working: no reflection reads the properties.
/// </summary>
public interface ITypstTemplateOptions
{
    /// <summary>Maps the record to template parameters, keyed by manifest name.</summary>
    IReadOnlyDictionary<string, object?> ToParameters();
}
