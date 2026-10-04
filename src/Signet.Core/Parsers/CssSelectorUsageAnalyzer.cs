using System;
using System.Collections.Generic;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Parsers;

/// <summary>The marker used instead of a file name when a selector was deemed used because of a parse error.</summary>
/// <remarks>Its value is <c>"*** Selector Parse Error ***"</c>.</remarks>
file static class Markers
{
    public const string SelectorParseError = "*** Selector Parse Error ***";
}

/// <summary>
/// The usage of a single CSS selector in the book — the result of <see cref="CssSelectorUsageAnalyzer.GetSelectorUsage"/>.
/// </summary>
/// <param name="CssBookPath">The bookpath of the stylesheet (or of the XHTML file for a selector from a <c>&lt;style&gt;</c> block) in which the selector is defined.</param>
/// <param name="SelectorText">The selector text.</param>
/// <param name="Position">The 0-based offset of the selector in its file.</param>
/// <param name="UsedInHtmlBookPath">
/// The bookpath of the first XHTML file in which the selector matched something (or <c>"*** Selector Parse Error ***"</c>),
/// or <c>null</c> when the selector is unused.
/// </param>
public readonly record struct CssSelectorUsage(string CssBookPath, string SelectorText, int Position, string? UsedInHtmlBookPath)
{
    /// <summary>Whether the selector is used (it matched something in some XHTML or its parsing failed).</summary>
    public bool IsUsed => UsedInHtmlBookPath is not null;
}

/// <summary>
/// Detecting unused CSS selectors in the book. A selector is "used" if it matches something in any associated
/// XHTML file (state pseudo-classes and pseudo-elements stripped, <see cref="CssSelectorMatching"/>) — or if the
/// selector is invalid (then, to be safe, it is considered used).
/// </summary>
public static class CssSelectorUsageAnalyzer
{
    /// <summary>
    /// Returns the usage of every selector defined in CSS stylesheets and in the
    /// <c>&lt;style&gt;</c> blocks of XHTML files. Selectors from a stylesheet are tested only on the XHTML files
    /// that use that stylesheet — link it (<c>&lt;link rel="stylesheet"&gt;</c>) or import it through <c>@import</c>
    /// (<see cref="Book.GetVisibleStylesheets"/>).
    /// </summary>
    public static IReadOnlyList<CssSelectorUsage> GetSelectorUsage(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        IReadOnlyList<CssResource> cssResources = folderKeeper.GetResourceTypeList<CssResource>();
        IReadOnlyList<HtmlResource> htmlResources = book.GetHtmlResources();

        Dictionary<string, CssInfo> cssInfos = new(StringComparer.Ordinal);
        foreach (CssResource css in cssResources)
        {
            cssInfos[css.BookPath] = new CssInfo(css.GetText());
        }

        // key -> distinct html bookpaths (first one wins for the report)
        Dictionary<UsageKey, List<string>> used = new();

        void MarkUsed(UsageKey key, string htmlFile)
        {
            if (!used.TryGetValue(key, out List<string>? files))
            {
                files = new List<string>();
                used[key] = files;
            }

            if (!files.Contains(htmlFile))
            {
                files.Add(htmlFile);
            }
        }

        foreach (HtmlResource html in htmlResources)
        {
            string text = html.GetText();
            IHtmlDocument document = XhtmlDoc.Parse(text);

            foreach (string cssBookPath in book.GetVisibleStylesheets(html))
            {
                if (!cssInfos.TryGetValue(cssBookPath, out CssInfo? info))
                {
                    continue;
                }

                foreach (CssSelector selector in info.GetAllSelectors())
                {
                    (bool matched, bool parseError) = TestSelector(document, selector.Text);
                    if (matched || parseError)
                    {
                        MarkUsed(
                            new UsageKey(cssBookPath, selector.Pos, selector.Text),
                            parseError ? Markers.SelectorParseError : html.BookPath);
                    }
                }
            }

            HtmlStyleInfo styleInfo = new(text);
            if (styleInfo.HasStyles)
            {
                foreach (CssSelector selector in styleInfo.GetAllSelectors())
                {
                    (bool matched, bool parseError) = TestSelector(document, selector.Text);
                    if (matched || parseError)
                    {
                        MarkUsed(
                            new UsageKey(html.BookPath, selector.Pos, selector.Text),
                            parseError ? Markers.SelectorParseError : html.BookPath);
                    }
                }
            }
        }

        List<CssSelectorUsage> result = new();

        foreach (KeyValuePair<string, CssInfo> entry in cssInfos)
        {
            foreach (CssSelector selector in entry.Value.GetAllSelectors())
            {
                used.TryGetValue(new UsageKey(entry.Key, selector.Pos, selector.Text), out List<string>? files);
                result.Add(new CssSelectorUsage(entry.Key, selector.Text, selector.Pos, files is { Count: > 0 } ? files[0] : null));
            }
        }

        foreach (HtmlResource html in htmlResources)
        {
            HtmlStyleInfo styleInfo = new(html.GetText());
            if (!styleInfo.HasStyles)
            {
                continue;
            }

            foreach (CssSelector selector in styleInfo.GetAllSelectors())
            {
                used.TryGetValue(new UsageKey(html.BookPath, selector.Pos, selector.Text), out List<string>? files);
                result.Add(new CssSelectorUsage(html.BookPath, selector.Text, selector.Pos, files is { Count: > 0 } ? files[0] : null));
            }
        }

        return result;
    }

    /// <summary>
    /// Selectors that are defined but never used:
    /// skips the Media Overlays "active class" selectors (<c>OpfResource.GetMediaOverlayActiveClassSelectors</c>).
    /// </summary>
    public static IReadOnlyList<CssSelectorUsage> GetUnusedSelectors(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        HashSet<string> active = new(book.GetOpf().GetMediaOverlayActiveClassSelectors(), StringComparer.Ordinal);

        List<CssSelectorUsage> unused = new();
        foreach (CssSelectorUsage usage in GetSelectorUsage(book))
        {
            if (!usage.IsUsed && !active.Contains(usage.SelectorText))
            {
                unused.Add(usage);
            }
        }

        return unused;
    }

    // State pseudo-classes and pseudo-elements are stripped first (CssSelectorMatching): "a:hover" or "p::before"
    // never match a static document, but they are used wherever "a"/"p" exist.
    private static (bool Matched, bool ParseError) TestSelector(IParentNode document, string selectorText)
    {
        if (string.IsNullOrWhiteSpace(selectorText))
        {
            return (false, true);
        }

        bool? matched = CssSelectorMatching.TryMatchAny(document, selectorText);
        return matched is { } value ? (value, false) : (false, true);
    }

    private readonly record struct UsageKey(string BookPath, int Position, string SelectorText);
}
