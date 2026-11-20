using System.Collections.Generic;
using System.Text.Json;
using M3scalinum.Text.SharpTypst.Diagnostics;
using M3scalinum.Text.SharpTypst.Internal;
using M3scalinum.Text.SharpTypst.Native;

namespace M3scalinum.Text.SharpTypst;

/// <summary>Translates native failures into the public exception taxonomy (Spec 07 §3).</summary>
internal static class NativeErrorTranslator
{
    internal static TypstException Translate(NativeCallException exception)
    {
        var payload = Parse(exception.Payload);
        var diagnostics = ToDiagnostics(payload);
        var message = payload?.Message ?? exception.Message;

        return exception.Status switch
        {
            NativeStatus.Compilation or NativeStatus.File =>
                new TypstCompilationException(message, diagnostics),
            NativeStatus.Export =>
                new TypstExportException(message, diagnostics),
            NativeStatus.Panic =>
                new TypstNativeException(message, TypstErrorCode.Panic, exception.Payload, exception),
            NativeStatus.AbiMismatch =>
                new TypstNativeException(message, TypstErrorCode.AbiMismatch, exception.Payload, exception),
            NativeStatus.Limit =>
                new TypstNativeException(message, TypstErrorCode.LimitExceeded, exception.Payload, exception),
            NativeStatus.Unsupported =>
                new TypstNativeException(message, TypstErrorCode.Unsupported, exception.Payload, exception),
            NativeStatus.Cancelled =>
                new TypstNativeException(message, TypstErrorCode.Cancelled, exception.Payload, exception),
            NativeStatus.InvalidArgument or NativeStatus.InvalidHandle =>
                new TypstNativeException(message, TypstErrorCode.InvalidArgument, exception.Payload, exception),
            _ => new TypstNativeException(message, TypstErrorCode.Panic, exception.Payload, exception),
        };
    }

    internal static IReadOnlyList<TypstDiagnostic> ParseDiagnostics(string json)
        => ToDiagnostics(Parse(json));

    private static DiagnosticPayloadDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, TypstJsonContext.Default.DiagnosticPayloadDto);
        }
        catch (JsonException)
        {
            // A malformed payload must not mask the original failure.
            return null;
        }
    }

    private static List<TypstDiagnostic> ToDiagnostics(DiagnosticPayloadDto? payload)
    {
        if (payload?.Diagnostics is not { Count: > 0 } items)
        {
            return [];
        }

        var result = new List<TypstDiagnostic>(items.Count);
        foreach (var item in items)
        {
            result.Add(new TypstDiagnostic(
                ParseSeverity(item.Severity),
                item.Message ?? string.Empty,
                item.Hints ?? [],
                item.Trace ?? [],
                ToLocation(item),
                item.SourceLine));
        }

        return result;
    }

    private static TypstLocation? ToLocation(DiagnosticDto item)
    {
        if (item.File is null || item.Line is null || item.Column is null)
        {
            return null;
        }

        return new TypstLocation(
            item.File,
            item.Line.Value,
            item.Column.Value,
            item.EndLine ?? item.Line.Value,
            item.EndColumn ?? item.Column.Value,
            item.IsInSource);
    }

    private static TypstSeverity ParseSeverity(string? severity) => severity switch
    {
        "error" => TypstSeverity.Error,
        "warning" => TypstSeverity.Warning,
        _ => TypstSeverity.Hint,
    };
}
