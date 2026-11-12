using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Draws a vertical line in the text of Code Editor along the block the caret is in (the element, comment or CSS rule, as the
/// folding sees them): from the line after its first line to the line before its last one, at the column where the first
/// line is indented. The blocks around it get the same line, weaker, so that the nesting shows (the indent guides of the
/// JetBrains IDEs, with the block of the caret drawn out). The color is <c>editor.blockLine</c> of the color scheme.
/// </summary>
internal sealed class BlockLineRenderer : IBackgroundRenderer
{
    // How much of its color the lines of the blocks around the caret's block keep.
    private const double OuterOpacity = 0.45;

    // A block needs a line between its first and its last one to have something to draw along.
    private const int MinimumSpan = 2;

    private readonly Func<IEnumerable<(int Start, int End)>> _regions;

    /// <summary>Creates the renderer over the open blocks (the first and the last line of each) of the document.</summary>
    public BlockLineRenderer(Func<IEnumerable<(int Start, int End)>> regions)
    {
        _regions = regions ?? throw new ArgumentNullException(nameof(regions));
    }

    /// <summary>Whether the line is drawn (the setting).</summary>
    public bool Enabled { get; set; }

    /// <summary>The thickness of the line (px).</summary>
    public int Thickness { get; set; } = 1;

    /// <summary>The theme the line is colored by (<c>editor.blockLine</c>).</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

    /// <summary>The line of the caret (1-based).</summary>
    public int CaretLine { get; set; }

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Background;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (!Enabled || CaretLine < 1 || !textView.VisualLinesValid || textView.Document is not { } document
            || Theme.Color(ThemeKeys.EditorBlockLine) is not { } color)
        {
            return;
        }

        // The blocks the caret is in, the innermost first.
        List<(int Start, int End)> around = _regions()
            .Where(r => r.End - r.Start >= MinimumSpan && r.Start <= CaretLine && CaretLine <= r.End)
            .OrderBy(r => r.End - r.Start)
            .ToList();
        for (int i = 0; i < around.Count; i++)
        {
            Color shade = color;
            Color drawn = i == 0 ? shade : Color.FromArgb((byte)(shade.A * OuterOpacity), shade.R, shade.G, shade.B);
            DrawLine(textView, drawingContext, document, around[i], new Pen(new SolidColorBrush(drawn), Thickness));
        }
    }

    private static void DrawLine(TextView view, DrawingContext context, TextDocument document, (int Start, int End) block, Pen pen)
    {
        DocumentLine first = document.GetLineByNumber(block.Start);
        int indent = 0;
        while (first.Offset + indent < first.EndOffset && char.IsWhiteSpace(document.GetCharAt(first.Offset + indent)))
        {
            indent++;
        }

        double x = view.GetVisualPosition(new TextViewPosition(block.Start, indent + 1), VisualYPosition.LineTop).X - view.ScrollOffset.X;
        double top = view.GetVisualTopByDocumentLine(block.Start + 1) - view.VerticalOffset;
        double bottom = view.GetVisualTopByDocumentLine(block.End) - view.VerticalOffset;

        // Half a pixel in, so that a line of odd thickness is not smeared over two pixels.
        double at = Math.Floor(x) + 0.5;
        context.DrawLine(pen, new Point(at, top), new Point(at, bottom));
    }
}
