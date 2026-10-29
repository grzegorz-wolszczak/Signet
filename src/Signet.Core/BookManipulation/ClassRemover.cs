using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>How many times a class is used in one file — the per-file breakdown of a "Remove Class" confirmation.</summary>
/// <param name="BookPath">The file.</param>
/// <param name="Occurrences">Class attribute tokens and CSS selector references in this file, combined.</param>
public sealed record ClassUsageFileSummary(string BookPath, int Occurrences);

/// <summary>The scale of a class removal (the counter shown in the confirmation dialog).</summary>
/// <param name="AttributeOccurrences">Tokens removed from <c>class="…"</c> attributes.</param>
/// <param name="RulesRemoved">CSS rules deleted entirely because their whole selector group named only this class.</param>
/// <param name="ChangedFiles">The files (XHTML and CSS) that change.</param>
public sealed record ClassRemovalStats(int AttributeOccurrences, int RulesRemoved, int ChangedFiles);

/// <summary>The result of a class removal: statistics and the new texts of the changed files (key = path in the book).</summary>
public sealed record ClassRemovalResult(ClassRemovalStats Stats, IReadOnlyDictionary<string, string> ChangedTexts);

/// <summary>
/// "Remove Class" from the Code Editor context menu — removes a class from every <c>class="…"</c> attribute and
/// every CSS selector (stylesheets and <c>&lt;style&gt;</c> blocks) across the whole book.
/// </summary>
/// <remarks>
/// <para>
/// A <c>class</c> attribute left empty by the removal is dropped entirely. In a selector, only the
/// <c>.name</c> naming this class is stripped — <c>.name.other</c> keeps <c>.other</c>, <c>div.name</c> keeps
/// <c>div</c> — but when a member of a comma-separated selector group named only this class, that member is
/// dropped from the group; a rule whose whole group collapses this way (e.g. a plain <c>.name { … }</c>) is
/// deleted entirely.
/// </para>
/// <para>
/// The class is matched literally and case-sensitively, like <see cref="ClassUsageFinder"/> and
/// <see cref="ClassRenamer"/>. Unlike <see cref="ClassRenamer"/> there is no cascade or scope to choose — a
/// removal always applies everywhere the name occurs. Operates only on the texts passed to the constructor;
/// saving is done by <see cref="Book.ApplyClassRemoval"/>.
/// </para>
/// </remarks>
public sealed class ClassRemover
{
    private readonly List<ClassUsageSource> _sources;

    /// <summary>Loads the book's XHTML and CSS files.</summary>
    public ClassRemover(IEnumerable<ClassRenameSource> htmlSources, IEnumerable<ClassRenameSource> cssSources)
    {
        ArgumentNullException.ThrowIfNull(htmlSources);
        ArgumentNullException.ThrowIfNull(cssSources);

        _sources = cssSources.Select(s => new ClassUsageSource(s.BookPath, s.Text, IsStyleSheet: true))
            .Concat(htmlSources.Select(s => new ClassUsageSource(s.BookPath, s.Text, IsStyleSheet: false)))
            .ToList();
    }

    /// <summary>The usages of <paramref name="className"/> in the book (for a confirmation dialog), in file order.</summary>
    public IReadOnlyList<ClassUsage> FindUsages(string className) => ClassUsageFinder.Find(className, _sources);

    /// <summary>The usages of <paramref name="className"/>, grouped and counted by file, in file order.</summary>
    public IReadOnlyList<ClassUsageFileSummary> SummarizeUsages(string className) =>
        FindUsages(className)
            .GroupBy(u => u.BookPath)
            .Select(g => new ClassUsageFileSummary(g.Key, g.Count()))
            .ToList();

    /// <summary>Removes <paramref name="className"/> everywhere it is used; the changed files' new texts and the statistics.</summary>
    public ClassRemovalResult Remove(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        int attributeOccurrences = 0;
        int rulesRemoved = 0;
        Dictionary<string, string> changed = new(StringComparer.Ordinal);
        HashSet<string> classNames = new(StringComparer.Ordinal) { className };

        foreach (ClassUsageSource source in _sources)
        {
            if (!source.Text.Contains(className, StringComparison.Ordinal))
            {
                continue;
            }

            List<(int Start, int End, string? Replacement)> edits = new();

            if (source.IsStyleSheet)
            {
                rulesRemoved += RemoveFromSelectors(edits, source.Text, new CssInfo(source.Text).Rules, className);
            }
            else
            {
                attributeOccurrences += ClassAttributeEditor.AddRemovalEdits(edits, source.Text, classNames);
                foreach (CssInfo block in new HtmlStyleInfo(source.Text).Styles)
                {
                    rulesRemoved += RemoveFromSelectors(edits, source.Text, block.Rules, className);
                }
            }

            if (edits.Count > 0)
            {
                changed[source.BookPath] = ClassAttributeEditor.ApplyEdits(source.Text, edits);
            }
        }

        return new ClassRemovalResult(new ClassRemovalStats(attributeOccurrences, rulesRemoved, changed.Count), changed);
    }

    // ---- CSS selectors (stylesheets and <style> blocks) ----

    private static int RemoveFromSelectors(
        List<(int Start, int End, string? Replacement)> edits, string text, IEnumerable<CssRule> rules, string className)
    {
        int rulesRemoved = 0;
        foreach (CssRule rule in rules)
        {
            if (rule.SelectorText.Length == 0 || rule.BlockStart <= rule.SelectorStart || rule.BlockStart > text.Length)
            {
                continue;
            }

            string region = text[rule.SelectorStart..rule.BlockStart];
            if (!region.Contains(className, StringComparison.Ordinal))
            {
                continue;
            }

            bool anyMatched = false;
            List<string> survivors = new();
            foreach (string member in CssInfo.SplitGroupSelector(region))
            {
                List<(int Index, string Name)> classes = CssToolbox.FindClassesInSelector(member)
                    .Where(c => string.Equals(c.Name, className, StringComparison.Ordinal))
                    .ToList();
                if (classes.Count == 0)
                {
                    survivors.Add(member);
                    continue;
                }

                anyMatched = true;
                string stripped = StripClassOccurrences(member, classes);
                if (stripped.Trim().Length > 0)
                {
                    survivors.Add(stripped);
                }
            }

            if (!anyMatched)
            {
                continue;
            }

            if (survivors.Count == 0)
            {
                rulesRemoved++;
                edits.Add((rule.SelectorStart, rule.BlockEnd, null));
            }
            else
            {
                edits.Add((rule.SelectorStart, rule.BlockStart, string.Join(", ", survivors) + " "));
            }
        }

        return rulesRemoved;
    }

    private static string StripClassOccurrences(string text, IReadOnlyList<(int Index, string Name)> classes)
    {
        StringBuilder sb = new(text);
        foreach ((int index, string name) in classes.OrderByDescending(c => c.Index))
        {
            sb.Remove(index - 1, name.Length + 1); // the dot and the name
        }

        return sb.ToString();
    }

}
