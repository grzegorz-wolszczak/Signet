using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Parsers;

namespace Signet.Core.MainUI;

/// <summary>
/// The suggestion site of a CSS class name: the caret in the value of a <c>class</c> attribute. The class being typed is the
/// word the caret is in (or at the edge of); the other classes of the attribute, before and after it, are not suggested again.
/// </summary>
/// <param name="ReplaceStart">The start of the word the caret is in (the caret when it is between words).</param>
/// <param name="ReplaceEnd">The end of that word (the caret when it is between words).</param>
/// <param name="Query">The part of the word before the caret: what was typed so far.</param>
/// <param name="Used">The other classes of the attribute.</param>
public sealed record ClassCompletionContext(int ReplaceStart, int ReplaceEnd, string Query, IReadOnlySet<string> Used);

/// <summary>
/// Suggestions of CSS class names in Code Editor: where the caret is in <c>class="…"</c>
/// (<see cref="Detect"/>), which classes the stylesheets define (<see cref="ClassesOfStylesheet"/>), and the ones to offer
/// (<see cref="Candidates"/>) — those that match what was typed, best match first, without the ones the attribute has.
/// </summary>
public static partial class ClassCompletion
{
    // How far above the caret the tag may start (a tag with many attributes goes over some lines).
    private const int MaxTagLines = 5;

    /// <summary>
    /// The suggestion context for the caret, or <see langword="null"/> when the caret is not inside the value of a
    /// <c>class</c> attribute of a tag in (X)HTML. The tag may still be unfinished (being typed).
    /// </summary>
    public static ClassCompletionContext? Detect(string text, int caret, CodeEditorSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (syntax != CodeEditorSyntax.Html)
        {
            return null;
        }

        int pos = Math.Clamp(caret, 0, text.Length);
        int tagStart = text.LastIndexOf('<', Math.Max(pos - 1, 0));
        if (tagStart < 0 || tagStart >= pos || text.IndexOf('>', tagStart, pos - tagStart) >= 0 || CountLines(text, tagStart, pos) > MaxTagLines)
        {
            return null;
        }

        Match attribute = ClassAttributeRegex().Match(text[tagStart..pos]);
        if (!attribute.Success)
        {
            return null;
        }

        Group before = attribute.Groups["value"];
        int valueStart = tagStart + before.Index;

        // The rest of the value: up to the closing quote (an unfinished tag has none).
        char quote = text[valueStart - 1];
        int tail = pos;
        while (tail < text.Length && text[tail] != quote && text[tail] != '<' && text[tail] != '>' && text[tail] != '\n')
        {
            tail++;
        }

        int valueEnd = tail < text.Length && text[tail] == quote ? tail : pos;

        // The word the caret is in.
        int wordStart = pos;
        while (wordStart > valueStart && !char.IsWhiteSpace(text[wordStart - 1]))
        {
            wordStart--;
        }

        int wordEnd = pos;
        while (wordEnd < valueEnd && !char.IsWhiteSpace(text[wordEnd]))
        {
            wordEnd++;
        }

        // The other classes of the attribute: every word but the one the caret is in.
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (Match word in WordRegex().Matches(text[valueStart..valueEnd]))
        {
            bool current = wordEnd > wordStart && valueStart + word.Index == wordStart;
            if (!current)
            {
                used.Add(word.Value);
            }
        }

        return new ClassCompletionContext(wordStart, wordEnd, text[wordStart..pos], used);
    }

    /// <summary>
    /// The class names a stylesheet (or the text of one <c>&lt;style&gt;</c> block) defines: the ones its selectors name, in
    /// the order they first occur, without repetitions.
    /// </summary>
    public static IReadOnlyList<string> ClassesOfStylesheet(string cssText)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        return ClassesOf(new CssInfo(cssText));
    }

    /// <summary>The class names the <c>&lt;style&gt;</c> blocks of an (X)HTML file define (see <see cref="ClassesOfStylesheet"/>).</summary>
    public static IReadOnlyList<string> ClassesOfStyleBlocks(string htmlText)
    {
        ArgumentNullException.ThrowIfNull(htmlText);
        return new HtmlStyleInfo(htmlText).Styles.SelectMany(ClassesOf).Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> ClassesOf(CssInfo info) =>
        info.GetAllSelectors().SelectMany(s => s.ClassNames).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The classes to offer for the query: the defined ones that the attribute does not have, those that match the
    /// query first (an empty query takes all of them), alphabetically among equals.
    /// </summary>
    /// <param name="defined">The classes of the stylesheets, with the name of the file that defines each (the first one).</param>
    /// <param name="used">The classes the attribute already has.</param>
    /// <param name="query">What was typed so far.</param>
    public static IReadOnlyList<LinkCompletionItem> Candidates(
        IEnumerable<(string Name, string Source)> defined, IReadOnlySet<string> used, string query)
    {
        ArgumentNullException.ThrowIfNull(defined);
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(query);
        List<LinkCompletionItem> items = defined
            .Where(d => !used.Contains(d.Name))
            .DistinctBy(d => d.Name, StringComparer.Ordinal)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new LinkCompletionItem(d.Name, d.Source))
            .ToList();
        return LinkCompletion.Filter(items, query);
    }

    private static int CountLines(string text, int from, int to)
    {
        int lines = 0;
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    // The text of the tag up to the caret ends inside the value of a class attribute: the quote and what follows it.
    [GeneratedRegex(@"\sclass\s*=\s*(?:'(?<value>[^']*)|""(?<value>[^""]*))$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassAttributeRegex();

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordRegex();
}
