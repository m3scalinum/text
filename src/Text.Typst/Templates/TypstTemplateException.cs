using System;

namespace M3scalinum.Text.Typst.Templates;

/// <summary>A template manifest or parameter is invalid.</summary>
public sealed class TypstTemplateException : TypstException
{
    /// <summary>Initialises the exception.</summary>
    public TypstTemplateException(
        string message,
        string? templateName = null,
        string? parameterName = null,
        Exception? innerException = null)
        : base(message, TypstErrorCode.Template, diagnostics: null, innerException)
    {
        TemplateName = templateName;
        ParameterName = parameterName;
    }

    /// <summary>Name of the template, when known.</summary>
    public string? TemplateName { get; }

    /// <summary>Name of the offending parameter, when the failure is about one.</summary>
    public string? ParameterName { get; }
}
