using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Galimatias;

/// <summary>
/// A parsed URL (galimatias' <c>URL</c>): hierarchical for the relative schemes (ftp, file, gopher, http, https, ws,
/// wss), with user info, host, port and path; opaque for the others, with scheme data. Immutable.
/// </summary>
public sealed class Url : IEquatable<Url>
{
    /// <summary>The <c>file</c> scheme.</summary>
    public const string FileScheme = "file";

    private Url(string scheme, string? schemeData, string? username, string? password, Host? host, int port, string? path,
        string? query, string? fragment, bool isHierarchical)
    {
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme), "scheme cannot be null");
        SchemeData = schemeData ?? string.Empty;
        if (isHierarchical)
        {
            Username = username ?? string.Empty;
            Password = password;
            Host = host;
            _port = port == DefaultPortOf(scheme) ? -1 : port;
            Path = path;
        }
        else
        {
            Username = string.Empty;
            _port = -1;
        }

        Query = query;
        Fragment = fragment;
        IsHierarchical = isHierarchical;
    }

    internal Url(string scheme, string schemeData, string? username, string? password, Host? host, int port,
        IEnumerable<string>? pathSegments, string? query, string? fragment, bool isHierarchical)
        : this(scheme, schemeData, username, password, host, port, SegmentsToPath(pathSegments), query, fragment, isHierarchical)
    {
    }

    private readonly int _port;

    /// <summary>The scheme, lower case.</summary>
    public string Scheme { get; }

    /// <summary>What follows the scheme of an opaque URL (empty for a hierarchical one).</summary>
    public string SchemeData { get; }

    /// <summary>The user name (empty when there is none).</summary>
    public string Username { get; }

    /// <summary>The password, or <c>null</c>.</summary>
    public string? Password { get; }

    /// <summary>The user name, and <c>:password</c> when there is one.</summary>
    public string UserInfo => Password is null ? Username : Username + ":" + Password;

    /// <summary>The host, or <c>null</c>.</summary>
    public Host? Host { get; }

    /// <summary>The port: the given one, or the default port of the scheme (-1 when it has none).</summary>
    public int Port => _port == -1 ? DefaultPort : _port;

    /// <summary>The default port of the scheme, or -1.</summary>
    public int DefaultPort => DefaultPortOf(Scheme);

    /// <summary>The path of a hierarchical URL (starts with <c>/</c>), or <c>null</c>.</summary>
    public string? Path { get; }

    /// <summary>The segments of the path (a new list each time), or <c>null</c> for an opaque URL.</summary>
    public List<string>? PathSegments => IsHierarchical ? PathToSegments(Path) : null;

    /// <summary>The query, without its <c>?</c>, or <c>null</c>.</summary>
    public string? Query { get; }

    /// <summary>The fragment, without its <c>#</c>, or <c>null</c>.</summary>
    public string? Fragment { get; }

    /// <summary>Whether the URL has an authority and a path (a relative scheme).</summary>
    public bool IsHierarchical { get; }

    /// <summary>Whether the URL has scheme data instead of an authority and a path.</summary>
    public bool IsOpaque => !IsHierarchical;

    /// <summary>The user info, host and port of a hierarchical URL with a host, or <c>null</c>.</summary>
    public string? Authority
    {
        get
        {
            if (!IsHierarchical || Host is null)
            {
                return null;
            }

            StringBuilder output = new();
            if (UserInfo.Length != 0)
            {
                output.Append(UserInfo).Append('@');
            }

            output.Append(Host);
            if (_port != -1)
            {
                output.Append(':').Append(_port.ToString(CultureInfo.InvariantCulture));
            }

            return output.ToString();
        }
    }

    /// <summary>Parses an absolute URL.</summary>
    /// <exception cref="GalimatiasParseException">The input is not a URL.</exception>
    public static Url Parse(string input, IGalimatiasErrorHandler? errorHandler = null) => new UrlParser(null, input, errorHandler: errorHandler).Parse();

    /// <summary>Parses <paramref name="input"/> against <paramref name="baseUrl"/>.</summary>
    /// <exception cref="GalimatiasParseException">The input is not a URL.</exception>
    public static Url Parse(Url? baseUrl, string input, IGalimatiasErrorHandler? errorHandler = null) => new UrlParser(baseUrl, input, errorHandler: errorHandler).Parse();

    /// <summary>Like <see cref="Parse(Url?, string, IGalimatiasErrorHandler?)"/>, without the exception.</summary>
    public static bool TryParse(Url? baseUrl, string input, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Url? url)
    {
        try
        {
            url = Parse(baseUrl, input);
            return true;
        }
        catch (GalimatiasParseException)
        {
            url = null;
            return false;
        }
    }

    /// <summary>Resolves <paramref name="input"/> against this URL.</summary>
    /// <exception cref="GalimatiasParseException">The input is not a URL.</exception>
    public Url Resolve(string input) => Parse(this, input);

    /// <summary>
    /// <paramref name="url"/> relative to this URL when it is under it (same scheme and authority, a path under this
    /// one's directory or this path); otherwise its whole serialization.
    /// </summary>
    public string Relativize(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (IsOpaque || url.IsOpaque || Scheme != url.Scheme || (Authority is null) != (url.Authority is null)
            || (Authority is not null && Authority != url.Authority))
        {
            return url.ToString();
        }

        string path = Path!;
        string otherPath = url.Path!;
        string prefixPath = path.EndsWith('/') ? path : path + "/";
        if (!otherPath.StartsWith(prefixPath, StringComparison.Ordinal) && path != otherPath)
        {
            return url.ToString();
        }

        StringBuilder output = new();
        if (path != otherPath)
        {
            output.Append(otherPath, prefixPath.Length, otherPath.Length - prefixPath.Length);
        }

        if (url.Query is not null)
        {
            output.Append('?').Append(url.Query);
        }

        if (url.Fragment is not null)
        {
            output.Append('#').Append(url.Fragment);
        }

        return output.ToString();
    }

    /// <summary>This URL with the user name (percent-encoded).</summary>
    /// <exception cref="GalimatiasParseException">The URL is opaque.</exception>
    public Url WithUsername(string? username)
    {
        RequireHierarchical("username");
        string newUsername = username is null ? string.Empty : new UrlParser(null, username).ParseUsername();
        return Username == newUsername ? this : new Url(Scheme, SchemeData, newUsername, Password, Host, _port, Path, Query, Fragment, true);
    }

    /// <summary>This URL with the password (percent-encoded; none when null or empty).</summary>
    /// <exception cref="GalimatiasParseException">The URL is opaque.</exception>
    public Url WithPassword(string? password)
    {
        RequireHierarchical("password");
        if (Password is not null && Password == password)
        {
            return this;
        }

        string? newPassword = string.IsNullOrEmpty(password) ? null : new UrlParser(null, password).ParsePassword();
        return new Url(Scheme, SchemeData, Username, newPassword, Host, _port, Path, Query, Fragment, true);
    }

    /// <summary>This URL with the path, parsed as from the path start state.</summary>
    /// <exception cref="GalimatiasParseException">The URL is opaque, or the path is not valid.</exception>
    public Url WithPath(string path)
    {
        RequireHierarchical("path");
        return new UrlParser(null, path, this, ParseUrlState.RelativePathStart).Parse();
    }

    /// <summary>This URL with the query (a leading <c>?</c> is dropped; <c>null</c> removes it).</summary>
    public Url WithQuery(string? query)
    {
        if (Query == query)
        {
            return this;
        }

        if (string.IsNullOrEmpty(query))
        {
            return new Url(Scheme, SchemeData, Username, Password, Host, _port, Path, query, Fragment, IsHierarchical);
        }

        return new UrlParser(null, query[0] == '?' ? query[1..] : query, this, ParseUrlState.Query).Parse();
    }

    /// <summary>This URL with the fragment (a leading <c>#</c> is dropped; <c>null</c> removes it).</summary>
    public Url WithFragment(string? fragment)
    {
        if (Fragment == fragment)
        {
            return this;
        }

        if (string.IsNullOrEmpty(fragment))
        {
            return new Url(Scheme, SchemeData, Username, Password, Host, _port, Path, Query, fragment, IsHierarchical);
        }

        return new UrlParser(null, fragment[0] == '#' ? fragment[1..] : fragment, this, ParseUrlState.Fragment).Parse();
    }

    /// <summary>The serialization of the URL.</summary>
    public override string ToString() => Serialize(human: false);

    /// <summary>The URL as a person reads it: percent-decoded, the domain in Unicode.</summary>
    public string ToHumanString() => Serialize(human: true);

    /// <inheritdoc />
    public bool Equals(Url? other) =>
        other is not null
        && IsHierarchical == other.IsHierarchical
        && Scheme == other.Scheme
        && SchemeData == other.SchemeData
        && Username == other.Username
        && Password == other.Password
        && Equals(Host, other.Host)
        && _port == other._port
        && Path == other.Path
        && Fragment == other.Fragment
        && Query == other.Query;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Url);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Scheme, SchemeData, Username, Password, Host, _port, Path, HashCode.Combine(Query, Fragment, IsHierarchical));

    private static int DefaultPortOf(string scheme) => UrlUtils.GetDefaultPortForScheme(scheme) ?? -1;

    private static string? SegmentsToPath(IEnumerable<string>? segments)
    {
        if (segments is null)
        {
            return null;
        }

        string path = string.Concat(segments.Select(segment => "/" + segment));
        return path.Length == 0 ? "/" : path;
    }

    private static List<string> PathToSegments(string? path)
    {
        if (path is null)
        {
            return [];
        }

        return [.. (path.StartsWith('/') ? path[1..] : path).Split('/')];
    }

    private void RequireHierarchical(string part)
    {
        if (!IsHierarchical)
        {
            throw new GalimatiasParseException("Cannot set " + part + " on opaque URL");
        }
    }

    private string Serialize(bool human)
    {
        string Text(string value) => human ? UrlUtils.PercentDecode(value) : value;
        StringBuilder output = new();
        output.Append(Scheme).Append(':');
        if (IsHierarchical)
        {
            output.Append("//");
            if (UserInfo.Length != 0)
            {
                output.Append(Text(UserInfo)).Append('@');
            }

            if (Host is IPv6Address)
            {
                output.Append('[').Append(Host).Append(']');
            }
            else if (Host is not null)
            {
                output.Append(human ? Host.ToHumanString() : Host.ToString());
            }

            if (_port != -1)
            {
                output.Append(':').Append(_port.ToString(CultureInfo.InvariantCulture));
            }

            if (Path is not null)
            {
                output.Append(Text(Path));
            }
        }
        else
        {
            output.Append(Text(SchemeData));
        }

        if (Query is not null)
        {
            output.Append('?').Append(Text(Query));
        }

        if (Fragment is not null)
        {
            output.Append('#').Append(Text(Fragment));
        }

        return output.ToString();
    }
}
