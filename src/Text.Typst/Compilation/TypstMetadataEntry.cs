using System.Text.Json;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>One <c>#metadata(...)</c> entry.</summary>
/// <param name="Value">The embedded value, as JSON.</param>
/// <param name="Page">One-based page number, or zero when the entry has no position.</param>
public sealed record TypstMetadataEntry(JsonElement Value, int Page);