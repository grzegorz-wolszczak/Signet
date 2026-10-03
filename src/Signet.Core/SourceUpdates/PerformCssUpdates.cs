using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// Updates <c>url(...)</c> / <c>@import</c> references in CSS text after resources are renamed or
/// moved. A targeted replacement
/// through regular expressions (without parsing a full CSS AST), so it preserves
/// the file's formatting outside the references that actually change.
/// </summary>
/// <remarks>
/// Used both for <c>.css</c> files and (through <see cref="PerformHtmlUpdates"/>) for
/// <c>style="…"</c> attributes and the contents of <c>&lt;style&gt;</c> elements in (X)HTML.
/// </remarks>
public static class PerformCssUpdates
{
    // Properties that may contain url() / a file name, plus @import.
    private static readonly Regex PropertyWithUrl = new(
        @"(?:(?:src|background|background-image|block|border|border-image|border-image-source|" +
        @"content|cursor|filter|list-style|list-style-image|mask|mask-image|(?:-webkit-)?shape-outside)\s*:|" +
        @"@import)\s*([^;}]*)(?:;|})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlFunction = new(
        @"url\([""']?([^()""']*)[""']?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlOrQuotedString = new(
        @"url\([""']?([^()""']*)[""']?\)|[""']([^()""']*)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Recalculates all references in <paramref name="source"/> (CSS text, an inline style or
    /// the contents of <c>&lt;style&gt;</c>) according to <paramref name="updates"/>.
    /// </summary>
    /// <param name="source">The text to process.</param>
    /// <param name="updates">A map: old bookpath → new bookpath for the moved resources.</param>
    /// <param name="oldBookPath">The bookpath of the file containing <paramref name="source"/> before the operation.</param>
    /// <param name="newBookPath">The bookpath of this file after the operation.</param>
    public static string Apply(
        string source,
        IReadOnlyDictionary<string, string> updates,
        string oldBookPath,
        string newBookPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0 || source.Length == 0)
        {
            return source;
        }

        string result = source;
        int searchFrom = 0;
        Match match = PropertyWithUrl.Match(result, searchFrom);
        if (!match.Success)
        {
            return result;
        }

        while (match.Success)
        {
            Group valueGroup = match.Groups[1];
            if (valueGroup.Success && valueGroup.Value.Trim().Length > 0)
            {
                string fragment = valueGroup.Value;
                bool isImport = match.Value.StartsWith("@import", StringComparison.OrdinalIgnoreCase);
                Regex urlPattern = isImport ? UrlOrQuotedString : UrlFunction;

                string newFragment = ReplaceUrlsInFragment(fragment, urlPattern, updates, oldBookPath, newBookPath, out bool changed);
                if (changed)
                {
                    result = result.Remove(valueGroup.Index, valueGroup.Length).Insert(valueGroup.Index, newFragment);
                    searchFrom = valueGroup.Index + newFragment.Length;
                    match = PropertyWithUrl.Match(result, searchFrom);
                    continue;
                }
            }

            searchFrom = match.Index + match.Length;
            match = PropertyWithUrl.Match(result, searchFrom);
        }

        return result;
    }

    private static string ReplaceUrlsInFragment(
        string fragment,
        Regex urlPattern,
        IReadOnlyDictionary<string, string> updates,
        string oldBookPath,
        string newBookPath,
        out bool changed)
    {
        changed = false;
        string result = fragment;
        int searchFrom = 0;
        Match match = urlPattern.Match(result, searchFrom);

        while (match.Success)
        {
            Group captured = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
            if (!captured.Success || captured.Value.Trim().Length == 0)
            {
                searchFrom = match.Index + match.Length;
                match = urlPattern.Match(result, searchFrom);
                continue;
            }

            string oldValue = captured.Value;
            string newValue = HrefUpdate.UpdateValue(oldValue, updates, oldBookPath, newBookPath);
            if (!string.Equals(newValue, oldValue, StringComparison.Ordinal))
            {
                result = result.Remove(captured.Index, captured.Length).Insert(captured.Index, newValue);
                searchFrom = captured.Index + newValue.Length;
                changed = true;
            }
            else
            {
                searchFrom = match.Index + match.Length;
            }

            match = urlPattern.Match(result, searchFrom);
        }

        return result;
    }
}
