using System;

namespace M3scalinum.Text.Documents;

/// <summary>What the HTML importer does with an element outside the supported subset.</summary>
public enum TypstHtmlUnknownElementPolicy
{
    /// <summary>Keep going: unwrap the element, count it, and keep the text inside it.</summary>
    Skip = 0,

    /// <summary>Stop at the first unsupported element with a <see cref="TypstHtmlImportException"/>.</summary>
    Throw = 1,
}
