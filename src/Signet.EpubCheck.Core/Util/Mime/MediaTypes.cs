using System;
using System.Globalization;

namespace Signet.EpubCheck.Core.Util.Mime;

/// <summary>The media types EPUBCheck names (upstream's <c>org.w3c.epubcheck.constants.MIMEType</c>).</summary>
public static class MediaTypes
{
    /// <summary><c>text/css</c>.</summary>
    public const string Css = "text/css";

    /// <summary><c>application/x-dtbook+xml</c>.</summary>
    public const string DtBook = "application/x-dtbook+xml";

    /// <summary><c>application/epub+zip</c>.</summary>
    public const string Epub = "application/epub+zip";

    /// <summary><c>text/html</c>.</summary>
    public const string Html = "text/html";

    /// <summary><c>image/jpeg</c>.</summary>
    public const string ImageJpeg = "image/jpeg";

    /// <summary><c>image/gif</c>.</summary>
    public const string ImageGif = "image/gif";

    /// <summary><c>image/png</c>.</summary>
    public const string ImagePng = "image/png";

    /// <summary><c>text/x-oeb1-document</c>.</summary>
    public const string Oebps = "text/x-oeb1-document";

    /// <summary><c>application/oebps-package+xml</c>.</summary>
    public const string PackageDocument = "application/oebps-package+xml";

    /// <summary><c>application/vnd.epub.search-key-map+xml</c>.</summary>
    public const string SearchKeyMap = "application/vnd.epub.search-key-map+xml";

    /// <summary><c>application/smil+xml</c>.</summary>
    public const string Smil = "application/smil+xml";

    /// <summary><c>image/svg+xml</c>.</summary>
    public const string Svg = "image/svg+xml";

    /// <summary><c>application/xhtml+xml</c>.</summary>
    public const string Xhtml = "application/xhtml+xml";

    /// <summary><c>application/xml</c> (the files of <c>META-INF</c>).</summary>
    public const string Xml = "application/xml";

    /// <summary>Whether <paramref name="value"/> is the media type <paramref name="mediaType"/>, case aside (upstream's <c>MIMEType.is</c>).</summary>
    public static bool Is(string mediaType, string? value) =>
        value is not null && string.Equals(mediaType, value.ToLower(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
