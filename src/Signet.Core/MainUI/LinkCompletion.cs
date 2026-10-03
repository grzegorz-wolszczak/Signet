using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.MainUI;

/// <summary>Which files to suggest.</summary>
public enum LinkTargetKind
{
    /// <summary>All the book's files.</summary>
    Any,

    /// <summary>Text documents (<c>&lt;a href&gt;</c>).</summary>
    TextLink,

    /// <summary>Images (<c>&lt;img src&gt;</c>, <c>&lt;image&gt;</c>).</summary>
    Image,

    /// <summary>Stylesheets (<c>&lt;link href&gt;</c>).</summary>
    Stylesheet,

    /// <summary>Images and fonts (<c>url()</c> in CSS).</summary>
    CssResource,
}

/// <summary>
/// The suggestion site under the caret: the replaced fragment
/// [<see cref="ReplaceStart"/>, caret) with the <see cref="Query"/> already typed.
/// </summary>
/// <param name="ReplaceStart">The start of the replaced text in the document.</param>
/// <param name="Query">The text typed from <see cref="ReplaceStart"/> to the caret.</param>
/// <param name="Kind">The kind of files suggested (for file names).</param>
/// <param name="AnchorHref">
/// For anchors — the part of the reference before <c>#</c> (empty — the current file); <c>null</c> — file
/// names are suggested.
/// </param>
public sealed record LinkCompletionContext(int ReplaceStart, string Query, LinkTargetKind Kind, string? AnchorHref);

/// <summary>A book file offered for suggestions.</summary>
/// <param name="BookPath">The file's bookpath.</param>
/// <param name="MediaType">The MIME type.</param>
public sealed record LinkCompletionFile(string BookPath, string MediaType);

/// <summary>A suggestion entry.</summary>
/// <param name="Text">The inserted text (a relative href or an anchor id).</param>
/// <param name="Description">The description (the file kind or the anchor element's text).</param>
public sealed record LinkCompletionItem(string Text, string Description);

/// <summary>
/// Suggestions in Code View: the book's file names in <c>href</c>/<c>src</c>
/// (filtered by tag) and in CSS <c>url()</c>, <c>#id</c> anchors of the target document,
/// and fuzzy matching (the best-scoring substring).
/// </summary>
public static partial class LinkCompletion
{
    private const int MaxResults = 50;

    /// <summary>
    /// The suggestion context for the caret or <c>null</c>: in (X)HTML/XML — the caret in the
    /// <c>href</c>/<c>src</c> value of an opening tag (the tag started at most 5 lines above);
    /// in CSS (and in <c>&lt;style&gt;</c>) — in the <c>url(</c> argument.
    /// </summary>
    public static LinkCompletionContext? Detect(string text, int caret, CodeViewSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caret, 0, text.Length);
        int lineStart = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;

        if (syntax is CodeViewSyntax.Css or CodeViewSyntax.Html &&
            CssUrlRegex().Match(text[lineStart..pos]) is { Success: true } url)
        {
            Group q = url.Groups["query"];
            return new LinkCompletionContext(lineStart + q.Index, q.Value, LinkTargetKind.CssResource, null);
        }

        if (syntax is not (CodeViewSyntax.Html or CodeViewSyntax.Xml))
        {
            return null;
        }

        int tagStart = text.LastIndexOf('<', Math.Max(pos - 1, 0));
        if (tagStart < 0 || text.IndexOf('>', tagStart, pos - tagStart) >= 0 || CountLines(text, tagStart, pos) > 5)
        {
            return null;
        }

        string tagText = text[tagStart..pos];
        if (TagNameRegex().Match(tagText) is not { Success: true } tagName ||
            AttributeRegex().Match(tagText) is not { Success: true } attribute)
        {
            return null;
        }

        string attributeName = attribute.Groups["name"].Value.ToLowerInvariant();
        attributeName = attributeName[(attributeName.LastIndexOf(':') + 1)..];
        if (attributeName is not ("href" or "src"))
        {
            return null;
        }

        Group value = attribute.Groups["sq"].Success ? attribute.Groups["sq"] : attribute.Groups["dq"];
        int valueStart = tagStart + value.Index;
        LinkTargetKind kind = tagName.Groups[1].Value.ToLowerInvariant() switch
        {
            "a" => LinkTargetKind.TextLink,
            "img" or "image" => LinkTargetKind.Image,
            "link" => LinkTargetKind.Stylesheet,
            _ => LinkTargetKind.Any,
        };

        int hash = value.Value.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0 && kind is LinkTargetKind.Any or LinkTargetKind.TextLink)
        {
            return new LinkCompletionContext(valueStart + hash + 1, value.Value[(hash + 1)..], kind, value.Value[..hash]);
        }

        return new LinkCompletionContext(valueStart, value.Value, kind, null);
    }

    private static int CountLines(string text, int from, int to)
    {
        int lines = 0;
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>
    /// Relative references from the file <paramref name="ownerBookPath"/> to files of the given kind
    /// (segments encoded as in a URL), with a description of the file kind; sorted.
    /// </summary>
    public static IReadOnlyList<LinkCompletionItem> FileCandidates(
        IEnumerable<LinkCompletionFile> files, string ownerBookPath, LinkTargetKind kind)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(ownerBookPath);
        return files
            .Where(f => !string.Equals(f.BookPath, ownerBookPath, StringComparison.Ordinal) || kind == LinkTargetKind.Any)
            .Select(f => (File: f, Type: Describe(f.MediaType)))
            .Where(x => kind switch
            {
                LinkTargetKind.TextLink => x.Type == "Text",
                LinkTargetKind.Image => x.Type == "Image",
                LinkTargetKind.Stylesheet => x.Type == "Stylesheet",
                LinkTargetKind.CssResource => x.Type is "Image" or "Font",
                _ => true,
            })
            .Select(x => new LinkCompletionItem(Href(ownerBookPath, x.File.BookPath), x.Type))
            .DistinctBy(i => i.Text, StringComparer.Ordinal)
            .OrderBy(i => i.Text, NaturalComparer.Instance)
            .ToList();
    }

    private static string Describe(string mediaType) => MediaTypes.GetResourceType(mediaType) switch
    {
        ResourceType.Html => "Text",
        ResourceType.Css => "Stylesheet",
        ResourceType.Image or ResourceType.Svg => "Image",
        ResourceType.Font => "Font",
        _ => string.Empty,
    };

    private static string Href(string ownerBookPath, string targetBookPath) =>
        string.Join('/', BookPath.Relative(ownerBookPath, targetBookPath).Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// Document anchors: the <c>id</c>/<c>name</c> values in order, without repetitions, with a description
    /// (the <c>title</c> or the start of the element's text, up to 30 characters).
    /// </summary>
    public static IReadOnlyList<LinkCompletionItem> AnchorCandidates(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        List<LinkCompletionItem> items = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (Match m in AnchorRegex().Matches(html))
        {
            string id = m.Groups["id"].Value;
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            items.Add(new LinkCompletionItem(id, DescribeAnchor(html, m)));
        }

        return items.OrderBy(i => i.Text, NaturalComparer.Instance).ToList();
    }

    private static string DescribeAnchor(string html, Match tag)
    {
        if (TitleRegex().Match(tag.Value) is { Success: true } title && title.Groups[1].Value.Trim().Length >= 4)
        {
            return Clip(title.Groups[1].Value.Trim());
        }

        if (tag.Value.EndsWith("/>", StringComparison.Ordinal))
        {
            return string.Empty; // an empty tag — no text
        }

        int end = html.IndexOf("</", tag.Index + tag.Length, StringComparison.Ordinal);
        string inner = end < 0 ? string.Empty : html[(tag.Index + tag.Length)..end];
        string plain = WhitespaceRegex().Replace(MarkupRegex().Replace(inner, " "), " ").Trim();
        return Clip(plain);
    }

    private static string Clip(string s) => s.Length > 30 ? s[..30] : s;

    /// <summary>
    /// Filters and orders the entries by the query: an empty query — all of them in order;
    /// otherwise only the entries containing the query as a substring (case-insensitive),
    /// best score first (<see cref="Score"/>), at most 50.
    /// </summary>
    public static IReadOnlyList<LinkCompletionItem> Filter(IReadOnlyList<LinkCompletionItem> items, string query)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return items;
        }

        return items
            .Select((item, index) => (Item: item, Index: index, Score: Score(item.Text, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Take(MaxResults)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>
    /// The fuzzy match score: the best score over all choices of positions of the query characters
    /// in <paramref name="haystack"/>; a character right after the previous one — full points, at the start
    /// of a segment after "/" — 0.9, after "-_ digit space" or at a lowercase/uppercase boundary — 0.8, after "." —
    /// 0.7, otherwise 0.75/distance. <c>0</c> — no match.
    /// </summary>
    public static double Score(string haystack, string needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return 0;
        }

        double maxPerChar = ((1.0 / haystack.Length) + (1.0 / needle.Length)) / 2.0;
        Dictionary<(int, int, int), double> memo = new();
        double best = Best(haystack, needle, 0, 0, -1, maxPerChar, memo);
        return best < 0 ? 0 : best;
    }

    // The best score for needle[nidx..] searched from haystack[hidx..]; -1 — no match.
    private static double Best(string haystack, string needle, int hidx, int nidx, int lastIdx, double maxPerChar, Dictionary<(int, int, int), double> memo)
    {
        if (nidx == needle.Length)
        {
            return 0;
        }

        if (memo.TryGetValue((hidx, nidx, lastIdx), out double cached))
        {
            return cached;
        }

        double best = -1;
        char n = char.ToLowerInvariant(needle[nidx]);
        for (int pos = hidx; pos <= haystack.Length - (needle.Length - nidx); pos++)
        {
            if (char.ToLowerInvariant(haystack[pos]) != n)
            {
                continue;
            }

            double rest = Best(haystack, needle, pos + 1, nidx + 1, pos, maxPerChar, memo);
            if (rest < 0)
            {
                continue;
            }

            int distance = pos - lastIdx;
            double charScore = distance <= 1 ? maxPerChar : CharScore(haystack[pos - 1], haystack[pos], distance, maxPerChar);
            best = Math.Max(best, charScore + rest);
        }

        memo[(hidx, nidx, lastIdx)] = best;
        return best;
    }

    private static double CharScore(char prev, char current, int distance, double maxPerChar)
    {
        double factor;
        if (prev == '/')
        {
            factor = 0.9;
        }
        else if ("-_ 0123456789".Contains(prev, StringComparison.Ordinal) || (char.IsLower(prev) && char.IsUpper(current)))
        {
            factor = 0.8;
        }
        else if (prev == '.')
        {
            factor = 0.7;
        }
        else
        {
            factor = 1.0 / distance * 0.75;
        }

        return maxPerChar * factor;
    }

    // "Natural" order (numeric sort key): ch2 before ch10.
    private sealed partial class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null)
            {
                return string.CompareOrdinal(x, y);
            }

            string[] a = DigitsRegex().Split(x);
            string[] b = DigitsRegex().Split(y);
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int c = a[i].Length > 0 && char.IsAsciiDigit(a[i][0]) && b[i].Length > 0 && char.IsAsciiDigit(b[i][0])
                    ? decimal.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture)
                        .CompareTo(decimal.Parse(b[i], System.Globalization.CultureInfo.InvariantCulture))
                    : string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
                if (c != 0)
                {
                    return c;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        [GeneratedRegex(@"(\d+)")]
        private static partial Regex DigitsRegex();
    }

    // Matches url( with an optional quote, up to the caret.
    [GeneratedRegex(@"url\s*\(\s*['""]?(?<query>[^)'""]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    [GeneratedRegex(@"^<([A-Za-z][\w:.-]*)")]
    private static partial Regex TagNameRegex();

    // Matches name="value up to the caret (without a closing quote).
    [GeneratedRegex(@"(?<name>[a-zA-Z0-9_:-]+)\s*=\s*(?:'(?<sq>[^']*)|""(?<dq>[^""]*))$")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"<[A-Za-z][^>]*?\s(?:id|name)\s*=\s*([""'])(?<id>[^""']*)\1[^>]*>")]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"\stitle\s*=\s*[""']([^""']*)[""']")]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex MarkupRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
