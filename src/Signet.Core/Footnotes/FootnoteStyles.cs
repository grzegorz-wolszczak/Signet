using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;

namespace Signet.Core.Footnotes;

/// <summary>What is wrong with a footnote style definition (<see cref="FootnoteStyles.Validate"/>).</summary>
public enum FootnoteStyleProblem
{
    /// <summary>A brace — it would end the rule or open a nested one.</summary>
    Brace,

    /// <summary>A comment marker.</summary>
    Comment,

    /// <summary>A declaration without a colon between the property and the value.</summary>
    MissingColon,

    /// <summary>A declaration running into the next line: a semicolon is missing.</summary>
    MissingSemicolon,

    /// <summary>Not a property name.</summary>
    InvalidProperty,

    /// <summary>A property that CSS does not have (the list of Check Book).</summary>
    UnknownProperty,

    /// <summary>A property without a value.</summary>
    EmptyValue,
}

/// <summary>The first problem of a footnote style definition.</summary>
/// <param name="Problem">What is wrong.</param>
/// <param name="Detail">Where: the property or the text of the declaration.</param>
public sealed record FootnoteStyleError(FootnoteStyleProblem Problem, string Detail);

/// <summary>
/// The CSS of Signet's footnote classes (Preferences → Footnotes): the declarations of each rule, one per line, e.g.
/// <c>vertical-align: super;</c>. <see cref="FootnoteEditor"/> keeps the rules of the book equal to them: a missing
/// rule is added and a different one is rewritten. An empty definition leaves its rule alone.
/// </summary>
/// <param name="Reference">The reference in the text (<c>.signet-fn-ref</c>).</param>
/// <param name="Note">A note (<c>.signet-fn</c>).</param>
/// <param name="Paragraph">A paragraph of a note (<c>.signet-fn-p</c>).</param>
/// <param name="LocalContainer">The section with the notes at the end of a file (<c>.signet-footnotes</c>).</param>
/// <param name="GlobalContainer">The section with the notes of the notes file (<c>.signet-endnotes</c>).</param>
/// <param name="Separator">The line above the notes at the end of a file (<c>.signet-fn-separator</c>).</param>
/// <param name="Group">A group of global notes (<c>.signet-fn-group</c>).</param>
/// <param name="GroupTitle">The heading of a group of global notes (<c>.signet-fn-group-title</c>).</param>
public sealed record FootnoteStyles(
    string Reference, string Note, string Paragraph, string LocalContainer, string GlobalContainer, string Separator,
    string Group, string GroupTitle)
{
    private static readonly Regex PropertyName = new(@"^(?:--|-?[A-Za-z_])[A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    private static readonly Regex SignetRule = new(
        @"\.signet-(?:fn|footnotes|endnotes)(?![\w-])|\.signet-fn-(?:ref|separator|p|group|group-title)(?![\w-])", RegexOptions.CultureInvariant);

    /// <summary>The default definitions.</summary>
    public static FootnoteStyles Default { get; } = new(
        "vertical-align: super;\nfont-size: smaller;\nline-height: normal;",
        "margin: 0 0 0.5em 0;",
        "margin: 0;\ntext-indent: 0;",
        "font-size: 0.85em;",
        "font-size: 0.9em;",
        "width: 30%;\nmargin: 2em auto 0.5em 0;\nborder: none;\nborder-top: 1px solid;",
        "margin: 1.5em 0 0 0;",
        "font-size: 1.1em;\nmargin: 0 0 0.5em 0;");

    /// <summary>The class names with their definitions, in the order the rules are added.</summary>
    public IReadOnlyList<(string ClassName, string Declarations)> Rules =>
    [
        (FootnoteEditor.ReferenceClass, Reference),
        (FootnoteEditor.SeparatorClass, Separator),
        (FootnoteEditor.LocalContainerClass, LocalContainer),
        (FootnoteEditor.GlobalContainerClass, GlobalContainer),
        (FootnoteEditor.NoteClass, Note),
        (FootnoteEditor.ParagraphClass, Paragraph),
        (FootnoteEditor.GroupClass, Group),
        (FootnoteEditor.GroupTitleClass, GroupTitle),
    ];

    /// <summary>Whether <paramref name="declarations"/> can be a rule body (<see cref="Validate"/> finds no problem).</summary>
    public static bool IsValidDeclarations(string declarations) => Validate(declarations) is null;

    /// <summary>
    /// The first problem of <paramref name="declarations"/> as a rule body, or <c>null</c> when it is valid (an empty
    /// one is): declarations <c>property: value</c> separated by semicolons (the last one may lack it), each property a
    /// known CSS property (a vendor-prefixed or custom one too), no braces and no comments.
    /// </summary>
    public static FootnoteStyleError? Validate(string declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.IndexOfAny(['{', '}']) is var brace and >= 0)
        {
            return new FootnoteStyleError(FootnoteStyleProblem.Brace, declarations[brace].ToString());
        }

        if (declarations.Contains("/*", StringComparison.Ordinal) || declarations.Contains("*/", StringComparison.Ordinal))
        {
            return new FootnoteStyleError(FootnoteStyleProblem.Comment, "/* */");
        }

        foreach (string chunk in SplitDeclarations(declarations.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            string declaration = chunk.Trim();
            if (declaration.Length == 0)
            {
                continue;
            }

            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                return new FootnoteStyleError(FootnoteStyleProblem.MissingColon, FirstLine(declaration));
            }

            string property = declaration[..colon].Trim();
            string value = declaration[(colon + 1)..].Trim();
            if (value.Contains('\n', StringComparison.Ordinal))
            {
                return new FootnoteStyleError(FootnoteStyleProblem.MissingSemicolon, FirstLine(declaration));
            }

            if (!PropertyName.IsMatch(property))
            {
                return new FootnoteStyleError(FootnoteStyleProblem.InvalidProperty, property);
            }

            if (!CssPropertyValidator.IsKnownProperty(property.ToLowerInvariant()))
            {
                return new FootnoteStyleError(FootnoteStyleProblem.UnknownProperty, property);
            }

            if (Regex.Replace(value, @"!\s*important\s*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Trim().Length == 0)
            {
                return new FootnoteStyleError(FootnoteStyleProblem.EmptyValue, property);
            }
        }

        return null;
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

    // The declarations: the text split at the semicolons outside quotes and parentheses (e.g. url("a;b")).
    private static List<string> SplitDeclarations(string text)
    {
        List<string> chunks = new();
        int start = 0;
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                chunks.Add(text[start..i]);
                start = i + 1;
            }
        }

        chunks.Add(text[start..]);
        return chunks;
    }

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();

    /// <summary>Whether <paramref name="css"/> has a rule for one of Signet's footnote classes.</summary>
    internal static bool HasSignetRule(string css) => SignetRule.IsMatch(css);

    // "{\n  a: 1;\n  b: 2;\n}": one declaration per line, indented.
    private static string Block(string declarations) =>
        "{\n" + string.Join("\n", declarations.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => "  " + l))
        + "\n}";
}
