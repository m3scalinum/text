using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using M3scalinum.Text.Typst.Compilation;

namespace M3scalinum.Text.Typst.Templates;

/// <summary>
/// A template bound to a typed options record. The record implements
/// <see cref="ITypstTemplateOptions"/>, so the mapping is explicit and survives trimming.
/// </summary>
/// <typeparam name="TOptions">Options record describing the template's parameters.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Load is the factory for the typed template; a non-generic factory would lose the type.")]
public sealed class TypstTemplate<TOptions>
    where TOptions : ITypstTemplateOptions
{
    private readonly TypstTemplate _template;

    private TypstTemplate(TypstTemplate template) => _template = template;

    /// <summary>The parsed manifest.</summary>
    public TypstTemplateManifest Manifest => _template.Manifest;

    /// <summary>Loads a template from a directory.</summary>
    public static TypstTemplate<TOptions> Load(string directory)
        => new(TypstTemplate.Load(directory));

    /// <summary>Loads a template from embedded resources.</summary>
    public static TypstTemplate<TOptions> LoadEmbedded(Assembly assembly, string prefix)
        => new(TypstTemplate.LoadEmbedded(assembly, prefix));

    /// <summary>Renders the template to PDF.</summary>
    public byte[] Render(TypstCompiler compiler, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _template.Render(compiler, options.ToParameters());
    }

    /// <summary>Renders the template and returns the document, for another export format.</summary>
    public CompilationResult RenderDocument(TypstCompiler compiler, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _template.RenderDocument(compiler, options.ToParameters());
    }

    /// <summary>
    /// Renders the template with a chosen layout pass, so the result can be exported to HTML,
    /// Markdown or plain text as well as to PDF.
    /// </summary>
    public CompilationResult RenderDocument(TypstCompiler compiler, TOptions options, TypstDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _template.RenderDocument(compiler, options.ToParameters(), kind);
    }
}
