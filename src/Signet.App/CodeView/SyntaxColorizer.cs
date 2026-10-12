using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// Common part of Code View colorizing with line-by-line highlighters: gives every <see cref="SyntaxSpan"/> the look its
/// theme key has (<see cref="SyntaxKeys"/>: text color, background, bold, italic) and remembers the spans that need
/// more than the text can carry — the ones needing attention (<see cref="SyntaxSpan.Issue"/>: errors, links, for the
/// squiggle and the hover tooltips) and the ones whose key has an effect (a line, a wave, a frame) — for
/// <see cref="SyntaxIssueRenderer"/>.
/// </summary>
internal abstract class SyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly SyntaxSpan[] NoIssues = [];
    private static readonly (int Start, int Length, TextAttributes Look)[] NoEffects = [];

    /// <summary>The theme key of every format the highlighters report (a syntax error has only its squiggle).</summary>
    public static readonly IReadOnlyDictionary<SyntaxFormat, ThemeKey> SyntaxKeys = new Dictionary<SyntaxFormat, ThemeKey>
    {
        [SyntaxFormat.XhtmlDoctype] = ThemeKeys.XhtmlDoctype,
        [SyntaxFormat.XhtmlTagName] = ThemeKeys.XhtmlTag,
        [SyntaxFormat.XhtmlComment] = ThemeKeys.XhtmlComment,
        [SyntaxFormat.XhtmlCss] = ThemeKeys.XhtmlCss,
        [SyntaxFormat.XhtmlCssComment] = ThemeKeys.XhtmlCssComment,
        [SyntaxFormat.XhtmlAttributeName] = ThemeKeys.XhtmlAttributeName,
        [SyntaxFormat.XhtmlAttributeValue] = ThemeKeys.XhtmlAttributeValue,
        [SyntaxFormat.XhtmlEntity] = ThemeKeys.XhtmlEntity,
        [SyntaxFormat.XhtmlSpecialSpace] = ThemeKeys.XhtmlSpecialSpace,
        [SyntaxFormat.CssSelector] = ThemeKeys.CssSelector,
        [SyntaxFormat.CssProperty] = ThemeKeys.CssProperty,
        [SyntaxFormat.CssValue] = ThemeKeys.CssValue,
        [SyntaxFormat.CssQuote] = ThemeKeys.CssString,
        [SyntaxFormat.CssComment] = ThemeKeys.CssComment,
        [SyntaxFormat.CssSpecialSelector] = ThemeKeys.CssSpecialSelector,
        [SyntaxFormat.CssAtRule] = ThemeKeys.CssAtRule,
        [SyntaxFormat.CssConstant] = ThemeKeys.CssConstant,
        [SyntaxFormat.XhtmlNamespacePrefix] = ThemeKeys.XhtmlNamespacePrefix,
        [SyntaxFormat.XhtmlBoldText] = ThemeKeys.XhtmlTextBold,
        [SyntaxFormat.XhtmlItalicText] = ThemeKeys.XhtmlTextItalic,
        [SyntaxFormat.XhtmlBoldItalicText] = ThemeKeys.XhtmlTextBoldItalic,
        [SyntaxFormat.Link] = ThemeKeys.Link,
        [SyntaxFormat.BadLink] = ThemeKeys.LinkBroken,
    };

    // Spans needing attention per document line (offsets relative to the line start). A DocumentLine
    // survives edits of other lines, so entries do not "shift" when lines are inserted above.
    private readonly ConditionalWeakTable<DocumentLine, SyntaxSpan[]> _issues = new();
    private readonly ConditionalWeakTable<DocumentLine, (int Start, int Length, TextAttributes Look)[]> _effects = new();
    private Dictionary<SyntaxFormat, Look> _looks = new();

    protected SyntaxColorizer(ResolvedTheme theme)
    {
        SetLooks(theme);
    }

    /// <summary>The look of the squiggle under syntax errors and bad links (<c>errors.syntax</c>).</summary>
    public TextAttributes ErrorLook { get; private set; } = TextAttributes.Empty;

    /// <summary>The text color of the editor — for an effect without a color of its own.</summary>
    public Color TextColor { get; private set; } = Colors.Black;

    /// <summary>The most recently colorized view (redrawn after a change).</summary>
    protected TextView? TextView { get; set; }

    /// <summary>Changes the colors (e.g. after another theme was applied) and redraws the view.</summary>
    public void SetTheme(ResolvedTheme theme)
    {
        SetLooks(theme);
        TextView?.Redraw();
    }

    /// <summary>
    /// Spans needing attention (error, link) in the given line, from the last colorizing pass — offsets
    /// relative to the line start. Empty when the line has not been drawn yet.
    /// </summary>
    public IReadOnlyList<SyntaxSpan> IssuesOf(DocumentLine line) =>
        line is not null && _issues.TryGetValue(line, out SyntaxSpan[]? spans) ? spans : NoIssues;

    /// <summary>The spans of the line whose look has an effect, from the last colorizing pass (offsets relative to the line start).</summary>
    public IReadOnlyList<(int Start, int Length, TextAttributes Look)> EffectsOf(DocumentLine line) =>
        line is not null && _effects.TryGetValue(line, out (int, int, TextAttributes)[]? spans) ? spans : NoEffects;

    /// <summary>Applies the highlighter spans to the current line (in order — the later one wins).</summary>
    protected void ApplySpans(DocumentLine line, List<SyntaxSpan> spans)
    {
        List<SyntaxSpan>? issues = null;
        List<(int, int, TextAttributes)>? effects = null;
        foreach (SyntaxSpan span in spans)
        {
            int start = line.Offset + span.Start;
            int end = Math.Min(start + span.Length, line.EndOffset);
            if (end <= start)
            {
                continue;
            }

            if (span.Issue != SyntaxIssue.None)
            {
                (issues ??= new List<SyntaxSpan>()).Add(span);
            }

            if (!_looks.TryGetValue(span.Format, out Look? look))
            {
                continue;
            }

            if (look.Foreground is { } foreground)
            {
                ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(foreground));
            }

            if (look.Background is { } background)
            {
                ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(background));
            }

            if (look.Attributes.Bold || look.Attributes.Italic)
            {
                FontWeight weight = look.Attributes.Bold ? FontWeight.Bold : FontWeight.Normal;
                FontStyle style = look.Attributes.Italic ? FontStyle.Italic : FontStyle.Normal;
                ChangeLinePart(start, end, e => SetStyle(e, weight, style));
            }

            if (look.Attributes.Effect != EffectType.None)
            {
                (effects ??= new List<(int, int, TextAttributes)>()).Add((span.Start, end - start, look.Attributes with { Background = null }));
            }
        }

        if (effects is null)
        {
            _effects.Remove(line);
        }
        else
        {
            _effects.AddOrUpdate(line, effects.ToArray());
        }

        if (issues is null)
        {
            _issues.Remove(line);
        }
        else
        {
            _issues.AddOrUpdate(line, issues.ToArray());
        }
    }

    private static void SetStyle(VisualLineElement element, FontWeight weight, FontStyle style)
    {
        Typeface current = element.TextRunProperties.Typeface;
        element.TextRunProperties.SetTypeface(new Typeface(current.FontFamily, style, weight, current.Stretch));
    }

    private void SetLooks(ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _looks = SyntaxKeys.ToDictionary(p => p.Key, p => new Look(theme.Attributes(p.Value)));
        ErrorLook = theme.Attributes(ThemeKeys.ErrorsSyntax);
        TextColor = theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
    }

    // The attributes of a key and their brushes, made once per theme.
    private sealed class Look(TextAttributes attributes)
    {
        public TextAttributes Attributes { get; } = attributes;

        public IBrush? Foreground { get; } = attributes.Foreground is { } c ? new ImmutableSolidColorBrush(ResolvedTheme.ToColor(c)) : null;

        public IBrush? Background { get; } = attributes.Background is { } c ? new ImmutableSolidColorBrush(ResolvedTheme.ToColor(c)) : null;
    }
}
