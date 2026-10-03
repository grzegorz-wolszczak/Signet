using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.Core.Misc;

namespace Signet.App.CodeView;

/// <summary>
/// Common part of Code View colorizing with line-by-line highlighters: maps
/// <see cref="SyntaxSpan"/> to colors/fonts from <see cref="CodeViewAppearance"/> and remembers
/// spans that need attention (<see cref="SyntaxSpan.Issue"/> — errors, links) for the squiggle
/// (<see cref="SyntaxIssueRenderer"/>) and hover tooltips.
/// </summary>
internal abstract class SyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly SyntaxSpan[] NoIssues = [];

    // Spans needing attention per document line (offsets relative to the line start). A DocumentLine
    // survives edits of other lines, so entries do not "shift" when lines are inserted above.
    private readonly ConditionalWeakTable<DocumentLine, SyntaxSpan[]> _issues = new();
    private Dictionary<SyntaxFormat, IBrush> _brushes;
    private TextDecorationCollection? _specialSpaceDecoration;

    protected SyntaxColorizer(CodeViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        _brushes = BuildBrushes(appearance);
        _specialSpaceDecoration = BuildSpecialSpaceDecoration(appearance);
        ErrorBrush = new ImmutableSolidColorBrush(Color.Parse(appearance.ErrorUnderlineColor));
    }

    /// <summary>Squiggle color for errors / bad links.</summary>
    public IBrush ErrorBrush { get; private set; }

    /// <summary>The most recently colorized view (redrawn after a change).</summary>
    protected TextView? TextView { get; set; }

    /// <summary>Changes the colors (e.g. after switching between light/dark theme) and redraws the view.</summary>
    public void SetAppearance(CodeViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        _brushes = BuildBrushes(appearance);
        _specialSpaceDecoration = BuildSpecialSpaceDecoration(appearance);
        ErrorBrush = new ImmutableSolidColorBrush(Color.Parse(appearance.ErrorUnderlineColor));
        TextView?.Redraw();
    }

    /// <summary>
    /// Spans needing attention (error, link) in the given line, from the last colorizing pass — offsets
    /// relative to the line start. Empty when the line has not been drawn yet.
    /// </summary>
    public IReadOnlyList<SyntaxSpan> IssuesOf(DocumentLine line) =>
        line is not null && _issues.TryGetValue(line, out SyntaxSpan[]? spans) ? spans : NoIssues;

    /// <summary>Applies the highlighter spans to the current line (in order — the later one wins).</summary>
    protected void ApplySpans(DocumentLine line, List<SyntaxSpan> spans)
    {
        List<SyntaxSpan>? issues = null;
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

            switch (span.Format)
            {
                case SyntaxFormat.XhtmlSpecialSpace:
                    TextDecorationCollection? decoration = _specialSpaceDecoration;
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetTextDecorations(decoration));
                    break;
                case SyntaxFormat.XhtmlBoldText:
                    ChangeLinePart(start, end, e => SetStyle(e, FontWeight.Bold, FontStyle.Normal));
                    break;
                case SyntaxFormat.XhtmlItalicText:
                    ChangeLinePart(start, end, e => SetStyle(e, FontWeight.Normal, FontStyle.Italic));
                    break;
                case SyntaxFormat.XhtmlBoldItalicText:
                    ChangeLinePart(start, end, e => SetStyle(e, FontWeight.Bold, FontStyle.Italic));
                    break;
                case SyntaxFormat.SyntaxError:
                    break; // squiggle only (SyntaxIssueRenderer), color unchanged
                default:
                    if (_brushes.TryGetValue(span.Format, out IBrush? brush))
                    {
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
                    }

                    break;
            }
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

    private static Dictionary<SyntaxFormat, IBrush> BuildBrushes(CodeViewAppearance a)
    {
        static IBrush B(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
        IBrush link = B(a.LinkColor);
        return new Dictionary<SyntaxFormat, IBrush>
        {
            [SyntaxFormat.XhtmlDoctype] = B(a.XhtmlDoctypeColor),
            [SyntaxFormat.XhtmlTagName] = B(a.XhtmlHtmlColor),
            [SyntaxFormat.XhtmlComment] = B(a.XhtmlHtmlCommentColor),
            [SyntaxFormat.XhtmlCss] = B(a.XhtmlCssColor),
            [SyntaxFormat.XhtmlCssComment] = B(a.XhtmlCssCommentColor),
            [SyntaxFormat.XhtmlAttributeName] = B(a.XhtmlAttributeNameColor),
            [SyntaxFormat.XhtmlAttributeValue] = B(a.XhtmlAttributeValueColor),
            [SyntaxFormat.XhtmlEntity] = B(a.XhtmlEntityColor),
            [SyntaxFormat.CssSelector] = B(a.CssSelectorColor),
            [SyntaxFormat.CssProperty] = B(a.CssPropertyColor),
            [SyntaxFormat.CssValue] = B(a.CssValueColor),
            [SyntaxFormat.CssQuote] = B(a.CssQuoteColor),
            [SyntaxFormat.CssComment] = B(a.CssCommentColor),
            [SyntaxFormat.CssSpecialSelector] = B(a.CssSpecialSelectorColor),
            [SyntaxFormat.CssAtRule] = B(a.CssAtRuleColor),
            [SyntaxFormat.CssConstant] = B(a.CssConstantColor),
            [SyntaxFormat.XhtmlNamespacePrefix] = B(a.XhtmlNamespacePrefixColor),
            [SyntaxFormat.Link] = link,
            [SyntaxFormat.BadLink] = link,
        };
    }

    // Special space: dashed underline in the entity color.
    private static TextDecorationCollection BuildSpecialSpaceDecoration(CodeViewAppearance a) =>
        new()
        {
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = new ImmutableSolidColorBrush(Color.Parse(a.XhtmlEntityColor)),
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 2 },
            },
        };
}
