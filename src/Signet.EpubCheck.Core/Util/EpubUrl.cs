using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Util;

/// <summary>A URL reference parsed with the URL rules needed by EPUB resources.</summary>
public sealed class EpubUrl : IEquatable<EpubUrl>
{
    private EpubUrl(string scheme, string authority, string path, string? query, string? fragment)
    {
        Scheme = scheme;
        Authority = authority;
        Path = path;
        Query = query;
        Fragment = fragment;
    }

    /// <summary>The lower-case scheme, or an empty string for a relative reference.</summary>
    public string Scheme { get; }

    /// <summary>The authority, including user information and port, or an empty string.</summary>
    public string Authority { get; }

    /// <summary>The normalized, percent-encoded path.</summary>
    public string Path { get; }

    /// <summary>The query without its leading question mark, or <c>null</c> when absent.</summary>
    public string? Query { get; }

    /// <summary>The fragment without its leading number sign, or <c>null</c> when absent.</summary>
    public string? Fragment { get; }

    /// <summary>Whether this URL has a scheme.</summary>
    public bool IsAbsolute => Scheme.Length != 0;

    /// <summary>The normalized serialized URL reference.</summary>
    public string Href => ToString();

    /// <summary>Parses an absolute or relative URL reference.</summary>
    public static EpubUrl Parse(string value) => Parse(value, null);

    /// <summary>Parses an absolute or relative URL reference against an optional base URL.</summary>
    public static EpubUrl Parse(string value, EpubUrl? baseUrl)
    {
        if (!TryParse(value, baseUrl, out EpubUrl? url))
        {
            throw new FormatException("The value is not a valid URL reference.");
        }

        return url!;
    }

    /// <summary>Attempts to parse an absolute or relative URL reference.</summary>
    public static bool TryParse(string? value, EpubUrl? baseUrl, out EpubUrl? url)
    {
        url = null;
        if (value is null || ContainsForbiddenCodePoint(value))
        {
            return false;
        }

        string input = value.Trim(' ', '\t', '\r', '\n');
        SplitSuffix(input, out string reference, out string? query, out string? fragment);
        SplitScheme(reference, out string scheme, out string remainder);
        bool hasAuthority = remainder.StartsWith("//", StringComparison.Ordinal);
        string authority = string.Empty;
        string path = remainder;
        if (hasAuthority)
        {
            int pathStart = remainder.IndexOf('/', 2);
            authority = pathStart < 0 ? remainder[2..] : remainder[2..pathStart];
            path = pathStart < 0 ? string.Empty : remainder[pathStart..];
            if (authority.Length == 0)
            {
                return false;
            }
        }

        if (scheme.Length == 0 && !hasAuthority && baseUrl is not null)
        {
            scheme = baseUrl.Scheme;
            if (path.Length == 0)
            {
                authority = baseUrl.Authority;
                path = baseUrl.Path;
                query ??= baseUrl.Query;
            }
            else if (!path.StartsWith('/'))
            {
                authority = baseUrl.Authority;
                path = Merge(baseUrl.Path, path);
            }
            else
            {
                authority = baseUrl.Authority;
            }
        }
        else if (scheme.Length == 0 && !hasAuthority && path.Length == 0 && baseUrl is not null)
        {
            scheme = baseUrl.Scheme;
            authority = baseUrl.Authority;
            path = baseUrl.Path;
            query ??= baseUrl.Query;
        }

        if (scheme.Length != 0 && IsSpecialScheme(scheme))
        {
            path = path.Replace('\\', '/');
        }

        url = new EpubUrl(scheme, NormalizeAuthority(authority), NormalizePath(path), NormalizeQueryOrFragment(query), NormalizeQueryOrFragment(fragment));
        return true;
    }

    /// <summary>Returns the URL without its fragment.</summary>
    public EpubUrl DocumentUrl() => new(Scheme, Authority, Path, Query, null);

    /// <summary>Normalizes an EPUB resource path without treating it as an operating-system path.</summary>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string encoded = EncodePath(PercentDecode(path).Normalize(NormalizationForm.FormC));
        bool absolute = encoded.StartsWith('/');
        bool trailingSlash = encoded.EndsWith('/');
        string[] segments = encoded.Split('/', StringSplitOptions.None);
        List<string> normalized = [];
        foreach (string segment in segments)
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (normalized.Count > 0 && normalized[^1] != "..")
                {
                    normalized.RemoveAt(normalized.Count - 1);
                }
                else if (!absolute)
                {
                    normalized.Add("..");
                }

                continue;
            }

            normalized.Add(segment);
        }

        string normalizedPath = string.Join("/", normalized);
        if (absolute)
        {
            normalizedPath = "/" + normalizedPath;
        }

        if (trailingSlash && normalizedPath.Length > 0 && normalizedPath[^1] != '/')
        {
            normalizedPath += "/";
        }

        return normalizedPath;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder result = new();
        if (Scheme.Length != 0)
        {
            result.Append(Scheme);
            result.Append(':');
        }

        if (Authority.Length != 0)
        {
            result.Append("//");
            result.Append(Authority);
        }

        result.Append(Path);
        if (Query is not null)
        {
            result.Append('?');
            result.Append(Query);
        }

        if (Fragment is not null)
        {
            result.Append('#');
            result.Append(Fragment);
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public bool Equals(EpubUrl? other) => other is not null && ToString() == other.ToString();

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EpubUrl);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    private static void SplitSuffix(string input, out string reference, out string? query, out string? fragment)
    {
        int fragmentIndex = input.IndexOf('#');
        fragment = fragmentIndex < 0 ? null : input[(fragmentIndex + 1)..];
        string withoutFragment = fragmentIndex < 0 ? input : input[..fragmentIndex];
        int queryIndex = withoutFragment.IndexOf('?');
        query = queryIndex < 0 ? null : withoutFragment[(queryIndex + 1)..];
        reference = queryIndex < 0 ? withoutFragment : withoutFragment[..queryIndex];
    }

    private static void SplitScheme(string reference, out string scheme, out string remainder)
    {
        int colon = reference.IndexOf(':');
        if (colon > 0 && IsScheme(reference.AsSpan(0, colon)))
        {
            scheme = reference[..colon].ToLowerInvariant();
            remainder = reference[(colon + 1)..];
            return;
        }

        scheme = string.Empty;
        remainder = reference;
    }

    private static bool IsScheme(ReadOnlySpan<char> value)
    {
        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (char character in value[1..])
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '-' and not '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSpecialScheme(string scheme) => scheme is "file" or "ftp" or "http" or "https" or "ws" or "wss";

    private static string Merge(string basePath, string path)
    {
        int lastSlash = basePath.LastIndexOf('/');
        return lastSlash < 0 ? path : basePath[..(lastSlash + 1)] + path;
    }

    private static string NormalizeAuthority(string authority)
    {
        int userInfoEnd = authority.LastIndexOf('@');
        string userInfo = userInfoEnd < 0 ? string.Empty : authority[..(userInfoEnd + 1)];
        return userInfo + authority[(userInfoEnd + 1)..].ToLowerInvariant();
    }

    private static string? NormalizeQueryOrFragment(string? value) => value is null ? null : EncodeComponent(PercentDecode(value));

    private static bool ContainsForbiddenCodePoint(string value)
    {
        foreach (char character in value)
        {
            if (character is '\0' or < '\x20' or '\x7f')
            {
                return true;
            }
        }

        return false;
    }

    internal static string PercentDecode(string value)
    {
        StringBuilder result = new();
        Span<byte> bytes = stackalloc byte[4];
        for (int index = 0; index < value.Length;)
        {
            if (value[index] != '%' || index + 2 >= value.Length || !TryReadHex(value.AsSpan(index + 1, 2), out byte first))
            {
                result.Append(value[index++]);
                continue;
            }

            int count = 0;
            do
            {
                bytes[count++] = first;
                index += 3;
            }
            while (count < bytes.Length && index + 2 < value.Length && value[index] == '%' && TryReadHex(value.AsSpan(index + 1, 2), out first));

            result.Append(Encoding.UTF8.GetString(bytes[..count]));
        }

        return result.ToString();
    }

    private static string EncodePath(string value) => Encode(value, static character =>
        char.IsAsciiLetterOrDigit(character) || character is '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/' or ':' or ';' or '=' or '@' or '_' or '~');

    private static string EncodeComponent(string value) => Encode(value, static character =>
        char.IsAsciiLetterOrDigit(character) || character is '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/' or ':' or ';' or '=' or '?' or '@' or '_' or '~');

    private static string Encode(string value, Func<char, bool> isAllowed)
    {
        StringBuilder result = new();
        Span<byte> bytes = stackalloc byte[4];
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.IsAscii && isAllowed((char)rune.Value))
            {
                result.Append((char)rune.Value);
                continue;
            }

            int length = rune.EncodeToUtf8(bytes);
            for (int index = 0; index < length; index++)
            {
                result.Append('%');
                result.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    private static bool TryReadHex(ReadOnlySpan<char> value, out byte result) =>
        byte.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
}
