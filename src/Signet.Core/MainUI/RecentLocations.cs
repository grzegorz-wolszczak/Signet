using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Parsers;

namespace Signet.Core.MainUI;

/// <summary>What a breadcrumb of a <see cref="RecentLocations"/> entry describes.</summary>
public enum BreadcrumbKind
{
    /// <summary>No breadcrumb (plain text, JavaScript…).</summary>
    None,

    /// <summary>The path of elements around the place ((X)HTML, XML, SVG).</summary>
    Markup,

    /// <summary>The selector of the CSS rule around the place.</summary>
    Css,
}

/// <summary>A few lines of text around a place (<see cref="RecentLocations.Snippet"/>).</summary>
/// <param name="FirstLine">The number (1-based) of the first line.</param>
/// <param name="Text">The lines, separated by <c>\n</c>.</param>
public sealed record LocationSnippet(int FirstLine, string Text);

/// <summary>
/// The content of the Recent Locations popup (modelled on IntelliJ's RecentLocationsDataModel): which places to show,
/// the lines of text around each one and its breadcrumb.
/// </summary>
public static class RecentLocations
{
    /// <summary>The default number of places shown.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The largest number of places shown.</summary>
    public const int MaxLimit = 50;

    /// <summary>How many lines around the place a snippet shows on each side (IntelliJ shows 2).</summary>
    public const int ContextLines = 2;

    /// <summary>
    /// The places to show, newest first: only places with a caret in files that still exist, at most one of the
    /// places that are the same (<see cref="NavigationHistory.IsSame"/>), at most <paramref name="limit"/>.
    /// </summary>
    /// <param name="places">The places, oldest first (<see cref="NavigationHistory.BackPlaces"/> / <see cref="NavigationHistory.EditedPlaces"/>).</param>
    /// <param name="limit">The largest number of places.</param>
    /// <param name="exists">Whether a file with the given bookpath exists.</param>
    public static IReadOnlyList<NavigationPlace> Pick(IReadOnlyList<NavigationPlace> places, int limit, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(exists);
        List<NavigationPlace> result = new();
        for (int i = places.Count - 1; i >= 0 && result.Count < limit; i--)
        {
            NavigationPlace place = places[i];
            if (place.HasCaret && exists(place.BookPath) && !result.Any(r => NavigationHistory.IsSame(r, place)))
            {
                result.Add(place);
            }
        }

        return result;
    }

    /// <summary>
    /// The lines around <paramref name="offset"/>: <see cref="ContextLines"/> before and after (more on one side near
    /// the start or the end of the text, so there are always up to <c>2 × ContextLines + 1</c> lines), without blank
    /// lines at the edges.
    /// </summary>
    public static LocationSnippet Snippet(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int line = LineIndexAt(text, Math.Clamp(offset, 0, text.Length));
        int before = Math.Min(ContextLines, line);
        int after = Math.Min(ContextLines, lines.Length - 1 - line);
        int first = Math.Max(line - (before + ContextLines - after), 0);
        int last = Math.Min(line + (after + ContextLines - before), lines.Length - 1);

        while (first < line && string.IsNullOrWhiteSpace(lines[first]))
        {
            first++;
        }

        while (last > line && string.IsNullOrWhiteSpace(lines[last]))
        {
            last--;
        }

        return new LocationSnippet(first + 1, string.Join('\n', lines[first..(last + 1)]));
    }

    /// <summary>
    /// The breadcrumb of the place: for markup the elements around it (from <c>body</c> when the place is inside it),
    /// e.g. <c>body &gt; section#ch1 &gt; p.indent</c>; for CSS the selector of the rule (with its <c>@media</c>…
    /// prelude). Empty when there is nothing to show.
    /// </summary>
    public static string Breadcrumb(string text, int offset, BreadcrumbKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        return kind switch
        {
            BreadcrumbKind.Markup => MarkupBreadcrumb(text, Math.Clamp(offset, 0, text.Length)),
            BreadcrumbKind.Css => CssBreadcrumb(text, Math.Clamp(offset, 0, text.Length)),
            _ => string.Empty,
        };
    }

    private static int LineIndexAt(string text, int offset)
    {
        int line = 0;
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    // The open elements at the offset: a stack of opening tags walked in source order.
    private static string MarkupBreadcrumb(string text, int offset)
    {
        TagLister lister = new(text);
        List<TagLister.TagInfo> open = new();
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Pos < 0 || tag.Pos >= offset)
            {
                break;
            }

            switch (tag.Kind)
            {
                case TagKind.Begin:
                    open.Add(tag);
                    break;
                case TagKind.End:
                    int match = open.FindLastIndex(t => string.Equals(t.TagName, tag.TagName, StringComparison.OrdinalIgnoreCase));
                    if (match >= 0)
                    {
                        open.RemoveRange(match, open.Count - match);
                    }

                    break;
                case TagKind.SelfClosing when offset < tag.Pos + tag.Len:
                    open.Add(tag);
                    break;
            }
        }

        int body = open.FindIndex(t => string.Equals(t.TagName, "body", StringComparison.OrdinalIgnoreCase));
        return string.Join(" > ", open.Skip(Math.Max(body, 0)).Select(t => Describe(text, t)));
    }

    // "name#id.class1.class2".
    private static string Describe(string text, TagLister.TagInfo tag)
    {
        string source = text.Substring(tag.Pos, tag.Len);
        string id = TagLister.ParseAttribute(source, "id").AValue;
        string classes = TagLister.ParseAttribute(source, "class").AValue;
        string result = tag.TagName;
        if (id.Length > 0)
        {
            result += "#" + id;
        }

        foreach (string cls in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            result += "." + cls;
        }

        return result;
    }

    private static string CssBreadcrumb(string text, int offset)
    {
        CssRule? rule = new CssInfo(text).Rules.LastOrDefault(r => r.SelectorStart <= offset && offset < r.BlockEnd);
        if (rule is null)
        {
            return string.Empty;
        }

        string selector = rule.SelectorText.Trim();
        return rule.AtRulePrelude is { Length: > 0 } prelude
            ? selector.Length > 0 ? $"{prelude.Trim()} > {selector}" : prelude.Trim()
            : selector;
    }
}
