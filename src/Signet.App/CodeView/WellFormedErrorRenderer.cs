using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeView;

/// <summary>
/// Draws a red squiggle under the text range reported as a well-formedness error.
/// One active range at a time.
/// </summary>
internal sealed class WellFormedErrorRenderer : IBackgroundRenderer
{
    private static readonly Pen SquigglePen = new(new SolidColorBrush(Color.FromRgb(0xE0, 0x1B, 0x1B)), 1);

    private ErrorSegment? _segment;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the error range (document offsets); <paramref name="length"/> is at least 1.</summary>
    public void SetError(int offset, int length)
    {
        _segment = new ErrorSegment(Math.Max(0, offset), Math.Max(1, length));
    }

    /// <summary>Clears the error underline.</summary>
    public void Clear() => _segment = null;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_segment is not { } segment || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            DrawSquiggle(drawingContext, rect);
        }
    }

    private static void DrawSquiggle(DrawingContext context, Rect rect)
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
            context.DrawLine(SquigglePen, points[i], points[i + 1]);
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
