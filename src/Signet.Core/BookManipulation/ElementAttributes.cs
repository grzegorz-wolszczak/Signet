using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The attributes of an opening tag in the source text, for deciding whether two elements are "the same": the same
/// set of attributes and values, attribute order ignored, the classes of <c>class</c> compared as a set; any other
/// value (<c>style</c> included) must be identical.
/// </summary>
internal static class ElementAttributes
{
    private static readonly Regex AttributeRegex = new(
        @"(?<name>[^\s=/>""']+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>""']+)))?",
        RegexOptions.Compiled);

    private static readonly SearchValues<char> TagNameEnd = SearchValues.Create(" \t\r\n\f>/");

    /// <summary>The attributes of <paramref name="openTag"/> (<c>&lt;name …&gt;</c>), <c>class</c> normalized to a sorted set.</summary>
    public static Dictionary<string, string> Parse(string openTag)
    {
        ArgumentNullException.ThrowIfNull(openTag);

        // Skip "<name" — the rest up to ">" are the attributes.
        int start = openTag.AsSpan().IndexOfAny(TagNameEnd);
        string body = start < 0 ? string.Empty : openTag[start..].TrimEnd('>').TrimEnd('/');
        Dictionary<string, string> attributes = new(StringComparer.Ordinal);
        foreach (Match match in AttributeRegex.Matches(body))
        {
            string name = match.Groups["name"].Value;
            string value = match.Groups["value"].Success ? match.Groups["value"].Value : string.Empty;
            attributes[name] = name == "class" ? NormalizeClasses(value) : value;
        }

        return attributes;
    }

    /// <summary>Whether both attribute sets (from <see cref="Parse"/>) are the same.</summary>
    public static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Count == b.Count
            && a.All(kv => b.TryGetValue(kv.Key, out string? value) && string.Equals(value, kv.Value, StringComparison.Ordinal));
    }

    private static string NormalizeClasses(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
}
