using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeView;

/// <summary>
/// Draws a red squiggle under every misspelled word. Many ranges at once
/// (like <see cref="SearchHighlightRenderer"/>), squiggle style as in <see cref="WellFormedErrorRenderer"/>.
/// </summary>
internal sealed class SpellCheckHighlightRenderer : IBackgroundRenderer
{
    private static readonly Pen SquigglePen = new(new SolidColorBrush(Color.FromRgb(0xE0, 0x1B, 0x1B)), 1);

    private readonly List<(int Start, int End)> _words = new();

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the list of misspelled word ranges to underline.</summary>
    public void SetWords(IEnumerable<(int Start, int End)> words)
    {
        _words.Clear();
        if (words is not null)
        {
            _words.AddRange(words);
        }
    }

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (!textView.VisualLinesValid)
        {
            return;
        }

        foreach ((int start, int end) in _words)
        {
            if (end <= start)
            {
                continue;
            }

            var segment = new WordSegment(start, end - start);
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawSquiggle(drawingContext, rect);
            }
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

    private sealed class WordSegment : ISegment
    {
        public WordSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }

        public int EndOffset => Offset + Length;
    }
}
