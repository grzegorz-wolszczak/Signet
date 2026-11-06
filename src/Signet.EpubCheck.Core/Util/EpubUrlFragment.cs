using System;
using System.Text.RegularExpressions;
using System.Xml;
using Signet.EpubCheck.Core.Galimatias;

namespace Signet.EpubCheck.Core.Util;

/// <summary>A URL fragment parsed according to the MIME type of its target resource.</summary>
public sealed class EpubUrlFragment : IEquatable<EpubUrlFragment>
{
    // Upstream's patterns, used with Java's Matcher.matches(): the whole string must match (\A...\z), and \w and \d
    // are ASCII only, as in Java ([A-Za-z0-9_] and [0-9]).
    private static readonly Regex s_schemeBased = new(@"\A([A-Za-z0-9_]+)\(.*\)\z", RegexOptions.CultureInvariant);
    private static readonly Regex s_htmlMedia = new(@"\A(t|xywh|track|id|xyn|xyr)=[^&]+(&[^&=]+=[^&]+)*\z", RegexOptions.CultureInvariant);
    private static readonly Regex s_svgView = new(@"\AsvgView\(.+\)\z", RegexOptions.CultureInvariant);
    private static readonly Regex s_spatial = new(@"\Axywh=(pixel:|percent:)?[0-9]+,[0-9]+,[0-9]+,[0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex s_temporal = new(@"\At=(?:npt:)?(?:([0-9.:]+)(?:,([0-9.:]+))?|,([0-9.:]+))\z", RegexOptions.CultureInvariant);
    private static readonly Regex s_nptTime = new(@"\A(([0-9]+)|([0-5][0-9]:[0-5][0-9])|([0-9]+:[0-5][0-9]:[0-5][0-9]))(\.[0-9]*)?\z", RegexOptions.CultureInvariant);

    private EpubUrlFragment(string? fragment, string id, string scheme, bool isMediaFragment, bool isValid)
    {
        Fragment = fragment;
        Id = id;
        Scheme = scheme;
        IsMediaFragment = isMediaFragment;
        IsValid = isValid;
    }

    /// <summary>A fragment absent from its URL.</summary>
    public static EpubUrlFragment None { get; } = new(null, string.Empty, string.Empty, false, true);

    /// <summary>The fragment without its leading number sign, or <c>null</c> when absent.</summary>
    public string? Fragment { get; }

    /// <summary>The target element identifier, or an empty string.</summary>
    public string Id { get; }

    /// <summary>The fragment scheme, or an empty string.</summary>
    public string Scheme { get; }

    /// <summary>Whether the URL contains a fragment.</summary>
    public bool Exists => Fragment is not null;

    /// <summary>Whether the fragment is absent or empty.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Fragment);

    /// <summary>Whether the fragment is valid for its target MIME type.</summary>
    public bool IsValid { get; }

    /// <summary>Whether the fragment is a media fragment.</summary>
    public bool IsMediaFragment { get; }

    /// <summary>Parses a URL fragment according to a target MIME type.</summary>
    public static EpubUrlFragment Parse(Url? url, string? mimeType = null) =>
        url?.Fragment is null ? None : Parse(url.Fragment, mimeType);

    /// <summary>Parses a fragment without its leading number sign according to a target MIME type.</summary>
    public static EpubUrlFragment Parse(string fragment, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        if (IsSvg(mimeType))
        {
            return ParseSvg(fragment);
        }

        return IsHtml(mimeType) ? ParseHtml(fragment) : ParseXml(fragment);
    }

    /// <inheritdoc />
    public override string? ToString() => Fragment;

    /// <inheritdoc />
    public bool Equals(EpubUrlFragment? other) => other is not null && Fragment == other.Fragment;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EpubUrlFragment);

    /// <inheritdoc />
    public override int GetHashCode() => Fragment is null ? 0 : StringComparer.Ordinal.GetHashCode(Fragment);

    private static EpubUrlFragment ParseXml(string fragment)
    {
        Match scheme = s_schemeBased.Match(fragment);
        if (scheme.Success)
        {
            return new EpubUrlFragment(fragment, string.Empty, scheme.Groups[1].Value, false, true);
        }

        string id = Decode(fragment);
        return new EpubUrlFragment(fragment, id, string.Empty, false, IsNcName(id));
    }

    private static EpubUrlFragment ParseHtml(string fragment)
    {
        int directive = fragment.IndexOf(":~:", StringComparison.Ordinal);
        string primary = directive < 0 ? fragment : fragment[..directive];
        Match scheme = s_schemeBased.Match(primary);
        if (scheme.Success)
        {
            return new EpubUrlFragment(fragment, string.Empty, scheme.Groups[1].Value, false, true);
        }

        if (s_htmlMedia.IsMatch(primary))
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, true, true);
        }

        return new EpubUrlFragment(fragment, Decode(primary), string.Empty, false, true);
    }

    private static EpubUrlFragment ParseSvg(string fragment)
    {
        if (fragment.Length == 0)
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, false, true);
        }

        string[] components = fragment.Split('&');
        string first = components[0];
        if (first.StartsWith("svgView(", StringComparison.Ordinal))
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, false, s_svgView.IsMatch(first) && RemainingAre(components, ParseTimeSegment));
        }

        if (first.StartsWith("t=", StringComparison.Ordinal))
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, true, ParseTimeSegment(first) && RemainingAre(components, ParseSpaceSegment));
        }

        if (first.StartsWith("xywh=", StringComparison.Ordinal))
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, true, ParseSpaceSegment(first) && RemainingAre(components, ParseTimeSegment));
        }

        if (first.Contains('='))
        {
            return new EpubUrlFragment(fragment, string.Empty, string.Empty, false, false);
        }

        string id = Decode(first);
        return new EpubUrlFragment(fragment, id, string.Empty, false, IsNcName(id) && RemainingAre(components, ParseTimeSegment));
    }

    private static bool RemainingAre(string[] components, Func<string, bool> parser)
    {
        for (int index = 1; index < components.Length; index++)
        {
            if (!parser(components[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ParseSpaceSegment(string value) => s_spatial.IsMatch(value);

    private static bool ParseTimeSegment(string value)
    {
        Match match = s_temporal.Match(value);
        if (!match.Success)
        {
            return false;
        }

        for (int index = 1; index < match.Groups.Count; index++)
        {
            if (match.Groups[index].Success && !s_nptTime.IsMatch(match.Groups[index].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHtml(string? mimeType) =>
        string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsSvg(string? mimeType) => string.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);

    // XmlConvert.VerifyNCName throws on an empty name instead of rejecting it.
    private static bool IsNcName(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string Decode(string value)
    {
        return Galimatias.UrlUtils.PercentDecode(value);
    }
}
