using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.Core.Misc;

namespace Signet.App.CodeView;

/// <summary>
/// Squiggle under syntax errors and bad links reported by extended highlighting.
/// Takes the spans from <see cref="SyntaxColorizer.IssuesOf"/> for visible lines; squiggle style as in
/// <see cref="SpellCheckHighlightRenderer"/>.
/// </summary>
internal sealed class SyntaxIssueRenderer : IBackgroundRenderer
{
    private SyntaxColorizer? _colorizer;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the span source (or <c>null</c> — draws nothing).</summary>
    public void SetColorizer(SyntaxColorizer? colorizer) => _colorizer = colorizer;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_colorizer is not { } colorizer || !textView.VisualLinesValid || textView.Document is null)
        {
            return;
        }

        var pen = new Pen(colorizer.ErrorBrush, 1);
        foreach (VisualLine visualLine in textView.VisualLines)
        {
            DocumentLine line = visualLine.FirstDocumentLine;
            if (line.IsDeleted)
            {
                continue;
            }

            foreach (SyntaxSpan span in colorizer.IssuesOf(line))
            {
                if (span.Format is not (SyntaxFormat.SyntaxError or SyntaxFormat.BadLink))
                {
                    continue;
                }

                int start = line.Offset + span.Start;
                int length = Math.Min(span.Length, line.EndOffset - start);
                if (length <= 0)
                {
                    continue;
                }

                var segment = new TextSegment { StartOffset = start, Length = length };
                foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    DrawSquiggle(drawingContext, pen, rect);
                }
            }
        }
    }

    private static void DrawSquiggle(DrawingContext context, Pen pen, Rect rect)
    {
        double baseline = rect.Bottom - 1;
        const double step = 2.0;
        var points = new List<Point>();
        bool up = true;
        for (double x = rect.Left; x <= rect.Right; x += step)
        {
            points.Add(new Point(x, baseline + (up ? -2 : 0)));
            up = !up;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }
}
