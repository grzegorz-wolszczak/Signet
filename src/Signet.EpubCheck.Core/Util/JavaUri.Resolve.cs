using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Core.Util;

/// <summary>The reference resolution of <c>java.net.URI</c> (RFC 2396, section 5.2, as the JDK does it).</summary>
public static partial class JavaUri
{
    private const char PathSeparator = '/';
    private const string CurrentSegment = ".";
    private const string ParentSegment = "..";

    /// <summary>
    /// <c>new URI(baseUri).resolve(new URI(reference)).toString()</c>: <paramref name="reference"/> resolved against
    /// <paramref name="baseUri"/>. An opaque URI on either side gives the reference back unchanged; the merged path is
    /// normalized as the JDK's <c>URI.normalize()</c> does it (no <c>.</c> segments, no <c>segment/..</c> pairs, no
    /// redundant slashes; leading <c>..</c> segments stay).
    /// </summary>
    /// <exception cref="FormatException">One of the two is not a <c>java.net.URI</c>.</exception>
    public static string Resolve(string baseUri, string reference)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(reference);
        Parse(baseUri);
        Parse(reference);
        UriParts parent = UriParts.Split(baseUri);
        UriParts child = UriParts.Split(reference);
        if (child.IsOpaque || parent.IsOpaque)
        {
            return reference;
        }

        // 5.2 (2): a reference to the current document (a lone fragment).
        if (child.Scheme is null && child.Authority is null && child.Path.Length == 0 && child.Fragment is not null
            && child.Query is null)
        {
            if (parent.Fragment is not null && string.Equals(child.Fragment, parent.Fragment, StringComparison.Ordinal))
            {
                return baseUri;
            }

            return new UriParts(parent.Scheme, parent.Authority, parent.Path, parent.Query, child.Fragment).ToString();
        }

        // 5.2 (3): the reference is absolute.
        if (child.Scheme is not null)
        {
            return reference;
        }

        // 5.2 (4): the authority.
        if (child.Authority is not null)
        {
            return new UriParts(parent.Scheme, child.Authority, child.Path, child.Query, child.Fragment).ToString();
        }

        // 5.2 (5) and (6): an absolute path, or a relative one merged with the base path.
        string path = child.Path.Length > 0 && child.Path[0] == PathSeparator
            ? child.Path
            : ResolvePath(parent.Path, child.Path);
        return new UriParts(parent.Scheme, parent.Authority, path, child.Query, child.Fragment).ToString();
    }

    private static string ResolvePath(string basePath, string childPath)
    {
        int i = basePath.LastIndexOf(PathSeparator);
        string directory = i >= 0 ? basePath[..(i + 1)] : string.Empty;
        return Normalize(directory + childPath);
    }

    // URI.normalize() of the JDK.
    private static string Normalize(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }

        bool absolute = path[0] == PathSeparator;
        bool trailingSeparator = path[^1] == PathSeparator;
        string[] segments = path.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        List<string> kept = [];
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool last = i == segments.Length - 1;
            if (segment == CurrentSegment)
            {
                trailingSeparator |= last;
                continue;
            }

            if (segment == ParentSegment && kept.Count > 0 && kept[^1] != ParentSegment)
            {
                kept.RemoveAt(kept.Count - 1);
                trailingSeparator |= last;
                continue;
            }

            kept.Add(segment);
        }

        StringBuilder result = new();
        if (absolute)
        {
            result.Append(PathSeparator);
        }
        else if (kept.Count > 0 && kept[0].Contains(':', StringComparison.Ordinal))
        {
            // A first segment with a colon would read as a scheme: the JDK puts "./" before it.
            result.Append('.').Append(PathSeparator);
        }

        result.AppendJoin(PathSeparator, kept);
        if (trailingSeparator && kept.Count > 0)
        {
            result.Append(PathSeparator);
        }

        return result.ToString();
    }

    // The components of a URI that has already been checked by Parse (RFC 2396, appendix B).
    private sealed partial record UriParts(string? Scheme, string? Authority, string Path, string? Query, string? Fragment)
    {
        private const int SchemeGroup = 2;
        private const int AuthorityGroup = 4;
        private const int PathGroup = 5;
        private const int QueryGroup = 7;
        private const int FragmentGroup = 9;

        // An opaque URI has a scheme and a scheme-specific part that does not start with a slash (mailto:x).
        public bool IsOpaque => Scheme is not null && Authority is null && (Path.Length == 0 || Path[0] != PathSeparator);

        public static UriParts Split(string uri)
        {
            Match m = Rfc2396Pattern().Match(uri);
            return new UriParts(
                Group(m, SchemeGroup),
                Group(m, AuthorityGroup),
                m.Groups[PathGroup].Value,
                Group(m, QueryGroup),
                Group(m, FragmentGroup));
        }

        public override string ToString()
        {
            StringBuilder s = new();
            if (Scheme is not null)
            {
                s.Append(Scheme).Append(':');
            }

            if (Authority is not null)
            {
                s.Append("//").Append(Authority);
            }

            s.Append(Path);
            if (Query is not null)
            {
                s.Append('?').Append(Query);
            }

            if (Fragment is not null)
            {
                s.Append('#').Append(Fragment);
            }

            return s.ToString();
        }

        private static string? Group(Match m, int group) => m.Groups[group].Success ? m.Groups[group].Value : null;

        [GeneratedRegex(@"^(([^:/?#]+):)?(//([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?$", RegexOptions.Singleline)]
        private static partial Regex Rfc2396Pattern();
    }
}
