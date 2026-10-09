using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Toc;

namespace Signet.Core.BookManipulation;

/// <summary>What <see cref="EditionPage.Stamp"/> did.</summary>
/// <param name="Page">The edition page (new or updated).</param>
/// <param name="Created">Whether the page was created by this stamp.</param>
/// <param name="Revision">The revision written to the book.</param>
public sealed record EditionStampResult(HtmlResource Page, bool Created, int Revision);

/// <summary>
/// The edition page: a page with a table of fields (the user's fields from <see cref="EditionPageSettings"/>, the
/// revision and the save date) that Signet adds to the book and refreshes on every save, so the copy on a reader can
/// be told apart from an older one.
/// </summary>
/// <remarks>
/// <para>The page is recognized by <c>&lt;meta name="signet:edition-page"&gt;</c> in its <c>&lt;head&gt;</c>, wherever it
/// is and whatever it is called. The book remembers in its OPF: the revision counter
/// (<c>&lt;meta name="signet:revision"&gt;</c>) and the labels of the revision and date rows the user changed by
/// editing the page (<c>signet:edition-program-label</c>, <c>signet:edition-version-label</c>,
/// <c>signet:edition-revision-label</c>, <c>signet:edition-date-label</c>). The default labels follow the book's
/// language.</para>
/// </remarks>
public static partial class EditionPage
{
    /// <summary>The file name of a newly created edition page.</summary>
    public const string FileName = "signet_edition.xhtml";

    /// <summary>The <c>name</c> of the <c>&lt;meta&gt;</c> that marks the edition page.</summary>
    public const string MarkerMetaName = "signet:edition-page";

    /// <summary>The OPF <c>&lt;meta name&gt;</c> holding the revision counter.</summary>
    public const string RevisionMetaName = "signet:revision";

    /// <summary>The OPF <c>&lt;meta name&gt;</c> holding the program label the user set on the page.</summary>
    public const string ProgramLabelMetaName = "signet:edition-program-label";

    /// <summary>The OPF <c>&lt;meta name&gt;</c> holding the version label the user set on the page.</summary>
    public const string VersionLabelMetaName = "signet:edition-version-label";

    /// <summary>The OPF <c>&lt;meta name&gt;</c> holding the revision label the user set on the page.</summary>
    public const string RevisionLabelMetaName = "signet:edition-revision-label";

    /// <summary>The OPF <c>&lt;meta name&gt;</c> holding the date label the user set on the page.</summary>
    public const string DateLabelMetaName = "signet:edition-date-label";

    private const string ProgramRowClass = "signet-edition-program";
    private const string VersionRowClass = "signet-edition-version";
    private const string RevisionRowClass = "signet-edition-revision";
    private const string DateRowClass = "signet-edition-date";

    // Default texts per book language (they are book content, not UI strings).
    private static readonly IReadOnlyDictionary<string, EditionTexts> Texts =
        new Dictionary<string, EditionTexts>(StringComparer.Ordinal)
        {
            ["en"] = new("About this edition", "Program", "Version", "Revision", "Last updated"),
            ["pl"] = new("O tym wydaniu", "Program", "Wersja", "Rewizja", "Data zapisu"),
        };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    /// <summary>The edition page of the book (the first HTML file with the marker meta); <c>null</c> when there is none.</summary>
    public static HtmlResource? Find(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.GetHtmlResources().FirstOrDefault(IsEditionPage);
    }

    /// <summary>Whether the HTML file carries the edition page marker.</summary>
    public static bool IsEditionPage(HtmlResource html)
    {
        ArgumentNullException.ThrowIfNull(html);
        string text = html.GetText();
        if (!text.Contains(MarkerMetaName, StringComparison.Ordinal))
        {
            return false;
        }

        IDocument document = new HtmlParser().ParseDocument(text);
        return document.Head?.QuerySelectorAll("meta").Any(m => m.GetAttribute("name") == MarkerMetaName) == true;
    }

    /// <summary>
    /// Creates or refreshes the edition page: raises the revision, writes the page, puts it at
    /// <see cref="EditionPageSettings.Position"/> in the spine and adds or removes its TOC entry.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="settings">The edition page settings.</param>
    /// <param name="utcNow">The save time (UTC).</param>
    /// <param name="fallbackLanguage">The language of the default texts when the book has none (the UI language).</param>
    /// <param name="programVersion">The version of Signet writing the page (the "Version" row).</param>
    public static EditionStampResult Stamp(
        Book book, EditionPageSettings settings, DateTime utcNow, string fallbackLanguage, string programVersion)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fallbackLanguage);
        ArgumentNullException.ThrowIfNull(programVersion);

        OpfResource opf = book.GetOpf();
        HtmlResource? page = Find(book);
        IDocument? existing = page is null ? null : new HtmlParser().ParseDocument(page.GetText());

        string bookLanguage = book.GetMetadataValues("dc:language").FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        EditionTexts defaults = TextsFor(bookLanguage, fallbackLanguage);
        string Label(string metaName, string rowClass, Func<EditionTexts, string> text) =>
            ResolveLabel(opf, metaName, RowLabel(existing, rowClass), text(defaults), Texts.Values.Select(text));
        string programLabel = Label(ProgramLabelMetaName, ProgramRowClass, t => t.Program);
        string versionLabel = Label(VersionLabelMetaName, VersionRowClass, t => t.Version);
        string revisionLabel = Label(RevisionLabelMetaName, RevisionRowClass, t => t.Revision);
        string dateLabel = Label(DateLabelMetaName, DateRowClass, t => t.Date);

        int revision = (int.TryParse(opf.GetNamedMeta(RevisionMetaName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int previous)
            && previous > 0 ? previous : 0) + 1;
        opf.SetNamedMeta(RevisionMetaName, revision.ToString(CultureInfo.InvariantCulture));

        bool created = page is null;
        if (page is null)
        {
            page = book.CreateEmptyHtmlFile();
            string fileName = book.GetFolderKeeper().GetUniqueFilenameVersion(FileName);
            page.RenameTo(fileName);
        }

        string title = settings.Title.Trim().Length > 0 ? settings.Title.Trim() : defaults.Title;
        List<(string Class, string Label, string Value)> rows = settings.Fields
            .Where(f => f.Show && f.Key.Trim().Length > 0)
            .Select(f => ("signet-edition-field", f.Key.Trim(), f.Value.Trim()))
            .ToList();
        if (settings.ShowProgram)
        {
            rows.Add((ProgramRowClass, programLabel, ApplicationInfo.Name));
        }

        if (settings.ShowVersion)
        {
            rows.Add((VersionRowClass, versionLabel, programVersion));
        }

        if (settings.ShowRevision)
        {
            rows.Add((RevisionRowClass, revisionLabel, revision.ToString(CultureInfo.InvariantCulture)));
        }

        if (settings.ShowDate)
        {
            rows.Add((DateRowClass, dateLabel, utcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"));
        }

        page.SetText(BuildXhtml(book.IsEpub3, bookLanguage, title, Math.Clamp(settings.HeadingLevel, 1, 6), settings.AddToToc, rows));
        page.SaveToDisk();
        PlaceInSpine(book, page, settings.Position);

        if (settings.AddToToc)
        {
            TocFileEntries.SetFileEntry(book, page, title);
        }
        else
        {
            TocFileEntries.RemoveEntriesForFiles(book, new[] { page.BookPath });
        }

        book.Modified = true;
        return new EditionStampResult(page, created, revision);
    }

    /// <summary>
    /// Removes the edition page from the book together with its TOC entries. The revision counter and the labels stay in
    /// the OPF, so a page added later continues the numbering. Returns <c>false</c> when the book has no edition page.
    /// </summary>
    public static bool Remove(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (Find(book) is not { } page)
        {
            return false;
        }

        TocFileEntries.RemoveEntriesForFiles(book, new[] { page.BookPath });
        book.GetFolderKeeper().BulkRemoveResources(new Resource[] { page });
        book.Modified = true;
        return true;
    }

    // The language of the default texts: the book's language, then the fallback (UI) language, then English.
    private static EditionTexts TextsFor(string bookLanguage, string fallbackLanguage)
    {
        foreach (string language in new[] { bookLanguage, fallbackLanguage })
        {
            string primary = language.Split('-', '_')[0].ToLowerInvariant();
            if (Texts.TryGetValue(primary, out var texts))
            {
                return texts;
            }
        }

        return Texts["en"];
    }

    // The label shown on the existing page wins when the user edited it there (it is then remembered in the OPF);
    // a default label of any language means "not customized" (e.g. the book language changed).
    private static string ResolveLabel(OpfResource opf, string metaName, string? pageLabel, string defaultLabel, IEnumerable<string> defaultLabels)
    {
        string? custom = opf.GetNamedMeta(metaName);
        if (!string.IsNullOrEmpty(pageLabel) && pageLabel != custom)
        {
            custom = defaultLabels.Contains(pageLabel, StringComparer.Ordinal) ? null : pageLabel;
            opf.SetNamedMeta(metaName, custom);
        }

        return string.IsNullOrEmpty(custom) ? defaultLabel : custom;
    }

    private static string? RowLabel(IDocument? document, string rowClass)
    {
        string? text = document?.QuerySelector($"tr.{rowClass} > th")?.TextContent;
        return text is null ? null : WhitespaceRun().Replace(text, " ").Trim();
    }

    private static void PlaceInSpine(Book book, HtmlResource page, EditionPagePosition position)
    {
        FolderKeeper keeper = book.GetFolderKeeper();
        List<HtmlResource> spine = book.GetOpf().GetSpineOrderBookPaths()
            .Select(keeper.GetResourceByBookPathNoThrow)
            .OfType<HtmlResource>()
            .Where(html => !ReferenceEquals(html, page))
            .ToList();

        int index = position switch
        {
            EditionPagePosition.First => 0,
            EditionPagePosition.Second => Math.Min(1, spine.Count),
            EditionPagePosition.Penultimate => Math.Max(0, spine.Count - 1),
            _ => spine.Count,
        };
        spine.Insert(index, page);
        book.GetOpf().UpdateSpineOrder(spine);
    }

    private static string BuildXhtml(
        bool epub3,
        string language,
        string title,
        int headingLevel,
        bool inToc,
        List<(string Class, string Label, string Value)> rows)
    {
        string lang = Escape(language);
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        if (epub3)
        {
            sb.Append("<!DOCTYPE html>\n\n");
            sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"");
            if (lang.Length > 0)
            {
                sb.Append(" lang=\"").Append(lang).Append("\" xml:lang=\"").Append(lang).Append('"');
            }
        }
        else
        {
            sb.Append("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n  \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n\n");
            sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
            if (lang.Length > 0)
            {
                sb.Append(" xml:lang=\"").Append(lang).Append('"');
            }
        }

        sb.Append(">\n<head>\n");
        sb.Append("  <title>").Append(Escape(title)).Append("</title>\n");
        sb.Append("  <meta name=\"").Append(MarkerMetaName).Append("\" content=\"1\" />\n");
        sb.Append("  <style type=\"text/css\">\n");
        sb.Append("    .signet-edition-title { text-align: center; }\n");
        sb.Append("    table.signet-edition { margin: 1.5em auto; border-collapse: collapse; font-family: monospace; }\n");
        sb.Append("    table.signet-edition th, table.signet-edition td { padding: 0.25em 0.75em; vertical-align: top; }\n");
        sb.Append("    table.signet-edition th { text-align: right; font-weight: bold; }\n");
        sb.Append("    table.signet-edition td { text-align: left; }\n");
        sb.Append("  </style>\n");
        sb.Append("</head>\n\n<body>\n");

        string tag = "h" + headingLevel.ToString(CultureInfo.InvariantCulture);
        string headingClass = inToc ? "signet-edition-title" : "signet-edition-title " + Headings.SignetNotInTocClass;
        sb.Append("  <").Append(tag).Append(" class=\"").Append(headingClass).Append("\">")
            .Append(Escape(title)).Append("</").Append(tag).Append(">\n");

        if (rows.Count > 0)
        {
            sb.Append("  <table class=\"signet-edition\">\n");
            foreach ((string rowClass, string label, string value) in rows)
            {
                sb.Append("    <tr class=\"").Append(rowClass).Append("\"><th>").Append(Escape(label))
                    .Append("</th><td>").Append(Escape(value)).Append("</td></tr>\n");
            }

            sb.Append("  </table>\n");
        }

        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // The default page texts of one language.
    private sealed record EditionTexts(string Title, string Program, string Version, string Revision, string Date);

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
