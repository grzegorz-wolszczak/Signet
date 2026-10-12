using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core.Fonts;

/// <summary>What an item of a <see cref="FontRemovalPlan"/> does.</summary>
public enum FontRemovalItemKind
{
    /// <summary>Removes an <c>@font-face</c> rule all of whose sources are deleted fonts.</summary>
    RemoveRule,

    /// <summary>Removes the deleted fonts from the <c>src</c> of a rule that has other sources too.</summary>
    RemoveSources,

    /// <summary>Removes a removed family from a <c>font-family</c> / <c>font</c> list that names other families too.</summary>
    RemoveFamily,

    /// <summary>Removes a <c>font-family</c> declaration that names only removed families.</summary>
    RemoveDeclaration,

    /// <summary>A <c>font</c> shorthand naming only removed families — it cannot be fixed automatically (a note).</summary>
    ManualFix,

    /// <summary>A family keeps some of its faces but loses others (a note).</summary>
    FamilyLosesFaces,
}

/// <summary>One change (or note) of a <see cref="FontRemovalPlan"/>.</summary>
/// <param name="Key">A key identifying the item (for the set of excluded items).</param>
/// <param name="Kind">What the item does.</param>
/// <param name="Text">A readable description.</param>
/// <param name="BookPath">The file the item concerns (for navigation).</param>
/// <param name="Offset">The offset of the rule / declaration in the file (for navigation).</param>
public sealed record FontRemovalItem(string Key, FontRemovalItemKind Kind, string Text, string BookPath, int Offset)
{
    /// <summary>Whether the item changes the book (otherwise it is only a note).</summary>
    public bool IsActionable => Kind is not (FontRemovalItemKind.ManualFix or FontRemovalItemKind.FamilyLosesFaces);

    internal int EditStart { get; init; }

    internal int EditEnd { get; init; }

    internal string Replacement { get; init; } = string.Empty;

    // For an item inside a style attribute: the attribute, so that an attribute left empty can be removed.
    internal FontFamilyUsage? Attribute { get; init; }
}

/// <summary>
/// What deleting font files means for the stylesheets: the <c>@font-face</c> rules that point only to the deleted
/// files are removed, the deleted files are removed from the <c>src</c> of rules with other sources, and a family that
/// loses all its rules is removed from the <c>font-family</c> / <c>font</c> lists that name it (a declaration naming
/// nothing else is removed). Each change can be excluded; the notes say what cannot be fixed automatically and which
/// families only lose some of their faces.
/// </summary>
public sealed class FontRemovalPlan
{
    private readonly BookFontIndex _index;

    private FontRemovalPlan(
        BookFontIndex index,
        IReadOnlyList<string> fontBookPaths,
        IReadOnlyList<FontRemovalItem> ruleItems,
        IReadOnlyList<FontRemovalItem> usageItems,
        IReadOnlyList<FontRemovalItem> notes)
    {
        _index = index;
        FontBookPaths = fontBookPaths;
        RuleItems = ruleItems;
        UsageItems = usageItems;
        Notes = notes;
    }

    /// <summary>The font files to delete.</summary>
    public IReadOnlyList<string> FontBookPaths { get; }

    /// <summary>The changes of <c>@font-face</c> rules.</summary>
    public IReadOnlyList<FontRemovalItem> RuleItems { get; }

    /// <summary>The changes of the places that use the removed families (and the shorthands to fix by hand).</summary>
    public IReadOnlyList<FontRemovalItem> UsageItems { get; }

    /// <summary>Families that lose only some of their faces.</summary>
    public IReadOnlyList<FontRemovalItem> Notes { get; }

    /// <summary>Whether deleting the fonts touches no stylesheet at all (a plain confirmation is enough).</summary>
    public bool IsEmpty => RuleItems.Count == 0 && UsageItems.Count == 0 && Notes.Count == 0;

    /// <summary>Plans the removal of the font files <paramref name="fontBookPaths"/>.</summary>
    public static FontRemovalPlan Create(BookFontIndex index, IReadOnlyCollection<string> fontBookPaths)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(fontBookPaths);
        HashSet<string> deleted = new(fontBookPaths, StringComparer.Ordinal);

        List<FontRemovalItem> ruleItems = new();
        HashSet<FontFaceRule> removedRules = new();
        foreach (FontFaceRule rule in index.Rules)
        {
            List<FontFaceSource> remaining = rule.Sources.Where(s => s.BookPath is null || !deleted.Contains(s.BookPath)).ToList();
            if (remaining.Count == rule.Sources.Count)
            {
                continue;
            }

            string text = index.TextOf(rule.BookPath);
            string face = Describe(rule);
            if (remaining.Count == 0 || rule.SrcDeclaration is null)
            {
                removedRules.Add(rule);
                (int start, int end) = ExpandToLines(text, rule.Start, rule.End);
                ruleItems.Add(new FontRemovalItem(
                    "rule|" + rule.BookPath + "|" + rule.Start,
                    FontRemovalItemKind.RemoveRule,
                    CoreStrings.Format("FontRemoval_RemoveRule", rule.Family, face),
                    rule.BookPath,
                    rule.Start)
                {
                    EditStart = start,
                    EditEnd = end,
                });
                continue;
            }

            string removedSources = string.Join(", ", rule.Sources.Except(remaining).Select(s => s.Text));
            ruleItems.Add(new FontRemovalItem(
                "src|" + rule.BookPath + "|" + rule.Start,
                FontRemovalItemKind.RemoveSources,
                CoreStrings.Format("FontRemoval_RemoveSources", rule.Family, face, removedSources),
                rule.BookPath,
                rule.Start)
            {
                EditStart = rule.SrcDeclaration.ValueStart,
                EditEnd = rule.SrcDeclaration.ValueEnd,
                Replacement = string.Join(", ", remaining.Select(s => s.Text)),
            });
        }

        // Families: removed entirely, or losing some faces.
        List<FontRemovalItem> notes = new();
        HashSet<string> removedFamilies = new(StringComparer.Ordinal);
        foreach (IGrouping<string, FontFaceRule> family in index.Rules.Where(r => r.Family.Length > 0).GroupBy(r => r.FamilyKey))
        {
            List<FontFaceRule> lost = family.Where(removedRules.Contains).ToList();
            if (lost.Count == 0)
            {
                continue;
            }

            List<FontFaceRule> kept = family.Where(r => !removedRules.Contains(r)).ToList();
            if (kept.Count == 0)
            {
                removedFamilies.Add(family.Key);
                continue;
            }

            List<string> keptFaces = kept.Select(Describe).Distinct(StringComparer.Ordinal).ToList();
            List<string> lostFaces = lost.Select(Describe).Distinct(StringComparer.Ordinal).Except(keptFaces, StringComparer.Ordinal).ToList();
            if (lostFaces.Count == 0)
            {
                // Every lost face is still defined by another rule — nothing changes for the reader.
                continue;
            }

            FontFaceRule first = lost[0];
            notes.Add(new FontRemovalItem(
                "faces|" + family.Key,
                FontRemovalItemKind.FamilyLosesFaces,
                CoreStrings.Format("FontRemoval_FamilyLosesFaces", first.Family, string.Join(", ", lostFaces), string.Join(", ", keptFaces)),
                first.BookPath,
                first.Start));
        }

        List<FontRemovalItem> usageItems = new();
        foreach (FontFamilyUsage usage in index.Usages)
        {
            if (!usage.Families.Any(f => IsRemoved(f, removedFamilies)))
            {
                continue;
            }

            usageItems.Add(PlanUsage(index.TextOf(usage.BookPath), usage, removedFamilies));
        }

        return new FontRemovalPlan(index, fontBookPaths.ToList(), ruleItems, usageItems, notes);
    }

    /// <summary>
    /// The new texts of the files changed by the actionable items whose <see cref="FontRemovalItem.Key"/> is not in
    /// <paramref name="excludedKeys"/> (by book path; the texts the plan was made from are the base).
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildNewTexts(IReadOnlySet<string> excludedKeys)
    {
        ArgumentNullException.ThrowIfNull(excludedKeys);
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        IEnumerable<FontRemovalItem> applied = RuleItems.Concat(UsageItems)
            .Where(i => i.IsActionable && !excludedKeys.Contains(i.Key));
        foreach (IGrouping<string, FontRemovalItem> file in applied.GroupBy(i => i.BookPath))
        {
            string text = _index.TextOf(file.Key);
            List<(int Start, int End, string Replacement)> edits = new();

            // Edits inside a style attribute are combined per attribute — an attribute left empty is removed.
            foreach (IGrouping<int, FontRemovalItem> attribute in file.Where(i => i.Attribute is not null).GroupBy(i => i.Attribute!.AttributeValueStart))
            {
                FontFamilyUsage owner = attribute.First().Attribute!;
                string value = ApplyEdits(
                    text[owner.AttributeValueStart..owner.AttributeValueEnd],
                    attribute.Select(i => (i.EditStart - owner.AttributeValueStart, i.EditEnd - owner.AttributeValueStart, i.Replacement)));
                edits.Add(string.IsNullOrWhiteSpace(value)
                    ? (owner.AttributeStart, owner.AttributeEnd, string.Empty)
                    : (owner.AttributeValueStart, owner.AttributeValueEnd, value));
            }

            edits.AddRange(file.Where(i => i.Attribute is null).Select(i => (i.EditStart, i.EditEnd, i.Replacement)));
            string newText = ApplyEdits(text, edits);
            if (!string.Equals(newText, text, StringComparison.Ordinal))
            {
                result[file.Key] = newText;
            }
        }

        return result;
    }

    private static FontRemovalItem PlanUsage(string text, FontFamilyUsage usage, IReadOnlySet<string> removedFamilies)
    {
        string key = "usage|" + usage.BookPath + "|" + usage.DeclarationStart;
        string removedNames = string.Join(", ", usage.Families.Where(f => IsRemoved(f, removedFamilies)).Select(f => f.Name));
        string remaining = FontFamilyList.Without(text, usage.Families, f => IsRemoved(f, removedFamilies));
        FontFamilyUsage? attribute = usage.Kind == FontStyleSourceKind.StyleAttribute ? usage : null;
        if (remaining.Length > 0)
        {
            return new FontRemovalItem(
                key,
                FontRemovalItemKind.RemoveFamily,
                CoreStrings.Format("FontRemoval_RemoveFamily", usage.Context, usage.Property, usage.Value, remaining),
                usage.BookPath,
                usage.DeclarationStart)
            {
                EditStart = usage.Families[0].Start,
                EditEnd = usage.Families[^1].End,
                Replacement = remaining,
                Attribute = attribute,
            };
        }

        if (usage.Property == "font")
        {
            return new FontRemovalItem(
                key,
                FontRemovalItemKind.ManualFix,
                CoreStrings.Format("FontRemoval_ManualFix", usage.Context, usage.Value, removedNames),
                usage.BookPath,
                usage.DeclarationStart);
        }

        (int start, int end) = attribute is null
            ? ExpandToLines(text, usage.DeclarationStart, usage.DeclarationEnd)
            : (usage.DeclarationStart, SkipSpaces(text, usage.DeclarationEnd, usage.AttributeValueEnd));
        return new FontRemovalItem(
            key,
            FontRemovalItemKind.RemoveDeclaration,
            CoreStrings.Format("FontRemoval_RemoveDeclaration", usage.Context, usage.Property, usage.Value),
            usage.BookPath,
            usage.DeclarationStart)
        {
            EditStart = start,
            EditEnd = end,
            Attribute = attribute,
        };
    }

    private static bool IsRemoved(FontFamilyEntry entry, IReadOnlySet<string> removedFamilies) =>
        !entry.IsGeneric && removedFamilies.Contains(FontFaceDescriptors.FamilyKey(entry.Name));

    private static string Describe(FontFaceRule rule) => FontFaceDescriptors.Describe(rule.Weight, rule.Style);

    /// <summary>
    /// Widens the range of a rule or declaration being removed to whole lines when it stands alone on its lines: the
    /// indentation before it, the rest of its last line and the blank lines after it.
    /// </summary>
    internal static (int Start, int End) ExpandToLines(string text, int start, int end)
    {
        int s = start;
        while (s > 0 && text[s - 1] is ' ' or '\t')
        {
            s--;
        }

        if (s > 0 && text[s - 1] != '\n')
        {
            // Something else precedes it on the line — remove it with the spaces after it only.
            return (start, SkipSpaces(text, end, text.Length));
        }

        int e = end;
        while (e < text.Length && text[e] is ' ' or '\t' or '\r')
        {
            e++;
        }

        if (e < text.Length && text[e] != '\n')
        {
            return (start, e);
        }

        // Its line ends here — the line break and the blank lines after it go too.
        while (e < text.Length)
        {
            int lineEnd = text.IndexOf('\n', e);
            if (lineEnd < 0)
            {
                return string.IsNullOrWhiteSpace(text[e..]) ? (s, text.Length) : (s, e);
            }

            if (!string.IsNullOrWhiteSpace(text[e..lineEnd]))
            {
                break;
            }

            e = lineEnd + 1;
        }

        return (s, e);
    }

    private static int SkipSpaces(string text, int pos, int end)
    {
        while (pos < end && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }

        return pos;
    }

    // Applies non-overlapping edits (an edit overlapping an already applied one is skipped).
    private static string ApplyEdits(string text, IEnumerable<(int Start, int End, string Replacement)> edits)
    {
        StringBuilder sb = new(text);
        int limit = int.MaxValue;
        foreach ((int start, int end, string replacement) in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.End))
        {
            if (end > limit || start < 0 || end > text.Length || start > end)
            {
                continue;
            }

            sb.Remove(start, end - start).Insert(start, replacement);
            limit = start;
        }

        return sb.ToString();
    }
}
