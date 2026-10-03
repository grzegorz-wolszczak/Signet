using System;
using Signet.Core.Resources;

namespace Signet.Core.Toc;

/// <summary>
/// Conversion of the <c>navMap</c> of an NCX resource to an editable <see cref="TocEntry"/> tree and back
/// (writing goes through <see cref="NcxGenerator.GenerateFromTocEntries"/>). <c>content/@src</c>
/// (relative to the NCX) is resolved to a URL-encoded bookpath (+ an optional <c>#fragment</c>).
/// </summary>
public static class NcxTocEntries
{
    /// <summary>
    /// Builds the root of the entry tree from the <c>navMap</c> of the given NCX. The <see cref="TocEntry.Target"/>
    /// of every entry is an absolute, URL-encoded bookpath with an optional fragment.
    /// </summary>
    public static TocEntry GetRootTocEntry(NcxResource ncx)
    {
        ArgumentNullException.ThrowIfNull(ncx);

        TocEntry root = new() { IsRoot = true };
        NcxDocument document = ncx.GetNcxDocument();
        string ncxFolder = Core.BookPath.StartingDir(ncx.BookPath);
        string ncxBookPath = ncx.BookPath;

        foreach (NcxNavPoint navPoint in document.NavMap)
        {
            root.Children.Add(ToTocEntry(navPoint, ncxFolder, ncxBookPath));
        }

        return root;
    }

    private static TocEntry ToTocEntry(NcxNavPoint navPoint, string ncxFolder, string ncxBookPath)
    {
        TocEntry entry = new()
        {
            Text = navPoint.Label,
            Target = ConvertSrcToBookPath(navPoint.ContentSrc, ncxFolder, ncxBookPath),
        };

        foreach (NcxNavPoint child in navPoint.Children)
        {
            entry.Children.Add(ToTocEntry(child, ncxFolder, ncxBookPath));
        }

        return entry;
    }

    // Works on URL-encoded hrefs, returns URL-encoded.
    private static string ConvertSrcToBookPath(string src, string ncxFolder, string ncxBookPath)
    {
        if (src.Contains(':', StringComparison.Ordinal))
        {
            return src;
        }

        int hash = src.IndexOf('#', StringComparison.Ordinal);
        string basePart = hash < 0 ? src : src[..hash];
        string fragment = hash < 0 ? string.Empty : src[(hash + 1)..];

        string bookPath = basePart is "./" or ""
            ? Utility.UrlEncodePath(ncxBookPath)
            : Utility.UrlEncodePath(Core.BookPath.BuildBookPath(Utility.UrlDecodePath(basePart), ncxFolder));

        return fragment.Length > 0 ? bookPath + "#" + fragment : bookPath;
    }
}
