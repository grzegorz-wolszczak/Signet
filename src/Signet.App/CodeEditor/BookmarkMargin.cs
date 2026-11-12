using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeEditor;

/// <summary>
/// The column of the gutter of Code Editor, right of the line numbers (and left of the folding markers), that marks the lines which carry a bookmark with a
/// small bookmark ribbon (like the bookmark icon in the gutter of the JetBrains IDEs). It only draws: the lines come from
/// <see cref="Lines"/>, and toggling a bookmark (F11, the gutter menu) is done by the tab.
/// </summary>
public sealed class BookmarkMargin : AbstractMargin
{
    /// <summary>How many icons wide the column is: one now (the bookmark); another kind of icon would get a slot of its own.</summary>
    public const int SlotCount = 1;

    /// <summary>The width of the slot of one icon (px).</summary>
    public const double SlotWidth = 16;

    /// <summary>The width of the column (px).</summary>
    public const double ColumnWidth = SlotCount * SlotWidth;

    // The ribbon: its width and the room kept above and below it, relative to the height of a line.
    private const double RibbonWidth = 8;
    private const double RibbonVerticalMargin = 3;

    // The notch cut into the bottom of the ribbon, as a fraction of its width.
    private const double NotchDepth = 0.4;

    private static readonly IBrush DefaultMarkerBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x8F, 0xC9));

    /// <summary>The lines (1-based) that carry a bookmark now; <see langword="null"/> = none.</summary>
    public Func<IReadOnlyCollection<int>>? Lines { get; set; }

    /// <summary>The background of the column; <see langword="null"/> = the editor's.</summary>
    public IBrush? Background { get; set; }

    /// <summary>The color of the ribbons.</summary>
    public IBrush MarkerBrush { get; set; } = DefaultMarkerBrush;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth, 0);

    /// <inheritdoc />
    public override void Render(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Background is { } background)
        {
            drawingContext.FillRectangle(background, new Rect(Bounds.Size));
        }

        if (TextView is not { VisualLinesValid: true } view || Lines?.Invoke() is not { Count: > 0 } lines)
        {
            return;
        }

        foreach (VisualLine line in view.VisualLines)
        {
            if (!lines.Contains(line.FirstDocumentLine.LineNumber))
            {
                continue;
            }

            // The ribbon sits next to the first row of the line (a wrapped line has more rows).
            double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - view.VerticalOffset;
            double rowHeight = line.TextLines[0].Height;
            drawingContext.DrawGeometry(MarkerBrush, null, Ribbon((SlotWidth - RibbonWidth) / 2, top + RibbonVerticalMargin, rowHeight - 2 * RibbonVerticalMargin));
        }
    }

    /// <summary>
    /// The 1-based number of the line at the vertical position <paramref name="y"/> of the gutter (relative to the top of
    /// the text view); <see langword="null"/> below the last line.
    /// </summary>
    public static int? LineAt(TextView view, double y)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.GetVisualLineFromVisualTop(y + view.VerticalOffset)?.FirstDocumentLine.LineNumber;
    }

    /// <inheritdoc />
    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    // A flag of the given height with a notch in its bottom edge.
    private static StreamGeometry Ribbon(double left, double top, double height)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext context = geometry.Open();
        context.BeginFigure(new Point(left, top), isFilled: true);
        context.LineTo(new Point(left + RibbonWidth, top));
        context.LineTo(new Point(left + RibbonWidth, top + height));
        context.LineTo(new Point(left + RibbonWidth / 2, top + height - RibbonWidth * NotchDepth));
        context.LineTo(new Point(left, top + height));
        context.EndFigure(isClosed: true);
        return geometry;
    }
}
