using System;
using System.Collections.Generic;
using Signet.Core.Resources;

namespace Signet.Core.Search;

/// <summary>
/// Stateless multi-file search/replace operations. All functions take a final PCRE2 pattern
/// (built by <see cref="SearchRegexBuilder"/>); an invalid pattern is treated as "zero matches".
/// </summary>
public static class SearchOperations
{
    /// <summary>Operation result for a single file (for the report in the panel).</summary>
    /// <param name="BookPath">Book path of the resource.</param>
    /// <param name="Count">Number of matches / replacements performed.</param>
    public sealed record FileSearchResult(string BookPath, int Count);

    /// <summary>
    /// Number of matches of <paramref name="searchRegex"/> in <paramref name="text"/>.
    /// </summary>
    public static int CountInText(string searchRegex, string text)
    {
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(text);

        Spcre spcre = PcreCache.Instance.GetObject(searchRegex);
        return spcre.IsValid ? spcre.GetEveryMatchInfo(text).Count : 0;
    }

    /// <summary>
    /// The matches of <paramref name="searchRegex"/> in <paramref name="text"/> as offsets <c>[Start, End)</c>, in text
    /// order. An invalid pattern has no matches.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> FindInText(string searchRegex, string text)
    {
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(text);

        Spcre spcre = PcreCache.Instance.GetObject(searchRegex);
        if (!spcre.IsValid)
        {
            return Array.Empty<(int Start, int End)>();
        }

        IReadOnlyList<Spcre.MatchInfo> matches = spcre.GetEveryMatchInfo(text);
        List<(int Start, int End)> found = new(matches.Count);
        foreach (Spcre.MatchInfo match in matches)
        {
            found.Add((match.Offset.Start, match.Offset.End));
        }

        return found;
    }

    /// <summary>
    /// Replaces all matches of <paramref name="searchRegex"/> in <paramref name="text"/>
    /// with the expanded <paramref name="replacement"/> (iterating from the end so that length
    /// changes do not shift offsets that are still to be processed).
    /// </summary>
    /// <returns>The new text and the number of replacements performed.</returns>
    public static (string NewText, int Count) PerformGlobalReplace(
        string text, string searchRegex, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(replacement);

        Spcre spcre = PcreCache.Instance.GetObject(searchRegex);
        if (!spcre.IsValid)
        {
            return (text, 0);
        }

        IReadOnlyList<Spcre.MatchInfo> matches = spcre.GetEveryMatchInfo(text);
        if (matches.Count == 0)
        {
            return (text, 0);
        }

        // One pass from the start into a builder: the text between two matches is copied once. (Replacing from the end and joining the whole text
        // again for every match made a file with thousands of matches cost the square of its length.) The matches do not overlap and come in
        // order, so what is left of the text before a match is the original text, as it was when the replacements were made from the end.
        System.Text.StringBuilder result = new(text.Length + (text.Length / 8));
        int copied = 0;
        int count = 0;
        foreach (Spcre.MatchInfo match in matches)
        {
            int start = match.Offset.Start;
            int end = match.Offset.End;
            string matchSegment = text.Substring(start, end - start);
            if (spcre.ReplaceText(matchSegment, match.CaptureGroupsOffsets, replacement, out string replaced))
            {
                result.Append(text, copied, start - copied).Append(replaced);
                copied = end;
                count++;
            }
        }

        result.Append(text, copied, text.Length - copied);
        return (result.ToString(), count);
    }

    /// <summary>
    /// Counts the matches in each of the <paramref name="resources"/>.
    /// Returns one entry per file — including files without matches (for a complete report).
    /// </summary>
    public static IReadOnlyList<FileSearchResult> CountInFiles(
        string searchRegex, IReadOnlyList<TextResource> resources)
    {
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(resources);

        var results = new List<FileSearchResult>(resources.Count);
        foreach (TextResource resource in resources)
        {
            resource.InitialLoad();
            results.Add(new FileSearchResult(resource.BookPath, CountInText(searchRegex, resource.GetText())));
        }

        return results;
    }

    /// <summary>
    /// Replaces in all <paramref name="resources"/>: for each file calls
    /// <see cref="PerformGlobalReplace"/> and, when the text changed, <see cref="TextResource.SetText"/>.
    /// Returns the number of replacements per file (entries only for files in which something
    /// was replaced).
    /// </summary>
    public static IReadOnlyList<FileSearchResult> ReplaceInAllFiles(
        string searchRegex, string replacement, IReadOnlyList<TextResource> resources)
    {
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(resources);

        var results = new List<FileSearchResult>();
        foreach (TextResource resource in resources)
        {
            resource.InitialLoad();
            string text = resource.GetText();
            (string newText, int count) = PerformGlobalReplace(text, searchRegex, replacement);

            if (count > 0 && !string.Equals(newText, text, StringComparison.Ordinal))
            {
                resource.SetText(newText);
            }

            if (count > 0)
            {
                results.Add(new FileSearchResult(resource.BookPath, count));
            }
        }

        return results;
    }
}
