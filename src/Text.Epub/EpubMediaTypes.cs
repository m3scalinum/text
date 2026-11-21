using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Epub;

/// <summary>The media types an asset can be declared with from its extension.</summary>
internal static class EpubMediaTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".avif"] = "image/avif",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
        [".pdf"] = "application/pdf",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".ncx"] = "application/x-dtbncx+xml",
    };

    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
        {
            throw new EpubValidationException($"The asset '{name}' has no extension, so its media type cannot be inferred; pass one explicitly.");
        }

        return ByExtension.TryGetValue(name[dot..], out var mediaType)
            ? mediaType
            : throw new EpubValidationException($"The asset '{name}' has an extension with no known media type; pass one explicitly.");
    }
}
