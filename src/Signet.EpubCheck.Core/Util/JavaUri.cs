using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Signet.EpubCheck.Core.Util;

/// <summary>
/// Whether a string is a URI for Java's <c>java.net.URI(String)</c> constructor (RFC 2396 with Java's deviations),
/// which upstream uses to accept or reject some URIs (e.g. the URIs of prefix declarations, <c>OPF-006</c>). This
/// follows the checks of the JDK's <c>URI.Parser</c>; an IPv6 literal is checked with <see cref="IPAddress"/>.
/// </summary>
public static class JavaUri
{
    // The character classes of RFC 2396 as java.net.URI defines them.
    private const string Mark = "-_.!~*'()";
    private const string Reserved = ";/?:@&=+$,[]";
    private const string PathPunctuation = ":@&=+$,;/";
    private const string UserInfoPunctuation = ";:&=+$,";
    private const string RegistryPunctuation = "$,;:@&=+";
    private const string ServerPunctuation = ".:@[]-";
    private const string SchemePunctuation = "+-.";
    private const string ScopeIdPunctuation = "_.";
    private const char Escape = '%';
    private const int EscapeLength = 3;

    // Java lets unescaped characters above U+0080 through where escapes are allowed, unless they are space or control.
    private const char FirstOtherCharacter = (char)(UnicodeRanges.C1ControlsFirst + 1);

    private delegate bool CharClass(char c);

    /// <summary>Whether <paramref name="value"/> parses as a <c>java.net.URI</c>.</summary>
    public static bool IsValid(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            Parse(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || Mark.Contains(c, StringComparison.Ordinal);

    private static bool IsUric(char c) => IsUnreserved(c) || Reserved.Contains(c, StringComparison.Ordinal);

    private static bool IsPath(char c) => IsUnreserved(c) || PathPunctuation.Contains(c, StringComparison.Ordinal);

    private static bool IsUserInfo(char c) => IsUnreserved(c) || UserInfoPunctuation.Contains(c, StringComparison.Ordinal);

    private static bool IsRegistry(char c) => IsUnreserved(c) || RegistryPunctuation.Contains(c, StringComparison.Ordinal);

    private static bool IsServer(char c) => IsUserInfo(c) || char.IsAsciiLetterOrDigit(c) || ServerPunctuation.Contains(c, StringComparison.Ordinal);

    private static bool IsScheme(char c) => char.IsAsciiLetterOrDigit(c) || SchemePunctuation.Contains(c, StringComparison.Ordinal);

    private static bool IsScopeId(char c) => char.IsAsciiLetterOrDigit(c) || ScopeIdPunctuation.Contains(c, StringComparison.Ordinal);

    private static void Parse(string input)
    {
        int n = input.Length;
        int p = ScanUntil(input, 0, n, "/?#", ":");
        if (p >= 0 && At(input, p, n, ':'))
        {
            if (p == 0)
            {
                throw Fail("Expected scheme name");
            }

            if (!char.IsAsciiLetter(input[0]) || Scan(input, 1, p, IsScheme, escapes: false) != p)
            {
                throw Fail("Illegal character in scheme name");
            }

            p++;
            if (At(input, p, n, '/'))
            {
                p = ParseHierarchical(input, p, n);
            }
            else
            {
                int q = ScanUntil(input, p, n, string.Empty, "#");
                if (q <= p)
                {
                    throw Fail("Expected scheme-specific part");
                }

                CheckChars(input, p, q, IsUric, "opaque part");
                p = q;
            }
        }
        else
        {
            p = ParseHierarchical(input, 0, n);
        }

        if (At(input, p, n, '#'))
        {
            CheckChars(input, p + 1, n, IsUric, "fragment");
            p = n;
        }

        if (p < n)
        {
            throw Fail("End of URI expected");
        }
    }

    private static int ParseHierarchical(string input, int start, int n)
    {
        int p = start;
        if (At(input, p, n, '/') && At(input, p + 1, n, '/'))
        {
            p += 2;
            int q = ScanUntil(input, p, n, string.Empty, "/?#");
            if (q > p)
            {
                p = ParseAuthority(input, p, q);
            }
            else if (q >= n)
            {
                throw Fail("Expected authority");
            }
        }

        int end = ScanUntil(input, p, n, string.Empty, "?#");
        CheckChars(input, p, end, IsPath, "path");
        p = end;
        if (At(input, p, n, '?'))
        {
            p++;
            end = ScanUntil(input, p, n, string.Empty, "#");
            CheckChars(input, p, end, IsUric, "query");
            p = end;
        }

        return p;
    }

    private static int ParseAuthority(string input, int start, int n)
    {
        bool serverChars = Scan(input, start, n, IsServer, escapes: true) == n;
        bool registryChars = Scan(input, start, n, IsRegistry, escapes: true) == n;
        if (registryChars && !serverChars)
        {
            return n;
        }

        FormatException? serverError = null;
        if (serverChars)
        {
            try
            {
                if (ParseServer(input, start, n) == n)
                {
                    return n;
                }
            }
            catch (FormatException ex)
            {
                serverError = ex;
            }
        }

        // Not a server-based authority: a registry-based one, when its characters allow it.
        if (registryChars)
        {
            return n;
        }

        throw serverError ?? Fail("Illegal character in authority");
    }

    private static int ParseServer(string input, int start, int n)
    {
        int p = start;
        int q = ScanUntil(input, p, n, "/?#", "@");
        if (q >= p && At(input, q, n, '@'))
        {
            CheckChars(input, p, q, IsUserInfo, "user info");
            p = q + 1;
        }

        if (At(input, p, n, '['))
        {
            p++;
            q = ScanUntil(input, p, n, "/?#", "]");
            if (q <= p || !At(input, q, n, ']'))
            {
                throw Fail("Expected closing bracket for IPv6 address");
            }

            int scope = input.IndexOf(Escape, p, q - p);
            string address = input[p..(scope < 0 ? q : scope)];
            if (!IPAddress.TryParse(address, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw Fail("Malformed IPv6 address");
            }

            if (scope >= 0)
            {
                if (scope + 1 == q)
                {
                    throw Fail("Scope id expected");
                }

                CheckChars(input, scope + 1, q, IsScopeId, "scope id");
            }

            p = q + 1;
        }
        else
        {
            p = ParseHostname(input, p, n);
        }

        if (At(input, p, n, ':'))
        {
            p++;
            q = ScanUntil(input, p, n, string.Empty, "/");
            if (q > p)
            {
                if (Scan(input, p, q, char.IsAsciiDigit, escapes: false) != q
                    || !int.TryParse(input.AsSpan(p, q - p), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    throw Fail("Malformed port number");
                }
            }

            p = q;
        }

        if (p < n)
        {
            throw Fail("Expected port number");
        }

        return p;
    }

    // An IPv4 address is a hostname of digit labels for this check, except that its last label may start with a digit.
    private static int ParseHostname(string input, int start, int n)
    {
        int p = start;
        int lastLabel = -1;
        bool allDigits = true;
        do
        {
            int q = Scan(input, p, n, char.IsAsciiLetterOrDigit, escapes: false);
            if (q <= p)
            {
                break;
            }

            lastLabel = p;
            p = q;
            q = Scan(input, p, n, static c => char.IsAsciiLetterOrDigit(c) || c == '-', escapes: false);
            if (q > p)
            {
                if (input[q - 1] == '-')
                {
                    throw Fail("Illegal character in hostname");
                }

                p = q;
            }

            for (int i = lastLabel; i < p; i++)
            {
                allDigits &= char.IsAsciiDigit(input[i]);
            }

            q = Scan(input, p, n, static c => c == '.', escapes: false);
            if (q <= p)
            {
                break;
            }

            p = q;
        }
        while (p < n);

        if (p < n && !At(input, p, n, ':'))
        {
            throw Fail("Illegal character in hostname");
        }

        if (lastLabel < 0)
        {
            return start; // an empty host: allowed when the authority is only a port or user info
        }

        // The top label must start with a letter (an IPv4 address is all digits).
        if (lastLabel > start && !allDigits && !char.IsAsciiLetter(input[lastLabel]))
        {
            throw Fail("Illegal character in hostname");
        }

        return p;
    }

    private static void CheckChars(string input, int start, int end, CharClass accepts, string what)
    {
        if (Scan(input, start, end, accepts, escapes: true) < end)
        {
            throw Fail("Illegal character in " + what);
        }
    }

    // The end of the run from start of characters of the class (and of escapes and other characters, when allowed).
    private static int Scan(string input, int start, int end, CharClass accepts, bool escapes)
    {
        int p = start;
        while (p < end)
        {
            char c = input[p];
            if (accepts(c))
            {
                p++;
                continue;
            }

            if (!escapes)
            {
                break;
            }

            if (c == Escape)
            {
                if (p + EscapeLength > end || !char.IsAsciiHexDigit(input[p + 1]) || !char.IsAsciiHexDigit(input[p + 2]))
                {
                    throw Fail("Malformed escape pair");
                }

                p += EscapeLength;
                continue;
            }

            if (c >= FirstOtherCharacter && !IsSpaceChar(c) && !char.IsControl(c))
            {
                p++;
                continue;
            }

            break;
        }

        return p;
    }

    // The JDK's scan(start, end, err, stop): the index of the first stop character, the end when there is none, or -1
    // when an error character comes first.
    private static int ScanUntil(string input, int start, int end, string errors, string stops)
    {
        for (int p = start; p < end; p++)
        {
            char c = input[p];
            if (errors.Contains(c, StringComparison.Ordinal))
            {
                return -1;
            }

            if (stops.Contains(c, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return end;
    }

    private static bool At(string input, int p, int n, char c) => p < n && input[p] == c;

    // Java's Character.isSpaceChar: the space, line and paragraph separators.
    private static bool IsSpaceChar(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static FormatException Fail(string reason) => new(reason);
}
