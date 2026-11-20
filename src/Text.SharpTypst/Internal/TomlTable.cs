using System;
using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>
/// A parsed TOML table. Values are <see cref="string"/>, <see cref="bool"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="List{T}"/> of those, or a nested <see cref="TomlTable"/>.
/// </summary>
internal sealed class TomlTable
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> Values => _values;

    public object? this[string key] => _values.TryGetValue(key, out var value) ? value : null;

    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

    public TomlTable GetOrAddTable(string key)
    {
        if (_values.TryGetValue(key, out var existing) && existing is TomlTable table)
        {
            return table;
        }

        var created = new TomlTable();
        _values[key] = created;
        return created;
    }

    public void Set(string key, object? value) => _values[key] = value;
}