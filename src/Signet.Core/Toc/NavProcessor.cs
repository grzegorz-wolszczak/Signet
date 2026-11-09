using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Semantics;

namespace Signet.Core.Toc;

/// <summary>
/// Handling of the EPUB 3 Navigation Document (<c>nav.xhtml</c>) wrapping an <see cref="HtmlResource"/>.
/// Reading/writing the <c>nav[epub:type=toc]</c>, <c>nav[epub:type=landmarks]</c>,
/// <c>nav[epub:type=page-list]</c> sections, operations on landmark codes and NCX &#8596; Nav conversions.
/// </summary>
/// <remarks>
/// Reading a section: first a regular expression cuts the <c>&lt;nav epub:type=X&gt;…&lt;/nav&gt;</c> block
/// out of the text, and only then is the fragment parsed (AngleSharp) — so a syntax error
/// outside the given section does not break reading it.
/// Writing a section: the <c>&lt;nav&gt;</c> block is replaced in the text in place (a splice), the rest of
/// the document stays untouched; a missing section is inserted before <c>&lt;/body&gt;</c>.
/// The <c>&lt;h1&gt;</c> headings are not localized (the English name from
/// <see cref="Landmarks"/>); <see cref="GetAllLandmarkInfoByBookPath"/> returns records.
/// </remarks>
public sealed class NavProcessor
{
    /// <summary>The default language code when the nav does not declare one.</summary>
    public const string DefaultLanguage = "en";

    private const string DefaultNavTemplate =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" " +
        "lang=\"{0}\" xml:lang=\"{0}\">\n" +
        "<head>\n" +
        "  <meta charset=\"utf-8\" />\n" +
        "  <style type=\"text/css\">\n" +
        "    nav#landmarks, nav#page-list { display:none; }\n" +
        "    ol { list-style-type: none; }\n" +
        "  </style>\n" +
        "</head>\n" +
        "<body epub:type=\"frontmatter\">\n" +
        "  <nav epub:type=\"toc\" id=\"toc\" role=\"doc-toc\">\n" +
        "  </nav>\n" +
        "  <nav epub:type=\"landmarks\" id=\"landmarks\" hidden=\"\">\n" +
        "  </nav>\n" +
        "</body>\n" +
        "</html>";

    private static readonly HtmlParser Parser = new();

    private static readonly Regex TocNavOpen = MakeSectionRegex("toc");
    private static readonly Regex LandmarksNavOpen = MakeSectionRegex("landmarks");
    private static readonly Regex PageListNavOpen = MakeSectionRegex("page-list");

    private readonly HtmlResource _nav;
    private readonly string _language;

    /// <summary>
    /// Creates a processor for a nav resource. If the resource is empty, fills it with the default template
    /// (the <c>toc</c> + <c>landmarks</c> sections); otherwise reads the language from <c>&lt;html&gt;</c>.
    /// </summary>
    /// <param name="navResource">The nav document.</param>
    /// <param name="templateLanguage">
    /// The language of the default template written into an empty resource; <c>null</c> = <see cref="DefaultLanguage"/>.
    /// </param>
    public NavProcessor(HtmlResource navResource, string? templateLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(navResource);
        _nav = navResource;
        _language = string.IsNullOrEmpty(templateLanguage) ? DefaultLanguage : templateLanguage;

        string source = _nav.GetText();
        if (string.IsNullOrEmpty(source))
        {
            _nav.SetText(DefaultNavTemplate.Replace("{0}", _language, StringComparison.Ordinal));
            return;
        }

        IElement? html = Parse(source).QuerySelector("html");
        if (html is not null)
        {
            string lang = html.GetAttribute("lang") ?? html.GetAttribute("xml:lang") ?? string.Empty;
            if (!string.IsNullOrEmpty(lang))
            {
                _language = lang;
            }
        }
    }

    /// <summary>The language code used by the nav (from <c>&lt;html lang&gt;</c> or <see cref="DefaultLanguage"/>).</summary>
    public string Language => _language;

    // --- reading sections ---

    /// <summary>
    /// Returns a flat list of TOC entries (<see cref="NavTocEntry.Level"/> from 1); empty if the section is absent.
    /// </summary>
    public IReadOnlyList<NavTocEntry> GetToc()
    {
        string? section = ExtractSection(_nav.GetText(), TocNavOpen);
        if (section is null)
        {
            return Array.Empty<NavTocEntry>();
        }

        IElement? nav = FindNav(Parse(section), "toc");
        IElement? ol = nav?.QuerySelector("ol");
        return ol is null ? Array.Empty<NavTocEntry>() : GetNodeToc(ol, 1);
    }

    /// <summary>Returns the TOC entries as a tree (<see cref="MakeHierarchy"/> over <see cref="GetToc"/>).</summary>
    public IReadOnlyList<NavTocEntry> GetTocTree() => MakeHierarchy(GetToc());

    /// <summary>Returns the entries of the landmarks section; empty if absent.</summary>
    public IReadOnlyList<NavLandmarkEntry> GetLandmarks()
    {
        string? section = ExtractSection(_nav.GetText(), LandmarksNavOpen);
        if (section is null)
        {
            return Array.Empty<NavLandmarkEntry>();
        }

        IElement? nav = FindNav(Parse(section), "landmarks");
        if (nav is null)
        {
            return Array.Empty<NavLandmarkEntry>();
        }

        List<NavLandmarkEntry> list = new();
        foreach (IElement anchor in nav.QuerySelectorAll("a"))
        {
            list.Add(new NavLandmarkEntry
            {
                EpubType = GetEpubType(anchor),
                Href = anchor.GetAttribute("href") ?? string.Empty,
                Title = LocalText(anchor),
            });
        }

        return list;
    }

    /// <summary>Returns the entries of the page-list section; empty if absent.</summary>
    public IReadOnlyList<NavPageListEntry> GetPageList()
    {
        string? section = ExtractSection(_nav.GetText(), PageListNavOpen);
        if (section is null)
        {
            return Array.Empty<NavPageListEntry>();
        }

        IElement? nav = FindNav(Parse(section), "page-list");
        if (nav is null)
        {
            return Array.Empty<NavPageListEntry>();
        }

        List<NavPageListEntry> list = new();
        foreach (IElement anchor in nav.QuerySelectorAll("a"))
        {
            list.Add(new NavPageListEntry
            {
                PageName = LocalText(anchor),
                Href = anchor.GetAttribute("href") ?? string.Empty,
            });
        }

        return list;
    }

    // --- writing sections ---

    /// <summary>Writes the <c>toc</c> section from a flat list of entries.</summary>
    public void SetToc(IReadOnlyList<NavTocEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SpliceSection(TocNavOpen, BuildToc(entries, ReadOldSection(TocNavOpen, "toc")));
    }

    /// <summary>Writes the <c>toc</c> section from an entry tree (flattening it into a flat list).</summary>
    public void SetTocTree(IReadOnlyList<NavTocEntry> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        SetToc(Flatten(tree));
    }

    /// <summary>Writes the <c>landmarks</c> section.</summary>
    public void SetLandmarks(IReadOnlyList<NavLandmarkEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SpliceSection(LandmarksNavOpen, BuildLandmarks(entries, ReadOldSection(LandmarksNavOpen, "landmarks")));
    }

    /// <summary>Writes the <c>page-list</c> section.</summary>
    public void SetPageList(IReadOnlyList<NavPageListEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SpliceSection(PageListNavOpen, BuildPageList(entries, ReadOldSection(PageListNavOpen, "page-list")));
    }

    // --- operations on landmark codes ---

    /// <summary>
    /// Adds / changes / toggles a landmark code for a resource.
    /// </summary>
    public void AddLandmarkCode(Resource resource, string newCode, bool toggle = true, string targetId = "")
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(newCode);
        ArgumentNullException.ThrowIfNull(targetId);
        if (newCode.Length == 0)
        {
            return;
        }

        List<NavLandmarkEntry> list = GetLandmarks().ToList();
        int pos = GetResourceLandmarkPos(resource, list, targetId);
        string currentCode = pos > -1 ? list[pos].EpubType : string.Empty;

        if (!string.Equals(currentCode, newCode, StringComparison.Ordinal) || !toggle)
        {
            string title = Landmarks.GetTitle(newCode, _language);
            if (pos > -1)
            {
                list[pos].EpubType = newCode;
                list[pos].Title = title;
            }
            else
            {
                string href = ConvertBookPathToNavRelative(resource.BookPath);
                if (targetId.Length > 0)
                {
                    href = href + "#" + targetId;
                }

                if (ReferenceEquals(resource, _nav) && string.Equals(newCode, "toc", StringComparison.Ordinal))
                {
                    href = "#toc";
                }

                list.Add(new NavLandmarkEntry { EpubType = newCode, Title = title, Href = href });
            }
        }
        else if (pos > -1)
        {
            list.RemoveAt(pos);
        }

        SetLandmarks(list);
    }

    /// <summary>Removes the landmark entry for a resource (and an optional fragment).</summary>
    public void RemoveLandmarkForResource(Resource resource, string targetId = "")
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(targetId);
        List<NavLandmarkEntry> list = GetLandmarks().ToList();
        int pos = GetResourceLandmarkPos(resource, list, targetId);
        if (pos > -1)
        {
            list.RemoveAt(pos);
        }

        SetLandmarks(list);
    }

    /// <summary>
    /// Removes every landmark entry pointing at the whole of <paramref name="resource"/> (an <c>href</c> without a
    /// fragment); entries pointing at a fragment of it stay. The nav is rewritten only when something was removed.
    /// </summary>
    /// <returns><c>true</c> when at least one entry was removed.</returns>
    public bool RemoveWholeFileLandmarksForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        string target = Utility.UrlEncodePath(resource.BookPath);
        List<NavLandmarkEntry> list = GetLandmarks().ToList();
        if (list.RemoveAll(entry => string.Equals(ConvertHrefToBookPath(entry.Href), target, StringComparison.Ordinal)) == 0)
        {
            return false;
        }

        SetLandmarks(list);
        return true;
    }

    /// <summary>Removes all landmark entries pointing at the given resource.</summary>
    public void RemoveAllLandmarksForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        string target = Utility.UrlEncodePath(resource.BookPath);
        List<NavLandmarkEntry> list = GetLandmarks()
            .Where(entry => !string.Equals(PathPart(ConvertHrefToBookPath(entry.Href)), target, StringComparison.Ordinal))
            .ToList();
        SetLandmarks(list);
    }

    /// <summary>The landmark code for a resource (and an optional fragment); <c>""</c> if none.</summary>
    public string GetLandmarkCodeForResource(Resource resource, string targetId = "")
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(targetId);
        IReadOnlyList<NavLandmarkEntry> list = GetLandmarks();
        int pos = GetResourceLandmarkPos(resource, list, targetId);
        return pos > -1 ? list[pos].EpubType : string.Empty;
    }

    /// <summary>The landmark display name for a resource; <c>""</c> if none.</summary>
    public string GetLandmarkNameForResource(Resource resource, string targetId = "")
    {
        string code = GetLandmarkCodeForResource(resource, targetId);
        return code.Length > 0 ? Landmarks.GetName(code) : string.Empty;
    }

    /// <summary>A bookpath (URL-encoded, without a fragment) -&gt; list of landmark codes map.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetLandmarkCodeForPaths() =>
        GroupLandmarks(entry => entry.EpubType);

    /// <summary>A bookpath (URL-encoded, without a fragment) -&gt; list of landmark names map.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetLandmarkNameForPaths() =>
        GroupLandmarks(entry => Landmarks.GetName(entry.EpubType));

    /// <summary>
    /// All landmarks split into bookpath / fragment / code / title.
    /// </summary>
    public IReadOnlyList<LandmarkInfo> GetAllLandmarkInfoByBookPath()
    {
        List<LandmarkInfo> result = new();
        foreach (NavLandmarkEntry entry in GetLandmarks())
        {
            (string path, string fragment) = SplitFragment(ConvertHrefToBookPath(entry.Href));
            result.Add(new LandmarkInfo(Utility.UrlDecodePath(path), fragment, entry.EpubType, entry.Title));
        }

        return result;
    }

    // --- generating the toc section from the book headings ---

    /// <summary>
    /// Overwrites the <c>nav[epub:type=toc]</c> section with a table of contents built from the
    /// <c>h1</c>–<c>h6</c> headings of all spine XHTML files (omitting the nav). Headings with the class
    /// <c>signet_not_in_toc</c> are skipped.
    /// </summary>
    /// <param name="book">The book whose headings the TOC is built from.</param>
    /// <returns><c>true</c> if the new section content differs from the previous one.</returns>
    public bool GenerateTocFromBookContents(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        // (Both are built without the ids of the old section: only the entries are compared.)
        string previousXml = BuildToc(GetToc(), null);

        IReadOnlyList<Heading> headings =
            Headings.MakeHeadingHierarchy(Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav()));

        List<NavTocEntry> tocList = new();
        foreach (Heading heading in headings)
        {
            tocList.AddRange(HeadingWalker(heading, 1));
        }

        string newXml = BuildToc(tocList, null);
        SetToc(tocList);
        return !string.Equals(newXml, previousXml, StringComparison.Ordinal);
    }

    private List<NavTocEntry> HeadingWalker(Heading heading, int level)
    {
        List<NavTocEntry> list = new();

        if (heading.IncludeInToc &&
            !string.Equals(_nav.BookPath, heading.ResourceFile.BookPath, StringComparison.Ordinal))
        {
            string fileHref = ConvertBookPathToNavRelative(heading.ResourceFile.BookPath);
            list.Add(new NavTocEntry
            {
                Level = level,
                Title = heading.Text,
                Href = heading.AtFileStart ? fileHref : fileHref + "#" + heading.Id,
            });
        }

        foreach (Heading child in heading.Children)
        {
            list.AddRange(HeadingWalker(child, level + 1));
        }

        return list;
    }

    // --- the editable entry tree (the Edit TOC dialog) ---

    /// <summary>
    /// Returns the <c>toc</c> section as an editable <see cref="TocEntry"/> tree root. The targets
    /// (<see cref="TocEntry.Target"/>) are resolved to absolute, URL-encoded bookpaths
    /// (with an optional fragment).
    /// </summary>
    public TocEntry GetRootTocEntry()
    {
        TocEntry root = new() { IsRoot = true };
        foreach (NavTocEntry navEntry in MakeHierarchy(GetToc()))
        {
            AddTocEntry(navEntry, root);
        }

        return root;
    }

    /// <summary>
    /// Overwrites the <c>nav[epub:type=toc]</c> section with a table of contents built from an editable
    /// entry tree.
    /// </summary>
    /// <param name="root">The tree root (its <see cref="TocEntry.Children"/> are the level-1 entries).</param>
    public void GenerateNavTocFromTocEntries(TocEntry root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<NavTocEntry> flat = new();
        foreach (TocEntry entry in root.Children)
        {
            flat.AddRange(AddEditTocEntry(entry, 1));
        }

        SetToc(flat);
    }

    private void AddTocEntry(NavTocEntry navEntry, TocEntry parent)
    {
        TocEntry entry = new()
        {
            Text = navEntry.Title,
            Target = ConvertHrefToBookPath(navEntry.Href),
        };

        foreach (NavTocEntry navChild in navEntry.Children)
        {
            AddTocEntry(navChild, entry);
        }

        parent.Children.Add(entry);
    }

    private List<NavTocEntry> AddEditTocEntry(TocEntry entry, int level)
    {
        List<NavTocEntry> list = new()
        {
            new NavTocEntry
            {
                Level = level,
                Title = entry.Text,
                Href = ConvertBookPathToNavRelative(entry.Target),
            },
        };

        foreach (TocEntry child in entry.Children)
        {
            list.AddRange(AddEditTocEntry(child, level + 1));
        }

        return list;
    }

    // --- NCX <-> Nav conversions ---

    /// <summary>
    /// Builds an <see cref="NcxDocument"/> from the <c>toc</c> section (as <c>navMap</c>) and <c>page-list</c>
    /// (as <c>pageList</c>) of this nav. The landmarks section has no NCX counterpart.
    /// </summary>
    /// <param name="ncxBookPath">The bookpath under which the NCX will be created (used to compute <c>content/@src</c>).</param>
    /// <param name="docTitle">The title for <c>&lt;docTitle&gt;</c>.</param>
    /// <param name="uid">The publication identifier for <c>dtb:uid</c>.</param>
    /// <param name="epub2">Whether the NCX should have a DOCTYPE (EPUB 2). <c>false</c> by default (the epub3 context).</param>
    public NcxDocument ToNcx(string ncxBookPath, string docTitle, string uid, bool epub2 = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(ncxBookPath);
        ArgumentNullException.ThrowIfNull(docTitle);
        ArgumentNullException.ThrowIfNull(uid);

        NcxDocument ncx = NcxDocument.CreateEmpty(docTitle, uid, epub2);

        foreach (NavTocEntry entry in MakeHierarchy(GetToc()))
        {
            ncx.NavMap.Add(TocEntryToNavPoint(entry, ncxBookPath));
        }

        foreach (NavPageListEntry entry in GetPageList())
        {
            ncx.PageList.Add(new NcxPageTarget
            {
                Type = "normal",
                Value = entry.PageName,
                Label = entry.PageName,
                ContentSrc = NavHrefToNcxRelative(entry.Href, ncxBookPath),
            });
        }

        ncx.SyncHeadCounts();
        return ncx;
    }

    /// <summary>
    /// Writes the <c>toc</c> section (from <c>navMap</c>) and — if non-empty — <c>page-list</c>
    /// (from <c>pageList</c>) of this nav based on <paramref name="ncx"/>. The landmarks section
    /// stays untouched.
    /// </summary>
    /// <param name="ncx">The source NCX model.</param>
    /// <param name="ncxBookPath">The bookpath of the NCX (used to convert <c>src</c> to be relative to the nav).</param>
    public void ApplyNcx(NcxDocument ncx, string ncxBookPath)
    {
        ArgumentNullException.ThrowIfNull(ncx);
        ArgumentException.ThrowIfNullOrEmpty(ncxBookPath);

        List<NavTocEntry> flat = new();
        foreach (NcxNavPoint navPoint in ncx.NavMap)
        {
            FlattenNavPoint(navPoint, 1, ncxBookPath, flat);
        }

        SetToc(flat);

        if (ncx.PageList.Count > 0)
        {
            List<NavPageListEntry> pages = ncx.PageList.Select(target => new NavPageListEntry
            {
                PageName = target.Label.Length > 0 ? target.Label : target.Value,
                Href = NcxRelativeToNavHref(target.ContentSrc, ncxBookPath),
            }).ToList();
            SetPageList(pages);
        }
    }

    // --- flat list <-> tree ---

    /// <summary>
    /// Builds a tree from a flat list of entries (by <see cref="NavTocEntry.Level"/>).
    /// The entries are copied; the input is not modified.
    /// </summary>
    public static IReadOnlyList<NavTocEntry> MakeHierarchy(IReadOnlyList<NavTocEntry> flat)
    {
        ArgumentNullException.ThrowIfNull(flat);

        List<NavTocEntry> tree = flat
            .Select(entry => new NavTocEntry { Level = entry.Level, Title = entry.Title, Href = entry.Href })
            .ToList();

        for (int i = 0; i < tree.Count; i++)
        {
            while (i != tree.Count - 1 && tree[i + 1].Level > tree[i].Level)
            {
                AddChildEntry(tree[i], tree[i + 1]);
                tree.RemoveAt(i + 1);
            }
        }

        return tree;
    }

    /// <summary>Flattens an entry tree into a flat list (<see cref="NavTocEntry.Level"/> from 1).</summary>
    public static IReadOnlyList<NavTocEntry> Flatten(IReadOnlyList<NavTocEntry> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        List<NavTocEntry> flat = new();
        foreach (NavTocEntry entry in tree)
        {
            FlattenEntry(entry, 1, flat);
        }

        return flat;
    }

    private static void FlattenEntry(NavTocEntry entry, int level, List<NavTocEntry> accumulator)
    {
        accumulator.Add(new NavTocEntry { Level = level, Title = entry.Title, Href = entry.Href });
        foreach (NavTocEntry child in entry.Children)
        {
            FlattenEntry(child, level + 1, accumulator);
        }
    }

    private static void AddChildEntry(NavTocEntry parent, NavTocEntry child)
    {
        if (parent.Children.Count > 0 && parent.Children[^1].Level < child.Level)
        {
            AddChildEntry(parent.Children[^1], child);
        }
        else
        {
            parent.Children.Add(child);
        }
    }

    private NcxNavPoint TocEntryToNavPoint(NavTocEntry entry, string ncxBookPath)
    {
        NcxNavPoint navPoint = new()
        {
            Label = entry.Title,
            ContentSrc = NavHrefToNcxRelative(entry.Href, ncxBookPath),
        };

        foreach (NavTocEntry child in entry.Children)
        {
            navPoint.Children.Add(TocEntryToNavPoint(child, ncxBookPath));
        }

        return navPoint;
    }

    private void FlattenNavPoint(NcxNavPoint navPoint, int level, string ncxBookPath, List<NavTocEntry> accumulator)
    {
        accumulator.Add(new NavTocEntry
        {
            Level = level,
            Title = navPoint.Label,
            Href = NcxRelativeToNavHref(navPoint.ContentSrc, ncxBookPath),
        });

        foreach (NcxNavPoint child in navPoint.Children)
        {
            FlattenNavPoint(child, level + 1, ncxBookPath, accumulator);
        }
    }

    // --- building the section XHTML (TOC / landmarks / page-list) ---

    // The ids of the section that is about to be written over (Calibre 5a3593a1ef, 82d8dc6886): other files can link to them, so they
    // go to the new section with the entries they belong to — the opening tag of the nav and its heading stay as they were, an entry
    // keeps the ids of its li and a — and the ids of what is gone stay as empty anchors before the nav (a nav may hold only a heading and
    // a list).
    private sealed record OldItem(string Href, string? LiId, string? AId);

    private sealed record OldSection(string OpenTag, string? Heading, string? OlId, List<OldItem> Items, List<string> AllIds, string OtherText);

    private static readonly Regex HeadingAfterOpenTag = new(
        @"^\s*(<h[1-6]\b[^>]*>.*?</h[1-6]\s*>)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private OldSection? ReadOldSection(Regex sectionOpen, string epubType)
    {
        string text = _nav.GetText();
        Match match = sectionOpen.Match(text);
        int end = match.Success ? text.IndexOf("</nav>", match.Index, StringComparison.OrdinalIgnoreCase) : -1;
        if (end < 0)
        {
            return null;
        }

        int sectionEnd = end + "</nav>".Length;
        string section = text[match.Index..sectionEnd];
        IElement? nav = FindNav(Parse(section), epubType);
        if (nav is null)
        {
            return null;
        }

        Match heading = HeadingAfterOpenTag.Match(section[match.Length..]);
        List<OldItem> items = new();
        foreach (IElement li in nav.QuerySelectorAll("li"))
        {
            IElement? anchor = li.Children.FirstOrDefault(c => c.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase));
            if (anchor is not null)
            {
                items.Add(new OldItem(anchor.GetAttribute("href") ?? string.Empty, NonEmpty(li.GetAttribute("id")), NonEmpty(anchor.GetAttribute("id"))));
            }
        }

        IElement? ol = nav.Children.FirstOrDefault(c => c.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase));
        List<string> allIds = nav.QuerySelectorAll("[id]").Where(e => !ReferenceEquals(e, nav)).Select(e => e.Id!).ToList();
        return new OldSection(
            match.Value, heading.Success ? heading.Groups[1].Value : null, NonEmpty(ol?.GetAttribute("id")), items, allIds,
            string.Concat(text.AsSpan(0, match.Index), text.AsSpan(sectionEnd)));
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    // The old entry that an entry of the new section continues: the same link, else the first one not taken yet that links to the same file.
    private static List<OldItem?> MatchItems(OldSection? old, IEnumerable<string> hrefs, bool byFileToo)
    {
        List<OldItem?> matches = new();
        if (old is null)
        {
            foreach (string _ in hrefs)
            {
                matches.Add(null);
            }

            return matches;
        }

        HashSet<OldItem> taken = new();
        List<string> list = hrefs.ToList();
        OldItem?[] result = new OldItem?[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            OldItem? exact = old.Items.FirstOrDefault(o => !taken.Contains(o) && o.Href == list[i]);
            if (exact is not null)
            {
                taken.Add(exact);
                result[i] = exact;
            }
        }

        if (byFileToo)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (result[i] is not null)
                {
                    continue;
                }

                OldItem? sameFile = old.Items.FirstOrDefault(o => !taken.Contains(o) && PathPart(o.Href) == PathPart(list[i]));
                if (sameFile is not null)
                {
                    taken.Add(sameFile);
                    result[i] = sameFile;
                }
            }
        }

        matches.AddRange(result);
        return matches;
    }

    // An anchor of a nav must contain text (EPUBCheck: RSC-005): an entry without any gets the given text or, failing that, the name of the
    // file it links to.
    private static string TextOrFileName(string title, string href, string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        string path = PathPart(href);
        string name = Path.GetFileNameWithoutExtension(Utility.UrlDecodePath(path));
        return name.Length > 0 ? name : href;
    }

    private static string IdAttribute(string? id) => id is null ? string.Empty : " id=\"" + WebUtility.HtmlEncode(id) + "\"";

    // The opening tag of the old nav as it was, with the id and the role of the default one added when it has none.
    private static string OpenTagOf(OldSection? old, string defaultOpenTag)
    {
        if (old is null)
        {
            return defaultOpenTag;
        }

        string tag = old.OpenTag;
        foreach (string attribute in new[] { "id", "role" })
        {
            TagLister.AttInfo wanted = TagLister.ParseAttribute(defaultOpenTag, attribute);
            if (wanted.Pos >= 0 && TagLister.ParseAttribute(tag, attribute).Pos < 0)
            {
                int close = tag.EndsWith("/>", StringComparison.Ordinal) ? tag.Length - 2 : tag.Length - 1;
                tag = tag[..close].TrimEnd() + " " + attribute + "=\"" + wanted.AValue + "\"" + tag[close..];
            }
        }

        return tag;
    }

    // The ids of the old section that the new one does not carry, as empty anchors before it (unless something else in the file has them).
    private static string OrphanAnchors(OldSection? old, string newSection)
    {
        if (old is null)
        {
            return string.Empty;
        }

        StringBuilder anchors = new();
        foreach (string id in old.AllIds.Distinct(StringComparer.Ordinal))
        {
            string quoted = "id=\"" + WebUtility.HtmlEncode(id) + "\"";
            if (!newSection.Contains(quoted, StringComparison.Ordinal) && !old.OtherText.Contains(quoted, StringComparison.Ordinal))
            {
                anchors.Append("  <span").Append(IdAttribute(id)).Append("></span>\n");
            }
        }

        return anchors.Length == 0 ? string.Empty : "\n" + anchors.ToString().TrimEnd('\n') + "\n";
    }

    private string BuildToc(IReadOnlyList<NavTocEntry> entries, OldSection? old)
    {
        const string step = "  ";
        string indentBase = step + step;
        List<OldItem?> matches = MatchItems(old, entries.Select(e => e.Href), byFileToo: true);

        StringBuilder res = new();
        res.Append('\n').Append(step).Append(OpenTagOf(old, "<nav epub:type=\"toc\" id=\"toc\" role=\"doc-toc\">")).Append('\n');
        res.Append(indentBase).Append(old?.Heading ?? "<h1>" + Landmarks.GetTitle("toc", _language) + "</h1>").Append('\n');
        res.Append(indentBase).Append("<ol").Append(IdAttribute(old?.OlId)).Append(">\n");

        int curLevel = 1;
        int previousLevel = 0;
        bool initial = true;
        for (int index = 0; index < entries.Count; index++)
        {
            NavTocEntry entry = entries[index];
            OldItem? match = matches[index];

            // A list can go one level deeper at a time and starts on the top level: an entry below that is put on the level it can be on.
            int level = Math.Clamp(entry.Level, 1, previousLevel + 1);
            previousLevel = level;
            string href = entry.Href;
            string title = Utility.EncodeXml(TextOrFileName(entry.Title, href));
            string li = "<li" + IdAttribute(match?.LiId) + ">";
            string anchor = "<a" + IdAttribute(match?.AId) + " href=\"" + href + "\">" + title + "</a>";

            if (level > curLevel)
            {
                while (level > curLevel)
                {
                    string indent = indentBase + Repeat(step, curLevel);
                    res.Append(indent).Append("<ol>\n");
                    res.Append(indent).Append(step).Append(li).Append('\n');
                    res.Append(indent).Append(step).Append(step).Append(anchor).Append('\n');
                    curLevel++;
                }
            }
            else if (level < curLevel)
            {
                while (level < curLevel)
                {
                    string indent = indentBase + Repeat(step, curLevel - 1);
                    res.Append(indent).Append(step).Append("</li>\n");
                    res.Append(indent).Append("</ol>\n");
                    curLevel--;
                }

                string closeIndent = indentBase + Repeat(step, level - 1);
                res.Append(closeIndent).Append(step).Append("</li>\n");
                res.Append(closeIndent).Append(step).Append(li).Append('\n');
                res.Append(closeIndent).Append(step).Append(step).Append(anchor).Append('\n');
            }
            else
            {
                string indent = indentBase + Repeat(step, level - 1);
                if (!initial)
                {
                    res.Append(indent).Append(step).Append("</li>\n");
                }

                res.Append(indent).Append(step).Append(li).Append('\n');
                res.Append(indent).Append(step).Append(step).Append(anchor).Append('\n');
            }

            initial = false;
            curLevel = level;
        }

        while (curLevel > 0)
        {
            string indent = indentBase + Repeat(step, curLevel - 1);
            if (!initial)
            {
                res.Append(indent).Append(step).Append("</li>\n");
            }

            res.Append(indent).Append("</ol>\n");
            curLevel--;
        }

        res.Append(step).Append("</nav>\n");
        return OrphanAnchors(old, res.ToString()) + res.ToString();
    }

    private string BuildLandmarks(IReadOnlyList<NavLandmarkEntry> entries, OldSection? old)
    {
        const string step = "  ";
        string indentBase = step + step;
        List<OldItem?> matches = MatchItems(old, entries.Select(e => e.Href), byFileToo: true);

        StringBuilder res = new();
        res.Append('\n').Append(step).Append(OpenTagOf(old, "<nav epub:type=\"landmarks\" id=\"landmarks\" hidden=\"\">")).Append('\n');
        res.Append(indentBase).Append(old?.Heading ?? "<h1>" + Landmarks.GetTitle("landmarks", _language) + "</h1>").Append('\n');
        res.Append(indentBase).Append("<ol").Append(IdAttribute(old?.OlId)).Append(">\n");

        for (int index = 0; index < entries.Count; index++)
        {
            NavLandmarkEntry entry = entries[index];
            OldItem? match = matches[index];
            res.Append(indentBase).Append(step).Append("<li").Append(IdAttribute(match?.LiId)).Append(">\n");
            res.Append(indentBase).Append(step).Append(step)
                .Append("<a").Append(IdAttribute(match?.AId)).Append(" epub:type=\"").Append(entry.EpubType).Append("\" href=\"").Append(entry.Href).Append("\">")
                .Append(Utility.EncodeXml(TextOrFileName(entry.Title, entry.Href, Landmarks.GetTitle(entry.EpubType, _language)))).Append("</a>\n");
            res.Append(indentBase).Append(step).Append("</li>\n");
        }

        res.Append(indentBase).Append("</ol>\n");
        res.Append(step).Append("</nav>\n");
        return OrphanAnchors(old, res.ToString()) + res.ToString();
    }

    private string BuildPageList(IReadOnlyList<NavPageListEntry> entries, OldSection? old)
    {
        const string step = "  ";
        string indentBase = step + step + step;
        List<OldItem?> matches = MatchItems(old, entries.Select(e => e.Href), byFileToo: false);

        StringBuilder res = new();
        res.Append('\n').Append(step).Append(OpenTagOf(old, "<nav epub:type=\"page-list\" id=\"page-list\" role=\"doc-pagelist\" hidden=\"\">")).Append('\n');
        res.Append(indentBase).Append(old?.Heading ?? "<h1>" + Landmarks.GetTitle("page-list", _language) + "</h1>").Append('\n');
        res.Append('\n').Append(indentBase).Append("<ol").Append(IdAttribute(old?.OlId)).Append(">\n");

        for (int index = 0; index < entries.Count; index++)
        {
            NavPageListEntry entry = entries[index];
            OldItem? match = matches[index];
            res.Append(indentBase).Append(step)
                .Append("<li").Append(IdAttribute(match?.LiId)).Append("><a").Append(IdAttribute(match?.AId)).Append(" href=\"").Append(entry.Href).Append("\">")
                .Append(Utility.EncodeXml(TextOrFileName(entry.PageName, entry.Href))).Append("</a></li>\n");
        }

        res.Append(indentBase).Append("</ol>\n");
        res.Append(step).Append("</nav>\n");
        return OrphanAnchors(old, res.ToString()) + res.ToString();
    }

    // --- helpers: parsing / cutting out / splice ---

    private void SpliceSection(Regex sectionOpen, string builtFragment)
    {
        string text = _nav.GetText();
        Match match = sectionOpen.Match(text);
        if (match.Success)
        {
            int end = text.IndexOf("</nav>", match.Index, StringComparison.OrdinalIgnoreCase);
            if (end > 0)
            {
                int spanStart = match.Index;
                while (spanStart > 0 && char.IsWhiteSpace(text[spanStart - 1]))
                {
                    spanStart--;
                }

                int spanEnd = end + "</nav>".Length;
                while (spanEnd < text.Length && char.IsWhiteSpace(text[spanEnd]))
                {
                    spanEnd++;
                }

                _nav.SetText(string.Concat(text.AsSpan(0, spanStart), builtFragment, text.AsSpan(spanEnd)));
                return;
            }
        }

        int bodyClose = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose >= 0)
        {
            int insertAt = bodyClose;
            while (insertAt > 0 && char.IsWhiteSpace(text[insertAt - 1]))
            {
                insertAt--;
            }

            _nav.SetText(string.Concat(text.AsSpan(0, insertAt), builtFragment, text.AsSpan(insertAt)));
        }
        else
        {
            _nav.SetText(text + builtFragment);
        }
    }

    private static string? ExtractSection(string source, Regex sectionOpen)
    {
        Match match = sectionOpen.Match(source);
        if (!match.Success)
        {
            return null;
        }

        int end = source.IndexOf("</nav>", match.Index, StringComparison.OrdinalIgnoreCase);
        return end > 0 ? source.Substring(match.Index, end - match.Index + "</nav>".Length) : null;
    }

    private static Regex MakeSectionRegex(string epubType) =>
        new(
            "<\\s*nav\\s[^>]*epub:type[^>]*[\"']" + Regex.Escape(epubType) + "[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IHtmlDocument Parse(string html) => Parser.ParseDocument(html);

    private static IElement? FindNav(IParentNode root, string epubType)
    {
        foreach (IElement nav in root.QuerySelectorAll("nav"))
        {
            if (string.Equals(GetEpubType(nav), epubType, StringComparison.Ordinal))
            {
                return nav;
            }
        }

        return null;
    }

    private static List<NavTocEntry> GetNodeToc(IElement ol, int level)
    {
        List<NavTocEntry> result = new();
        foreach (IElement li in ol.Children)
        {
            if (!li.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (IElement child in li.Children)
            {
                if (child.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new NavTocEntry
                    {
                        Level = level,
                        Href = child.GetAttribute("href") ?? string.Empty,
                        Title = LocalText(child),
                    });
                }
                else if (child.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(GetNodeToc(child, level + 1));
                }
            }
        }

        return result;
    }

    private static string GetEpubType(IElement element)
    {
        foreach (IAttr attribute in element.Attributes)
        {
            if (attribute.Name.Equals("epub:type", StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return string.Empty;
    }

    private static string LocalText(IElement element)
    {
        StringBuilder sb = new();
        foreach (INode node in element.ChildNodes)
        {
            if (node.NodeType == NodeType.Text)
            {
                sb.Append(node.TextContent);
            }
        }

        return sb.ToString().Trim();
    }

    private static string Repeat(string value, int count) =>
        count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(value, count));

    // --- helpers: href conversions ---

    private int GetResourceLandmarkPos(Resource resource, IReadOnlyList<NavLandmarkEntry> list, string targetId)
    {
        string target = Utility.UrlEncodePath(resource.BookPath);
        if (targetId.Length > 0)
        {
            target = target + "#" + targetId;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(ConvertHrefToBookPath(list[i].Href), target, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private Dictionary<string, IReadOnlyList<string>> GroupLandmarks(Func<NavLandmarkEntry, string> selector)
    {
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);
        foreach (NavLandmarkEntry entry in GetLandmarks())
        {
            string key = PathPart(ConvertHrefToBookPath(entry.Href));
            if (!map.TryGetValue(key, out List<string>? values))
            {
                values = new List<string>();
                map[key] = values;
            }

            values.Add(selector(entry));
        }

        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<string>> pair in map)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private string ConvertHrefToBookPath(string navRelativeHref)
    {
        if (navRelativeHref.Contains(':', StringComparison.Ordinal))
        {
            return navRelativeHref;
        }

        (string basePart, string fragment) = SplitFragment(navRelativeHref);
        string basePath = Utility.UrlDecodePath(basePart);

        string bookPath;
        if (basePath is "./" or "")
        {
            bookPath = Utility.UrlEncodePath(_nav.BookPath);
        }
        else
        {
            bookPath = Utility.UrlEncodePath(Core.BookPath.BuildBookPath(basePath, _nav.Folder));
        }

        return fragment.Length > 0 ? bookPath + "#" + fragment : bookPath;
    }

    private string ConvertBookPathToNavRelative(string bookPath)
    {
        (string basePart, string fragment) = SplitFragment(bookPath);
        string destination = Utility.UrlDecodePath(basePart);
        string newHref = Utility.UrlEncodePath(Core.BookPath.Relative(_nav.BookPath, destination));
        if (fragment.Length > 0)
        {
            newHref = newHref + "#" + fragment;
        }

        return newHref.Length == 0 ? "#" : newHref;
    }

    private string NavHrefToNcxRelative(string navRelativeHref, string ncxBookPath)
    {
        (string basePart, string fragment) = SplitFragment(ConvertHrefToBookPath(navRelativeHref));
        string destination = Utility.UrlDecodePath(basePart);
        string relative = Utility.UrlEncodePath(Core.BookPath.Relative(ncxBookPath, destination));
        return fragment.Length > 0 ? relative + "#" + fragment : relative;
    }

    private string NcxRelativeToNavHref(string ncxRelativeSrc, string ncxBookPath)
    {
        if (ncxRelativeSrc.Contains(':', StringComparison.Ordinal))
        {
            return ncxRelativeSrc;
        }

        (string basePart, string fragment) = SplitFragment(ncxRelativeSrc);
        string destination = Core.BookPath.BuildBookPath(Utility.UrlDecodePath(basePart), Core.BookPath.StartingDir(ncxBookPath));
        string combined = fragment.Length > 0
            ? Utility.UrlEncodePath(destination) + "#" + fragment
            : Utility.UrlEncodePath(destination);
        return ConvertBookPathToNavRelative(combined);
    }

    private static (string Path, string Fragment) SplitFragment(string value)
    {
        int hash = value.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (value, string.Empty) : (value[..hash], value[(hash + 1)..]);
    }

    private static string PathPart(string value) => SplitFragment(value).Path;
}
