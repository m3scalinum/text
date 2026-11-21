using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Internal;

namespace M3scalinum.Text.Typst.Templates;

/// <summary>
/// A Typst layout with declared parameters. The template file defines
/// <c>#let render(p) = { … }</c>; the host appends the call with the bound parameters, so the
/// file itself stays a valid Typst document.
/// </summary>
public sealed class TypstTemplate
{
    private readonly string _entrySource;
    private readonly IReadOnlyDictionary<string, byte[]> _assets;

    private TypstTemplate(
        TypstTemplateManifest manifest,
        string entrySource,
        IReadOnlyDictionary<string, byte[]> assets)
    {
        Manifest = manifest;
        _entrySource = entrySource;
        _assets = assets;
    }

    /// <summary>The parsed manifest.</summary>
    public TypstTemplateManifest Manifest { get; }

    /// <summary>Loads a template from a directory containing <c>template.toml</c>.</summary>
    /// <exception cref="TypstTemplateException">The manifest, the entry file or a parameter is invalid.</exception>
    public static TypstTemplate Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "template.toml");
        if (!File.Exists(manifestPath))
        {
            throw new TypstTemplateException(
                $"template manifest not found: {manifestPath}", templateName: root);
        }

        var manifest = TypstTemplateManifest.Parse(File.ReadAllText(manifestPath), root);

        var entryPath = ResolveEntry(root, manifest);
        if (!File.Exists(entryPath))
        {
            throw new TypstTemplateException(
                $"template '{manifest.Name}': entry file '{manifest.Entry}' not found",
                manifest.Name);
        }

        var entrySource = File.ReadAllText(entryPath);
        EnsureRenderIsDefined(manifest, entrySource);

        return new TypstTemplate(manifest, entrySource, LoadAssets(root));
    }

    /// <summary>
    /// Loads a template from a manifest and an in-memory file set.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="files">
    /// Every file of the template, keyed by path relative to the template root — for example
    /// <c>template.typ</c> and <c>assets/logo.svg</c>. The entry file must be present.
    /// </param>
    /// <exception cref="TypstTemplateException">The entry file is missing or does not define <c>render</c>.</exception>
    public static TypstTemplate Load(
        TypstTemplateManifest manifest,
        IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);

        if (!files.TryGetValue(manifest.Entry, out var entryBytes))
        {
            throw new TypstTemplateException(
                $"template '{manifest.Name}': entry file '{manifest.Entry}' is missing from the supplied files",
                manifest.Name);
        }

        var entrySource = Encoding.UTF8.GetString(entryBytes);
        EnsureRenderIsDefined(manifest, entrySource);

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in files)
        {
            if (!string.Equals(pair.Key, manifest.Entry, StringComparison.Ordinal))
            {
                assets[pair.Key] = pair.Value;
            }
        }

        return new TypstTemplate(manifest, entrySource, assets);
    }

    /// <summary>Loads a template from manifest TOML and an in-memory file set.</summary>
    /// <param name="manifestToml">The contents of <c>template.toml</c>.</param>
    /// <param name="files">Every file of the template, keyed by path relative to the template root.</param>
    public static TypstTemplate Load(string manifestToml, IReadOnlyDictionary<string, byte[]> files)
        => Load(TypstTemplateManifest.Parse(manifestToml), files);

    /// <summary>Loads a template whose files are embedded resources of <paramref name="assembly"/>.</summary>
    /// <param name="assembly">Assembly holding the resources.</param>
    /// <param name="prefix">Resource prefix, for example <c>MyLib.Templates.Invoice</c>.</param>
    public static TypstTemplate LoadEmbedded(Assembly assembly, string prefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var names = assembly.GetManifestResourceNames();

        string? Find(string relative)
        {
            var suffix = "." + relative.Replace('/', '.');
            return names.FirstOrDefault(name =>
                name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(suffix, StringComparison.Ordinal));
        }

        var manifestName = Find("template.toml")
            ?? throw new TypstTemplateException(
                $"template manifest not found in resources of '{assembly.GetName().Name}' under '{prefix}'",
                prefix);

        var manifest = TypstTemplateManifest.Parse(ReadResource(assembly, manifestName), prefix);

        var entryName = Find(manifest.Entry)
            ?? throw new TypstTemplateException(
                $"template '{manifest.Name}': entry file '{manifest.Entry}' not found in resources",
                manifest.Name);

        var entrySource = ReadResource(assembly, entryName);
        EnsureRenderIsDefined(manifest, entrySource);

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!name.StartsWith(prefix + ".assets.", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = name[(prefix.Length + 1)..].Replace('.', '/');
            assets[relative] = ReadBytes(assembly, name);
        }

        return new TypstTemplate(manifest, entrySource, assets);
    }

    /// <summary>Renders the template to PDF.</summary>
    public byte[] Render(TypstCompiler compiler, IReadOnlyDictionary<string, object?> parameters)
    {
        using var result = RenderDocument(compiler, parameters);
        return result.ExportPdf();
    }

    /// <summary>Renders the template and returns the document, for another export format.</summary>
    public CompilationResult RenderDocument(
        TypstCompiler compiler,
        IReadOnlyDictionary<string, object?> parameters)
        => RenderDocument(compiler, parameters, TypstDocumentKind.Paged);

    /// <summary>
    /// Renders the template with a chosen layout pass, so the result can be exported to HTML,
    /// Markdown or plain text as well as to PDF.
    /// </summary>
    /// <param name="compiler">The compiler to render with.</param>
    /// <param name="parameters">Values keyed by manifest name.</param>
    /// <param name="kind">
    /// <see cref="TypstDocumentKind.Paged"/> for PDF, SVG and PNG; <see cref="TypstDocumentKind.Html"/>
    /// for HTML, Markdown and text. <see cref="CompilationResult.PageCount"/> is only meaningful for
    /// a paged document.
    /// </param>
    public CompilationResult RenderDocument(
        TypstCompiler compiler,
        IReadOnlyDictionary<string, object?> parameters,
        TypstDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(parameters);

        var arguments = TemplateBinder.Bind(Manifest, parameters);
        var source = new StringBuilder(_entrySource)
            .Append("\n#render(")
            .Append(arguments)
            .Append(")\n")
            .ToString();

        var builder = new TypstWorldBuilder().AddSource(Manifest.Entry, source);
        foreach (var (path, bytes) in _assets)
        {
            builder.AddAsset(path, bytes);
        }

        using var world = builder.Build();
        return compiler.Compile(
            world,
            new TypstCompilationOptions { Root = Manifest.Entry, DocumentKind = kind });
    }

    private static void EnsureRenderIsDefined(TypstTemplateManifest manifest, string source)
    {
        // A heuristic, not a parse: the managed layer has no Typst parser, and compiling at
        // load time would make every Load pay for a full layout.
        if (!source.Contains("#let render", StringComparison.Ordinal))
        {
            throw new TypstTemplateException(
                $"template '{manifest.Name}': '{manifest.Entry}' does not define '#let render(p)'",
                manifest.Name);
        }
    }

    /// <summary>
    /// Resolves the manifest's entry file against the template root, refusing anything that escapes
    /// it.
    /// </summary>
    /// <remarks>
    /// The manifest is a file on disk, and a template can come from a package or a directory the
    /// caller does not control. An `entry` of `../../etc/passwd` must not read outside the template.
    /// </remarks>
    private static string ResolveEntry(string root, TypstTemplateManifest manifest)
    {
        var entry = manifest.Entry;

        if (entry.Length == 0
            || Path.IsPathRooted(entry)
            || entry.StartsWith('/')
            || entry.StartsWith('\\')
            || entry.Contains(':', StringComparison.Ordinal)
            || entry.Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new TypstTemplateException(
                $"template '{manifest.Name}': entry '{entry}' is not a relative path inside the template directory",
                manifest.Name);
        }

        var target = Path.GetFullPath(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar)));
        var back = Path.GetRelativePath(root, target);

        if (back.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(back))
        {
            throw new TypstTemplateException(
                $"template '{manifest.Name}': entry '{entry}' resolves outside the template directory",
                manifest.Name);
        }

        return target;
    }

    private static Dictionary<string, byte[]> LoadAssets(string root)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var directory = Path.Combine(root, "assets");
        if (!Directory.Exists(directory))
        {
            return assets;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            assets[relative] = File.ReadAllBytes(file);
        }

        return assets;
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new TypstTemplateException($"resource '{name}' could not be opened");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new TypstTemplateException($"resource '{name}' could not be opened");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
