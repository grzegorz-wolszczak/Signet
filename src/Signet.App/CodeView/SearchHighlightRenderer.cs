using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Signet.App.CodeView;

/// <summary>
/// Highlights all Find matches and — in a separate style — the "marked text" area
/// to which the search is restricted.
/// </summary>
internal sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    private static readonly IPen MarkedBorder =
        new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x4D, 0x9B, 0xFF)), 1);
    private static readonly IBrush MarkedFill = new SolidColorBrush(Color.FromArgb(0x18, 0x4D, 0x9B, 0xFF));

    private readonly List<(int Start, int End)> _matches = new();
    private (int Start, int End)? _marked;
    private IBrush _matchFill = new SolidColorBrush(Color.Parse("#FFF0C4"));

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the list of match ranges to highlight.</summary>
    public void SetMatches(IEnumerable<(int Start, int End)> matches)
    {
        _matches.Clear();
        if (matches is not null)
        {
            _matches.AddRange(matches);
        }
    }

    /// <summary>
    /// Match background color (<c>CodeViewAppearance.SearchMatchBackgroundColor</c>). Opaque — the
    /// Selection layer lies below the text, and the selection of the current match is drawn above it.
    /// </summary>
    public void SetMatchBrush(IBrush brush) => _matchFill = brush;

    /// <summary>Sets (or clears, for <c>null</c>) the "marked text" area.</summary>
    public void SetMarked((int Start, int End)? marked) => _marked = marked;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (!textView.VisualLinesValid)
        {
            return;
        }

        if (_marked is { } marked && marked.End > marked.Start)
        {
            foreach (Rect rect in RectsFor(textView, marked.Start, marked.End))
            {
                drawingContext.DrawRectangle(MarkedFill, MarkedBorder, rect);
            }
        }

        foreach ((int start, int end) in _matches)
        {
            if (end <= start)
            {
                continue;
            }

            foreach (Rect rect in RectsFor(textView, start, end))
            {
                drawingContext.DrawRectangle(_matchFill, null, rect.Inflate(new Thickness(0.5, 0)), 2, 2);
            }
        }
    }

    private static IEnumerable<Rect> RectsFor(TextView textView, int start, int end)
    {
        var segment = new HighlightSegment(start, end - start);
        return BackgroundGeometryBuilder.GetRectsForSegment(textView, segment);
    }

    private sealed class HighlightSegment : ISegment
    {
        public HighlightSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }

        public int EndOffset => Offset + Length;
    }
}
