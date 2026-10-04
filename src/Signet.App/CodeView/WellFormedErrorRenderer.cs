using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeView;

/// <summary>
/// Draws a red squiggle under the text range reported as a well-formedness error, and a yellow one
/// under the range of a non-blocking structural warning (e.g. a missing DOCTYPE).
/// At most one error and one warning range at a time.
/// </summary>
internal sealed class WellFormedErrorRenderer : IBackgroundRenderer
{
    private static readonly Pen SquigglePen = new(new SolidColorBrush(Color.FromRgb(0xE0, 0x1B, 0x1B)), 1);

    // Amber rather than pure yellow, so it stays visible on the light theme background too.
    private static readonly Pen WarningSquigglePen = new(new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x00)), 1);

    private ErrorSegment? _segment;
    private ErrorSegment? _warningSegment;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the error range (document offsets); <paramref name="length"/> is at least 1.</summary>
    public void SetError(int offset, int length)
    {
        _segment = new ErrorSegment(Math.Max(0, offset), Math.Max(1, length));
    }

    /// <summary>Clears the error underline.</summary>
    public void Clear() => _segment = null;

    /// <summary>Sets the warning range (document offsets); <paramref name="length"/> is at least 1.</summary>
    public void SetWarning(int offset, int length)
    {
        _warningSegment = new ErrorSegment(Math.Max(0, offset), Math.Max(1, length));
    }

    /// <summary>Clears the warning underline.</summary>
    public void ClearWarning() => _warningSegment = null;

    /// <summary>Whether the document offset lies inside the warning range.</summary>
    public bool IsInWarning(int offset) =>
        _warningSegment is { } segment && offset >= segment.Offset && offset < segment.EndOffset;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (!textView.VisualLinesValid)
        {
            return;
        }

        if (_warningSegment is { } warning)
        {
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, warning))
            {
                DrawSquiggle(drawingContext, rect, WarningSquigglePen);
            }
        }

        if (_segment is { } segment)
        {
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawSquiggle(drawingContext, rect, SquigglePen);
            }
        }
    }

    private static void DrawSquiggle(DrawingContext context, Rect rect, Pen pen)
    {
        double baseline = rect.Bottom - 1;
        const double step = 2.0;
        var points = new List<Point>();
        bool up = true;
        for (double x = rect.Left; x < rect.Right; x += step)
        {
            points.Add(new Point(x, baseline + (up ? -2 : 0)));
            up = !up;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }

    private sealed class ErrorSegment : ISegment
    {
        public ErrorSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }

        public int EndOffset => Offset + Length;
    }
}
