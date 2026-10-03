using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.Semantics;

/// <summary>
/// Builds a ready-made HTML fragment from an <see cref="AriaClips"/> template — the view-independent logic
/// of the Insert → "Aria Clip..." menu command. The interactive choice of
/// clip / role (the clip and role dialogs) belongs to the App; this class only holds the
/// pure text substitution logic so that it can be tested without UI.
/// </summary>
public static class AriaClipBuilder
{
    // The clip codes whose _N_ placeholder is replaced (and, for the second set, which are also filled).
    private static readonly HashSet<string> UpdateOnlyNumber = new(StringComparer.Ordinal)
    {
        "fn_ref", "fn_backlink", "endnote_ref", "endnote_backlink", "pagebreak_span",
    };

    private static readonly HashSet<string> UpdateNumberAndFill = new(StringComparer.Ordinal)
    {
        "fn_aside", "fn_div", "fn_p", "endnote_li",
    };

    // Matches a leading number in the selected text, e.g. "[12]." or "12".
    private static readonly Regex LeadingNumber =
        new(@"^\s*\[?(\d+)\]?\.?\s*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Whether an additional role selection dialog is shown for the given clip code
    /// (the <c>(code == "section") || (code == "aside")</c> condition).
    /// </summary>
    public static bool RequiresRoleSelection(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code is "section" or "aside";
    }

    /// <summary>
    /// Candidates for the labels of the "Insert Aria Clip" list (code → display name "Name (code)" +
    /// description = the translated template).
    /// </summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> ClipOptions(string bookLang)
    {
        ArgumentNullException.ThrowIfNull(bookLang);
        var map = new Dictionary<string, DescriptiveInfo>(StringComparer.Ordinal);
        foreach (string code in AriaClips.GetAllCodes())
        {
            string name = AriaClips.GetName(code) + " (" + code + ")";
            string description = AriaClips.TranslatePlaceholders(AriaClips.GetDescriptionByCode(code), bookLang);
            map[code] = new DescriptiveInfo(name, description);
        }

        return map;
    }

    /// <summary>
    /// Candidates for the labels of the "Select Role" list for the given clip code (<c>section</c>/<c>aside</c>
    /// treated as the tag name) — filtered by <c>AriaRoles.AllowedTags(role).Contains(tagCode)</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> RoleOptions(string tagCode)
    {
        ArgumentNullException.ThrowIfNull(tagCode);
        var map = new Dictionary<string, DescriptiveInfo>(StringComparer.Ordinal);
        foreach (string roleCode in AriaRoles.GetAllCodes())
        {
            if (!AriaRoles.AllowedTags(roleCode).Contains(tagCode, StringComparer.Ordinal))
            {
                continue;
            }

            string name = AriaRoles.GetName(roleCode) + " (" + roleCode + ")";
            map[roleCode] = new DescriptiveInfo(name, AriaRoles.GetDescriptionByCode(roleCode));
        }

        return map;
    }

    /// <summary>
    /// Builds the clip content (without the interactive role selection,
    /// which the caller performs before calling this method). For codes from <see cref="UpdateOnlyNumber"/>/
    /// <see cref="UpdateNumberAndFill"/> it replaces <c>_N_</c> with the number extracted from the start of
    /// <paramref name="selectedText"/> (and <c>\1</c> with the selected text — only for "fill"); for
    /// the remaining codes the <c>\1</c> placeholder is left untouched (it is handled later by
    /// <c>CodeInsertOperations.PasteClipText</c>, as for an ordinary user clip). When
    /// <see cref="RequiresRoleSelection"/> returns <see langword="true"/> and
    /// <paramref name="roleCode"/> is given, it injects the <c>epub:type</c>/<c>role</c> attributes before the first
    /// <c>&gt;</c>. At the end it always calls <see cref="AriaClips.TranslatePlaceholders"/>.
    /// </summary>
    public static string BuildClip(string code, string selectedText, string? roleCode, string bookLang)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(selectedText);
        ArgumentNullException.ThrowIfNull(bookLang);

        string clip = AriaClips.GetDescriptionByCode(code);

        if (selectedText.Length > 0 && (UpdateOnlyNumber.Contains(code) || UpdateNumberAndFill.Contains(code)))
        {
            Match match = LeadingNumber.Match(selectedText);
            string number = match.Success ? match.Groups[1].Value : string.Empty;
            if (number.Length > 0)
            {
                clip = clip.Replace("_N_", number, StringComparison.Ordinal);
                if (UpdateNumberAndFill.Contains(code))
                {
                    clip = clip.Replace("\\1", selectedText, StringComparison.Ordinal);
                }
            }
        }

        if (RequiresRoleSelection(code) && !string.IsNullOrEmpty(roleCode))
        {
            string epubType = AriaRoles.EpubTypeMapping(roleCode);
            string attributesAdded = string.Equals(epubType, roleCode, StringComparison.Ordinal)
                ? " epub:type=\"" + epubType + "\""
                : (epubType.Length > 0 ? " epub:type=\"" + epubType + "\" " : " ") + "role=\"" + roleCode + "\"";

            int gt = clip.IndexOf('>', StringComparison.Ordinal);
            if (gt > -1)
            {
                clip = clip.Insert(gt, attributesAdded);
            }
        }

        return AriaClips.TranslatePlaceholders(clip, bookLang);
    }
}
