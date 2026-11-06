using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the path of a file of the container for the characters EPUB does not allow (upstream's
/// <c>OCFFilenameChecker</c>). The whole path is checked as one string, as upstream does.
/// </summary>
public static class OCFFilenameChecker
{
    private const string AsciiSet = "ASCII";
    private const int FullStop = '.';
    private const int VerticalLine = '|';

    // The sets of disallowed characters of EPUB 3, in upstream's order: a character is described by the first set it is in.
    private static readonly (string Name, Func<int, bool> Contains)[] s_disallowedEpub3 =
    [
        (AsciiSet, static c => IsDisallowedInEpub2(c) || c == VerticalLine),
        ("NON CHARACTER", static c => c is >= UnicodeRanges.NonCharactersFirst and <= UnicodeRanges.NonCharactersLast
            || (c & UnicodeRanges.PlaneEndNonCharacterMask) == UnicodeRanges.PlaneEndNonCharacterMask),
        ("CONTROL", static c => c is <= UnicodeRanges.C0ControlsLast or UnicodeRanges.Delete
            or >= UnicodeRanges.C1ControlsFirst and <= UnicodeRanges.C1ControlsLast),
        ("PRIVATE USE", static c => c is >= UnicodeRanges.PrivateUseFirst and <= UnicodeRanges.PrivateUseLast
            or >= UnicodeRanges.SupplementaryPrivateUseAFirst and <= UnicodeRanges.SupplementaryPrivateUseALast
            or >= UnicodeRanges.SupplementaryPrivateUseBFirst and <= UnicodeRanges.SupplementaryPrivateUseBLast),
        ("SPECIALS", static c => c is >= UnicodeRanges.SpecialsFirst and <= UnicodeRanges.SpecialsLast),
        ("DEPRECATED", static c => c == UnicodeRanges.LanguageTag), // LANGUAGE TAG (CANCEL TAG is reinstated in emoji tag sequences)
    ];

    // Upstream prints ICU's character name, when the character has one. Of the disallowed characters, only these do
    // (the controls, the private use characters and the non characters have no name).
    private static readonly Dictionary<int, string> s_characterNames = new()
    {
        [UnicodeRanges.InterlinearAnnotationAnchor] = "INTERLINEAR ANNOTATION ANCHOR",
        [UnicodeRanges.InterlinearAnnotationSeparator] = "INTERLINEAR ANNOTATION SEPARATOR",
        [UnicodeRanges.InterlinearAnnotationTerminator] = "INTERLINEAR ANNOTATION TERMINATOR",
        [UnicodeRanges.ObjectReplacementCharacter] = "OBJECT REPLACEMENT CHARACTER",
        [UnicodeRanges.ReplacementCharacter] = "REPLACEMENT CHARACTER",
        [UnicodeRanges.LanguageTag] = "LANGUAGE TAG",
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
            isAsciiOnly = isAsciiOnly && codePoint <= UnicodeRanges.AsciiLast;
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
    private static bool IsDisallowedInEpub2(int codePoint) => codePoint is '"' or '*' or ':' or '<' or '>' or '?' or '\\';

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
