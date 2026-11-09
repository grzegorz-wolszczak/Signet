using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using Signet.Core.Localization;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>An element with a <c>style</c> attribute that "Extract Inline Styles" would turn into a class.</summary>
/// <param name="BookPath">The (X)HTML file of the element.</param>
/// <param name="Offset">The 0-based offset of the element's opening tag in the file.</param>
/// <param name="TagName">The element name in lower case.</param>
/// <param name="Line">The 1-based line of the opening tag.</param>
/// <param name="Snippet">The opening tag and the start of what follows it, on one line (for the report).</param>
public sealed record InlineStyleOccurrence(string BookPath, int Offset, string TagName, int Line, string Snippet);

/// <summary>
/// One value of the <c>style</c> attribute used by several elements of the book: what would become a class
/// (<see cref="InlineStyleExtraction.Survey"/>).
/// </summary>
public sealed class InlineStyleGroup
{
    internal InlineStyleGroup(
        string key, IReadOnlyList<CssDeclaration> declarations, IReadOnlyList<InlineStyleOccurrence> occurrences,
        IReadOnlyList<CleanupConsequence> consequences)
    {
        Key = key;
        Declarations = declarations;
        Occurrences = occurrences;
        Consequences = consequences;
    }

    /// <summary>The normalized value of the attribute (<c>property:value;…</c>) — what makes two attributes "the same".</summary>
    public string Key { get; }

    /// <summary>The declarations of the value, in their order.</summary>
    public IReadOnlyList<CssDeclaration> Declarations { get; }

    /// <summary>The elements that have the value, in book order.</summary>
    public IReadOnlyList<InlineStyleOccurrence> Occurrences { get; }

    /// <summary>
    /// What would stop working as before as the value moves from the attribute to a class: rules of the stylesheets that
    /// the class would lose to (<see cref="InlineStyleExtraction"/>). Empty when the extraction is safe.
    /// </summary>
    public IReadOnlyList<CleanupConsequence> Consequences { get; }

    /// <summary>The value on one line, for the report: <c>text-indent: 16.6pt; margin-left: 2pt</c>.</summary>
    public string Summary => string.Join("; ", Declarations.Select(InlineStyleExtraction.DeclarationText));
}

/// <summary>What <see cref="InlineStyleExtraction.Survey"/> found in the book.</summary>
/// <param name="StylesheetBookPath">
/// The stylesheet that receives the classes — the one most of the files with <c>style</c> attributes link;
/// <c>null</c> when the book has no stylesheet, or no such attribute.
/// </param>
/// <param name="Groups">The values, the most used first.</param>
/// <param name="FirstFreeNumber">The number of the first class name that no class of the book has yet.</param>
/// <param name="ElementsOutsideStylesheet">How many elements are in files that do not link the stylesheet (left alone).</param>
public sealed record InlineStyleSurvey(
    string? StylesheetBookPath, IReadOnlyList<InlineStyleGroup> Groups, int FirstFreeNumber, int ElementsOutsideStylesheet);

/// <summary>What "Extract Inline Styles" does (<see cref="Book.ExtractInlineStyles"/>).</summary>
/// <param name="StylesheetBookPath">The stylesheet that receives the classes.</param>
/// <param name="ClassNames">The class name of each chosen value (<see cref="InlineStyleGroup.Key"/>).</param>
public sealed record InlineStyleExtractionPlan(string StylesheetBookPath, IReadOnlyDictionary<string, string> ClassNames);

/// <summary>
/// "Extract Inline Styles": the same value of the <c>style</c> attribute, repeated on many elements (a PDF converter
/// writes one on nearly every paragraph), becomes one class of a stylesheet and the elements get the class instead.
/// The value is the whole attribute, normalized (character references decoded, white space and the letter case of the
/// property names ignored; the order of the declarations is kept). Works on the source text (<see cref="TagLister"/>), file
/// by file; whether a class is as strong as the attribute was is decided by the cascade: a rule of the stylesheets that is
/// more specific than a class selector (or <c>!important</c>) and sets the same property would now win
/// (<see cref="InlineStyleGroup.Consequences"/>).
/// </summary>
public static class InlineStyleExtraction
{
    /// <summary>The start of the names of the classes: <c>signet-cls-1</c>, <c>signet-cls-2</c>…</summary>
    public const string ClassPrefix = "signet-cls-";

    private const int MaxConsequencesPerGroup = 10;
    private const int SnippetLength = 120;
    private const int SnippetLookahead = 200;
    private const string StyleAttribute = "style";
    private const string ClassAttribute = "class";
    private const string Important = "!important";

    // What a class selector weighs; a rule with more (or !important) beats a class.
    private static readonly CssSpecificity ClassSpecificity = CssSpecificity.ForSelector(".x");

    private static readonly Regex NumberedClass = new(Regex.Escape(ClassPrefix) + @"(\d+)", RegexOptions.Compiled);
    private static readonly Regex WhiteSpaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The class name for the <paramref name="number"/>th group: <c>signet-cls-3</c>.</summary>
    public static string ClassName(int number) => ClassPrefix + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>One declaration as written in a class: <c>property: value</c> (with <c>!important</c> when it has it).</summary>
    public static string DeclarationText(CssDeclaration declaration)
    {
        string value = declaration.Value.Trim();
        if (declaration.IsImportant && !value.Contains(Important, StringComparison.OrdinalIgnoreCase))
        {
            value += " " + Important;
        }

        return declaration.Property.Trim() + ": " + value;
    }

    /// <summary>
    /// Finds the values of the <c>style</c> attribute of the whole book and what moving them to classes would change.
    /// Only files that link the target stylesheet count; the others are left alone (<see cref="InlineStyleSurvey.ElementsOutsideStylesheet"/>).
    /// </summary>
    public static InlineStyleSurvey Survey(Book book, FileBatchRun run = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<HtmlResource> files = book.GetHtmlResources();
        Dictionary<string, CssResource> sheets = book.GetCssResources().ToDictionary(c => c.BookPath, StringComparer.Ordinal);
        Dictionary<string, NormalizedStyle?> cache = new(StringComparer.Ordinal);
        List<(HtmlResource Html, string Text, List<StyleTag> Tags, IReadOnlyList<string> Visible)> found = new();
        for (int i = 0; i < files.Count; i++)
        {
            run.Cancellation.ThrowIfCancellationRequested();
            run.Progress?.Report(new FileBatchProgress(i, files.Count, files[i].BookPath));
            string text = files[i].GetText();
            List<StyleTag> tags = FindTags(text, cache).ToList();
            if (tags.Count > 0)
            {
                found.Add((files[i], text, tags, book.GetVisibleStylesheets(files[i]).Where(sheets.ContainsKey).ToList()));
            }
        }

        int first = FirstFreeNumber(book);
        string? target = found
            .SelectMany(f => f.Visible)
            .GroupBy(path => path, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (target is null)
        {
            return new InlineStyleSurvey(null, Array.Empty<InlineStyleGroup>(), first, found.Sum(f => f.Tags.Count));
        }

        Dictionary<string, List<InlineStyleOccurrence>> occurrences = new(StringComparer.Ordinal);
        List<string> firstSeen = new();
        Dictionary<string, NormalizedStyle> styles = new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<string, (int Count, CleanupConsequence First)>> risks = new(StringComparer.Ordinal);
        int outside = 0;
        foreach ((HtmlResource html, string text, List<StyleTag> tags, IReadOnlyList<string> visible) in found)
        {
            if (!visible.Contains(target, StringComparer.Ordinal))
            {
                outside += tags.Count;
                continue;
            }

            int line = 1;
            int linePos = 0;
            foreach (StyleTag tag in tags)
            {
                (line, linePos) = AdvanceLine(text, linePos, tag.Pos, line);
                if (!occurrences.TryGetValue(tag.Style.Key, out List<InlineStyleOccurrence>? list))
                {
                    occurrences[tag.Style.Key] = list = new List<InlineStyleOccurrence>();
                    firstSeen.Add(tag.Style.Key);
                    styles[tag.Style.Key] = tag.Style;
                }

                list.Add(new InlineStyleOccurrence(html.BookPath, tag.Pos, tag.Name, line, Snippet(text, tag.Pos)));
            }

            AnalyseFile(html, text, tags, visible, sheets, risks);
        }

        List<InlineStyleGroup> groups = firstSeen
            .Select((key, order) => (Key: key, Order: order))
            .OrderByDescending(e => occurrences[e.Key].Count)
            .ThenBy(e => e.Order)
            .Select(e => new InlineStyleGroup(
                e.Key, styles[e.Key].Declarations, occurrences[e.Key], Consequences(risks.GetValueOrDefault(e.Key))))
            .ToList();
        return new InlineStyleSurvey(target, groups, first, outside);
    }

    /// <summary>
    /// Replaces, in <paramref name="text"/>, the <c>style</c> attribute of every element whose value has a class in
    /// <paramref name="classNames"/> with that class (added to a <c>class</c> attribute the element already has).
    /// </summary>
    /// <param name="text">The (X)HTML text.</param>
    /// <param name="classNames">The class name by <see cref="InlineStyleGroup.Key"/>.</param>
    /// <param name="used">Counts the replacements by class name (the caller adds the classes that were used to the stylesheet).</param>
    internal static string Replace(string text, IReadOnlyDictionary<string, string> classNames, Dictionary<string, int> used)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<(int Start, int End, string? Replacement)> edits = new();
        Dictionary<string, NormalizedStyle?> cache = new(StringComparer.Ordinal);
        foreach (StyleTag tag in FindTags(text, cache))
        {
            if (!classNames.TryGetValue(tag.Style.Key, out string? name))
            {
                continue;
            }

            used[name] = used.GetValueOrDefault(name) + 1;
            TagLister.AttInfo style = tag.StyleAttribute;
            string openTag = text.Substring(tag.Pos, tag.Len);
            TagLister.AttInfo classes = TagLister.ParseAttribute(openTag, ClassAttribute);
            if (classes.Pos < 0)
            {
                edits.Add((tag.Pos + style.Pos, tag.Pos + style.Pos + style.Len, TagLister.SerializeAttribute(ClassAttribute, name)));
                continue;
            }

            string existing = classes.AValue.Trim();
            edits.Add((tag.Pos + classes.VPos, tag.Pos + classes.VPos + classes.AValue.Length, existing.Length == 0 ? name : existing + " " + name));
            int start = tag.Pos + style.Pos;
            int end = start + style.Len;
            if (start > tag.Pos && char.IsWhiteSpace(text[start - 1]))
            {
                start--;
            }
            else if (end < tag.Pos + tag.Len && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            edits.Add((start, end, null));
        }

        return edits.Count == 0 ? text : ClassAttributeEditor.ApplyEdits(text, edits);
    }

    /// <summary>
    /// <paramref name="css"/> with the rules of the classes of <paramref name="plan"/> that <paramref name="used"/> names
    /// added at the end, one per class, in the order of the plan.
    /// </summary>
    internal static string AppendClasses(
        string css, InlineStyleExtractionPlan plan, IReadOnlyDictionary<string, InlineStyleGroup> groups, IReadOnlyDictionary<string, int> used)
    {
        string newline = css.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        StringBuilder sb = new(css);
        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append(newline);
        }

        foreach ((string key, string name) in plan.ClassNames)
        {
            if (used.GetValueOrDefault(name) == 0 || !groups.TryGetValue(key, out InlineStyleGroup? group))
            {
                continue;
            }

            sb.Append(newline).Append('.').Append(name).Append(" {").Append(newline);
            foreach (CssDeclaration declaration in group.Declarations)
            {
                sb.Append("  ").Append(DeclarationText(declaration)).Append(';').Append(newline);
            }

            sb.Append('}').Append(newline);
        }

        return sb.ToString();
    }

    // The number after the highest "signet-cls-N" that any stylesheet or (X)HTML file of the book mentions, so a second
    // run (or a book that has such classes) never reuses a name.
    private static int FirstFreeNumber(Book book)
    {
        int highest = 0;
        foreach (string text in book.GetCssResources().Select(c => c.GetText()).Concat(book.GetHtmlResources().Select(h => h.GetText())))
        {
            if (!text.Contains(ClassPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in NumberedClass.Matches(text))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    highest = Math.Max(highest, number);
                }
            }
        }

        return highest + 1;
    }

    private sealed record NormalizedStyle(string Key, IReadOnlyList<CssDeclaration> Declarations);

    private sealed record StyleTag(int Pos, int Len, string Name, TagLister.AttInfo StyleAttribute, NormalizedStyle Style);

    // The elements of the body with a style attribute that has a usable value, in text order.
    private static IEnumerable<StyleTag> FindTags(string text, Dictionary<string, NormalizedStyle?> cache)
    {
        if (!text.Contains(StyleAttribute, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        TagLister lister = TagLister.For(text);
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Pos < 0 || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing) || !lister.IsPositionInBody(tag.Pos))
            {
                continue;
            }

            string openTag = text.Substring(tag.Pos, tag.Len);
            if (!openTag.Contains(StyleAttribute, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TagLister.AttInfo style = TagLister.ParseAttribute(openTag, StyleAttribute);
            if (style.Pos < 0)
            {
                continue;
            }

            if (!cache.TryGetValue(style.AValue, out NormalizedStyle? normalized))
            {
                cache[style.AValue] = normalized = Normalize(style.AValue);
            }

            if (normalized is not null)
            {
                yield return new StyleTag(tag.Pos, tag.Len, tag.TagName.ToLowerInvariant(), style, normalized);
            }
        }
    }

    // null when the value is empty or not a plain list of declarations.
    private static NormalizedStyle? Normalize(string raw)
    {
        string value = WebUtility.HtmlDecode(raw).Trim();
        if (value.Length == 0 || value.Contains('{', StringComparison.Ordinal) || value.Contains('}', StringComparison.Ordinal))
        {
            return null;
        }

        CssInfo info = new("x{" + value + "}");
        if (info.ParseErrors.Count > 0 || info.Rules.Count != 1 || info.Rules[0].Declarations.Count == 0)
        {
            return null;
        }

        List<CssDeclaration> declarations = info.Rules[0].Declarations
            .Select(d => new CssDeclaration(d.Property.Trim().ToLowerInvariant(), d.Value.Trim(), d.IsImportant))
            .ToList();
        return new NormalizedStyle(string.Join(";", declarations.Select(DeclarationText)), declarations);
    }

    private static (int Line, int Pos) AdvanceLine(string text, int from, int to, int line)
    {
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return (line, to);
    }

    private static string Snippet(string text, int pos)
    {
        string raw = text.Substring(pos, Math.Min(SnippetLookahead, text.Length - pos));
        string line = WhiteSpaceRun.Replace(raw, " ").Trim();
        return line.Length > SnippetLength ? line[..SnippetLength] + "…" : line;
    }

    // What the stylesheets already say about the properties a value sets, on the elements that have the value: a rule that
    // beats a class selector (more specific, or !important on both sides) would now beat the value, which the attribute
    // used to put above it.
    private static void AnalyseFile(
        HtmlResource html,
        string text,
        List<StyleTag> tags,
        IReadOnlyList<string> visible,
        Dictionary<string, CssResource> sheets,
        Dictionary<string, Dictionary<string, (int Count, CleanupConsequence First)>> risks)
    {
        List<CssInfo> stylesheets = visible.Select(path => new CssInfo(sheets[path].GetText())).ToList();
        List<(CssRule Rule, int Sequence)> all = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        HashSet<string> properties = tags
            .SelectMany(t => t.Style.Declarations.Select(d => d.Property))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<(CssRule Rule, int Sequence)> relevant = all
            .Where(r => r.Rule.Declarations.Any(d => properties.Contains(d.Property.Trim()))
                && (r.Rule.Declarations.Any(d => d.IsImportant) || r.Rule.Selectors.Any(s => CssSpecificity.ForSelector(s.Trim()) > ClassSpecificity)))
            .ToList();
        if (relevant.Count == 0)
        {
            return;
        }

        IHtmlDocument document = XhtmlDoc.Parse(text);
        foreach (StyleTag tag in tags)
        {
            if (NestedDivRiskAnalyzer.ElementAt(document, tag.Pos) is not { } element)
            {
                continue;
            }

            Dictionary<string, NestedDivRiskAnalyzer.Winner> winners = NestedDivRiskAnalyzer.Winners(relevant, element, includeInlineStyle: false);
            foreach (CssDeclaration declaration in tag.Style.Declarations)
            {
                if (!winners.TryGetValue(declaration.Property, out NestedDivRiskAnalyzer.Winner? winner)
                    || winner.Important != declaration.IsImportant
                    || winner.Specificity <= ClassSpecificity
                    || string.Equals(StripImportant(winner.Value), StripImportant(declaration.Value), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string file = html.Filename;
                string message = CoreStrings.Format(
                    "InlineStyles_Risk_Overridden",
                    file, declaration.Property, StripImportant(declaration.Value), winner.Selector,
                    NestedDivRiskAnalyzer.ConditionText(winner), StripImportant(winner.Value));
                if (!risks.TryGetValue(tag.Style.Key, out var byMessage))
                {
                    risks[tag.Style.Key] = byMessage = new Dictionary<string, (int, CleanupConsequence)>(StringComparer.Ordinal);
                }

                byMessage[message] = byMessage.TryGetValue(message, out var existing)
                    ? existing with { Count = existing.Count + 1 }
                    : (1, new CleanupConsequence(message, html.BookPath, tag.Pos));
            }
        }
    }

    private static string StripImportant(string value) =>
        value.Replace(Important, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    private static List<CleanupConsequence> Consequences(
        Dictionary<string, (int Count, CleanupConsequence First)>? byMessage)
    {
        if (byMessage is null)
        {
            return new List<CleanupConsequence>();
        }

        List<CleanupConsequence> list = byMessage.Values
            .Take(MaxConsequencesPerGroup)
            .Select(e => e.Count > 1
                ? e.First with { Text = e.First.Text + CoreStrings.Format("Cleanup_Risk_MoreElements", e.Count - 1) }
                : e.First)
            .ToList();
        if (byMessage.Count > MaxConsequencesPerGroup)
        {
            CleanupConsequence last = list[^1];
            list.Add(new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_MoreChanges", byMessage.Count - MaxConsequencesPerGroup), last.BookPath, last.Offset));
        }

        return list;
    }
}
