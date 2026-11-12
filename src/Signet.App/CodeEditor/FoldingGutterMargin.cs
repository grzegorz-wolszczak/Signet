using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeEditor;

/// <summary>
/// The folding markers of the gutter of Code Editor, drawn like the ones of the JetBrains IDEs: a chevron on the first line
/// of a foldable region — pointing down while it is open, to the right while it is folded — that a click folds or unfolds.
/// The chevrons of open regions show only while the pointer is over the gutter (<see cref="GutterHovered"/>); a folded
/// region keeps its chevron. With <see cref="ShowLines"/> a thin line joins the start of the open region the pointer is in to
/// its end (the outline of the folding of the JetBrains IDEs). The regions are those of the <see cref="Manager"/> of the
/// editor.
/// </summary>
public sealed class FoldingGutterMargin : AbstractMargin
{
    /// <summary>The width of the column (px).</summary>
    public const double ColumnWidth = 16;

    // The chevron: the size of its box, the stroke, and where its tips are (the proportions of the icons of the IntelliJ
    // gutter, which are 9 px boxes with the tips at 2.75 and 6.25).
    private const double ChevronBox = 9;
    private const double ChevronNear = 2.75;
    private const double ChevronFar = 6.25;
    private const double ChevronEdge = 1;
    private const double ChevronStroke = 1.2;

    // The line along a region.
    private const double LineStroke = 1;

    private FoldingManager? _manager;
    private bool _gutterHovered;
    private bool _showEndings;
    private int _hoverLine;
    private int _pointerLine;

    /// <summary>The regions that are drawn; <see langword="null"/> = none.</summary>
    public FoldingManager? Manager
    {
        get => _manager;
        set
        {
            _manager = value;
            _hoverLine = 0;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the pointer is over the gutter: the chevrons of the open regions show only then.</summary>
    public bool GutterHovered
    {
        get => _gutterHovered;
        set
        {
            if (_gutterHovered != value)
            {
                _gutterHovered = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Whether a line joins the start of the region under the pointer to its end while the gutter is hovered.</summary>
    public bool ShowLines
    {
        get => _showEndings;
        set
        {
            if (_showEndings != value)
            {
                _showEndings = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>The background of the column; <see langword="null"/> = the editor's.</summary>
    public IBrush? Background { get; set; }

    /// <summary>The color of the chevrons.</summary>
    public IBrush ChevronBrush { get; set; } = Brushes.Gray;

    /// <summary>The color of the chevron under the pointer.</summary>
    public IBrush HoverBrush { get; set; } = Brushes.Black;

    /// <summary>The line (1-based) whose chevron the pointer is over; 0 = none.</summary>
    public int HoverLine => _hoverLine;

    /// <summary>
    /// The line (1-based) the pointer is level with anywhere in the gutter, whichever column it is over; 0 = none. The region
    /// the line (see <see cref="ShowLines"/>) is drawn for is the innermost open one that contains this line.
    /// </summary>
    public int PointerLine
    {
        get => _pointerLine;
        set
        {
            if (_pointerLine != value)
            {
                _pointerLine = value;
                InvalidateVisual();
            }
        }
    }

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

        if (Manager is not { } manager || TextView is not { VisualLinesValid: true } view || Document is null)
        {
            return;
        }

        Dictionary<int, RegionsOnLine> starts = new();
        List<(int Start, int End)> open = new();
        foreach (FoldingSection section in manager.AllFoldings)
        {
            if (!TryLines(section, out int startLine, out int endLine))
            {
                continue;
            }

            starts.TryGetValue(startLine, out RegionsOnLine on);
            starts[startLine] = section.IsFolded ? on with { Folded = true } : on with { Open = true };
            if (!section.IsFolded)
            {
                open.Add((startLine, endLine));
            }
        }

        // The visible lines with the middle of their first row, for the line of the region and the chevrons.
        Dictionary<int, (double Top, double Height)> rows = new();
        foreach (VisualLine line in view.VisualLines)
        {
            double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - view.VerticalOffset;
            rows[line.FirstDocumentLine.LineNumber] = (top, line.TextLines[0].Height);
        }

        // The region the line under the pointer is in: the innermost open one.
        if (GutterHovered && ShowLines && _pointerLine > 0
            && open.Where(r => r.Start <= _pointerLine && _pointerLine <= r.End).OrderBy(r => r.End - r.Start).FirstOrDefault() is { End: > 0 } active)
        {
            double top = rows.TryGetValue(active.Start, out (double Top, double Height) first)
                ? first.Top + (first.Height / 2)
                : (active.Start < rows.Keys.DefaultIfEmpty(0).Min() ? 0 : Bounds.Height);
            double bottom = rows.TryGetValue(active.End, out (double Top, double Height) last)
                ? last.Top + (last.Height / 2)
                : (active.End > rows.Keys.DefaultIfEmpty(0).Max() ? Bounds.Height : 0);
            double x = ColumnWidth / 2;
            drawingContext.DrawLine(new Pen(HoverBrush, LineStroke), new Point(x, top), new Point(x, bottom));
        }

        foreach ((int number, (double Top, double Height) row) in rows)
        {
            IBrush brush = number == _hoverLine ? HoverBrush : ChevronBrush;
            starts.TryGetValue(number, out RegionsOnLine on);
            if (on.Folded)
            {
                Chevron(drawingContext, brush, row.Top, row.Height, ChevronKind.Right);
            }
            else if (on.Open && GutterHovered)
            {
                Chevron(drawingContext, brush, row.Top, row.Height, ChevronKind.Down);
            }
        }
    }

    /// <summary>
    /// Folds the region that starts on the line, or unfolds it when it is folded (the outermost one when several start there).
    /// Returns whether a region starts on the line.
    /// </summary>
    public bool ToggleAt(int line)
    {
        if (Manager is not { } manager || Document is null)
        {
            return false;
        }

        List<FoldingSection> here = manager.AllFoldings.Where(s => TryLines(s, out int start, out _) && start == line).ToList();
        if (here.Count == 0)
        {
            return false;
        }

        FoldingSection target = here.FirstOrDefault(s => s.IsFolded) ?? here[0];
        target.IsFolded = !target.IsFolded;
        InvalidateVisual();
        return true;
    }

    /// <summary>Whether a region starts on the line (it has a chevron to click).</summary>
    public bool HasRegionAt(int line) =>
        Manager is { } manager && manager.AllFoldings.Any(s => TryLines(s, out int start, out _) && start == line);

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (TextView is not { } view)
        {
            return;
        }

        int line = BookmarkMargin.LineAt(view, e.GetPosition(view).Y) ?? 0;
        if (line != _hoverLine)
        {
            _hoverLine = line;
            Cursor = line > 0 && HasRegionAt(line) ? new Cursor(StandardCursorType.Hand) : null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverLine = 0;
        Cursor = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TextView is not { } view)
        {
            return;
        }

        if (BookmarkMargin.LineAt(view, e.GetPosition(view).Y) is { } line && ToggleAt(line))
        {
            e.Handled = true;
        }
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

    // The lines a region spans; a region within one line has no chevron.
    private bool TryLines(FoldingSection section, out int startLine, out int endLine)
    {
        startLine = 0;
        endLine = 0;
        if (Document is null || section.Length <= 0 || section.EndOffset > Document.TextLength)
        {
            return false;
        }

        startLine = Document.GetLineByOffset(section.StartOffset).LineNumber;
        endLine = Document.GetLineByOffset(section.EndOffset).LineNumber;
        return endLine > startLine;
    }

    private static void Chevron(DrawingContext context, IBrush brush, double top, double lineHeight, ChevronKind kind)
    {
        double left = (ColumnWidth - ChevronBox) / 2;
        double y = top + ((lineHeight - ChevronBox) / 2);
        (Point a, Point b, Point c) = kind switch
        {
            ChevronKind.Down => (new Point(ChevronEdge, ChevronNear), new Point(ChevronBox / 2, ChevronFar), new Point(ChevronBox - ChevronEdge, ChevronNear)),
            _ => (new Point(ChevronNear, ChevronEdge), new Point(ChevronFar, ChevronBox / 2), new Point(ChevronNear, ChevronBox - ChevronEdge)),
        };

        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(left + a.X, y + a.Y), isFilled: false);
            path.LineTo(new Point(left + b.X, y + b.Y));
            path.LineTo(new Point(left + c.X, y + c.Y));
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(brush, ChevronStroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    private enum ChevronKind
    {
        Down,
        Right,
    }

    private readonly record struct RegionsOnLine(bool Folded, bool Open);
}
