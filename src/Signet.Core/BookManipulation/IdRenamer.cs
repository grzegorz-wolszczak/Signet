using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.Localization;
using Signet.Core.Misc;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>The reason a new id was rejected (<see cref="IdRenamer.Validate"/>).</summary>
public enum IdNameProblem
{
    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>Not a valid XML name without a colon (NCName) — see <see cref="MarkupEdits.IsValidId"/>.</summary>
    Invalid,

    /// <summary>The name is the same as the current one.</summary>
    SameAsOld,

    /// <summary>An id with this name already exists in the book (an <c>id</c> attribute or a CSS <c>#name</c> selector).</summary>
    AlreadyExists,
}

/// <summary>The result of validating a new id; an empty problem list = the name is valid.</summary>
public sealed record IdNameValidation(IReadOnlyList<IdNameProblem> Problems)
{
    /// <summary>Whether the name can be used.</summary>
    public bool IsValid => Problems.Count == 0;
}

/// <summary>The scale of an id rename (the counter shown in the dialog).</summary>
/// <param name="AttributeOccurrences">The <c>id="…"</c> attributes renamed (normally 1, more only for a pre-existing duplicate id).</param>
/// <param name="SelectorOccurrences">The <c>#name</c> occurrences renamed in CSS selectors (stylesheets and <c>&lt;style&gt;</c> blocks).</param>
/// <param name="ReferencesUpdated">The <c>href="…#name"</c> fragments updated because they pointed at a renamed <c>id</c>.</param>
/// <param name="ChangedFiles">The files that will change.</param>
public sealed record IdRenameStats(int AttributeOccurrences, int SelectorOccurrences, int ReferencesUpdated, int ChangedFiles);

/// <summary>The result of an id rename: statistics and the new texts of the changed files (key = path in the book).</summary>
public sealed record IdRenameResult(IdRenameStats Stats, IReadOnlyDictionary<string, string> ChangedTexts);

/// <summary>
/// "Rename #id" from the Code Editor context menu — renames an id across the whole book: every <c>id="…"</c>
/// attribute equal to the name (every one of them, so a pre-existing duplicate id is kept consistent rather than
/// split in two), every <c>#name</c> in a CSS selector (a stylesheet or a <c>&lt;style&gt;</c> block), and every
/// <c>href="…#name"</c> fragment that actually points at a file carrying that id. Scoped to XHTML and CSS, like
/// <see cref="ClassRenamer"/> (not SVG/XML).
/// </summary>
/// <remarks>
/// A <c>href</c> fragment is rewritten only when its target file (itself, for a bare <c>#name</c>, or the file the
/// path part resolves to) is one of the files that actually carry the renamed id — a link to an unrelated file
/// whose fragment happens to read the same name is left alone. Operates only on the texts passed to the
/// constructor; saving is done by <see cref="Book.ApplyIdRename"/>.
/// </remarks>
public sealed class IdRenamer
{
    private readonly List<ClassRenameSource> _htmlSources;
    private readonly List<ClassRenameSource> _cssSources;
    private readonly Dictionary<string, string> _textByPath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _existingIds = new(StringComparer.Ordinal);

    /// <summary>Loads the book's XHTML and CSS files.</summary>
    public IdRenamer(IEnumerable<ClassRenameSource> htmlSources, IEnumerable<ClassRenameSource> cssSources)
    {
        ArgumentNullException.ThrowIfNull(htmlSources);
        ArgumentNullException.ThrowIfNull(cssSources);

        _htmlSources = htmlSources.ToList();
        _cssSources = cssSources.ToList();

        foreach (ClassRenameSource html in _htmlSources)
        {
            _textByPath[html.BookPath] = html.Text;
            foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(html.Text))
            {
                TagLister.AttInfo id = TagLister.ParseAttribute(html.Text.Substring(pos, len), "id");
                if (id.Pos >= 0 && id.VLen > 0)
                {
                    _existingIds.Add(id.AValue);
                }
            }

            foreach (CssInfo block in new HtmlStyleInfo(html.Text).Styles)
            {
                AddSelectorIds(block.Rules, html.Text);
            }
        }

        foreach (ClassRenameSource css in _cssSources)
        {
            _textByPath[css.BookPath] = css.Text;
            AddSelectorIds(new CssInfo(css.Text).Rules, css.Text);
        }
    }

    /// <summary>
    /// Validates a new id: a valid XML name (NCName, see <see cref="MarkupEdits.IsValidId"/>), different from the
    /// current name and not yet present in the book.
    /// </summary>
    public IdNameValidation Validate(string oldId, string newId)
    {
        ArgumentNullException.ThrowIfNull(oldId);
        ArgumentNullException.ThrowIfNull(newId);

        List<IdNameProblem> problems = new();
        if (newId.Length == 0)
        {
            problems.Add(IdNameProblem.Empty);
            return new IdNameValidation(problems);
        }

        if (!MarkupEdits.IsValidId(newId))
        {
            problems.Add(IdNameProblem.Invalid);
        }

        if (string.Equals(newId, oldId, StringComparison.Ordinal))
        {
            problems.Add(IdNameProblem.SameAsOld);
        }
        else if (_existingIds.Contains(newId))
        {
            problems.Add(IdNameProblem.AlreadyExists);
        }

        return new IdNameValidation(problems);
    }

    /// <summary>The scale of the rename — without computing the new texts (it does not depend on the new name).</summary>
    public IdRenameStats Preview(string oldId) => Compute(oldId, oldId, buildTexts: false).Stats;

    /// <summary>Computes the new file texts after the rename. The name must pass <see cref="Validate"/>.</summary>
    public IdRenameResult Rename(string oldId, string newId)
    {
        ArgumentNullException.ThrowIfNull(oldId);
        if (!Validate(oldId, newId).IsValid)
        {
            throw new ArgumentException(CoreStrings.Format("Error_InvalidId", newId), nameof(newId));
        }

        return Compute(oldId, newId, buildTexts: true);
    }

    private IdRenameResult Compute(string oldId, string newId, bool buildTexts)
    {
        List<ClassUsageSource> sources = _htmlSources
            .Select(h => new ClassUsageSource(h.BookPath, h.Text, IsStyleSheet: false))
            .Concat(_cssSources.Select(c => new ClassUsageSource(c.BookPath, c.Text, IsStyleSheet: true)))
            .ToList();

        Dictionary<string, List<Edit>> editsByFile = new(StringComparer.Ordinal);
        int attributeCount = 0;
        int selectorCount = 0;
        HashSet<string> homeFiles = new(StringComparer.Ordinal);
        foreach (ClassUsage usage in IdUsageFinder.Find(oldId, sources))
        {
            EditsOf(editsByFile, usage.BookPath).Add(new Edit(usage.Offset, oldId.Length, newId));
            if (usage.Kind == ClassUsageKind.IdAttribute)
            {
                attributeCount++;
                homeFiles.Add(usage.BookPath);
            }
            else
            {
                selectorCount++;
            }
        }

        int referencesUpdated = 0;
        foreach (ClassRenameSource html in _htmlSources)
        {
            string folder = Core.BookPath.StartingDir(html.BookPath);
            foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(html.Text))
            {
                string tagText = html.Text.Substring(pos, len);
                TagLister.AttInfo href = TagLister.ParseAttribute(tagText, "href");
                if (href.Pos == -1 || href.VLen <= 0)
                {
                    continue;
                }

                string value = href.AValue;
                int hash = value.IndexOf('#', StringComparison.Ordinal);
                if (hash < 0 || !string.Equals(value[(hash + 1)..], oldId, StringComparison.Ordinal))
                {
                    continue;
                }

                string path = value[..hash];
                string? target = path.Length == 0
                    ? html.BookPath
                    : LinkReference.IsExternal(value) ? null : LinkReference.ResolveBookPath(value, folder);
                if (target is null || !homeFiles.Contains(target))
                {
                    continue;
                }

                EditsOf(editsByFile, html.BookPath).Add(new Edit(pos + href.VPos + hash + 1, oldId.Length, newId));
                referencesUpdated++;
            }
        }

        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        if (buildTexts)
        {
            foreach ((string bookPath, List<Edit> edits) in editsByFile)
            {
                texts[bookPath] = ApplyEdits(_textByPath[bookPath], edits);
            }
        }

        IdRenameStats stats = new(attributeCount, selectorCount, referencesUpdated, editsByFile.Count);
        return new IdRenameResult(stats, texts);
    }

    private void AddSelectorIds(IReadOnlyList<CssRule> rules, string text)
    {
        foreach (CssRule rule in rules)
        {
            if (rule.SelectorText.Length == 0 || rule.BlockStart <= rule.SelectorStart || rule.BlockStart > text.Length)
            {
                continue;
            }

            foreach ((int _, string name) in CssToolbox.FindIdsInSelector(text[rule.SelectorStart..rule.BlockStart]))
            {
                _existingIds.Add(name);
            }
        }
    }

    private static List<Edit> EditsOf(Dictionary<string, List<Edit>> editsByFile, string bookPath)
    {
        if (!editsByFile.TryGetValue(bookPath, out List<Edit>? edits))
        {
            edits = new List<Edit>();
            editsByFile[bookPath] = edits;
        }

        return edits;
    }

    private static string ApplyEdits(string text, List<Edit> edits)
    {
        StringBuilder sb = new(text);
        foreach (Edit edit in edits.OrderByDescending(e => e.Pos))
        {
            sb.Remove(edit.Pos, edit.Length).Insert(edit.Pos, edit.Replacement);
        }

        return sb.ToString();
    }

    private readonly record struct Edit(int Pos, int Length, string Replacement);
}
