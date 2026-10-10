using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks resource file names for portability between file systems. Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>, like <see cref="OpfStructureValidator"/>,
/// <see cref="LinkIntegrityValidator"/>, <see cref="FontIntegrityValidator"/> and
/// <see cref="CrossFileStructureValidator"/>.
/// </summary>
/// <remarks>
/// Checks only the file name (the last bookpath segment, <see cref="Resource.Filename"/>), not
/// the whole path — directories in the EPUB are created by the application and do not come from the user in a way
/// that would require a separate check. The rules concern portability to Windows (the most
/// restrictive of the popular file systems: characters disallowed in NTFS/FAT, reserved names,
/// trailing spaces/dots silently trimmed) and Unicode NFC/NFD normalization (macOS HFS+/APFS
/// normalizes names to NFD, which may diverge from links stored in the content as NFC).
/// </remarks>
public static class FileNamePortabilityValidator
{
    private static readonly char[] WindowsForbiddenChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Runs all the file name portability checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (Resource resource in book.GetAllResources())
        {
            foreach ((string code, string message) in CheckFileNameWithCodes(resource.Filename))
            {
                results.Add(new ValidationResult(ValidationSeverity.Warning, resource.BookPath, -1, -1, message, code));
            }
        }

        return results;
    }

    /// <summary>
    /// Checks a single file name (without directories) and returns warning messages — an empty
    /// list means the name is portable. A pure function: it touches neither the disk nor the <see cref="Book"/>.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="Validate"/> so that the rules can be tested on any system.
    /// Some of the names checked (e.g. ones containing <c>:</c> or <c>CON</c>) cannot be
    /// created on disk on Windows, so testing them through a real file works
    /// only on Linux and macOS.
    /// </remarks>
    /// <param name="filename">The file name, i.e. the last bookpath segment (<see cref="Resource.Filename"/>).</param>
    public static IReadOnlyList<string> CheckFileName(string filename) =>
        CheckFileNameWithCodes(filename).Select(m => m.Message).ToList();

    // The warnings of CheckFileName with their rule codes (the resource key of the message).
    private static List<(string Code, string Message)> CheckFileNameWithCodes(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        List<(string Code, string Message)> messages = new();
        CheckForbiddenCharacters(filename, messages);
        CheckReservedName(filename, messages);
        CheckTrailingSpaceOrDot(filename, messages);
        CheckUnicodeNormalization(filename, messages);
        return messages;
    }

    private static void CheckForbiddenCharacters(string filename, List<(string Code, string Message)> messages)
    {
        HashSet<char> found = new();
        foreach (char c in filename)
        {
            if (c < 0x20 || Array.IndexOf(WindowsForbiddenChars, c) >= 0)
            {
                found.Add(c);
            }
        }

        if (found.Count == 0)
        {
            return;
        }

        string chars = string.Join(", ", found);
        messages.Add(("Validation_FileNameIllegalChar", CoreStrings.Format("Validation_FileNameIllegalChar", filename, chars)));
    }

    private static void CheckReservedName(string filename, List<(string Code, string Message)> messages)
    {
        int dot = filename.IndexOf('.');
        string stem = dot < 0 ? filename : filename[..dot];

        if (!WindowsReservedNames.Contains(stem))
        {
            return;
        }

        messages.Add(("Validation_FileNameReserved", CoreStrings.Format("Validation_FileNameReserved", filename, stem.ToUpperInvariant())));
    }

    private static void CheckTrailingSpaceOrDot(string filename, List<(string Code, string Message)> messages)
    {
        if (filename.Length == 0)
        {
            return;
        }

        char last = filename[^1];
        if (last != ' ' && last != '.')
        {
            return;
        }

        string code = last == ' ' ? "Validation_FileNameTrailingSpace" : "Validation_FileNameTrailingDot";
        messages.Add((code, CoreStrings.Format(code, filename)));
    }

    private static void CheckUnicodeNormalization(string filename, List<(string Code, string Message)> messages)
    {
        if (filename.IsNormalized(NormalizationForm.FormC))
        {
            return;
        }

        messages.Add(("Validation_FileNameNotNfc", CoreStrings.Format("Validation_FileNameNotNfc", filename)));
    }
}
