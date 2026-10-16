using System;
using System.Collections.Generic;
using System.Globalization;
using Signet.Core.Localization;
using Signet.Core.Resources;

namespace Signet.Core.MainUI;

/// <summary>
/// What the "Mass Rename" window asks for: every file becomes <c>{Prefix}{number}{extension}</c>, the number counting up
/// from <see cref="StartNumber"/> and padded on the left with <see cref="PaddingChar"/> to <see cref="MinLength"/>
/// characters (<c>part002.html</c> for the prefix <c>part</c>, the character <c>0</c> and the length 3).
/// </summary>
/// <param name="Prefix">The text before the number (may be empty).</param>
/// <param name="StartNumber">The number of the first file.</param>
/// <param name="PaddingChar">The character the number is padded with (used when the number is shorter than <paramref name="MinLength"/>).</param>
/// <param name="MinLength">The minimum number of characters of the number (1 = no padding).</param>
/// <param name="BookOrder">Number the (HTML) files in the order of the book (the spine), not in the order they were selected.</param>
public readonly record struct MassRenameOptions(string Prefix, int StartNumber, char PaddingChar, int MinLength, bool BookOrder)
{
    /// <summary>Upper bound of <see cref="StartNumber"/>.</summary>
    public const int StartNumberMax = 999_999_999;

    /// <summary>Upper bound of <see cref="MinLength"/>.</summary>
    public const int MinLengthMax = 30;

    /// <summary>The defaults of the window: <c>part1.html</c>, <c>part2.html</c>, … in the order of the book.</summary>
    public static MassRenameOptions Default => new("part", 1, '0', 1, true);

    /// <summary>A copy with the numbers clamped into their ranges and the prefix trimmed of surrounding spaces.</summary>
    public MassRenameOptions Normalized() => new(
        Prefix?.Trim() ?? string.Empty,
        Math.Clamp(StartNumber, 0, StartNumberMax),
        PaddingChar,
        Math.Clamp(MinLength, 1, MinLengthMax),
        BookOrder);
}

/// <summary>
/// The naming logic of "Mass Rename" (no dialogs; the App layer supplies the options and the resources in the order they
/// are numbered). Every file keeps its extension.
/// </summary>
public static class MassRenameNaming
{
    // Characters not allowed in a file name.
    private static readonly char[] ForbiddenChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>
    /// The number as it appears in a name: <paramref name="number"/> padded on the left with <paramref name="padding"/> to
    /// <paramref name="minLength"/> characters, e.g. 2, '0', 3 → <c>002</c> and 12, '#', 7 → <c>#####12</c>. A longer number is
    /// never cut.
    /// </summary>
    public static string FormatNumber(long number, char padding, int minLength) =>
        number.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(minLength, 1), padding);

    /// <summary>
    /// Checks the options: no character that a file name cannot have in the prefix or in the padding character, and a padding
    /// character when the number is to be padded. Returns the message in <paramref name="error"/> when they are not valid.
    /// </summary>
    public static bool IsValid(MassRenameOptions options, out string? error)
    {
        foreach (char c in options.Prefix ?? string.Empty)
        {
            if (IsForbidden(c))
            {
                error = CoreStrings.Format("Rename_IllegalChar", c);
                return false;
            }
        }

        if (options.MinLength > 1)
        {
            if (options.PaddingChar == '\0')
            {
                error = CoreStrings.Get("MassRename_PaddingCharMissing");
                return false;
            }

            if (IsForbidden(options.PaddingChar))
            {
                error = CoreStrings.Format("Rename_IllegalChar", options.PaddingChar);
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsForbidden(char c) => char.IsControl(c) || Array.IndexOf(ForbiddenChars, c) >= 0;

    /// <summary>
    /// The new file names of <paramref name="resources"/> (in the order they are numbered), each with the extension of the
    /// file. Returns <c>null</c> and a message in <paramref name="error"/> when the options are not valid or when a name would
    /// collide with a file of the book (other than the resource itself) or with another name of the batch: one error aborts the
    /// whole operation before anything changes.
    /// </summary>
    /// <param name="resources">The resources to rename, in the order the numbers are given.</param>
    /// <param name="options">What to name them.</param>
    /// <param name="allFilenames">All the file names currently in the book (to detect collisions).</param>
    /// <param name="error">The error message when the names could not be made.</param>
    public static IReadOnlyList<string>? BuildFilenames(
        IReadOnlyList<Resource> resources, MassRenameOptions options, IReadOnlyList<string> allFilenames, out string? error)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(allFilenames);
        if (!IsValid(options, out error))
        {
            return null;
        }

        HashSet<string> bookFilenames = new(allFilenames, StringComparer.Ordinal);
        HashSet<string> newNames = new(StringComparer.Ordinal);
        List<string> result = new(resources.Count);
        long number = options.StartNumber;
        foreach (Resource resource in resources)
        {
            string oldFilename = resource.Filename;
            int dot = oldFilename.LastIndexOf('.');
            string extension = dot >= 0 ? oldFilename[dot..] : string.Empty;
            string name = options.Prefix
                + FormatNumber(number, options.PaddingChar == '\0' ? '0' : options.PaddingChar, options.MinLength) + extension;
            number++;

            if ((bookFilenames.Contains(name) && !string.Equals(oldFilename, name, StringComparison.Ordinal)) || !newNames.Add(name))
            {
                error = CoreStrings.Get("Rename_WouldDuplicate");
                return null;
            }

            result.Add(name);
        }

        error = null;
        return result;
    }
}
