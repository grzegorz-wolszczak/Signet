using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Themes;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Signet.App.CodeEditor;

/// <summary>A fragment of a sample shown in the look of a key.</summary>
/// <param name="Key">The key.</param>
/// <param name="Offset">The start (document offset).</param>
/// <param name="Length">The length.</param>
public readonly record struct KeyRange(ThemeKey Key, int Offset, int Length)
{
    /// <summary>The end (exclusive).</summary>
    public int End => Offset + Length;

    /// <summary>Whether the range holds <paramref name="offset"/>.</summary>
    public bool Contains(int offset) => offset >= Offset && offset < End;
}

/// <summary>
/// The demo text of a page of the theme editor (IntelliJ's <c>ColorSettingsPage.getDemoText</c>): the text, its
/// syntax, and the fragments marked with keys that the syntax does not produce (the selection, the search matches, the
/// errors, the lines of a diff…). In the markup <c>«key.id|text»</c> marks <c>text</c> with the key; marks can nest.
/// </summary>
public sealed class ThemeSample
{
    // The keys painted over the whole width of their lines.
    private static readonly HashSet<ThemeKey> WholeLineKeys = new()
    {
        ThemeKeys.DiffDeletedLine, ThemeKeys.DiffInsertedLine, ThemeKeys.DiffEmptyCell,
    };

    private IReadOnlyList<KeyRange>? _syntaxRanges;
    private ThemeSample? _basic;

    private ThemeSample(string text, CodeEditorSyntax syntax, IReadOnlyList<KeyRange> marks, int caretLine, bool extended)
    {
        Text = text;
        Syntax = syntax;
        Marks = marks;
        CaretLine = caretLine;
        IsExtended = extended;
    }

    /// <summary>
    /// Whether the syntax goes through the extended highlighting of Code Editor (the default) or the basic one — they use
    /// different keys (e.g. a <c>&lt;style&gt;</c> block: <c>css.*</c> or <c>xhtml.css</c>).
    /// </summary>
    public bool IsExtended { get; }

    /// <summary>The same sample with the basic highlighting.</summary>
    public ThemeSample Basic => IsExtended ? _basic ??= new ThemeSample(Text, Syntax, Marks, CaretLine, extended: false) : this;

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>The syntax of the text (<see cref="CodeEditorSyntax.PlainText"/> = none).</summary>
    public CodeEditorSyntax Syntax { get; }

    /// <summary>The fragments marked in the markup.</summary>
    public IReadOnlyList<KeyRange> Marks { get; }

    /// <summary>The line with the caret (1-based; 0 = none) — shown in the look of the current line.</summary>
    public int CaretLine { get; }

    /// <summary>The fragments the highlighting of the syntax gives a key (computed once).</summary>
    public IReadOnlyList<KeyRange> SyntaxRanges => _syntaxRanges ??= ComputeSyntaxRanges();

    /// <summary>Whether a key is painted over the whole width of its lines (the lines of a diff).</summary>
    public static bool IsWholeLine(ThemeKey key) => WholeLineKeys.Contains(key);

    /// <summary>A link target the samples treat as missing from the book (for <c>link.broken</c>).</summary>
    public static bool LinkExists(string target) => !target.Contains("missing", StringComparison.Ordinal);

    /// <summary>Reads the markup: <c>«key.id|text»</c> marks <c>text</c> with the key.</summary>
    /// <exception cref="FormatException">The markup is not balanced or names an unknown key.</exception>
    public static ThemeSample Parse(string markup, CodeEditorSyntax syntax, int caretLine = 0)
    {
        ArgumentNullException.ThrowIfNull(markup);
        StringBuilder text = new();
        List<KeyRange> marks = new();
        Stack<(ThemeKey Key, int Start)> open = new();
        for (int i = 0; i < markup.Length; i++)
        {
            char ch = markup[i];
            if (ch == '«')
            {
                int bar = markup.IndexOf('|', i);
                if (bar < 0)
                {
                    throw new FormatException($"A mark without '|' at {i}.");
                }

                open.Push((ThemeKeys.Get(markup[(i + 1)..bar]), text.Length));
                i = bar;
            }
            else if (ch == '»')
            {
                if (open.Count == 0)
                {
                    throw new FormatException($"A '»' without a mark at {i}.");
                }

                (ThemeKey key, int start) = open.Pop();
                marks.Add(new KeyRange(key, start, text.Length - start));
            }
            else
            {
                text.Append(ch);
            }
        }

        if (open.Count > 0)
        {
            throw new FormatException("A mark is not closed.");
        }

        return new ThemeSample(text.ToString(), syntax, marks, caretLine, extended: true);
    }

    /// <summary>
    /// The key shown at a place of the text: the innermost mark there, otherwise the last fragment of the syntax there
    /// (the later fragment wins, as in the highlighting); <c>null</c> = plain text.
    /// </summary>
    public ThemeKey? KeyAt(int offset)
    {
        KeyRange? mark = Marks.Where(m => m.Contains(offset) && !IsWholeLine(m.Key)).OrderBy(m => m.Length).Cast<KeyRange?>().FirstOrDefault()
            ?? Marks.Where(m => m.Contains(offset)).OrderBy(m => m.Length).Cast<KeyRange?>().FirstOrDefault();
        if (mark is { } m)
        {
            return m.Key;
        }

        return SyntaxRanges.LastOrDefault(r => r.Contains(offset)) is { Length: > 0 } syntax ? syntax.Key : null;
    }

    /// <summary>The fragments shown in the look of a key (marks and syntax).</summary>
    public IEnumerable<KeyRange> RangesOf(ThemeKey key) => Marks.Concat(SyntaxRanges).Where(r => r.Key == key);

    private IReadOnlyList<KeyRange> ComputeSyntaxRanges() => (Syntax, IsExtended) switch
    {
        (CodeEditorSyntax.Html, true) => Highlight(new ExtendedXhtmlHighlighter(false, LinkExists)),
        (CodeEditorSyntax.Xml, true) => Highlight(new ExtendedXhtmlHighlighter(true, LinkExists)),
        (CodeEditorSyntax.Css, true) => Highlight(new ExtendedCssHighlighter(LinkExists)),
        (CodeEditorSyntax.Html or CodeEditorSyntax.Xml, false) => Highlight(new XhtmlHighlighter()),
        (CodeEditorSyntax.Css, false) => Highlight(new CssHighlighter()),
        _ when CodeEditorTextMate.ScopeFor(Syntax) is { } scope => Tokenize(scope),
        _ => Array.Empty<KeyRange>(),
    };

    // The fragments of a line-by-line highlighter of Signet, with their keys.
    private List<KeyRange> Highlight<TState>(ILineSyntaxHighlighter<TState> highlighter)
        where TState : IEquatable<TState>
    {
        List<KeyRange> ranges = new();
        List<SyntaxSpan> spans = new();
        TState state = highlighter.InitialState;
        int lineStart = 0;
        foreach (string line in Text.Split('\n'))
        {
            spans.Clear();
            state = highlighter.HighlightLine(line, state, spans);
            foreach (SyntaxSpan span in spans)
            {
                ThemeKey? key = span.Format == SyntaxFormat.SyntaxError ? ThemeKeys.ErrorsSyntax
                    : SyntaxColorizer.SyntaxKeys.GetValueOrDefault(span.Format);
                if (key is not null && span.Length > 0)
                {
                    ranges.Add(new KeyRange(key, lineStart + span.Start, Math.Min(span.Length, line.Length - span.Start)));
                }
            }

            lineStart += line.Length + 1;
        }

        return ranges;
    }

    // The tokens of a TextMate grammar, with the key whose scopes color them.
    private List<KeyRange> Tokenize(string scope)
    {
        List<KeyRange> ranges = new();
        IGrammar? grammar = new Registry(CodeEditorTextMate.Options).LoadGrammar(scope);
        if (grammar is null)
        {
            return ranges;
        }

        IStateStack? stack = null;
        int lineStart = 0;
        foreach (string line in Text.Split('\n'))
        {
            ITokenizeLineResult result = grammar.TokenizeLine(line, stack, TimeSpan.MaxValue);
            stack = result.RuleStack;
            foreach (IToken token in result.Tokens)
            {
                int end = Math.Min(token.EndIndex, line.Length);
                if (end > token.StartIndex && CodeEditorTextMate.KeyFor(token.Scopes) is { } key)
                {
                    ranges.Add(new KeyRange(key, lineStart + token.StartIndex, end - token.StartIndex));
                }
            }

            lineStart += line.Length + 1;
        }

        return ranges;
    }
}
