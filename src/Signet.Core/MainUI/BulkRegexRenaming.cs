using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// The "Bulk rename regex" logic (a preview of the new names)
/// without dialogs: a single regular expression + replacement text, applied to the file name
/// (without the folder) of every selected resource.
/// </summary>
/// <remarks>
/// Matching and substitution go through <see cref="System.Text.RegularExpressions.Regex"/>
/// (group syntax <c>$1</c>). The Find&amp;Replace engine (PCRE.NET) is separate from this class.
/// </remarks>
public static class BulkRegexRenaming
{
    // URI delimiters — characters not allowed in the resulting file name.
    private const string UriDelimiters = ":/?#[]@";

    /// <summary>
    /// Builds a preview of the new file names for <paramref name="resources"/> (in the same order),
    /// applying <paramref name="pattern"/>/<paramref name="replacement"/> to each resource's file
    /// name and removing URI delimiter characters from the result. Returns <c>false</c> and a message in
    /// <paramref name="error"/> when <paramref name="pattern"/> is not a valid regular
    /// expression.
    /// </summary>
    public static bool TryPreviewNewFilenames(
        IReadOnlyList<Resource> resources,
        string pattern,
        string replacement,
        out IReadOnlyList<string>? newFilenames,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);

        Regex regex;
        try
        {
            regex = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            newFilenames = null;
            error = CoreStrings.Format("Rename_InvalidRegex", ex.Message);
            return false;
        }

        List<string> result = new(resources.Count);
        foreach (Resource resource in resources)
        {
            string after = regex.Replace(resource.Filename, replacement);
            result.Add(RemoveUriDelimiters(after));
        }

        newFilenames = result;
        error = null;
        return true;
    }

    private static string RemoveUriDelimiters(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (UriDelimiters.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
