using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Creates a new, empty <see cref="Book"/> publication from the EPUB version and (optionally)
/// a custom bookpath layout from the "Custom Epub Layout" wizard (no GUI part: dialogs and signals).
/// </summary>
public static class BookCreator
{
    /// <summary>The bookpaths of the standard Signet layout for EPUB 2.</summary>
    private static readonly string[] StandardEpub2 =
    {
        "OEBPS/content.opf", "OEBPS/Text/marker.xhtml", "OEBPS/Styles/marker.css",
        "OEBPS/Fonts/marker.otf", "OEBPS/Images/marker.jpg", "OEBPS/Audio/marker.mp3",
        "OEBPS/Video/marker.mp4", "OEBPS/Misc/marker.xml", "OEBPS/toc.ncx",
    };

    /// <summary>The bookpaths of the standard Signet layout for EPUB 3 (plus nav + js).</summary>
    private static readonly string[] StandardEpub3 =
        StandardEpub2.Concat(new[] { "OEBPS/Text/nav.xhtml", "OEBPS/Misc/marker.js" }).ToArray();

    /// <summary>
    /// The bookpaths of the standard Signet layout for the given EPUB version (to fill in the default
    /// content of the "Custom Epub Layout" wizard).
    /// </summary>
    public static IReadOnlyList<string> StandardLayout(string version) =>
        NormalizeVersion(version).StartsWith('3') ? StandardEpub3 : StandardEpub2;

    /// <summary>
    /// Builds a new empty publication.
    /// </summary>
    /// <param name="version">The EPUB version (<c>2.0</c> / <c>3.0</c>); empty =&gt; <c>2.0</c>.</param>
    /// <param name="bookPaths">
    /// An optional custom layout from the wizard (a list of marker bookpaths + OPF + NCX/NAV).
    /// When <c>null</c>, empty or incomplete — the standard layout is used.
    /// </param>
    public static Book CreateNewBook(string? version, IReadOnlyList<string>? bookPaths = null)
    {
        string epubVersion = NormalizeVersion(version);

        List<string> paths = (bookPaths ?? Array.Empty<string>())
            .Select(p => p.TrimStart('/'))
            .Where(p => p.Length > 0)
            .ToList();

        if (!IsValidLayout(paths, epubVersion))
        {
            paths = StandardLayout(epubVersion).ToList();
        }

        // Split the information out of the bookpaths.
        string opfBookPath = string.Empty;
        string ncxBookPath = string.Empty;
        string navDir = string.Empty;
        List<string> textDirs = new();
        List<string> mediaTypes = new();
        List<string> finalPaths = new();

        foreach (string bkPath in paths)
        {
            string filename = bkPath.Split('/').Last();
            string extension = filename.Split('.').Last();
            string folder = CoreBookPath.StartingDir(bkPath);
            string mt = MediaTypes.GetMediaTypeFromExtension(extension);

            if (filename.EndsWith(".opf", StringComparison.Ordinal))
            {
                opfBookPath = bkPath;
            }

            if (filename.EndsWith(".ncx", StringComparison.Ordinal))
            {
                ncxBookPath = bkPath;
            }

            if (filename.EndsWith("marker.xhtml", StringComparison.Ordinal))
            {
                textDirs.Add(folder);
            }

            if (filename.EndsWith(".xhtml", StringComparison.Ordinal)
                && !filename.EndsWith("marker.xhtml", StringComparison.Ordinal))
            {
                navDir = folder;
                continue;
            }

            mediaTypes.Add(mt);
            finalPaths.Add(bkPath);
        }

        string firstTextDir = textDirs.Count > 0 ? textDirs[0] : "OEBPS/Text";

        TempFolder tempFolder = new();
        FolderKeeper folderKeeper = new(tempFolder);
        try
        {
            Book book = new(folderKeeper) { EpubVersion = epubVersion };

            OpfResource opf = folderKeeper.AddOpfToFolder(epubVersion, opfBookPath);
            opf.EpubVersion = epubVersion;

            folderKeeper.SetGroupFolders(finalPaths, mediaTypes);

            foreach (string textFolder in textDirs.Distinct())
            {
                book.CreateEmptyHtmlFile(textFolder);
            }

            if (epubVersion.StartsWith('3'))
            {
                HtmlResource nav = book.CreateEmptyNavFile(
                    updateOpf: true,
                    folderPath: navDir.Length > 0 ? navDir : firstTextDir);
                opf.SetItemRefLinear(nav, false);
            }
            else
            {
                NcxResource ncx = folderKeeper.AddNcxToFolder(
                    epubVersion,
                    ncxBookPath.Length > 0 ? ncxBookPath : null,
                    firstTextDir);
                string ncxId = opf.AddNcxItem(ncx.BookPath);
                opf.UpdateNcxOnSpine(ncxId);
            }

            folderKeeper.UpdateShortPathNames();
            folderKeeper.PerformInitialLoads();
            book.Modified = false;
            return book;
        }
        catch
        {
            folderKeeper.Dispose();
            throw;
        }
    }

    private static string NormalizeVersion(string? version)
    {
        string v = (version ?? string.Empty).Trim();
        return v.Length == 0 ? "2.0" : v;
    }

    private static bool IsValidLayout(List<string> bookPaths, string version)
    {
        if (bookPaths.Count == 0)
        {
            return false;
        }

        bool hasOpf = false;
        bool hasNcx = false;
        bool hasNav = false;
        bool hasText = false;
        bool hasStyle = false;

        foreach (string bkPath in bookPaths)
        {
            if (bkPath.EndsWith(".opf", StringComparison.Ordinal))
            {
                hasOpf = true;
            }

            if (bkPath.EndsWith(".ncx", StringComparison.Ordinal))
            {
                hasNcx = true;
            }

            if (bkPath.EndsWith("marker.css", StringComparison.Ordinal))
            {
                hasStyle = true;
            }

            if (bkPath.EndsWith("marker.xhtml", StringComparison.Ordinal))
            {
                hasText = true;
            }

            if (bkPath.EndsWith(".xhtml", StringComparison.Ordinal)
                && !bkPath.EndsWith("marker.xhtml", StringComparison.Ordinal))
            {
                hasNav = true;
            }
        }

        return version.StartsWith('3')
            ? hasOpf && hasNav && hasText && hasStyle
            : hasOpf && hasNcx && hasText;
    }
}
