using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>Checks an OCF file name for characters that EPUB does not permit.</summary>
public static class OCFFilenameChecker
{
    /// <summary>Checks one resource path and reports all applicable filename messages.</summary>
    public static void Check(string path, EpubVersion version, IReport report)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(report);

        foreach (string component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            CheckComponent(component, version, report, path);
        }
    }

    /// <summary>Normalizes an OCF path for collision detection.</summary>
    public static string NormalizeForComparison(string path) =>
        string.Concat(path.Normalize(NormalizationForm.FormC).EnumerateRunes().Select(static rune => FoldCase(rune)));

    private static void CheckComponent(string component, EpubVersion version, IReport report, string path)
    {
        if (component.EndsWith('.'))
        {
            report.Message(MessageId.PKG_011, new EpubLocation(path), component);
        }

        HashSet<string> forbidden = new(StringComparer.Ordinal);
        List<string> forbiddenInOrder = [];
        bool whitespace = false;
        bool nonAscii = false;
        foreach (Rune rune in component.EnumerateRunes())
        {
            if (rune.Value > 0x7F)
            {
                nonAscii = true;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                whitespace = true;
            }

            if (IsForbidden(rune, version))
            {
                string description = Describe(rune);
                if (forbidden.Add(description))
                {
                    forbiddenInOrder.Add(description);
                }
            }
        }

        EpubLocation location = new(path);
        if (forbidden.Count != 0)
        {
            report.Message(MessageId.PKG_009, location, component, string.Join(", ", forbiddenInOrder));
        }

        if (whitespace)
        {
            report.Message(MessageId.PKG_010, location, component);
        }

        if (nonAscii)
        {
            report.Message(MessageId.PKG_012, location, component);
        }
    }

    private static bool IsForbidden(Rune rune, EpubVersion version)
    {
        int value = rune.Value;
        if (value is 0x22 or 0x2A or 0x3A or 0x3C or 0x3E or 0x3F or 0x5C ||
            (version == EpubVersion.Version3 && value == 0x7C))
        {
            return true;
        }

        if (version != EpubVersion.Version3)
        {
            return false;
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.Control ||
            category == UnicodeCategory.PrivateUse ||
            (value >= 0xFDD0 && value <= 0xFDEF) ||
            (value & 0xFFFE) == 0xFFFE ||
            (value >= 0xFFF0 && value <= 0xFFFF) ||
            value == 0xE0001;
    }

    private static string Describe(Rune rune)
    {
        int value = rune.Value;
        if (value <= 0x1F || (value >= 0x7F && value <= 0x9F))
        {
            return $"U+{value:X4} (CONTROL)";
        }

        if ((value >= 0xE000 && value <= 0xF8FF) || (value >= 0xF0000 && value <= 0xFFFFD) || (value >= 0x100000 && value <= 0x10FFFD))
        {
            return $"U+{value:X4} (PRIVATE USE)";
        }

        if ((value >= 0xFDD0 && value <= 0xFDEF) || (value & 0xFFFE) == 0xFFFE)
        {
            return $"U+{value:X4} (NON CHARACTER)";
        }

        if (value == 0xFFFD)
        {
            return "U+FFFD REPLACEMENT CHARACTER (SPECIALS)";
        }

        if (value is >= 0xFFF0 and <= 0xFFFF)
        {
            return $"U+{value:X4} (SPECIALS)";
        }

        return value == 0xE0001 ? "U+E0001 LANGUAGE TAG (DEPRECATED)" : $"U+{value:X4} ({rune})";
    }

    private static string FoldCase(Rune rune) => rune.Value switch
    {
        0x00DF or 0x1E9E => "SS",
        _ => rune.ToString().ToUpperInvariant(),
    };
}
