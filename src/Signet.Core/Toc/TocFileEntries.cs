using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Toc;

/// <summary>
/// Table of contents entries that point to whole files, kept in step in both TOC sources: the
/// <c>nav[epub:type=toc]</c> section (EPUB 3) and the <c>navMap</c> of the NCX. Unlike
/// <see cref="TocEditModel.Save"/>, the NCX is edited in place, so its <c>pageList</c> and head are kept.
/// </summary>
public static class TocFileEntries
{
    /// <summary>The <c>id</c> of a <c>navPoint</c> added by <see cref="SetFileEntry"/>.</summary>
    private const string NavPointIdPrefix = "navPoint-signet-";

    /// <summary>
    /// Removes the entries that point to any of the given files (with or without a fragment). The children of a removed
    /// entry take its place, so entries pointing to other files are not lost. Returns <c>true</c> if a TOC changed.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="bookPaths">The (decoded) bookpaths of the files.</param>
    public static bool RemoveEntriesForFiles(Book book, IEnumerable<string> bookPaths)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(bookPaths);
        HashSet<string> paths = new(bookPaths, StringComparer.Ordinal);
        if (paths.Count == 0)
        {
            return false;
        }

        bool changed = EditNav(book, entries => RemoveTocEntries(entries, paths));
        changed |= EditNcx(book, (navMap, resolve) => RemoveNavPoints(navMap, paths, resolve));
        return changed;
    }

    /// <summary>
    /// Makes <paramref name="file"/> have exactly one top-level entry with the given title, placed by the reading
    /// order: before the first top-level entry whose file comes later in the spine. Other entries pointing to the file
    /// are removed. Returns <c>true</c> if a TOC changed.
    /// </summary>
    public static bool SetFileEntry(Book book, Resource file, string title)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(title);

        HashSet<string> paths = new(StringComparer.Ordinal) { file.BookPath };
        IReadOnlyList<string> spine = book.GetOpf().GetSpineOrderBookPaths();
        List<string> spineList = spine.ToList();
        int SpineIndex(string path) => spineList.IndexOf(path);
        int fileIndex = SpineIndex(file.BookPath);

        bool changed = EditNav(book, entries =>
        {
            RemoveTocEntries(entries, paths);
            TocEntry entry = new() { Text = title, Target = Utility.UrlEncodePath(file.BookPath) };
            entries.Insert(InsertionIndex(entries.Select(e => PathOf(e.Target)).ToList(), fileIndex, SpineIndex), entry);
        });

        changed |= EditNcx(book, (navMap, resolve) =>
        {
            RemoveNavPoints(navMap, paths, resolve);
            NcxNavPoint navPoint = new()
            {
                Id = NavPointIdPrefix + "edition",
                Label = title,
                ContentSrc = Utility.UrlEncodePath(BookPath.Relative(book.GetNcx()!.BookPath, file.BookPath)),
            };
            navMap.Insert(InsertionIndex(navMap.Select(n => resolve(n.ContentSrc)).ToList(), fileIndex, SpineIndex), navPoint);
        });

        return changed;
    }

    // Before the first entry whose file comes later in the spine than the given file; entries outside the spine
    // (or without a target) do not decide the place.
    private static int InsertionIndex(List<string> entryPaths, int fileIndex, Func<string, int> spineIndex)
    {
        if (fileIndex < 0)
        {
            return entryPaths.Count;
        }

        for (int i = 0; i < entryPaths.Count; i++)
        {
            if (spineIndex(entryPaths[i]) > fileIndex)
            {
                return i;
            }
        }

        return entryPaths.Count;
    }

    private static bool EditNav(Book book, Action<IList<TocEntry>> edit)
    {
        if (!book.IsEpub3 || book.GetNavResource() is not { } nav)
        {
            return false;
        }

        NavProcessor processor = new(nav);
        TocEntry root = processor.GetRootTocEntry();
        string before = Describe(root.Children);
        edit(root.Children);
        if (Describe(root.Children) == before)
        {
            return false;
        }

        processor.GenerateNavTocFromTocEntries(root);
        return true;
    }

    private static bool EditNcx(Book book, Action<List<NcxNavPoint>, Func<string, string>> edit)
    {
        if (book.GetNcx() is not { } ncx)
        {
            return false;
        }

        NcxDocument document = ncx.GetNcxDocument();
        string ncxFolder = BookPath.StartingDir(ncx.BookPath);
        string Resolve(string src) => src.Length == 0 ? string.Empty : BookPath.BuildBookPath(Utility.UrlDecodePath(StripFragment(src)), ncxFolder);

        string before = Describe(document.NavMap);
        edit(document.NavMap, Resolve);
        if (Describe(document.NavMap) == before)
        {
            return false;
        }

        // The NCX requires at least one navPoint (as NcxGenerator does).
        if (document.NavMap.Count == 0 && book.GetHtmlResources().FirstOrDefault() is { } first)
        {
            document.NavMap.Add(new NcxNavPoint
            {
                Label = "Start",
                ContentSrc = Utility.UrlEncodePath(BookPath.Relative(ncx.BookPath, first.BookPath)),
            });
        }

        document.SyncHeadCounts();
        ncx.SetNcxDocument(document);
        return true;
    }

    private static void RemoveTocEntries(IList<TocEntry> entries, IReadOnlySet<string> paths)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            TocEntry entry = entries[i];
            RemoveTocEntries(entry.Children, paths);
            if (paths.Contains(PathOf(entry.Target)))
            {
                entries.RemoveAt(i);
                for (int c = 0; c < entry.Children.Count; c++)
                {
                    entries.Insert(i + c, entry.Children[c]);
                }

                i += entry.Children.Count - 1;
            }
        }
    }

    private static void RemoveNavPoints(IList<NcxNavPoint> navPoints, IReadOnlySet<string> paths, Func<string, string> resolve)
    {
        for (int i = 0; i < navPoints.Count; i++)
        {
            NcxNavPoint navPoint = navPoints[i];
            RemoveNavPoints(navPoint.Children, paths, resolve);
            if (paths.Contains(resolve(navPoint.ContentSrc)))
            {
                navPoints.RemoveAt(i);
                for (int c = 0; c < navPoint.Children.Count; c++)
                {
                    navPoints.Insert(i + c, navPoint.Children[c]);
                }

                i += navPoint.Children.Count - 1;
            }
        }
    }

    // TocEntry.Target is a URL-encoded bookpath with an optional fragment.
    private static string PathOf(string target) => Utility.UrlDecodePath(StripFragment(target));

    private static string StripFragment(string href)
    {
        int hash = href.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? href : href[..hash];
    }

    private static string Describe(IEnumerable<TocEntry> entries)
    {
        StringBuilder sb = new();
        foreach (TocEntry entry in entries)
        {
            sb.Append(entry.Text).Append('\u0001').Append(entry.Target).Append('{').Append(Describe(entry.Children)).Append('}');
        }

        return sb.ToString();
    }

    private static string Describe(IEnumerable<NcxNavPoint> navPoints)
    {
        StringBuilder sb = new();
        foreach (NcxNavPoint navPoint in navPoints)
        {
            sb.Append(navPoint.Label).Append('\u0001').Append(navPoint.ContentSrc).Append('{').Append(Describe(navPoint.Children)).Append('}');
        }

        return sb.ToString();
    }
}
