using System;
using System.IO;
using System.Text;
using Signet.EpubCheck.Core.Galimatias;

namespace Signet.EpubCheck.Core.Util;

/// <summary>EPUBCheck's own URL helpers on top of galimatias (upstream's <c>org.w3c.epubcheck.util.url.URLUtils</c>).</summary>
public static class EpubCheckUrlUtils
{
    private const string DataScheme = "data";
    private const int EscapeLength = 3; // "%XY"

    /// <summary>The <c>file:</c> URL of a local file or folder.</summary>
    public static Url ToUrl(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return Url.Parse(new Uri(Path.GetFullPath(filePath)).AbsoluteUri);
    }

    /// <summary>The local path of a <c>file:</c> URL.</summary>
    public static string ToFilePath(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Url.FileScheme)
        {
            throw new ArgumentException("Not a file URL: " + url, nameof(url));
        }

        try
        {
            return new Uri(url.ToString()).LocalPath;
        }
        catch (UriFormatException)
        {
            return Decode(url.Path ?? string.Empty);
        }
    }

    /// <summary>The URL without its fragment.</summary>
    public static Url DocUrl(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.WithFragment(null);
    }

    /// <summary>
    /// Whether two URLs have the same origin: equal, or of the same special scheme (ftp, http, https, ws, wss, file),
    /// host and port. <c>file</c> URLs are treated as hierarchical: a file checked alone is the same origin as the
    /// files its relative URLs point to.
    /// </summary>
    public static bool IsSameOrigin(Url? urlA, Url? urlB)
    {
        if (urlA is null || urlB is null)
        {
            return urlA is null && urlB is null;
        }

        if (urlA.Equals(urlB))
        {
            return true;
        }

        return urlA.Scheme is "ftp" or "http" or "https" or "ws" or "wss" or Url.FileScheme
            && urlA.Scheme == urlB.Scheme
            && Equals(urlA.Host, urlB.Host)
            && urlA.Port == urlB.Port;
    }

    /// <summary>Whether <paramref name="test"/> is remote from <paramref name="local"/>: not a <c>data:</c> URL and not of the same origin.</summary>
    public static bool IsRemote(Url? test, Url? local) => (test is null || test.Scheme != DataScheme) && !IsSameOrigin(test, local);

    /// <summary>Whether the string is an absolute URL (it does not fail for want of a scheme).</summary>
    public static bool IsAbsoluteUrlString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            Url.Parse(value);
            return true;
        }
        catch (GalimatiasParseException ex)
        {
            return ex.Issue != ParseIssue.MissingScheme;
        }
    }

    /// <summary>Percent-encodes what is not a URL code point in a path, and <c>?</c>.</summary>
    public static string? EncodePath(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder buffer = new(value.Length * 2);
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (!Galimatias.UrlUtils.IsUrlCodePoint(rune.Value) || rune.Value == '?')
            {
                foreach (byte b in Encoding.UTF8.GetBytes(rune.ToString()))
                {
                    Galimatias.UrlUtils.PercentEncode(b, buffer);
                }
            }
            else
            {
                buffer.Append(rune.ToString());
            }
        }

        return buffer.ToString();
    }

    /// <summary>Percent-decodes <paramref name="value"/> (UTF-8).</summary>
    public static string Decode(string value) => Galimatias.UrlUtils.PercentDecode(value);

    /// <summary>
    /// The URL with its path normalized (percent-decoded, NFC, percent-encoded again) and the escapes of unreserved
    /// characters decoded (galimatias' <c>DecodeUnreservedCanonicalizer</c>).
    /// </summary>
    public static Url? Normalize(Url? url)
    {
        if (url is null)
        {
            return null;
        }

        Url normalized = url;
        if (url.IsHierarchical && url.Path is not null)
        {
            normalized = url.WithPath(EncodePath(UnicodeUtils.Normalize(Decode(url.Path)))!);
        }

        return DecodeUnreserved(normalized);
    }

    /// <summary>The media type of a <c>data:</c> URL (what comes before <c>,</c> or <c>;</c>), or <c>null</c> for another scheme.</summary>
    public static string? GetDataUrlType(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != DataScheme)
        {
            return null;
        }

        int end = url.SchemeData.AsSpan().IndexOfAny(',', ';');
        return end < 0 ? url.SchemeData : url.SchemeData[..end];
    }

    private static Url DecodeUnreserved(Url input)
    {
        Url output = input;
        if (output.IsHierarchical)
        {
            output = output.WithUsername(DecodeUnreserved(output.Username))
                .WithPassword(DecodeUnreserved(output.Password))
                .WithPath(DecodeUnreserved(output.Path)!);
        }

        return output.WithQuery(DecodeUnreserved(output.Query)).WithFragment(DecodeUnreserved(output.Fragment));
    }

    // Decodes the escapes of the unreserved characters: ASCII letters and digits, '-', '.', '_', '~'.
    private static string? DecodeUnreserved(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        StringBuilder output = new(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '%' && input.Length > i + 2 && Galimatias.UrlUtils.IsAsciiHexDigit(input[i + 1]) && Galimatias.UrlUtils.IsAsciiHexDigit(input[i + 2]))
            {
                int decoded = Galimatias.UrlUtils.HexToInt(input[i + 1], input[i + 2]);
                if (Galimatias.UrlUtils.IsAsciiAlphanumeric(decoded) || decoded is '-' or '.' or '_' or '~')
                {
                    output.Append((char)decoded);
                }
                else
                {
                    output.Append(input, i, EscapeLength);
                }

                i += EscapeLength - 1;
            }
            else
            {
                output.Append(c);
            }
        }

        return output.ToString();
    }
}
