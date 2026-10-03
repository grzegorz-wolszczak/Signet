using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Toc;

/// <summary>
/// Generates the content of the NCX resource (<c>toc.ncx</c>) from the book's headings.
/// </summary>
public static class NcxGenerator
{
    /// <summary>
    /// Rebuilds the <c>navMap</c> of the NCX from the <c>h1</c>–<c>h6</c> headings of the spine XHTML files (omitting
    /// the nav). When there are no headings, writes a single fallback <c>navPoint</c> "Start" pointing at the first
    /// XHTML file (as the NCX specification requires). <c>dtb:depth</c> / <c>dtb:totalPageCount</c> /
    /// <c>dtb:maxPageNumber</c> are refreshed (<see cref="NcxDocument.SyncHeadCounts"/>).
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns><c>true</c> if the NCX text actually changed; <c>false</c> if the book has no NCX.</returns>
    public static bool GenerateFromBookContents(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        NcxResource? ncx = book.GetNcx();
        if (ncx is null)
        {
            return false;
        }

        string ncxBookPath = ncx.BookPath;
        bool epub2 = book.EpubVersion.StartsWith('2');

        string docTitle = book.GetOpf().GetPrimaryBookTitle();
        if (docTitle.Length == 0)
        {
            docTitle = "Unknown";
        }

        NcxDocument document = NcxDocument.CreateEmpty(docTitle, book.GetOpf().GetMainIdentifierValue(), epub2);

        IReadOnlyList<Heading> headings =
            Headings.MakeHeadingHierarchy(Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav()));

        foreach (Heading heading in headings)
        {
            document.NavMap.Add(ConvertHeadingWalker(heading, ncxBookPath));
        }

        if (document.NavMap.Count == 0)
        {
            HtmlResource? first = book.GetHtmlResources().FirstOrDefault();
            if (first is not null)
            {
                document.NavMap.Add(new NcxNavPoint
                {
                    Label = "Start",
                    ContentSrc = Utility.UrlEncodePath(Core.BookPath.Relative(ncxBookPath, first.BookPath)),
                });
            }
        }

        document.SyncHeadCounts();

        string newText = document.ToXml();
        if (string.Equals(newText, ncx.GetText(), StringComparison.Ordinal))
        {
            return false;
        }

        ncx.SetText(newText);
        return true;
    }

    /// <summary>
    /// Rebuilds the <c>navMap</c> of the NCX from an editable <see cref="TocEntry"/> entry tree (the
    /// "Edit Table Of Contents" dialog). When the tree is empty, a fallback
    /// <c>navPoint</c> "Start" pointing at the first XHTML file is written.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="root">The root of the entry tree (its <see cref="TocEntry.Children"/> become the top-level <c>navPoint</c> elements).</param>
    public static void GenerateFromTocEntries(Book book, TocEntry root)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(root);

        NcxResource? ncx = book.GetNcx();
        if (ncx is null)
        {
            return;
        }

        string ncxBookPath = ncx.BookPath;
        bool epub2 = book.EpubVersion.StartsWith('2');

        string docTitle = book.GetOpf().GetPrimaryBookTitle();
        if (docTitle.Length == 0)
        {
            docTitle = "Unknown";
        }

        NcxDocument document = NcxDocument.CreateEmpty(docTitle, book.GetOpf().GetMainIdentifierValue(), epub2);

        foreach (TocEntry entry in root.Children)
        {
            document.NavMap.Add(TocEntryWalker(entry, ncxBookPath));
        }

        if (document.NavMap.Count == 0)
        {
            HtmlResource? first = book.GetHtmlResources().FirstOrDefault();
            if (first is not null)
            {
                document.NavMap.Add(new NcxNavPoint
                {
                    Label = "Start",
                    ContentSrc = Utility.UrlEncodePath(Core.BookPath.Relative(ncxBookPath, first.BookPath)),
                });
            }
        }

        document.SyncHeadCounts();
        ncx.SetText(document.ToXml());
    }

    private static NcxNavPoint TocEntryWalker(TocEntry entry, string ncxBookPath)
    {
        NcxNavPoint navPoint = new()
        {
            Label = Headings.Simplified(entry.Text),
            ContentSrc = ConvertBookPathToNcxRelative(entry.Target, ncxBookPath),
        };

        foreach (TocEntry child in entry.Children)
        {
            navPoint.Children.Add(TocEntryWalker(child, ncxBookPath));
        }

        return navPoint;
    }

    // Converts a URL-encoded bookpath (+#fragment) to a src relative to the NCX.
    private static string ConvertBookPathToNcxRelative(string target, string ncxBookPath)
    {
        int hash = target.IndexOf('#', StringComparison.Ordinal);
        string basePart = hash < 0 ? target : target[..hash];
        string fragment = hash < 0 ? string.Empty : target[(hash + 1)..];

        if (basePart.Length == 0)
        {
            return fragment.Length > 0 ? "#" + fragment : string.Empty;
        }

        string src = Utility.UrlEncodePath(Core.BookPath.Relative(ncxBookPath, Utility.UrlDecodePath(basePart)));
        return fragment.Length > 0 ? src + "#" + fragment : src;
    }

    private static NcxNavPoint ConvertHeadingWalker(Heading heading, string ncxBookPath)
    {
        string src = Utility.UrlEncodePath(Core.BookPath.Relative(ncxBookPath, heading.ResourceFile.BookPath));
        if (!heading.AtFileStart)
        {
            src += "#" + heading.Id;
        }

        NcxNavPoint navPoint = new()
        {
            Label = Headings.Simplified(heading.Text),
            ContentSrc = src,
        };

        foreach (Heading child in heading.Children)
        {
            navPoint.Children.Add(ConvertHeadingWalker(child, ncxBookPath));
        }

        return navPoint;
    }
}
