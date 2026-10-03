using System;
using System.Text.RegularExpressions;

namespace Signet.Core.Misc;

/// <summary>
/// Resolves <c>href</c>/<c>src</c>/<c>url()</c> references to book files — used to highlight broken
/// links and for Ctrl+click navigation in Code View.
/// </summary>
public static partial class LinkReference
{
    /// <summary>
    /// Whether the reference is external: it starts with a scheme (<c>http:</c>, <c>mailto:</c>,
    /// <c>data:</c>…) or with <c>//</c>. A bare fragment (<c>#id</c>) is not
    /// external.
    /// </summary>
    public static bool IsExternal(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.StartsWith('#'))
        {
            return false;
        }

        return reference.StartsWith("//", StringComparison.Ordinal) || SchemeRegex().IsMatch(reference);
    }

    /// <summary>
    /// Splits a reference into the path (without the <c>?…</c> query, <c>%XX</c>-decoded) and the
    /// fragment (without <c>#</c>, decoded).
    /// </summary>
    public static (string Path, string Fragment) Split(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        int hash = reference.IndexOf('#', StringComparison.Ordinal);
        string path = hash >= 0 ? reference[..hash] : reference;
        string fragment = hash >= 0 ? reference[(hash + 1)..] : string.Empty;
        int query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        return (Decode(path.Trim()), Decode(fragment.Trim()));
    }

    /// <summary>
    /// Bookpath of the file the reference points to, as seen from a file in the
    /// <paramref name="ownerFolder"/> folder, or <c>null</c> when the reference is external or only
    /// points to a fragment of the current file.
    /// </summary>
    public static string? ResolveBookPath(string reference, string ownerFolder)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(ownerFolder);
        if (IsExternal(reference))
        {
            return null;
        }

        (string path, _) = Split(reference);
        return path.Length == 0 ? null : BookPath.BuildBookPath(path, ownerFolder);
    }

    /// <summary>
    /// Whether the reference target exists: always <c>true</c> for external links and bare fragments
    /// (fragments are not verified); otherwise the result of <paramref name="bookPathExists"/> for the
    /// resolved bookpath.
    /// </summary>
    public static bool TargetExists(string reference, string ownerFolder, Func<string, bool> bookPathExists)
    {
        ArgumentNullException.ThrowIfNull(bookPathExists);
        string? bookPath = ResolveBookPath(reference, ownerFolder);
        return bookPath is null || bookPathExists(bookPath);
    }

    /// <summary>
    /// Offset of the start of the tag whose <c>id</c> or <c>name</c> equals <paramref name="fragment"/>
    /// in (X)HTML text — the jump target for <c>#fragment</c>. <c>-1</c> when there is no such tag.
    /// </summary>
    public static int FindAnchorOffset(string text, string fragment)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Length == 0)
        {
            return -1;
        }

        Match m = Regex.Match(
            text,
            @"<[A-Za-z][^>]*?\s(?:id|name)\s*=\s*([""'])" + Regex.Escape(fragment) + @"\1",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return m.Success ? m.Index : -1;
    }

    // URI scheme (RFC 3986): a letter, then letters/digits/+/-/. and a colon.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemeRegex();

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
