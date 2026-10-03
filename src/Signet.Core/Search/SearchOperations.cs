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

        string newText = text;
        int count = 0;
        IReadOnlyList<Spcre.MatchInfo> matches = spcre.GetEveryMatchInfo(text);

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Spcre.MatchInfo match = matches[i];
            string matchSegment = newText.Substring(match.Offset.Start, match.Offset.End - match.Offset.Start);

            if (spcre.ReplaceText(matchSegment, match.CaptureGroupsOffsets, replacement, out string replaced))
            {
                newText = string.Concat(
                    newText.AsSpan(0, match.Offset.Start), replaced, newText.AsSpan(match.Offset.End));
                count++;
            }
        }

        return (newText, count);
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
