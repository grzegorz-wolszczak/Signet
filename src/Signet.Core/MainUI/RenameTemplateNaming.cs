using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// The naming logic of the "Rename with a template" operation (no dialogs;
/// the App layer supplies only the template text typed by the user).
/// </summary>
/// <remarks>
/// A template is a file name, e.g. <c>Section0001.xhtml</c>: a text part (<c>Section</c>),
/// a number with preserved zero padding (<c>0001</c>) and an optional extension
/// (<c>.xhtml</c>). For a list of selected resources it generates consecutive names
/// <c>Section0001.xhtml</c>, <c>Section0002.xhtml</c>, … — when the template has no digits at the end,
/// each resource keeps its old name (with only the extension replaced, if one was given).
/// </remarks>
public static class RenameTemplateNaming
{
    /// <summary>
    /// Finds the first free name (without an extension) of the form <c>{basePart}{number}</c>
    /// (the number zero-padded to the length of <paramref name="numberString"/>), starting from
    /// <paramref name="numberString"/> and incrementing until it hits a name not used in
    /// <paramref name="allFilenames"/>.
    /// </summary>
    public static string GetFirstAvailableTemplateName(
        IReadOnlyList<string> allFilenames, string basePart, string numberString)
    {
        ArgumentNullException.ThrowIfNull(allFilenames);
        ArgumentNullException.ThrowIfNull(basePart);
        ArgumentException.ThrowIfNullOrEmpty(numberString);

        HashSet<string> shortNames = new(
            allFilenames.Select(WithoutExtension), StringComparer.Ordinal);

        int number = int.Parse(numberString, System.Globalization.CultureInfo.InvariantCulture);
        string name;
        while (true)
        {
            name = basePart + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .PadLeft(numberString.Length, '0');
            if (!shortNames.Contains(name))
            {
                return name;
            }

            number++;
        }
    }

    // Characters not allowed in a file name.
    private static readonly char[] ForbiddenTemplateChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>
    /// Checks that the template contains no characters that are not allowed in a file name.
    /// </summary>
    public static bool IsTemplateNameValid(string templateName, out string? error)
    {
        ArgumentNullException.ThrowIfNull(templateName);
        foreach (char c in templateName)
        {
            if (Array.IndexOf(ForbiddenTemplateChars, c) >= 0)
            {
                error = CoreStrings.Format("Rename_IllegalChar", c);
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Splits the template (e.g. <c>Section0001.xhtml</c>) and generates consecutive file names for
    /// <paramref name="resources"/>, in the same order. Returns <c>null</c> and a message in
    /// <paramref name="error"/> when any proposed name would collide with an existing
    /// file (other than the resource being renamed itself) or with another name from the same batch —
    /// a single error aborts the whole operation before anything changes.
    /// </summary>
    /// <param name="resources">The resources to rename, in the order the numbering is generated.</param>
    /// <param name="templateName">The template typed by the user (e.g. <c>Section0001.xhtml</c>).</param>
    /// <param name="allFilenames">All the file names currently in the book (to detect collisions).</param>
    /// <param name="error">The error message when generation failed.</param>
    public static IReadOnlyList<string>? BuildSequentialFilenames(
        IReadOnlyList<Resource> resources,
        string templateName,
        IReadOnlyList<string> allFilenames,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(templateName);
        ArgumentNullException.ThrowIfNull(allFilenames);

        (string templateBase, string templateNumberString, string newExtension) = ParseTemplate(templateName);

        int templateNumber = templateNumberString.Length > 0
            ? int.Parse(templateNumberString, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        HashSet<string> bookFilenames = new(allFilenames, StringComparer.Ordinal);
        List<string> newFilenames = new();

        foreach (Resource resource in resources)
        {
            string oldFilename = resource.Filename;
            string fileExtension = newExtension;
            int oldDot = oldFilename.LastIndexOf('.');
            if (fileExtension.Length == 0 && oldDot >= 0)
            {
                fileExtension = oldFilename[oldDot..];
            }

            string name;
            if (templateNumberString.Length == 0)
            {
                // No number (and possibly no text) in the template — keep the old name, replace only the extension.
                string oldWithoutExtension = oldDot >= 0 ? oldFilename[..oldDot] : oldFilename;
                name = oldWithoutExtension + fileExtension;
            }
            else
            {
                name = templateBase + templateNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    .PadLeft(templateNumberString.Length, '0') + fileExtension;
                templateNumber++;
            }

            if ((bookFilenames.Contains(name) && !string.Equals(oldFilename, name, StringComparison.Ordinal))
                || newFilenames.Contains(name))
            {
                error = CoreStrings.Get("Rename_WouldDuplicate");
                return null;
            }

            newFilenames.Add(name);
        }

        error = null;
        return newFilenames;
    }

    /// <summary>The name without the last extension (only the part after the last dot is removed).</summary>
    private static string WithoutExtension(string filename)
    {
        int dot = filename.LastIndexOf('.');
        return dot >= 0 ? filename[..dot] : filename;
    }

    /// <summary>
    /// Splits the template into: the text base, the numeric suffix (with leading zeros preserved) and the
    /// extension. Public — also used to compute the "next" template after a successful rename.
    /// </summary>
    public static (string BasePart, string NumberString, string Extension) ParseTemplate(string templateName)
    {
        string extension = string.Empty;
        string nameWithoutExtension = templateName;
        int dot = templateName.LastIndexOf('.');
        if (dot >= 0)
        {
            extension = templateName[dot..];
            nameWithoutExtension = templateName[..dot];
        }

        string basePart = string.Empty;
        string numberString = string.Empty;
        if (nameWithoutExtension.Length > 0)
        {
            int pos = nameWithoutExtension.Length - 1;
            while (pos >= 0 && char.IsDigit(nameWithoutExtension[pos]))
            {
                pos--;
            }

            basePart = nameWithoutExtension[..(pos + 1)];
            numberString = nameWithoutExtension[(pos + 1)..];
            if (numberString.Length == 0)
            {
                numberString = "1";
            }
        }

        return (basePart, numberString, extension);
    }
}
