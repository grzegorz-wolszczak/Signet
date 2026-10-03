using System;
using System.Collections.Generic;

namespace Signet.Core;

/// <summary>
/// Stateless maps of the MIME types used in EPUB: extension -&gt; media-type,
/// media-type -&gt; extension, media-type -&gt; group (a section in the book tree),
/// media-type -&gt; <see cref="ResourceType"/>.
/// </summary>
/// <remarks>
/// The tables are static because they are immutable. They reflect the preferred media-types of
/// EPUB 3.2/3.3, with <c>image/avif</c> and <c>image/jxl</c> as known types, although they are not
/// widely supported by readers. Content sniffing and media-type detection from XML markup are
/// not handled here.
/// </remarks>
public static class MediaTypes
{
    private static readonly Dictionary<string, string> ExtToMediaType = new(StringComparer.Ordinal)
    {
        ["avif"] = "image/avif",
        ["bm"] = "image/bmp",
        ["bmp"] = "image/bmp",
        ["css"] = "text/css",
        ["epub"] = "application/epub+zip",
        ["gif"] = "image/gif",
        ["htm"] = "application/xhtml+xml",
        ["html"] = "application/xhtml+xml",
        ["jpeg"] = "image/jpeg",
        ["jpg"] = "image/jpeg",
        ["js"] = "application/javascript",
        ["json"] = "application/json",
        ["jxl"] = "image/jxl",
        ["es"] = "application/ecmascript",
        ["m4a"] = "audio/mp4",
        ["m4v"] = "video/mp4",
        ["mp3"] = "audio/mpeg",
        ["mp4"] = "video/mp4",
        ["ncx"] = "application/x-dtbncx+xml",
        ["oga"] = "audio/ogg",
        ["ogg"] = "audio/ogg",
        ["ogv"] = "video/ogg",
        ["opf"] = "application/oebps-package+xml",
        ["opus"] = "audio/opus",
        ["otf"] = "font/otf",
        ["pls"] = "application/pls+xml",
        ["pdf"] = "application/pdf",
        ["png"] = "image/png",
        ["smil"] = "application/smil+xml",
        ["svg"] = "image/svg+xml",
        ["tif"] = "image/tiff",
        ["tiff"] = "image/tiff",
        ["ttc"] = "font/collection",
        ["ttf"] = "font/ttf",
        ["ttml"] = "application/ttml+xml",
        ["txt"] = "text/plain",
        ["vtt"] = "text/vtt",
        ["webm"] = "video/webm",
        ["webp"] = "image/webp",
        ["woff"] = "font/woff",
        ["woff2"] = "font/woff2",
        ["xhtml"] = "application/xhtml+xml",
        ["xml"] = "application/xml",
        ["xpgt"] = "application/vnd.adobe-page-template+xml",
    };

    private static readonly Dictionary<string, string> MediaTypeToExt = new(StringComparer.Ordinal)
    {
        // Core EPUB
        ["application/epub+zip"] = "epub",
        ["application/oebps-package+xml"] = "opf",
        ["application/x-dtbncx+xml"] = "ncx",
        ["application/smil+xml"] = "smil",
        ["application/pls+xml"] = "pls",
        ["application/xhtml+xml"] = "xhtml",
        ["application/x-dtbook+xml"] = "xhtml",
        ["text/html"] = "xhtml",
        ["text/css"] = "css",

        // Adobe
        ["application/adobe-page-template+xml"] = "xpgt",
        ["application/vnd.adobe-page-template+xml"] = "xpgt",
        ["application/oebps-page-map+xml"] = "xml",
        ["application/vnd.adobe-page-map+xml"] = "xml",
        ["application/pdf"] = "pdf",

        // Apple
        ["vnd.apple.ibooks+xml"] = "xml",

        // Images
        ["image/bmp"] = "bmp",
        ["image/gif"] = "gif",
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/svg+xml"] = "svg",
        ["image/tiff"] = "tif",
        ["image/webp"] = "webp",
        ["image/avif"] = "avif",
        ["image/jxl"] = "jxl",

        // Fonts - OpenType
        ["font/otf"] = "otf",
        ["application/vnd.ms-opentype"] = "otf",
        ["application/x-font-otf"] = "otf",
        ["application/x-font-opentype"] = "otf",
        ["application/x-opentype-font"] = "otf",
        ["application/font-otf"] = "otf",

        // Fonts - TrueType
        ["font/ttf"] = "ttf",
        ["application/x-font-ttf"] = "ttf",
        ["application/x-font-truetype"] = "ttf",
        ["application/x-truetype-font"] = "ttf",
        ["application/font-ttf"] = "ttf",

        // Fonts - TrueType collection
        ["font/collection"] = "ttc",
        ["application/x-font-truetype-collection"] = "ttc",

        // Fonts - woff / woff2
        ["font/woff"] = "woff",
        ["application/font-woff"] = "woff",
        ["font/woff2"] = "woff2",
        ["application/font-woff2"] = "woff2",

        // Fonts - sfnt (ttf or otf)
        ["font/sfnt"] = "sfnt",
        ["application/font-sfnt"] = "sfnt",

        // JavaScript
        ["application/javascript"] = "js",
        ["application/json"] = "json",
        ["application/x-javascript"] = "js",
        ["application/ecmascript"] = "es",
        ["text/javascript"] = "js",

        // Text
        ["text/plain"] = "txt",

        // XML (generic)
        ["application/xml"] = "xml",
        ["text/xml"] = "xml",

        // Audio
        ["audio/mpeg"] = "mpeg",
        ["audio/mp3"] = "mp3",
        ["audio/mp4"] = "m4a",
        ["audio/ogg"] = "ogg",
        ["audio/opus"] = "opus",

        // Video
        ["video/mp4"] = "m4v",
        ["video/ogg"] = "ogv",
        ["video/webm"] = "webm",

        // Subtitles
        ["text/vtt"] = "vtt",
        ["application/ttml+xml"] = "ttml",
    };

    private static readonly Dictionary<string, string> MediaTypeToGroupMap = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = "Images",
        ["image/png"] = "Images",
        ["image/gif"] = "Images",
        ["image/svg+xml"] = "Images",
        ["image/bmp"] = "Images",
        ["image/tiff"] = "Images",
        ["image/webp"] = "Images",
        ["image/jxl"] = "Images",
        ["image/avif"] = "Images",

        ["application/xhtml+xml"] = "Text",
        ["application/x-dtbook+xml"] = "Text",
        ["text/html"] = "Text",

        ["font/woff2"] = "Fonts",
        ["font/woff"] = "Fonts",
        ["font/otf"] = "Fonts",
        ["font/ttf"] = "Fonts",
        ["font/sfnt"] = "Fonts",
        ["font/collection"] = "Fonts",
        ["application/vnd.ms-opentype"] = "Fonts",

        // deprecated font media-types
        ["application/font-ttf"] = "Fonts",
        ["application/font-otf"] = "Fonts",
        ["application/font-sfnt"] = "Fonts",
        ["application/font-woff"] = "Fonts",
        ["application/font-woff2"] = "Fonts",
        ["application/x-truetype-font"] = "Fonts",
        ["application/x-opentype-font"] = "Fonts",
        ["application/x-font-ttf"] = "Fonts",
        ["application/x-font-otf"] = "Fonts",
        ["application/x-font-opentype"] = "Fonts",
        ["application/x-font-truetype"] = "Fonts",
        ["application/x-font-truetype-collection"] = "Fonts",

        ["audio/mpeg"] = "Audio",
        ["audio/mp3"] = "Audio",
        ["audio/mp4"] = "Audio",
        ["audio/ogg"] = "Audio",
        ["audio/opus"] = "Audio",

        ["video/mp4"] = "Video",
        ["video/ogg"] = "Video",
        ["video/webm"] = "Video",
        ["text/vtt"] = "Video",
        ["application/ttml+xml"] = "Video",

        ["text/css"] = "Styles",

        ["application/x-dtbncx+xml"] = "ncx",

        ["application/oebps-package+xml"] = "opf",

        ["application/oebps-page-map+xml"] = "Misc",
        ["application/vnd.adobe-page-map+xml"] = "Misc",
        ["application/smil+xml"] = "Misc",
        ["application/pls+xml"] = "Misc",
        ["application/xml"] = "Misc",
        ["text/xml"] = "Misc",
        ["application/adobe-page-template+xml"] = "Misc",
        ["application/vnd.adobe-page-template+xml"] = "Misc",
        ["application/javascript"] = "Misc",
        ["application/json"] = "Misc",
        ["application/ecmascript"] = "Misc",
        ["application/x-javascript"] = "Misc",
        ["text/javascript"] = "Misc",
        ["application/pdf"] = "Misc",
        ["text/plain"] = "Misc",

        ["vnd.apple.ibooks+xml"] = "other",
    };

    private static readonly Dictionary<string, string> MediaTypeToResourceDesc = new(StringComparer.Ordinal)
    {
        ["application/xhtml+xml"] = "HTMLResource",
        ["application/x-dtbook+xml"] = "HTMLResource",
        ["text/html"] = "HTMLResource",

        ["text/css"] = "CSSResource",

        ["application/oebps-package+xml"] = "OPFResource",
        ["application/x-dtbncx+xml"] = "NCXResource",

        ["image/jpeg"] = "ImageResource",
        ["image/png"] = "ImageResource",
        ["image/gif"] = "ImageResource",
        ["image/bmp"] = "ImageResource",
        ["image/tiff"] = "ImageResource",
        ["image/webp"] = "ImageResource",
        ["image/avif"] = "ImageResource",
        ["image/jxl"] = "ImageResource",

        ["image/svg+xml"] = "SVGResource",

        ["font/woff2"] = "FontResource",
        ["font/woff"] = "FontResource",
        ["font/otf"] = "FontResource",
        ["font/ttf"] = "FontResource",
        ["application/vnd.ms-opentype"] = "FontResource",
        ["font/collection"] = "FontResource",

        // deprecated
        ["application/x-font-truetype-collection"] = "FontResource",
        ["application/x-font-ttf"] = "FontResource",
        ["application/x-font-otf"] = "FontResource",
        ["application/x-font-opentype"] = "FontResource",
        ["application/x-font-truetype"] = "FontResource",
        ["application/x-truetype-font"] = "FontResource",
        ["application/x-opentype-font"] = "FontResource",
        ["application/font-ttf"] = "FontResource",
        ["application/font-otf"] = "FontResource",
        ["application/font-sfnt"] = "FontResource",
        ["application/font-woff"] = "FontResource",
        ["application/font-woff2"] = "FontResource",

        ["audio/mpeg"] = "AudioResource",
        ["audio/mp3"] = "AudioResource",
        ["audio/mp4"] = "AudioResource",
        ["audio/ogg"] = "AudioResource",
        ["audio/opus"] = "AudioResource",

        ["video/mp4"] = "VideoResource",
        ["video/ogg"] = "VideoResource",
        ["video/webm"] = "VideoResource",
        ["text/vtt"] = "VideoResource",
        ["application/ttml+xml"] = "VideoResource",

        ["application/smil+xml"] = "XMLResource",
        ["application/pls+xml"] = "XMLResource",
        ["application/oebps-page-map+xml"] = "XMLResource",
        ["application/vnd.adobe-page-map+xml"] = "XMLResource",
        ["application/adobe-page-template+xml"] = "XMLResource",
        ["application/vnd.adobe-page-template+xml"] = "XMLResource",

        ["application/javascript"] = "MiscTextResource",
        ["application/json"] = "MiscTextResource",
        ["application/x-javascript"] = "MiscTextResource",
        ["application/ecmascript"] = "MiscTextResource",
        ["text/javascript"] = "MiscTextResource",
        ["text/plain"] = "MiscTextResource",

        ["application/xml"] = "XMLResource",
        ["text/xml"] = "XMLResource",

        ["application/pdf"] = "PdfResource",

        ["vnd.apple.ibooks+xml"] = "Resource",
    };

    private static readonly Dictionary<string, ResourceType> ResourceDescToType = new(StringComparer.Ordinal)
    {
        ["HTMLResource"] = ResourceType.Html,
        ["CSSResource"] = ResourceType.Css,
        ["OPFResource"] = ResourceType.Opf,
        ["NCXResource"] = ResourceType.Ncx,
        ["ImageResource"] = ResourceType.Image,
        ["SVGResource"] = ResourceType.Svg,
        ["FontResource"] = ResourceType.Font,
        ["AudioResource"] = ResourceType.Audio,
        ["VideoResource"] = ResourceType.Video,
        ["XMLResource"] = ResourceType.Xml,
        ["TextResource"] = ResourceType.Text,
        ["MiscTextResource"] = ResourceType.MiscText,
        ["PdfResource"] = ResourceType.Pdf,
        ["Resource"] = ResourceType.Generic,
    };

    /// <summary>A map of extension (without the dot, lowercase) -&gt; media-type.</summary>
    public static IReadOnlyDictionary<string, string> ExtensionToMediaType => ExtToMediaType;

    /// <summary>A map of media-type -&gt; preferred file extension (without the dot).</summary>
    public static IReadOnlyDictionary<string, string> MediaTypeToExtension => MediaTypeToExt;

    /// <summary>A map of media-type -&gt; group (the section name in the book tree).</summary>
    public static IReadOnlyDictionary<string, string> MediaTypeToGroup => MediaTypeToGroupMap;

    /// <summary>A map of media-type -&gt; resource class name (e.g. <c>HTMLResource</c>).</summary>
    public static IReadOnlyDictionary<string, string> MediaTypeToResourceDescription => MediaTypeToResourceDesc;

    /// <summary>
    /// The media-type for a file extension.
    /// A leading dot is stripped, and the extension is compared case-insensitively.
    /// </summary>
    /// <param name="extension">The extension, e.g. <c>xhtml</c>, <c>.PNG</c>.</param>
    /// <param name="fallback">The value returned when the extension is unknown.</param>
    public static string GetMediaTypeFromExtension(string extension, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(extension);
        return ExtToMediaType.GetValueOrDefault(NormalizeExtension(extension), fallback);
    }

    /// <summary>
    /// The preferred file extension for a media-type.
    /// </summary>
    /// <param name="mediaType">The media-type, e.g. <c>image/jpeg</c>.</param>
    /// <param name="fallback">The value returned when the media-type is unknown.</param>
    public static string GetExtensionFromMediaType(string mediaType, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        return MediaTypeToExt.GetValueOrDefault(mediaType, fallback);
    }

    /// <summary>
    /// The group (a section of the book tree) for a media-type.
    /// For unknown types, prefix-based heuristics are applied.
    /// </summary>
    /// <param name="mediaType">The media-type.</param>
    /// <param name="fallback">The value returned when the group cannot be determined.</param>
    public static string GetGroupFromMediaType(string mediaType, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        if (MediaTypeToGroupMap.TryGetValue(mediaType, out string? group))
        {
            return group;
        }

        string guess = string.Empty;
        if (mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            guess = "Images";
        }

        if (mediaType.StartsWith("application/font", StringComparison.Ordinal)
            || mediaType.StartsWith("application/x-font", StringComparison.Ordinal)
            || mediaType.StartsWith("font/", StringComparison.Ordinal))
        {
            guess = "Fonts";
        }

        if (mediaType.StartsWith("audio/", StringComparison.Ordinal))
        {
            guess = "Audio";
        }

        if (mediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            guess = "Video";
        }

        if (mediaType.Contains("adobe", StringComparison.Ordinal)
            && mediaType.Contains("template", StringComparison.Ordinal))
        {
            guess = "Misc";
        }

        if (mediaType.StartsWith("application/pdf", StringComparison.Ordinal))
        {
            guess = "Misc";
        }

        return guess.Length != 0 ? guess : fallback;
    }

    /// <summary>
    /// The resource class name for a media-type, using the same prefix-based heuristics.
    /// It is usually more convenient to use <see cref="GetResourceType"/>.
    /// </summary>
    /// <param name="mediaType">The media-type.</param>
    /// <param name="fallback">The value returned when the class cannot be determined.</param>
    public static string GetResourceDescriptionFromMediaType(string mediaType, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        if (MediaTypeToResourceDesc.TryGetValue(mediaType, out string? desc))
        {
            return desc;
        }

        string guess = string.Empty;
        if (mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            guess = "ImageResource";
        }

        if (mediaType.StartsWith("application/font", StringComparison.Ordinal)
            || mediaType.StartsWith("application/x-font", StringComparison.Ordinal)
            || mediaType.StartsWith("font/", StringComparison.Ordinal))
        {
            guess = "FontResource";
        }

        if (mediaType.StartsWith("audio/", StringComparison.Ordinal))
        {
            guess = "AudioResource";
        }

        if (mediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            guess = "VideoResource";
        }

        if (mediaType.Contains("adobe", StringComparison.Ordinal)
            && mediaType.Contains("template", StringComparison.Ordinal))
        {
            guess = "XMLResource";
        }

        if (mediaType.StartsWith("application/pdf", StringComparison.Ordinal))
        {
            guess = "PdfResource";
        }

        return guess.Length != 0 ? guess : fallback;
    }

    /// <summary>
    /// The <see cref="ResourceType"/> for a media-type. Combines the tables with prefix-based
    /// heuristics (EPUBs in practice misuse incorrect media-types, so we try to
    /// match a pattern).
    /// </summary>
    /// <param name="mediaType">The media-type.</param>
    /// <param name="fallback">The type returned when nothing can be determined.</param>
    public static ResourceType GetResourceType(string mediaType, ResourceType fallback = ResourceType.Generic)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        string desc = GetResourceDescriptionFromMediaType(mediaType, string.Empty);
        return desc.Length != 0 && ResourceDescToType.TryGetValue(desc, out ResourceType type)
            ? type
            : fallback;
    }

    private static string NormalizeExtension(string extension)
    {
        string trimmed = extension.Trim();
        if (trimmed.StartsWith('.'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.ToLowerInvariant();
    }
}
