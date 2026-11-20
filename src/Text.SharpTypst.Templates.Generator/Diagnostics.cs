using Microsoft.CodeAnalysis;

namespace M3scalinum.Text.SharpTypst.Templates.Generator;

/// <summary>Diagnostics reported by the template generator.</summary>
internal static class Diagnostics
{
    private const string Category = "M3scalinum.Text.SharpTypst.Templates";

    public static readonly DiagnosticDescriptor ManifestNotFound = new(
        id: "SHT001",
        title: "Template manifest not found",
        messageFormat: "No template.toml was found for the path '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidManifest = new(
        id: "SHT002",
        title: "Invalid template manifest",
        messageFormat: "The manifest '{0}' could not be read: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "SHT003",
        title: "Template options type must be partial",
        messageFormat: "'{0}' must be declared partial so the generator can add its properties",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HasPrimaryConstructor = new(
        id: "SHT004",
        title: "Template options type must not declare a primary constructor",
        messageFormat: "'{0}' declares a primary constructor; the generator adds the properties itself",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "SHT005",
        title: "Unsupported parameter type",
        messageFormat: "Parameter '{0}' declares the unknown type '{1}'; it is generated as object?",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
