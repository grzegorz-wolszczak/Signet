using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.Reports;

/// <summary>A row of the "All Files" report.</summary>
public sealed record AllFilesRow(string BookPath, string Name, string TypeName, long SizeBytes, bool InSpine);

/// <summary>A row of the "HTML Files" report.</summary>
public sealed record HtmlFilesRow(string BookPath, string Name, long SizeBytes, int WordCount, bool WellFormed);

/// <summary>A row of the "Image Files" report.</summary>
public readonly record struct ImageFilesRow(
    string BookPath, string Name, string Format, long SizeBytes, int Width, int Height, IReadOnlyList<string> UsedIn);

/// <summary>A row of the "CSS Files" report.</summary>
public sealed record CssFilesRow(string BookPath, string Name, long SizeBytes, int SelectorCount);

/// <summary>
/// A row of the "Classes in HTML" report — the usage of a single element.class pair in an (X)HTML file.
/// </summary>
/// <param name="HtmlBookPath">The bookpath of the (X)HTML file containing this element.class pair.</param>
/// <param name="ElementName">The element's tag name (lowercase).</param>
/// <param name="ClassName">The CSS class name.</param>
/// <param name="CssBookPath">The bookpath of the file in which a selector was matched, or <c>null</c> when unused.</param>
/// <param name="SelectorText">The text of the matched selector, or <c>null</c> when unused.</param>
/// <param name="Position">
/// The 0-based offset of the first occurrence of this element.class pair in <paramref name="HtmlBookPath"/>
/// (for "go to") — not the position of the CSS selector.
/// </param>
public sealed record HtmlClassUsageRow(
    string HtmlBookPath, string ElementName, string ClassName, string? CssBookPath, string? SelectorText, int Position)
{
    /// <summary>Whether the element.class pair matched any CSS selector ("unused in CSS" when <c>false</c>).</summary>
    public bool IsUsed => CssBookPath is not null;
}

/// <summary>
/// A row of the "Links" report — a single <c>&lt;a href&gt;</c> element (internal/external links, broken references).
/// </summary>
/// <param name="HtmlBookPath">The bookpath of the (X)HTML file containing the link.</param>
/// <param name="Position">The 0-based offset of the <c>&lt;a&gt;</c> element in the file source.</param>
/// <param name="Text">The link text (<c>TextContent</c>).</param>
/// <param name="TargetHref">The raw value of the <c>href</c> attribute.</param>
/// <param name="IsInternal">Whether the reference is internal (no scheme, e.g. not <c>http:</c>/<c>mailto:</c>).</param>
/// <param name="TargetExists">
/// <c>true</c>/<c>false</c> for internal links; <c>null</c> ("n/a") for external links
/// and empty <c>href</c>s.
/// </param>
public sealed record LinkRow(
    string HtmlBookPath, int Position, string Text, string TargetHref, bool IsInternal, bool? TargetExists);

/// <summary>
/// A row of the "Characters in HTML" report — a histogram of a single non-ASCII code point.
/// </summary>
public readonly record struct CharacterUsageRow(int CodePoint, int Count, IReadOnlyList<string> FoundIn);

/// <summary>The word/character count of a single (X)HTML file — a row of the "Word &amp; Character Counts" report.</summary>
public sealed record FileWordCountRow(string BookPath, int Words, int Characters);

/// <summary>
/// The "Word &amp; Character Counts" report — words/characters per
/// file and in total.
/// </summary>
public sealed record WordCharacterCountsReport(IReadOnlyList<FileWordCountRow> Files, int TotalWords, int TotalCharacters);

/// <summary>
/// The data engine for the "Reports" dialog. Pure logic with no UI dependency — testable directly on a <see cref="Book"/>.
/// </summary>
public static class BookReportEngine
{
    private static readonly Regex StyleBlock = new("<style[^<]*</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagStrip = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WordToken = new(@"[\p{L}\p{Nd}][\p{L}\p{Nd}'\-]*", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex CssUrlRef = new("url\\(\\s*['\"]?([^'\")]+)", RegexOptions.Compiled);

    /// <summary>The "All Files" report (name, type, size, in spine?).</summary>
    public static IReadOnlyList<AllFilesRow> GetAllFiles(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        HashSet<string> spineBookPaths = new(book.GetOpf().GetSpineOrderBookPaths(), StringComparer.Ordinal);

        List<AllFilesRow> rows = new();
        foreach (Resource resource in book.GetAllResources())
        {
            rows.Add(new AllFilesRow(
                resource.BookPath,
                resource.Filename,
                FriendlyTypeName(resource.Type),
                FileSize(resource),
                spineBookPaths.Contains(resource.BookPath)));
        }

        return rows;
    }

    /// <summary>The "HTML Files" report (size, word count, well-formed).</summary>
    public static IReadOnlyList<HtmlFilesRow> GetHtmlFiles(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<HtmlFilesRow> rows = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            rows.Add(new HtmlFilesRow(
                html.BookPath,
                html.Filename,
                FileSize(html),
                CountWords(text),
                IsWellFormed(html)));
        }

        return rows;
    }

    /// <summary>The "Image Files" report (dimensions, size, format, used in).</summary>
    public static IReadOnlyList<ImageFilesRow> GetImageFiles(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        Dictionary<string, List<string>> usedIn = BuildImageUsageMap(book);

        List<ImageFilesRow> rows = new();
        foreach (ImageResource image in book.GetFolderKeeper().GetResourceTypeList<ImageResource>())
        {
            (int width, int height) = image.GetDimensions();
            usedIn.TryGetValue(image.BookPath, out List<string>? users);
            rows.Add(new ImageFilesRow(
                image.BookPath,
                image.Filename,
                FriendlyImageFormat(image),
                image.FileSize,
                width,
                height,
                (IReadOnlyList<string>?)users ?? Array.Empty<string>()));
        }

        return rows;
    }

    /// <summary>The "CSS Files" report (size, selector count).</summary>
    public static IReadOnlyList<CssFilesRow> GetCssFiles(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<CssFilesRow> rows = new();
        foreach (CssResource css in book.GetFolderKeeper().GetResourceTypeList<CssResource>())
        {
            CssInfo info = new(css.GetText());
            rows.Add(new CssFilesRow(css.BookPath, css.Filename, FileSize(css), info.GetAllSelectors().Count));
        }

        return rows;
    }

    /// <summary>
    /// The "Classes in HTML" report (class → files/occurrences, "unused in CSS").
    /// </summary>
    public static IReadOnlyList<HtmlClassUsageRow> GetHtmlClassUsage(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        IReadOnlyList<HtmlResource> htmlResources = book.GetHtmlResources();
        IReadOnlyList<CssResource> cssResources = book.GetFolderKeeper().GetResourceTypeList<CssResource>();

        Dictionary<string, CssInfo> cssInfos = new(StringComparer.Ordinal);
        foreach (CssResource css in cssResources)
        {
            cssInfos[css.BookPath] = new CssInfo(css.GetText());
        }

        List<HtmlClassUsageRow> rows = new();
        foreach (HtmlResource html in htmlResources)
        {
            string text = html.GetText();
            IHtmlDocument document = XhtmlDoc.Parse(text);

            List<(string Element, string Class, int Position)> pairs = new();
            HashSet<(string, string)> seen = new();
            foreach (IElement element in document.All)
            {
                foreach (string className in element.ClassList)
                {
                    if (className.Length == 0 ||
                        string.Equals(className, Headings.SignetNotInTocClass, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (seen.Add((element.LocalName, className)))
                    {
                        pairs.Add((element.LocalName, className, XhtmlDoc.OffsetFromNode(element)));
                    }
                }
            }

            IReadOnlyList<string> linkedStylesheets = html.GetLinkedStylesheets();
            HtmlStyleInfo styleInfo = new(text);

            foreach ((string elementName, string className, int firstUsePosition) in pairs)
            {
                string? matchedCssBookPath = null;
                string? matchedSelectorText = null;

                if (styleInfo.HasStyles)
                {
                    CssSelector? selector = styleInfo.GetCssSelectorForElementClass(elementName, className);
                    if (selector is { ClassName.Length: > 0 } found)
                    {
                        matchedCssBookPath = html.BookPath;
                        matchedSelectorText = found.Text;
                    }
                }

                if (matchedCssBookPath is null)
                {
                    foreach (string cssBookPath in linkedStylesheets)
                    {
                        if (!cssInfos.TryGetValue(cssBookPath, out CssInfo? info))
                        {
                            continue;
                        }

                        CssSelector? selector = info.GetCssSelectorForElementClass(elementName, className);
                        if (selector is { ClassName.Length: > 0 } found)
                        {
                            matchedCssBookPath = cssBookPath;
                            matchedSelectorText = found.Text;
                            break;
                        }
                    }
                }

                rows.Add(new HtmlClassUsageRow(
                    html.BookPath, elementName, className, matchedCssBookPath, matchedSelectorText, firstUsePosition));
            }
        }

        return rows;
    }

    /// <summary>
    /// The "Styles in CSS" report (selector → defined in / used in HTML / unused). Delegates
    /// directly to <see cref="CssSelectorUsageAnalyzer.GetSelectorUsage"/>.
    /// </summary>
    public static IReadOnlyList<CssSelectorUsage> GetStylesInCss(Book book) =>
        CssSelectorUsageAnalyzer.GetSelectorUsage(book);

    /// <summary>The "Links" report (internal/external links, broken references).</summary>
    public static IReadOnlyList<LinkRow> GetLinks(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        HashSet<string> htmlBookPaths = new(book.GetHtmlResources().Select(h => h.BookPath), StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyList<string>> idsByFile = book.GetIdsInHtmlFiles();

        List<LinkRow> rows = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            IHtmlDocument document = XhtmlDoc.Parse(html.GetText());
            foreach (IElement anchor in document.QuerySelectorAll("a"))
            {
                string href = anchor.GetAttribute("href") ?? string.Empty;
                string text = anchor.TextContent.Trim();
                int position = XhtmlDoc.OffsetFromNode(anchor);

                bool isAbsolute = href.Contains(':', StringComparison.Ordinal);
                bool isInternal = !isAbsolute;
                bool? exists;

                if (href.Length == 0 || isAbsolute)
                {
                    exists = null;
                }
                else
                {
                    string path = href;
                    string fragment = string.Empty;
                    int hash = path.IndexOf('#', StringComparison.Ordinal);
                    if (hash >= 0)
                    {
                        fragment = path[(hash + 1)..];
                        path = path[..hash];
                    }

                    int query = path.IndexOf('?', StringComparison.Ordinal);
                    if (query >= 0)
                    {
                        path = path[..query];
                    }

                    string targetBookPath = path.Length == 0 ? html.BookPath : BookPath.BuildBookPath(path, html.Folder);
                    bool fileExists = htmlBookPaths.Contains(targetBookPath);
                    bool fragmentOk = fragment.Length == 0 ||
                        (idsByFile.TryGetValue(targetBookPath, out IReadOnlyList<string>? ids) && ids.Contains(fragment));
                    exists = fileExists && fragmentOk;
                }

                rows.Add(new LinkRow(html.BookPath, position, text, href, isInternal, exists));
            }
        }

        return rows;
    }

    /// <summary>The "Characters in HTML" report (a histogram of non-ASCII characters, with their names).</summary>
    public static IReadOnlyList<CharacterUsageRow> GetCharacterUsage(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        Dictionary<int, (int Count, List<string> Files)> byCodePoint = new();

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            IHtmlDocument document = XhtmlDoc.Parse(html.GetText());
            string bodyText = document.Body?.TextContent ?? string.Empty;

            HashSet<int> seenInThisFile = new();
            int i = 0;
            while (i < bodyText.Length)
            {
                int codePoint = char.ConvertToUtf32(bodyText, i);
                i += char.IsSurrogatePair(bodyText, i) ? 2 : 1;

                if (codePoint <= 0x7F)
                {
                    continue;
                }

                if (!byCodePoint.TryGetValue(codePoint, out (int Count, List<string> Files) entry))
                {
                    entry = (0, new List<string>());
                    byCodePoint[codePoint] = entry;
                }

                entry.Count++;
                if (seenInThisFile.Add(codePoint))
                {
                    entry.Files.Add(html.BookPath);
                }

                byCodePoint[codePoint] = entry;
            }
        }

        return byCodePoint
            .Select(kv => new CharacterUsageRow(kv.Key, kv.Value.Count, kv.Value.Files))
            .ToList();
    }

    /// <summary>
    /// The "Word &amp; Character Counts" report (words/characters per file and in total) — see
    /// <see cref="WordCharacterCountsReport"/>.
    /// </summary>
    public static WordCharacterCountsReport GetWordCharacterCounts(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<FileWordCountRow> rows = new();
        int totalWords = 0;
        int totalCharacters = 0;

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string plainText = ExtractPlainText(html.GetText());
            int words = WordToken.Count(plainText);
            int characters = WhitespaceRun.Replace(plainText, " ").Trim().Length;

            rows.Add(new FileWordCountRow(html.BookPath, words, characters));
            totalWords += words;
            totalCharacters += characters;
        }

        return new WordCharacterCountsReport(rows, totalWords, totalCharacters);
    }

    private static int CountWords(string htmlText) => WordToken.Count(ExtractPlainText(htmlText));

    private static string ExtractPlainText(string htmlText)
    {
        string noStyle = StyleBlock.Replace(htmlText, static m => new string(' ', m.Length));
        string noTags = TagStrip.Replace(noStyle, " ");
        return WebUtility.HtmlDecode(noTags);
    }

    private static bool IsWellFormed(HtmlResource html)
    {
        try
        {
            return WellFormedChecker.IsWellFormed(html.GetText(), html.MediaType);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static long FileSize(Resource resource) =>
        System.IO.File.Exists(resource.FullPath) ? new System.IO.FileInfo(resource.FullPath).Length : 0;

    private static string FriendlyImageFormat(ImageResource image) => image.MediaType switch
    {
        "image/png" => "PNG",
        "image/jpeg" => "JPEG",
        "image/gif" => "GIF",
        "image/bmp" => "BMP",
        "image/webp" => "WEBP",
        _ => System.IO.Path.GetExtension(image.Filename).TrimStart('.').ToUpperInvariant(),
    };

    private static string FriendlyTypeName(ResourceType type) => type switch
    {
        ResourceType.Html => "HTML",
        ResourceType.Css => "CSS",
        ResourceType.Image => "Image",
        ResourceType.Svg => "SVG",
        ResourceType.Font => "Font",
        ResourceType.Opf => "OPF",
        ResourceType.Ncx => "NCX",
        ResourceType.MiscText => "Misc",
        ResourceType.Audio => "Audio",
        ResourceType.Video => "Video",
        ResourceType.Pdf => "PDF",
        ResourceType.Xml => "XML",
        ResourceType.Text => "Text",
        _ => "Generic",
    };

    private static Dictionary<string, List<string>> BuildImageUsageMap(Book book)
    {
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);

        void MarkUsed(string imageBookPath, string userBookPath)
        {
            if (!map.TryGetValue(imageBookPath, out List<string>? users))
            {
                users = new List<string>();
                map[imageBookPath] = users;
            }

            if (!users.Contains(userBookPath))
            {
                users.Add(userBookPath);
            }
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            foreach (string linked in html.GetPathsToLinkedResources())
            {
                MarkUsed(linked, html.BookPath);
            }
        }

        foreach (CssResource css in book.GetFolderKeeper().GetResourceTypeList<CssResource>())
        {
            foreach (Match match in CssUrlRef.Matches(css.GetText()))
            {
                string reference = match.Groups[1].Value.Trim();
                if (reference.Length == 0 || reference.Contains(':', StringComparison.Ordinal))
                {
                    continue;
                }

                MarkUsed(BookPath.BuildBookPath(reference, css.Folder), css.BookPath);
            }
        }

        return map;
    }
}
