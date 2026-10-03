using System;
using System.Collections.Generic;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Updates <c>&lt;a href&gt;</c> references (and the NCX entries <c>content/@src</c>) to fragments
/// (<c>id</c>) after a Split or Merge of HTML files. Unlike <see cref="PerformHtmlUpdates"/>
/// (which recalculates references using a <em>bookpath → bookpath</em> map after a rename/move),
/// this module tracks which file physically contains a given <c>id</c> — because it,
/// not the whole file, is the unit that is moved during a Split/Merge.
/// </summary>
/// <remarks>
/// The NCX updates (<see cref="UpdateTocEntries"/>/
/// <see cref="UpdateTocEntriesAfterMerge"/>) operate natively on an
/// <see cref="NcxDocument"/>.
/// </remarks>
public static class AnchorUpdates
{
    /// <summary>
    /// Builds an <c>id → bookpath</c> map of the file in which a given identifier occurs (the last
    /// occurrence wins on duplicates).
    /// </summary>
    public static Dictionary<string, string> GetIdLocations(IEnumerable<HtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Dictionary<string, string> locations = new(StringComparer.Ordinal);
        foreach (HtmlResource resource in resources)
        {
            string bookPath = resource.BookPath;
            foreach (string id in XhtmlDoc.GetAllDescendantIds(resource.GetText()))
            {
                locations[id] = bookPath;
            }
        }

        return locations;
    }

    /// <summary>
    /// Updates the references between the files from <paramref name="resources"/> so that they point at
    /// the current location of each <c>id</c> (used after a Split — the files came from one, so their
    /// identifiers are by assumption unique within this set).
    /// </summary>
    public static void UpdateAllAnchorsWithIds(IReadOnlyList<HtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Dictionary<string, string> idLocations = GetIdLocations(resources);
        HashSet<string> impactedBookPaths = new(idLocations.Values, StringComparer.Ordinal);

        foreach (HtmlResource resource in resources)
        {
            UpdateAnchorsInOneFile(resource, idLocations, impactedBookPaths);
        }
    }

    /// <summary>
    /// Updates the references in <paramref name="resources"/> that pointed at
    /// <paramref name="originatingBookPath"/> (the file before the split) so that they point at the file from
    /// <paramref name="newFiles"/> that actually contains the target fragment.
    /// </summary>
    public static void UpdateExternalAnchors(
        IReadOnlyList<HtmlResource> resources, string originatingBookPath, IReadOnlyList<HtmlResource> newFiles)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(originatingBookPath);
        ArgumentNullException.ThrowIfNull(newFiles);
        Dictionary<string, string> idLocations = GetIdLocations(newFiles);

        foreach (HtmlResource resource in resources)
        {
            UpdateExternalAnchorsInOneFile(resource, originatingBookPath, idLocations);
        }
    }

    /// <summary>
    /// After a merge: updates in <paramref name="resources"/> (files outside the merge) the references
    /// that pointed at any of <paramref name="originatingBookPaths"/> so that they point
    /// at <paramref name="sinkResource"/> (using <paramref name="sectionIdMap"/> when the link pointed
    /// at the start of the merged file with no fragment).
    /// </summary>
    public static void UpdateAllAnchors(
        IReadOnlyList<HtmlResource> resources,
        IReadOnlyList<string> originatingBookPaths,
        HtmlResource sinkResource,
        IReadOnlyDictionary<string, string> sectionIdMap)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(originatingBookPaths);
        ArgumentNullException.ThrowIfNull(sinkResource);
        ArgumentNullException.ThrowIfNull(sectionIdMap);
        HashSet<string> originating = new(originatingBookPaths, StringComparer.Ordinal);
        string sinkBookPath = sinkResource.BookPath;

        foreach (HtmlResource resource in resources)
        {
            UpdateAllAnchorsInOneFile(resource, originating, sinkBookPath, sectionIdMap);
        }
    }

    /// <summary>
    /// For Merge: in the content of <paramref name="resource"/> recalculates the references to files from
    /// <paramref name="mergedBookPaths"/> into local ones (just <c>#fragment</c> — after the merge everything
    /// will be in one file) and returns the inner <c>&lt;body&gt;</c> content (without the
    /// <c>&lt;body&gt;</c>/<c>&lt;/body&gt;</c> tags).
    /// </summary>
    public static string LocalizeAnchorsAndExtractBody(HtmlResource resource, IReadOnlyList<string> mergedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(mergedBookPaths);
        string startDir = CoreBookPath.StartingDir(resource.BookPath);
        HashSet<string> merged = new(mergedBookPaths, StringComparer.Ordinal);
        IDocument document = ParseForEditing(resource.GetText());

        foreach (IElement anchor in document.QuerySelectorAll("a"))
        {
            string? href = anchor.GetAttribute("href");
            if (string.IsNullOrEmpty(href) || href.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            (string basePath, string fragment) = SplitFragment(href);
            string targetBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(basePath), startDir);
            if (merged.Contains(targetBookPath))
            {
                anchor.SetAttribute("href", "#" + Utility.UrlEncodePath(Utility.UrlDecodePath(fragment)));
            }
        }

        IElement? body = document.QuerySelector("body");
        if (body is null)
        {
            return string.Empty;
        }

        XhtmlMarkupFormatter formatter = new(emptyTagsToSelfClosing: false);
        StringBuilder builder = new();
        foreach (INode child in body.ChildNodes)
        {
            builder.Append(child.ToHtml(formatter));
        }

        return builder.ToString();
    }

    /// <summary>After a split: updates the <c>content/@src</c> entries in the NCX that pointed at <paramref name="originatingBookPath"/>.</summary>
    public static void UpdateTocEntries(NcxResource ncx, string originatingBookPath, IReadOnlyList<HtmlResource> newFiles)
    {
        ArgumentNullException.ThrowIfNull(ncx);
        ArgumentNullException.ThrowIfNull(originatingBookPath);
        ArgumentNullException.ThrowIfNull(newFiles);
        Dictionary<string, string> idLocations = GetIdLocations(newFiles);
        string ncxBookPath = ncx.BookPath;
        string startDir = CoreBookPath.StartingDir(ncxBookPath);

        UpdateNcxContentSources(ncx, src =>
        {
            (string apath, string fragment) = SplitFragment(src);
            if (fragment.Length == 0)
            {
                return null;
            }

            string targetBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(apath), startDir);
            if (targetBookPath != originatingBookPath)
            {
                return null;
            }

            string fragmentId = Utility.UrlDecodePath(fragment);
            if (!idLocations.TryGetValue(fragmentId, out string? newTargetBookPath))
            {
                return null;
            }

            return BuildNcxSrc(ncxBookPath, newTargetBookPath, fragmentId);
        });
    }

    /// <summary>After a merge: updates the <c>content/@src</c> entries in the NCX that pointed at any of the merged files.</summary>
    public static void UpdateTocEntriesAfterMerge(NcxResource ncx, string sinkBookPath, IReadOnlyList<string> mergedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(ncx);
        ArgumentNullException.ThrowIfNull(sinkBookPath);
        ArgumentNullException.ThrowIfNull(mergedBookPaths);
        HashSet<string> merged = new(mergedBookPaths, StringComparer.Ordinal);
        string ncxBookPath = ncx.BookPath;
        string startDir = CoreBookPath.StartingDir(ncxBookPath);

        UpdateNcxContentSources(ncx, src =>
        {
            (string apath, string fragment) = SplitFragment(src);
            string targetBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(apath), startDir);
            if (!merged.Contains(targetBookPath))
            {
                return null;
            }

            return fragment.Length > 0
                ? BuildNcxSrc(ncxBookPath, sinkBookPath, Utility.UrlDecodePath(fragment))
                : Utility.UrlEncodePath(CoreBookPath.Relative(ncxBookPath, sinkBookPath));
        });
    }

    // ------------------------------------------------------------------
    // Implementation
    // ------------------------------------------------------------------

    private static void UpdateAnchorsInOneFile(
        HtmlResource resource, Dictionary<string, string> idLocations, HashSet<string> impactedBookPaths)
    {
        string resourceBookPath = resource.BookPath;
        bool changed = false;
        IDocument document = ParseForEditing(resource.GetText());

        foreach (IElement anchor in document.QuerySelectorAll("a"))
        {
            string? href = anchor.GetAttribute("href");
            if (string.IsNullOrEmpty(href) || href.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            (string basePath, string fragment) = SplitFragment(href);
            if (fragment.Length == 0)
            {
                continue;
            }

            string destBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(basePath), CoreBookPath.StartingDir(resourceBookPath));
            string fileId = idLocations.GetValueOrDefault(fragment, string.Empty);

            // Duplicates of this id may exist in other files outside this set — they are taken into
            // account only when the original target was covered by the operation.
            if (basePath.Length > 0 && !impactedBookPaths.Contains(destBookPath))
            {
                fileId = string.Empty;
            }

            if (fileId.Length > 0 && !string.Equals(fileId, resourceBookPath, StringComparison.Ordinal))
            {
                anchor.SetAttribute("href", BuildHref(resourceBookPath, fileId, fragment));
                changed = true;
            }
            else if (string.Equals(fileId, resourceBookPath, StringComparison.Ordinal) && basePath.Length > 0)
            {
                anchor.SetAttribute("href", "#" + Utility.UrlEncodePath(fragment));
                changed = true;
            }
        }

        if (changed)
        {
            resource.SetText(CleanSource.ReserializeXhtmlDocument(document, resource.EpubVersion));
        }
    }

    private static void UpdateExternalAnchorsInOneFile(
        HtmlResource resource, string originatingBookPath, Dictionary<string, string> idLocations)
    {
        string resourceBookPath = resource.BookPath;
        string startDir = CoreBookPath.StartingDir(resourceBookPath);
        bool changed = false;
        IDocument document = ParseForEditing(resource.GetText());

        foreach (IElement anchor in document.QuerySelectorAll("a"))
        {
            string? href = anchor.GetAttribute("href");
            if (string.IsNullOrEmpty(href) || href.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            (string basePath, string fragment) = SplitFragment(href);
            if (fragment.Length == 0)
            {
                continue;
            }

            string targetBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(basePath), startDir);
            if (!string.Equals(targetBookPath, originatingBookPath, StringComparison.Ordinal))
            {
                continue;
            }

            string fragmentId = Utility.UrlDecodePath(fragment);
            if (!idLocations.TryGetValue(fragmentId, out string? newTargetBookPath))
            {
                continue;
            }

            anchor.SetAttribute("href", BuildHref(resourceBookPath, newTargetBookPath, fragmentId));
            changed = true;
        }

        if (changed)
        {
            resource.SetText(CleanSource.ReserializeXhtmlDocument(document, resource.EpubVersion));
        }
    }

    private static void UpdateAllAnchorsInOneFile(
        HtmlResource resource,
        HashSet<string> originatingBookPaths,
        string sinkBookPath,
        IReadOnlyDictionary<string, string> sectionIdMap)
    {
        string resourceBookPath = resource.BookPath;
        string startDir = CoreBookPath.StartingDir(resourceBookPath);
        bool changed = false;
        IDocument document = ParseForEditing(resource.GetText());

        foreach (IElement anchor in document.QuerySelectorAll("a"))
        {
            string? href = anchor.GetAttribute("href");
            if (string.IsNullOrEmpty(href) || href.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            (string basePath, string fragment) = SplitFragment(href);
            string targetBookPath = CoreBookPath.BuildBookPath(Utility.UrlDecodePath(basePath), startDir);
            if (!originatingBookPaths.Contains(targetBookPath))
            {
                continue;
            }

            if (fragment.Length == 0 && sectionIdMap.TryGetValue(targetBookPath, out string? sectionId))
            {
                fragment = sectionId;
            }

            string relative = Utility.UrlEncodePath(CoreBookPath.Relative(resourceBookPath, sinkBookPath));
            anchor.SetAttribute("href", fragment.Length > 0 ? relative + "#" + Utility.UrlEncodePath(fragment) : relative);
            changed = true;
        }

        if (changed)
        {
            resource.SetText(CleanSource.ReserializeXhtmlDocument(document, resource.EpubVersion));
        }
    }

    private static bool UpdateNcxContentSources(NcxResource ncx, Func<string, string?> rewrite)
    {
        NcxDocument document = ncx.GetNcxDocument();
        bool changed = false;

        void UpdateSrc(Func<string> get, Action<string> set)
        {
            string value = get();
            if (value.Length == 0)
            {
                return;
            }

            string? updated = rewrite(value);
            if (updated is not null && !string.Equals(updated, value, StringComparison.Ordinal))
            {
                set(updated);
                changed = true;
            }
        }

        void WalkNavPoint(NcxNavPoint navPoint)
        {
            UpdateSrc(() => navPoint.ContentSrc, v => navPoint.ContentSrc = v);
            foreach (NcxNavPoint child in navPoint.Children)
            {
                WalkNavPoint(child);
            }
        }

        foreach (NcxNavPoint navPoint in document.NavMap)
        {
            WalkNavPoint(navPoint);
        }

        foreach (NcxPageTarget target in document.PageList)
        {
            UpdateSrc(() => target.ContentSrc, v => target.ContentSrc = v);
        }

        foreach (NcxNavList navList in document.NavLists)
        {
            foreach (NcxNavTarget target in navList.Targets)
            {
                UpdateSrc(() => target.ContentSrc, v => target.ContentSrc = v);
            }
        }

        if (changed)
        {
            ncx.SetNcxDocument(document);
        }

        return changed;
    }

    private static string BuildNcxSrc(string ncxBookPath, string targetBookPath, string fragmentId) =>
        Utility.UrlEncodePath(CoreBookPath.Relative(ncxBookPath, targetBookPath)) + "#" + Utility.UrlEncodePath(fragmentId);

    private static string BuildHref(string sourceBookPath, string targetBookPath, string fragment) =>
        Utility.UrlEncodePath(CoreBookPath.Relative(sourceBookPath, targetBookPath)) + "#" + Utility.UrlEncodePath(fragment);

    private static (string Path, string Fragment) SplitFragment(string value)
    {
        int hash = value.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (value, string.Empty) : (value[..hash], value[(hash + 1)..]);
    }

    private static IDocument ParseForEditing(string source)
    {
        string stripped = CleanSource.StripXmlDeclaration(source);
        HtmlParser parser = new(new HtmlParserOptions { IsKeepingSourceReferences = true });
        return parser.ParseDocument(stripped);
    }
}
