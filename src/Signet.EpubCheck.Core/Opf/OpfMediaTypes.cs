using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// The media types EPUBCheck calls blessed or core (upstream's static helpers of <c>OPFChecker</c> and
/// <c>OPFChecker30</c>): which types a content document, a style sheet, an image, a font or a script may have.
/// </summary>
public static partial class OpfMediaTypes
{
    /// <summary>Whether <paramref name="type"/> is a content document type of <paramref name="version"/>.</summary>
    public static bool IsBlessedItemType(string type, EpubVersion version) =>
        version == EpubVersion.Version2
            ? type is "application/xhtml+xml" or "application/x-dtbook+xml"
            : type is "application/xhtml+xml" or "image/svg+xml";

    /// <summary>Whether <paramref name="type"/> is a content document type of OEB 1.x, which EPUB 2 deprecates.</summary>
    public static bool IsDeprecatedBlessedItemType(string type) => type is "text/x-oeb1-document" or "text/html";

    /// <summary>Whether <paramref name="type"/> is <c>text/css</c>.</summary>
    public static bool IsBlessedStyleType(string type) => type == "text/css";

    /// <summary>Whether <paramref name="type"/> is the style sheet type of OEB 1.x.</summary>
    public static bool IsDeprecatedBlessedStyleType(string type) => type == "text/x-oeb1-css";

    /// <summary>Whether <paramref name="type"/> is an image type of <paramref name="version"/>.</summary>
    public static bool IsBlessedImageType(string type, EpubVersion version) =>
        type is "image/gif" or "image/png" or "image/jpeg" or "image/svg+xml"
        || (version == EpubVersion.Version3 && type is "image/webp" or "image/avif" or "image/jxl");

    /// <summary>Whether <paramref name="mime"/> is a font type EPUB 2 knows.</summary>
    public static bool IsBlessedFontMimeType20(string? mime) =>
        mime is not null && (mime.StartsWith("font/", StringComparison.Ordinal) || mime.StartsWith("application/font", StringComparison.Ordinal)
            || mime.StartsWith("application/x-font", StringComparison.Ordinal) || mime == "application/vnd.ms-opentype");

    /// <summary>Whether <paramref name="type"/> is one of the media types of JavaScript.</summary>
    public static bool IsScriptType(string? type)
    {
        string? lower = type?.ToLower(CultureInfo.InvariantCulture);
        return lower is "application/javascript" or "text/javascript" or "application/ecmascript" or "application/x-ecmascript"
            or "application/x-javascript" or "text/ecmascript" or "text/javascript1.0" or "text/javascript1.1"
            or "text/javascript1.2" or "text/javascript1.3" or "text/javascript1.4" or "text/javascript1.5"
            or "text/jscript" or "text/livescript" or "text/x-ecmascript" or "text/x-javascript";
    }

    /// <summary>Whether <paramref name="type"/> looks like <c>type/subtype</c> (upstream's regular expression).</summary>
    public static bool IsWellFormed(string type) => WellFormedType().IsMatch(type);

    /// <summary>Whether <paramref name="type"/> is an audio type.</summary>
    public static bool IsAudioType(string? type) => type is not null && type.StartsWith("audio/", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="type"/> is one of the audio core media types.</summary>
    public static bool IsBlessedAudioType(string type) =>
        type is "audio/mpeg" or "audio/mp4" or "audio/mp4;codecs=aac" or "audio/mp4;codecs=opus" or "audio/ogg;codecs=opus";

    /// <summary>Whether <paramref name="type"/> is a video type.</summary>
    public static bool IsVideoType(string? type) => type is not null && type.StartsWith("video/", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="type"/> is one of the common video types.</summary>
    public static bool IsCommonVideoType(string? type) => type is "video/h264" or "video/webm" or "video/mp4";

    /// <summary>Whether <paramref name="type"/> is a font type.</summary>
    public static bool IsFontType(string type) =>
        type.StartsWith("font/", StringComparison.Ordinal) || type.StartsWith("application/font-", StringComparison.Ordinal)
        || type == "application/vnd.ms-opentype";

    /// <summary>Whether <paramref name="type"/> is one of the font core media types.</summary>
    public static bool IsBlessedFontType(string type) =>
        type is "font/otf" or "font/ttf" or "font/woff" or "font/woff2" or "application/font-sfnt" or "application/font-woff"
            or "application/vnd.ms-opentype" or "application/x-font-ttf";

    /// <summary>Whether <paramref name="type"/> is one of the script core media types.</summary>
    public static bool IsBlessedScriptType(string type) => type is "text/javascript" or "application/javascript" or "application/ecmascript";

    /// <summary>Whether <paramref name="type"/> is a core media type of EPUB 3.</summary>
    public static bool IsCoreMediaType(string? type) =>
        type is not null
        && (IsBlessedAudioType(type) || IsBlessedFontType(type) || IsBlessedItemType(type, EpubVersion.Version3)
            || IsBlessedImageType(type, EpubVersion.Version3) || IsBlessedScriptType(type) || IsBlessedStyleType(type)
            || type is "application/pls+xml" or "application/smil+xml" or "image/svg+xml");

    /// <summary>The media type EPUB 3 prefers to <paramref name="type"/> for the resource at <paramref name="path"/>, or <c>null</c>.</summary>
    public static string? GetPreferredMediaType(string? type, string path) => type switch
    {
        "application/font-sfnt" => path.EndsWith(".ttf", StringComparison.Ordinal) ? "font/ttf"
            : path.EndsWith(".otf", StringComparison.Ordinal) ? "font/otf" : "font/(ttf|otf)",
        "application/vnd.ms-opentype" => "font/otf",
        "application/font-woff" => "font/woff",
        "application/x-font-ttf" => "font/ttf",
        "text/javascript" or "application/ecmascript" => "application/javascript",
        _ => null,
    };

    // Upstream's "[a-zA-Z0-9!#$&+-^_]+/[a-zA-Z0-9!#$&+-^_]+": its character class contains the range from + to ^.
    [GeneratedRegex(@"\A[a-zA-Z0-9!#$&+-^_]+/[a-zA-Z0-9!#$&+-^_]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormedType();
}
