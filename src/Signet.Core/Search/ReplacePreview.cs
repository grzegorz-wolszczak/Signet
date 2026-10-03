using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;

namespace Signet.Core.Search;

/// <summary>
/// View-independent replace preview engine for "Dry Run Replace All" and "Filter Replacements".
/// It does not mutate resources — <see cref="BuildRows"/> only reads, and
/// <see cref="ApplySelected"/> returns the new text.
/// </summary>
public static class ReplacePreview
{
    /// <summary>Default number of context characters before and after the match.</summary>
    public const int DefaultContextAmount = 20;

    /// <summary>Allowed context sizes (10/20/30/40/50).</summary>
    public static readonly IReadOnlyList<int> ContextAmounts = new[] { 10, 20, 30, 40, 50 };

    /// <summary>
    /// Builds the list of all matches of <paramref name="searchRegex"/> in <paramref name="resources"/>
    /// together with the proposed replacement and context. Matches are in document order;
    /// an invalid pattern yields an empty list.
    /// </summary>
    /// <param name="resources">Resources to search.</param>
    /// <param name="searchRegex">The final PCRE2 pattern (from <see cref="SearchRegexBuilder"/>).</param>
    /// <param name="replacement">Replacement pattern (expanded per match by <see cref="Spcre.ReplaceText"/>).</param>
    /// <param name="contextAmount">Number of context characters before/after (≥ 0).</param>
    public static IReadOnlyList<ReplacePreviewRow> BuildRows(
        IReadOnlyList<TextResource> resources,
        string searchRegex,
        string replacement,
        int contextAmount)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(searchRegex);
        ArgumentNullException.ThrowIfNull(replacement);

        int amount = Math.Max(0, contextAmount);
        var rows = new List<ReplacePreviewRow>();

        Spcre spcre = PcreCache.Instance.GetObject(searchRegex);
        if (!spcre.IsValid)
        {
            return rows;
        }

        // Replacement through a Python function (\F<name>) is not supported — matches are shown,
        // but without a proposed replacement (can_replace = false).
        bool functionReplacement = IsFunctionReplacement(replacement);

        foreach (TextResource resource in resources)
        {
            resource.InitialLoad();
            string text = resource.GetText();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Spcre.MatchInfo match in spcre.GetEveryMatchInfo(text))
            {
                int start = match.Offset.Start;
                int end = match.Offset.End;
                string matchText = text.Substring(start, end - start);

                bool canReplace = false;
                string newText = matchText;
                if (!functionReplacement)
                {
                    canReplace = spcre.ReplaceText(matchText, match.CaptureGroupsOffsets, replacement, out string replaced);
                    newText = canReplace ? replaced : matchText;
                }

                rows.Add(new ReplacePreviewRow(
                    resource.BookPath,
                    start,
                    end - start,
                    matchText,
                    newText,
                    GetPriorContext(start, text, amount),
                    GetPostContext(end, text, amount),
                    canReplace));
            }
        }

        return rows;
    }

    /// <summary>
    /// Applies the selected rows to the text of a single file, replacing from the end to the
    /// beginning so that the offsets of the remaining matches stay valid. A row whose
    /// <see cref="ReplacePreviewRow.MatchText"/> is no longer at the recorded offset is skipped
    /// (protects against the file being edited between building the preview and confirming it).
    /// </summary>
    /// <returns>The new text and the number of replacements actually performed.</returns>
    public static (string NewText, int Count) ApplySelected(string text, IEnumerable<ReplacePreviewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rows);

        string result = text;
        int count = 0;

        foreach (ReplacePreviewRow row in rows.OrderByDescending(r => r.Offset))
        {
            if (row.Offset < 0 || row.Offset + row.MatchLength > result.Length)
            {
                continue;
            }

            if (!string.Equals(
                    result.Substring(row.Offset, row.MatchLength), row.MatchText, StringComparison.Ordinal))
            {
                continue;
            }

            result = string.Concat(
                result.AsSpan(0, row.Offset),
                row.ReplacementText,
                result.AsSpan(row.Offset + row.MatchLength));
            count++;
        }

        return (result, count);
    }

    /// <summary>Whether the replacement pattern is a Python function call <c>\F&lt;name&gt;</c> (not supported).</summary>
    public static bool IsFunctionReplacement(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        string trimmed = replacement.Trim();
        return trimmed.StartsWith("\\F<", StringComparison.Ordinal) && trimmed.EndsWith('>');
    }

    /// <summary>Context before the match, truncated to a word boundary.</summary>
    public static string GetPriorContext(int matchStart, string text, int amount)
    {
        ArgumentNullException.ThrowIfNull(text);
        int contextStart = Math.Max(0, matchStart - Math.Max(0, amount));
        while (contextStart < matchStart && !char.IsWhiteSpace(text[contextStart]))
        {
            contextStart++;
        }

        return text.Substring(contextStart, matchStart - contextStart).Replace('\n', ' ');
    }

    /// <summary>Context after the match, truncated to a word boundary.</summary>
    public static string GetPostContext(int matchEnd, string text, int amount)
    {
        ArgumentNullException.ThrowIfNull(text);
        int contextEnd = Math.Min(text.Length, matchEnd + Math.Max(0, amount));
        while (contextEnd > matchEnd && !char.IsWhiteSpace(text[contextEnd - 1]))
        {
            contextEnd--;
        }

        return text.Substring(matchEnd, contextEnd - matchEnd).Replace('\n', ' ');
    }
}
