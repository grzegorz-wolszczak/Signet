using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the path of a file of the container for the characters EPUB does not allow (upstream's
/// <c>OCFFilenameChecker</c>). The whole path is checked as one string, as upstream does.
/// </summary>
public static class OCFFilenameChecker
{
    private const string AsciiSet = "ASCII";
    private const int FullStop = 0x002E;

    // The sets of disallowed characters of EPUB 3, in upstream's order: a character is described by the first set it is in.
    private static readonly (string Name, Func<int, bool> Contains)[] s_disallowedEpub3 =
    [
        (AsciiSet, static c => IsDisallowedInEpub2(c) || c == 0x007C), // and VERTICAL LINE '|'
        ("NON CHARACTER", static c => c is >= 0xFDD0 and <= 0xFDEF || (c & 0xFFFE) == 0xFFFE),
        ("CONTROL", static c => c is <= 0x001F or 0x007F or >= 0x0080 and <= 0x009F),
        ("PRIVATE USE", static c => c is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFF or >= 0x100000 and <= 0x10FFFF),
        ("SPECIALS", static c => c is >= 0xFFF0 and <= 0xFFFF),
        ("DEPRECATED", static c => c == 0xE0001), // LANGUAGE TAG (CANCEL TAG is reinstated in emoji tag sequences)
    ];

    // Upstream prints ICU's character name, when the character has one. Of the disallowed characters, only these do
    // (the controls, the private use characters and the non characters have no name).
    private static readonly Dictionary<int, string> s_characterNames = new()
    {
        [0xFFF9] = "INTERLINEAR ANNOTATION ANCHOR",
        [0xFFFA] = "INTERLINEAR ANNOTATION SEPARATOR",
        [0xFFFB] = "INTERLINEAR ANNOTATION TERMINATOR",
        [0xFFFC] = "OBJECT REPLACEMENT CHARACTER",
        [0xFFFD] = "REPLACEMENT CHARACTER",
        [0xE0001] = "LANGUAGE TAG",
    };

    /// <summary>
    /// Checks <paramref name="filename"/> (a path in the container) and reports <see cref="MessageId.PKG_009"/>,
    /// <see cref="MessageId.PKG_010"/>, <see cref="MessageId.PKG_011"/> and <see cref="MessageId.PKG_012"/> at
    /// <paramref name="location"/> (by default, the path itself).
    /// </summary>
    public static void Check(string filename, EpubVersion version, IReport report, EpubLocation? location = null)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(report);
        location ??= new EpubLocation(filename);

        LinkedHashSet disallowed = new();
        bool hasSpaces = false;
        bool isAsciiOnly = true;
        int last = -1;
        foreach (Rune rune in filename.EnumerateRunes())
        {
            int codePoint = rune.Value;
            last = codePoint;
            isAsciiOnly = isAsciiOnly && codePoint <= 0x7F;
            hasSpaces = hasSpaces || Rune.IsWhiteSpace(rune);
            if (version == EpubVersion.Version2)
            {
                if (IsDisallowedInEpub2(codePoint))
                {
                    disallowed.Add(Describe(codePoint, AsciiSet));
                }

                continue;
            }

            foreach ((string name, Func<int, bool> contains) in s_disallowedEpub3)
            {
                if (contains(codePoint))
                {
                    disallowed.Add(Describe(codePoint, name));
                    break;
                }
            }
        }

        if (last == FullStop)
        {
            report.Message(MessageId.PKG_011, location, filename);
        }

        if (disallowed.Count != 0)
        {
            report.Message(MessageId.PKG_009, location, filename, string.Join(", ", disallowed.Items));
        }

        if (hasSpaces)
        {
            report.Message(MessageId.PKG_010, location, filename);
        }

        if (!isAsciiOnly)
        {
            report.Message(MessageId.PKG_012, location, filename);
        }
    }

    // QUOTATION MARK, ASTERISK, COLON, LESS-THAN SIGN, GREATER-THAN SIGN, QUESTION MARK, REVERSE SOLIDUS. SOLIDUS is the
    // path separator; FULL STOP is only disallowed as the last character.
    private static bool IsDisallowedInEpub2(int codePoint) => codePoint is 0x22 or 0x2A or 0x3A or 0x3C or 0x3E or 0x3F or 0x5C;

    private static string Describe(int codePoint, string setName)
    {
        StringBuilder result = new(string.Format(CultureInfo.InvariantCulture, "U+{0:X4} ", codePoint));
        if (setName == AsciiSet)
        {
            return result.Append('(').Append(char.ConvertFromUtf32(codePoint)).Append(')').ToString();
        }

        if (s_characterNames.TryGetValue(codePoint, out string? characterName))
        {
            result.Append(characterName).Append(' ');
        }

        return result.Append('(').Append(setName).Append(')').ToString();
    }

    // The descriptions in the order they were first found, each once (upstream's LinkedHashSet).
    private sealed class LinkedHashSet
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public List<string> Items { get; } = [];

        public int Count => Items.Count;

        public void Add(string item)
        {
            if (_seen.Add(item))
            {
                Items.Add(item);
            }
        }
    }
}
