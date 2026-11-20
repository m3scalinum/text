using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using M3scalinum.Text.SharpTypst.Templates;

namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>
/// Validates caller values against a manifest and emits the Typst dictionary literal passed
/// to the template's <c>render</c> function.
/// </summary>
internal static class TemplateBinder
{
    internal static string Bind(
        TypstTemplateManifest manifest,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var key in values.Keys)
        {
            if (!manifest.Parameters.ContainsKey(key) && !manifest.AllowExtraParameters)
            {
                throw new TypstTemplateException(
                    $"template '{manifest.Name}' has no parameter '{key}'",
                    manifest.Name,
                    key);
            }
        }

        var builder = new StringBuilder("(");
        var first = true;

        foreach (var parameter in manifest.Parameters.Values)
        {
            var supplied = values.TryGetValue(parameter.Name, out var value) && value is not null;
            var effective = supplied ? value : parameter.Default;

            if (effective is null)
            {
                if (parameter.Required)
                {
                    throw new TypstTemplateException(
                        $"template '{manifest.Name}' requires parameter '{parameter.Name}'",
                        manifest.Name,
                        parameter.Name);
                }

                continue;
            }

            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(parameter.Name).Append(": ");
            builder.Append(Emit(effective, parameter, manifest.Name));
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static string Emit(object value, TypstTemplateParameter parameter, string template)
    {
        try
        {
            return parameter.Type switch
            {
                TypstParameterType.String => Quote(AsString(value)),
                TypstParameterType.Number => AsNumber(value),
                TypstParameterType.Boolean => AsBoolean(value) ? "true" : "false",
                TypstParameterType.Date => AsDate(value),
                TypstParameterType.Asset => Quote(AsString(value)),
                TypstParameterType.Array => AsArray(value, parameter, template),
                TypstParameterType.Object => AsObject(value, template),
                _ => throw new InvalidOperationException($"unhandled parameter type {parameter.Type}"),
            };
        }
        catch (TypstTemplateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException)
        {
            throw new TypstTemplateException(
                $"template '{template}' parameter '{parameter.Name}': {exception.Message}",
                template,
                parameter.Name,
                exception);
        }
    }

    private static string AsString(object value) => value as string
        ?? throw new InvalidCastException($"expected a string, got {value.GetType().Name}");

    private static string AsNumber(object value) => value switch
    {
        int or long or short or byte or sbyte or ushort or uint or ulong
            => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        float or double or decimal => FormatReal(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        _ => throw new InvalidCastException($"expected a number, got {value.GetType().Name}"),
    };

    /// <summary>
    /// Formats a real number as a Typst literal.
    /// </summary>
    /// <remarks>
    /// `NaN` and `Infinity` have no Typst literal: emitting them produces `unknown variable: NaN`
    /// from the compiler, which names neither the parameter nor the real problem.
    /// </remarks>
    private static string FormatReal(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidCastException($"expected a finite number, got {value}");
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool AsBoolean(object value) => value is bool flag
        ? flag
        : throw new InvalidCastException($"expected a boolean, got {value.GetType().Name}");

    private static string AsDate(object value) => value switch
    {
        DateOnly date => $"datetime(year: {date.Year}, month: {date.Month}, day: {date.Day})",
        DateTimeOffset moment => $"datetime(year: {moment.Year}, month: {moment.Month}, day: {moment.Day})",
        DateTime moment => $"datetime(year: {moment.Year}, month: {moment.Month}, day: {moment.Day})",
        _ => throw new InvalidCastException($"expected a date, got {value.GetType().Name}"),
    };

    private static string AsArray(object value, TypstTemplateParameter parameter, string template)
    {
        if (value is not IEnumerable items || value is string)
        {
            throw new InvalidCastException($"expected a sequence, got {value.GetType().Name}");
        }

        var rendered = new List<string>();
        foreach (var item in items)
        {
            if (item is null)
            {
                rendered.Add("none");
                continue;
            }

            rendered.Add(parameter.ItemType switch
            {
                TypstParameterType.String => Quote(AsString(item)),
                TypstParameterType.Number => AsNumber(item),
                TypstParameterType.Boolean => AsBoolean(item) ? "true" : "false",
                TypstParameterType.Date => AsDate(item),
                TypstParameterType.Object => AsObject(item, template),
                _ => throw new InvalidCastException(
                    $"array parameter '{parameter.Name}' needs 'of' to be a supported type"),
            });
        }

        return Sequence(rendered);
    }

    private static string AsObject(object value, string template)
    {
        if (value is not IReadOnlyDictionary<string, object?> readOnly)
        {
            if (value is not IDictionary<string, object?> dictionary)
            {
                throw new InvalidCastException(
                    $"expected a dictionary of string to object, got {value.GetType().Name}");
            }

            readOnly = new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);
        }

        var builder = new StringBuilder("(");
        var first = true;

        foreach (var (key, item) in readOnly)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(key).Append(": ").Append(EmitValue(item, template));
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>Emits a value whose type is not declared, as happens inside an object.</summary>
    private static string EmitValue(object? value, string template) => value switch
    {
        null => "none",
        string text => Quote(text),
        bool flag => flag ? "true" : "false",
        int or long or short or byte or sbyte or ushort or uint or ulong or float or double or decimal
            => AsNumber(value),
        DateOnly or DateTimeOffset or DateTime => AsDate(value),
        IReadOnlyDictionary<string, object?> or IDictionary<string, object?> => AsObject(value, template),
        IEnumerable items => EmitSequence(items, template),
        _ => throw new InvalidCastException(
            $"unsupported value of type {value.GetType().Name} in template '{template}'"),
    };

    private static string EmitSequence(IEnumerable items, string template)
    {
        var rendered = new List<string>();
        foreach (var item in items)
        {
            rendered.Add(EmitValue(item, template));
        }

        return Sequence(rendered);
    }

    /// <summary>
    /// Emits a Typst array. A single element needs a trailing comma: <c>(x)</c> is a
    /// parenthesized value in Typst, <c>(x,)</c> is a one-element array.
    /// </summary>
    private static string Sequence(List<string> items) => items.Count switch
    {
        0 => "()",
        1 => $"({items[0]},)",
        _ => "(" + string.Join(", ", items) + ")",
    };

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
