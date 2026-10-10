using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Parsers;

namespace Signet.Core.Footnotes;

/// <summary>
/// The CSS of Signet's footnote classes (Preferences → Footnotes): the declarations of each rule, one per line, e.g.
/// <c>vertical-align: super;</c>. <see cref="FootnoteEditor"/> keeps the rules of the book equal to them: a missing
/// rule is added and a different one is rewritten. An empty definition leaves its rule alone.
/// </summary>
/// <param name="Reference">The reference in the text (<c>.signet-fn-ref</c>).</param>
/// <param name="Note">A note (<c>.signet-fn</c>).</param>
/// <param name="LocalContainer">The section with the notes at the end of a file (<c>.signet-footnotes</c>).</param>
/// <param name="GlobalContainer">The section with the notes of the notes file (<c>.signet-endnotes</c>).</param>
/// <param name="Separator">The line above the notes at the end of a file (<c>.signet-fn-separator</c>).</param>
public sealed record FootnoteStyles(string Reference, string Note, string LocalContainer, string GlobalContainer, string Separator)
{
    private static readonly Regex SignetRule = new(
        @"\.signet-(?:fn|footnotes|endnotes)(?![\w-])|\.signet-fn-(?:ref|separator)", RegexOptions.CultureInvariant);

    /// <summary>The default definitions.</summary>
    public static FootnoteStyles Default { get; } = new(
        "vertical-align: super;\nfont-size: smaller;\nline-height: normal;",
        "margin: 0 0 0.5em 0;",
        "font-size: 0.85em;",
        "font-size: 0.9em;",
        "width: 30%;\nmargin: 2em auto 0.5em 0;\nborder: none;\nborder-top: 1px solid;");

    /// <summary>The class names with their definitions, in the order the rules are added.</summary>
    public IReadOnlyList<(string ClassName, string Declarations)> Rules =>
    [
        (FootnoteEditor.ReferenceClass, Reference),
        (FootnoteEditor.SeparatorClass, Separator),
        (FootnoteEditor.LocalContainerClass, LocalContainer),
        (FootnoteEditor.GlobalContainerClass, GlobalContainer),
        (FootnoteEditor.NoteClass, Note),
    ];

    /// <summary>
    /// Whether <paramref name="declarations"/> can be a rule body: no braces (they would end the rule or open a
    /// nested one) and no comment markers.
    /// </summary>
    public static bool IsValidDeclarations(string declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        return declarations.IndexOfAny(['{', '}']) < 0
            && !declarations.Contains("/*", StringComparison.Ordinal)
            && !declarations.Contains("*/", StringComparison.Ordinal);
    }

    /// <summary>
    /// <paramref name="css"/> with the rules of Signet's footnote classes equal to these definitions: a top-level rule
    /// whose selector is exactly the class gets the new body, a missing one is appended. Everything else stays.
    /// </summary>
    public string ApplyTo(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        string newline = css.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string text = css.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach ((string className, string declarations) in Rules)
        {
            if (declarations.Trim().Length == 0 || !IsValidDeclarations(declarations))
            {
                continue;
            }

            string block = Block(declarations);
            CssRule? rule = new CssInfo(text).Rules.FirstOrDefault(r =>
                r.AtRulePrelude is null && r.SelectorText.Trim() == "." + className);
            if (rule is not null)
            {
                if (text[rule.BlockStart..rule.BlockEnd] != block)
                {
                    text = text[..rule.BlockStart] + block + text[rule.BlockEnd..];
                }
            }
            else
            {
                string separator = text.Length == 0 ? string.Empty : text.EndsWith('\n') ? "\n" : "\n\n";
                text += separator + "." + className + " " + block + "\n";
            }
        }

        return newline == "\n" ? text : text.Replace("\n", newline, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="css"/> has a rule for one of Signet's footnote classes.</summary>
    internal static bool HasSignetRule(string css) => SignetRule.IsMatch(css);

    // "{\n  a: 1;\n  b: 2;\n}": one declaration per line, indented.
    private static string Block(string declarations) =>
        "{\n" + string.Join("\n", declarations.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => "  " + l))
        + "\n}";
}
