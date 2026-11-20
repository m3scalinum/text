namespace M3scalinum.Text.SharpTypst.Compilation;

/// <summary>A file in a bundle: a document produced by <c>#document</c>, or an asset declared
/// with <c>#asset</c>.</summary>
/// <param name="Path">The path the file takes inside the bundle, without a leading slash.</param>
/// <param name="Data">The raw bytes written at that path.</param>
public sealed record BundleFile(string Path, byte[] Data);