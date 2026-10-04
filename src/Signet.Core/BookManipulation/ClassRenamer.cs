using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>A book file passed to <see cref="ClassRenamer"/>: its path in the book and its current text.</summary>
public sealed record ClassRenameSource(string BookPath, string Text);

/// <summary>
/// The class under the caret in a <c>class</c> attribute: the name and the chain of tag names from the root to
/// the element carrying that attribute, inclusive (e.g. <c>html, body, p</c>).
/// </summary>
public sealed record ClassAtCaret(string Name, IReadOnlyList<string> ElementPath);

/// <summary>
/// The class under the caret in a CSS selector — in a stylesheet or in a <c>&lt;style&gt;</c> block of an XHTML file.
/// </summary>
/// <param name="Name">The class name (without the dot).</param>
/// <param name="BookPath">The file containing the selector.</param>
/// <param name="StyleBlockIndex">The index of the <c>&lt;style&gt;</c> block in the XHTML file or <c>-1</c> for a CSS stylesheet.</param>
public sealed record StyleClassAtCaret(string Name, string BookPath, int StyleBlockIndex);

/// <summary>The scope of a class rename.</summary>
public enum ClassRenameScope
{
    /// <summary>All occurrences in the book; style definitions are renamed in place.</summary>
    Everywhere,

    /// <summary>Only elements with the same chain of tag names from the root as the element under the caret.</summary>
    SameNesting,

    /// <summary>
    /// Only elements that use the rules with this class in a single style source (a stylesheet or a
    /// <c>&lt;style&gt;</c> block) — a rename started in a selector.
    /// </summary>
    StyleSource,
}

/// <summary>What is being renamed: the class name and the scope (with the element or style source the rename started from).</summary>
public sealed record ClassRenameRequest
{
    private ClassRenameRequest(string oldName, ClassRenameScope scope, IReadOnlyList<string> elementPath, StyleClassAtCaret? source)
    {
        OldName = oldName;
        Scope = scope;
        ElementPath = elementPath;
        Source = source;
    }

    /// <summary>The current class name.</summary>
    public string OldName { get; }

    /// <summary>The scope.</summary>
    public ClassRenameScope Scope { get; }

    /// <summary>The element's tag chain (for <see cref="ClassRenameScope.SameNesting"/>), otherwise empty.</summary>
    public IReadOnlyList<string> ElementPath { get; }

    /// <summary>The style source (for <see cref="ClassRenameScope.StyleSource"/>), otherwise <c>null</c>.</summary>
    public StyleClassAtCaret? Source { get; }

    /// <summary>A rename started on an XHTML element (<see cref="ClassRenameScope.Everywhere"/> or <see cref="ClassRenameScope.SameNesting"/>).</summary>
    public static ClassRenameRequest ForElement(ClassAtCaret target, ClassRenameScope scope)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (scope == ClassRenameScope.StyleSource)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        return new ClassRenameRequest(target.Name, scope, target.ElementPath, null);
    }

    /// <summary>A rename started in a selector of a stylesheet or a <c>&lt;style&gt;</c> block.</summary>
    public static ClassRenameRequest ForStyleSource(StyleClassAtCaret target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ClassRenameRequest(target.Name, ClassRenameScope.StyleSource, Array.Empty<string>(), target);
    }
}

/// <summary>The reason a new class name was rejected.</summary>
public enum ClassNameProblem
{
    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>The name contains a whitespace character.</summary>
    ContainsWhitespace,

    /// <summary>The name contains characters outside a CSS identifier (see <see cref="ClassNameValidation.InvalidCharacters"/>).</summary>
    ContainsInvalidCharacters,

    /// <summary>The name starts with a digit.</summary>
    StartsWithDigit,

    /// <summary>The name starts with a hyphen followed by a digit.</summary>
    StartsWithHyphenAndDigit,

    /// <summary>The name is just a hyphen.</summary>
    SingleHyphen,

    /// <summary>The name is the same as the current one.</summary>
    SameAsOld,

    /// <summary>A class with this name already exists in the book (a <c>class</c> attribute or a CSS selector).</summary>
    AlreadyExists,
}

/// <summary>The result of validating a new class name; an empty problem list = the name is valid.</summary>
public sealed record ClassNameValidation(IReadOnlyList<ClassNameProblem> Problems, IReadOnlyList<char> InvalidCharacters)
{
    /// <summary>Whether the name can be used.</summary>
    public bool IsValid => Problems.Count == 0;
}

/// <summary>What happens to the class definitions in a single style source.</summary>
public enum ClassRenameSourceChange
{
    /// <summary>Only the renamed elements use these definitions (or none, when renaming everywhere / in this source) — renamed in place.</summary>
    RenamedInPlace,

    /// <summary>Both the renamed elements and elements keeping the old name use them — the rules are copied under the new name.</summary>
    Copied,

    /// <summary>None of the renamed elements uses them — unchanged.</summary>
    Unchanged,
}

/// <summary>A style source defining the class and what will happen to it.</summary>
/// <param name="BookPath">The source file.</param>
/// <param name="StyleBlockIndex">The index of the <c>&lt;style&gt;</c> block in the XHTML file or <c>-1</c> for a CSS stylesheet.</param>
/// <param name="Change">The change in this source.</param>
/// <param name="Rules">The rules with the class in the selector.</param>
public sealed record ClassRenameSourceAction(string BookPath, int StyleBlockIndex, ClassRenameSourceChange Change, int Rules);

/// <summary>The scale of a class rename (the counter and description in the dialog).</summary>
/// <param name="ChangedOccurrences">The occurrences of the class in <c>class</c> attributes that will get the new name.</param>
/// <param name="TotalOccurrences">All occurrences of the class in <c>class</c> attributes in the book.</param>
/// <param name="ChangedFiles">The files (XHTML and CSS) that will change.</param>
/// <param name="Sources">The style sources defining the class (order: stylesheets, then <c>&lt;style&gt;</c> blocks).</param>
/// <param name="SourcesUsedByChangedElements">How many sources in total are used by the renamed elements.</param>
public sealed record ClassRenameStats(
    int ChangedOccurrences,
    int TotalOccurrences,
    int ChangedFiles,
    IReadOnlyList<ClassRenameSourceAction> Sources,
    int SourcesUsedByChangedElements)
{
    /// <summary>Whether any source gets copies of the rules.</summary>
    public bool CopiesCssRules => Sources.Any(s => s.Change == ClassRenameSourceChange.Copied);
}

/// <summary>The result of a class rename: statistics and the new texts of the changed files (key = path in the book).</summary>
public sealed record ClassRenameResult(ClassRenameStats Stats, IReadOnlyDictionary<string, string> ChangedTexts);

/// <summary>
/// "Rename Class" from the Code View context menu — renames a class in <c>class</c> attributes and in the
/// selectors of CSS stylesheets and <c>&lt;style&gt;</c> blocks across the whole book, taking the
/// cascade into account: which style sources actually style which element.
/// </summary>
/// <remarks>
/// <para>
/// An element uses a style source when the source is visible to its document (a stylesheet linked
/// via <c>&lt;link rel="stylesheet"&gt;</c>, also indirectly through an <c>@import</c> chain, or a
/// <c>&lt;style&gt;</c> block of that document) and the element matches a selector with the class in one of its
/// rules. State pseudo-classes (<c>:hover</c>…) and pseudo-elements (<c>::before</c>…) are
/// ignored when matching, <c>@media</c> is treated as always satisfied, and a selector that AngleSharp does not
/// understand is treated as matching (safer to change too much than to lose a style).
/// </para>
/// <para>
/// For each source defining the class: when none of the renamed elements uses it —
/// unchanged (exception: the "everywhere" scope and the source the rename started from are renamed in place,
/// even when none of them is used); when only the renamed elements use it — renamed
/// in place; otherwise every rule with the class is copied right below the original under the
/// new name (for a selector group only the members with the class; inside <c>@media</c> the copy stays in the same block).
/// </para>
/// <para>
/// Operates only on the texts passed to the constructor; saving is done by
/// <see cref="Book.ApplyClassRename"/>. The remaining files are left untouched.
/// </para>
/// </remarks>
public sealed class ClassRenamer
{
    private readonly List<HtmlFile> _htmlFiles;
    private readonly List<StyleSheet> _sources = new();
    private readonly Dictionary<string, StyleSheet> _cssByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _importsByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cssTexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _existingClasses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Usage> _usageCache = new(StringComparer.Ordinal);

    /// <summary>Loads the book's XHTML and CSS files.</summary>
    public ClassRenamer(IEnumerable<ClassRenameSource> htmlSources, IEnumerable<ClassRenameSource> cssSources)
    {
        ArgumentNullException.ThrowIfNull(htmlSources);
        ArgumentNullException.ThrowIfNull(cssSources);

        foreach (ClassRenameSource css in cssSources)
        {
            StyleSheet sheet = new(css.BookPath, -1, new CssInfo(css.Text).Rules);
            _sources.Add(sheet);
            _cssByPath[css.BookPath] = sheet;
            _cssTexts[css.BookPath] = css.Text;
            _importsByPath[css.BookPath] = CssImports.Parse(css.Text, Core.BookPath.StartingDir(css.BookPath));
        }

        _htmlFiles = htmlSources.Select(s => new HtmlFile(s.BookPath, s.Text)).ToList();
        foreach (HtmlFile html in _htmlFiles)
        {
            _sources.AddRange(html.StyleBlocks);
            html.VisibleSheets = VisibleSheets(html);

            foreach (ClassAttribute attribute in html.ClassAttributes)
            {
                foreach ((int _, int _, string name) in attribute.Tokens)
                {
                    _existingClasses.Add(name);
                }
            }
        }

        foreach (StyleSheet source in _sources)
        {
            foreach (CssRule rule in source.Rules)
            {
                foreach ((int _, string name) in CssToolbox.FindClassesInSelector(rule.SelectorText))
                {
                    _existingClasses.Add(name);
                }
            }
        }
    }

    /// <summary>
    /// The class the caret is on in the <c>class</c> attribute value of an opening or empty
    /// tag (inside the name or right at its left/right edge), with the chain of tag names
    /// from the root. <c>null</c> when the caret is not on a class name.
    /// </summary>
    public static ClassAtCaret? FindClassAtCaret(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return null;
        }

        int pos = Math.Clamp(caretOffset, 0, text.Length);
        TagLister lister = new(text);
        TagLister.TagInfo tag = lister.At(lister.FindFirstTagOnOrAfter(pos));
        if (pos < tag.Pos || pos >= tag.Pos + tag.Len || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
        {
            return null;
        }

        TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), "class");
        if (attribute.Pos == -1 || attribute.VLen <= 0)
        {
            return null;
        }

        int relativeCaret = pos - (tag.Pos + attribute.VPos);
        foreach ((int start, int length, string name) in ClassTokens(attribute.AValue))
        {
            if (relativeCaret >= start && relativeCaret <= start + length)
            {
                return new ClassAtCaret(name, ElementPathOf(tag));
            }
        }

        return null;
    }

    /// <summary>
    /// The class the caret is on in a rule selector — in a CSS stylesheet
    /// (<paramref name="isStyleSheet"/>) or in a <c>&lt;style&gt;</c> block of an XHTML file. The caret may
    /// be inside the name, right after it, or before/on the dot (a <c>.class</c> selection
    /// made right to left). <c>null</c> when the caret is not on a class name in a selector.
    /// </summary>
    public static StyleClassAtCaret? FindStyleClassAtCaret(string text, int caretOffset, string bookPath, bool isStyleSheet)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(bookPath);

        IReadOnlyList<CssInfo> blocks = isStyleSheet ? [new CssInfo(text)] : new HtmlStyleInfo(text).Styles;
        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            foreach (CssRule rule in blocks[blockIndex].Rules)
            {
                if (rule.SelectorText.Length == 0 || caretOffset < rule.SelectorStart - 1 || caretOffset > rule.BlockStart)
                {
                    continue;
                }

                string region = text[rule.SelectorStart..rule.BlockStart];
                foreach ((int index, string name) in CssToolbox.FindClassesInSelector(region))
                {
                    int start = rule.SelectorStart + index;
                    if (caretOffset >= start - 1 && caretOffset <= start + name.Length)
                    {
                        return new StyleClassAtCaret(name, bookPath, isStyleSheet ? -1 : blockIndex);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Whether a class with this name occurs in the book — in a <c>class</c> attribute or a CSS selector.</summary>
    public bool ClassExists(string name) => _existingClasses.Contains(name);

    /// <summary>
    /// Validates a new class name: a CSS identifier without whitespace and escape sequences,
    /// different from the current name and not yet present in the book.
    /// </summary>
    public ClassNameValidation Validate(string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);

        List<ClassNameProblem> problems = new();
        if (newName.Length == 0)
        {
            problems.Add(ClassNameProblem.Empty);
            return new ClassNameValidation(problems, Array.Empty<char>());
        }

        if (newName.Any(char.IsWhiteSpace))
        {
            problems.Add(ClassNameProblem.ContainsWhitespace);
        }

        char[] invalid = newName.Where(c => !char.IsWhiteSpace(c) && !IsNameChar(c)).Distinct().ToArray();
        if (invalid.Length > 0)
        {
            problems.Add(ClassNameProblem.ContainsInvalidCharacters);
        }

        if (char.IsAsciiDigit(newName[0]))
        {
            problems.Add(ClassNameProblem.StartsWithDigit);
        }
        else if (newName == "-")
        {
            problems.Add(ClassNameProblem.SingleHyphen);
        }
        else if (newName.Length > 1 && newName[0] == '-' && char.IsAsciiDigit(newName[1]))
        {
            problems.Add(ClassNameProblem.StartsWithHyphenAndDigit);
        }

        if (string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            problems.Add(ClassNameProblem.SameAsOld);
        }
        else if (ClassExists(newName))
        {
            problems.Add(ClassNameProblem.AlreadyExists);
        }

        return new ClassNameValidation(problems, invalid);
    }

    /// <summary>The scale of the rename — without computing the new texts (it does not depend on the new name).</summary>
    public ClassRenameStats Preview(ClassRenameRequest request) =>
        Compute(request, request?.OldName ?? string.Empty, buildTexts: false).Stats;

    /// <summary>Computes the new file texts after the rename. The name must pass <see cref="Validate"/>.</summary>
    public ClassRenameResult Rename(ClassRenameRequest request, string newName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Validate(request.OldName, newName).IsValid)
        {
            throw new ArgumentException(CoreStrings.Format("Error_InvalidClassName", newName), nameof(newName));
        }

        return Compute(request, newName, buildTexts: true);
    }

    private ClassRenameResult Compute(ClassRenameRequest request, string newName, bool buildTexts)
    {
        ArgumentNullException.ThrowIfNull(request);
        string oldName = request.OldName;
        Usage usage = UsageOf(oldName);
        StyleSheet? origin = request.Source is { } s ? FindSource(s.BookPath, s.StyleBlockIndex) : null;

        HashSet<Occurrence> chosen = request.Scope switch
        {
            ClassRenameScope.Everywhere => usage.Occurrences.ToHashSet(),
            ClassRenameScope.SameNesting => usage.Occurrences
                .Where(o => o.Attribute.ElementPath.SequenceEqual(request.ElementPath, StringComparer.Ordinal))
                .ToHashSet(),
            _ => origin is not null && usage.UsersBySource.TryGetValue(origin, out HashSet<Occurrence>? users)
                ? users.ToHashSet()
                : new HashSet<Occurrence>(),
        };

        Dictionary<string, List<Edit>> editsByFile = new(StringComparer.Ordinal);
        foreach (Occurrence occurrence in chosen)
        {
            EditsOf(editsByFile, occurrence.File.BookPath)
                .Add(new Edit(occurrence.Attribute.ValueStart + occurrence.TokenStart, oldName.Length, newName));
        }

        List<ClassRenameSourceAction> actions = new();
        foreach (StyleSheet source in _sources)
        {
            List<CssRule> rules = source.Rules.Where(r => ClassPositions(r.SelectorText, oldName).Count > 0).ToList();
            if (rules.Count == 0)
            {
                continue;
            }

            HashSet<Occurrence> users = usage.UsersBySource.GetValueOrDefault(source) ?? new HashSet<Occurrence>();
            ClassRenameSourceChange change;
            if (users.Count == 0)
            {
                change = request.Scope == ClassRenameScope.Everywhere || ReferenceEquals(source, origin)
                    ? ClassRenameSourceChange.RenamedInPlace
                    : ClassRenameSourceChange.Unchanged;
            }
            else if (!users.Overlaps(chosen))
            {
                change = ClassRenameSourceChange.Unchanged;
            }
            else
            {
                change = users.IsSubsetOf(chosen) ? ClassRenameSourceChange.RenamedInPlace : ClassRenameSourceChange.Copied;
            }

            actions.Add(new ClassRenameSourceAction(source.BookPath, source.StyleBlockIndex, change, rules.Count));
            if (change != ClassRenameSourceChange.Unchanged)
            {
                AddCssEdits(editsByFile, source.BookPath, TextOf(source.BookPath), rules, oldName, newName, change == ClassRenameSourceChange.Copied);
            }
        }

        int sourcesUsed = usage.UsersBySource.Count(kv => kv.Value.Overlaps(chosen));

        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        if (buildTexts)
        {
            foreach ((string bookPath, List<Edit> edits) in editsByFile)
            {
                texts[bookPath] = ApplyEdits(TextOf(bookPath), edits);
            }
        }

        ClassRenameStats stats = new(chosen.Count, usage.Occurrences.Count, editsByFile.Count, actions, sourcesUsed);
        return new ClassRenameResult(stats, texts);
    }

    // Who uses which style sources with the class — computed once per class name.
    private Usage UsageOf(string className)
    {
        if (_usageCache.TryGetValue(className, out Usage? cached))
        {
            return cached;
        }

        Usage usage = new();
        foreach (HtmlFile html in _htmlFiles)
        {
            List<Occurrence> occurrences = new();
            foreach (ClassAttribute attribute in html.ClassAttributes)
            {
                foreach ((int start, int _, string name) in attribute.Tokens)
                {
                    if (string.Equals(name, className, StringComparison.Ordinal))
                    {
                        occurrences.Add(new Occurrence(html, attribute, start));
                    }
                }
            }

            usage.Occurrences.AddRange(occurrences);
            if (occurrences.Count == 0)
            {
                continue;
            }

            List<(StyleSheet Source, List<string> Selectors)> candidates = new();
            foreach (StyleSheet source in html.VisibleSheets.Concat(html.StyleBlocks))
            {
                List<string> selectors = source.Rules
                    .SelectMany(r => r.Selectors)
                    .Where(sel => ClassPositions(sel, className).Count > 0)
                    .ToList();
                if (selectors.Count > 0)
                {
                    candidates.Add((source, selectors));
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            Dictionary<int, IElement> elements = html.ElementsByOffset();
            foreach (Occurrence occurrence in occurrences)
            {
                IElement? element = elements.GetValueOrDefault(occurrence.Attribute.TagPos);
                foreach ((StyleSheet source, List<string> selectors) in candidates)
                {
                    if (element is null || selectors.Any(sel => Matches(element, sel)))
                    {
                        if (!usage.UsersBySource.TryGetValue(source, out HashSet<Occurrence>? users))
                        {
                            users = new HashSet<Occurrence>();
                            usage.UsersBySource[source] = users;
                        }

                        users.Add(occurrence);
                    }
                }
            }
        }

        _usageCache[className] = usage;
        return usage;
    }

    // A selector AngleSharp does not understand is treated as matching (safer to change too much than to lose a style).
    private static bool Matches(IElement element, string selector) =>
        CssSelectorMatching.TryMatch(element, selector) ?? true;

    // Stylesheets visible to a document: the linked ones (document order) and the ones imported by its
    // <style> blocks, with @import expanded.
    private List<StyleSheet> VisibleSheets(HtmlFile html) =>
        CssImports.VisibleStylesheets(
                html.LinkedStylesheets.Concat(CssImports.FromStyleBlocks(html.Text, html.BookPath)),
                bookPath => _importsByPath.TryGetValue(bookPath, out IReadOnlyList<string>? imports) ? imports : null)
            .Select(bookPath => _cssByPath[bookPath])
            .ToList();

    private StyleSheet? FindSource(string bookPath, int styleBlockIndex) =>
        _sources.FirstOrDefault(s => string.Equals(s.BookPath, bookPath, StringComparison.Ordinal) && s.StyleBlockIndex == styleBlockIndex);

    private string TextOf(string bookPath) =>
        _cssTexts.TryGetValue(bookPath, out string? css) ? css : _htmlFiles.First(h => string.Equals(h.BookPath, bookPath, StringComparison.Ordinal)).Text;

    // Renaming in the selectors (in place) or a copy of every rule with the class right after the original —
    // in the same @media block, because a nested rule ends before the @media brace. From a selector
    // group the copy takes only the members with the class.
    private static void AddCssEdits(
        Dictionary<string, List<Edit>> editsByFile,
        string bookPath,
        string text,
        List<CssRule> rules,
        string oldName,
        string newName,
        bool copy)
    {
        foreach (CssRule rule in rules)
        {
            if (rule.BlockStart <= rule.SelectorStart)
            {
                continue;
            }

            if (!copy)
            {
                foreach (int index in ClassPositions(text[rule.SelectorStart..rule.BlockStart], oldName))
                {
                    EditsOf(editsByFile, bookPath).Add(new Edit(rule.SelectorStart + index, oldName.Length, newName));
                }

                continue;
            }

            List<string> copiedSelectors = new();
            foreach (string selector in rule.Selectors)
            {
                List<int> inSelector = ClassPositions(selector, oldName);
                if (inSelector.Count > 0)
                {
                    copiedSelectors.Add(ReplaceAt(selector, inSelector, oldName.Length, newName));
                }
            }

            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string copyText = newline + LineIndent(text, rule.SelectorStart) + string.Join(", ", copiedSelectors)
                + " " + text[rule.BlockStart..rule.BlockEnd];
            EditsOf(editsByFile, bookPath).Add(new Edit(rule.BlockEnd, 0, copyText));
        }
    }

    private static List<int> ClassPositions(string selector, string className) =>
        CssToolbox.FindClassesInSelector(selector)
            .Where(c => string.Equals(c.Name, className, StringComparison.Ordinal))
            .Select(c => c.Index)
            .ToList();

    private static string ReplaceAt(string text, List<int> positions, int length, string replacement)
    {
        StringBuilder sb = new(text);
        for (int i = positions.Count - 1; i >= 0; i--)
        {
            sb.Remove(positions[i], length).Insert(positions[i], replacement);
        }

        return sb.ToString();
    }

    private static string LineIndent(string text, int pos)
    {
        int lineStart = pos;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
        {
            lineStart--;
        }

        int end = lineStart;
        while (end < pos && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text[lineStart..end];
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

    // Only CSS identifier characters without escape sequences (those cannot be typed into a class attribute).
    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 0x7F;

    // "html 0,body 0,p 1" → [html, body, p]
    private static string[] ElementPathOf(TagLister.TagInfo tag) =>
        tag.TagPath.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(' ')[0])
            .ToArray();

    // Class names in a class attribute value with their position (relative to the start of the value) and length.
    private static IEnumerable<(int Start, int Length, string Name)> ClassTokens(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && char.IsWhiteSpace(value[i]))
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i]))
            {
                i++;
            }

            if (i > start)
            {
                yield return (start, i - start, value[start..i]);
            }
        }
    }

    private readonly record struct Edit(int Pos, int Length, string Replacement);

    private sealed record ClassAttribute(int TagPos, int ValueStart, IReadOnlyList<(int Start, int Length, string Name)> Tokens, IReadOnlyList<string> ElementPath);

    private sealed record Occurrence(HtmlFile File, ClassAttribute Attribute, int TokenStart);

    private sealed class Usage
    {
        public List<Occurrence> Occurrences { get; } = new();

        public Dictionary<StyleSheet, HashSet<Occurrence>> UsersBySource { get; } = new();
    }

    private sealed class StyleSheet(string bookPath, int styleBlockIndex, IReadOnlyList<CssRule> rules)
    {
        public string BookPath { get; } = bookPath;

        public int StyleBlockIndex { get; } = styleBlockIndex;

        public IReadOnlyList<CssRule> Rules { get; } = rules;
    }

    private sealed class HtmlFile
    {
        public HtmlFile(string bookPath, string text)
        {
            BookPath = bookPath;
            Text = text;
            StyleBlocks = new HtmlStyleInfo(text).Styles
                .Select((info, index) => new StyleSheet(bookPath, index, info.Rules))
                .ToList();

            string folder = Core.BookPath.StartingDir(bookPath);
            TagLister lister = new(text);
            foreach (TagLister.TagInfo tag in lister.Tags)
            {
                if (tag.Pos < 0 || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
                {
                    continue;
                }

                string tagText = text.Substring(tag.Pos, tag.Len);
                if (string.Equals(tag.TagName, "link", StringComparison.OrdinalIgnoreCase)
                    && TagLister.ParseAttribute(tagText, "rel").AValue.Contains("stylesheet", StringComparison.OrdinalIgnoreCase)
                    && LinkReference.ResolveBookPath(TagLister.ParseAttribute(tagText, "href").AValue, folder) is { } sheet)
                {
                    LinkedStylesheets.Add(sheet);
                }

                TagLister.AttInfo attribute = TagLister.ParseAttribute(tagText, "class");
                if (attribute.Pos != -1 && attribute.VLen > 0)
                {
                    ClassAttributes.Add(new ClassAttribute(
                        tag.Pos, tag.Pos + attribute.VPos, ClassTokens(attribute.AValue).ToList(), ElementPathOf(tag)));
                }
            }
        }

        public string BookPath { get; }

        public string Text { get; }

        public List<ClassAttribute> ClassAttributes { get; } = new();

        public List<string> LinkedStylesheets { get; } = new();

        public IReadOnlyList<StyleSheet> StyleBlocks { get; }

        public List<StyleSheet> VisibleSheets { get; set; } = new();

        // The AngleSharp element by the offset of the opening tag (the same as TagLister.TagInfo.Pos).
        public Dictionary<int, IElement> ElementsByOffset()
        {
            Dictionary<int, IElement> result = new();
            foreach (IElement element in XhtmlDoc.Parse(Text).All)
            {
                result.TryAdd(XhtmlDoc.OffsetFromNode(element), element);
            }

            return result;
        }
    }
}
