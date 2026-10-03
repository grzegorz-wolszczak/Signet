using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.Core.MainUI;

namespace Signet.App.CodeView;

/// <summary>
/// Highlights the background of the opening and closing tag of the (X)HTML/XML pair at the current
/// caret position (setting <c>HighlightOpenCloseTags</c>).
/// </summary>
internal sealed class TagPairHighlightRenderer : IBackgroundRenderer
{
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

    private TagPairHighlight? _pair;

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets (or clears) the tag pair to highlight.</summary>
    public void SetPair(TagPairHighlight? pair) => _pair = pair;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_pair is not { } pair || !textView.VisualLinesValid)
        {
            return;
        }

        DrawRange(textView, drawingContext, pair.Open);
        DrawRange(textView, drawingContext, pair.Close);
    }

    private static void DrawRange(TextView textView, DrawingContext context, (int Offset, int Length)? range)
    {
        if (range is not { } r || r.Length <= 0)
        {
            return;
        }

        var segment = new SimpleSegment(r.Offset, r.Length);
        foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            context.DrawRectangle(Fill, null, rect.Inflate(new Thickness(0.5, 0)), 2, 2);
        }
    }

    private sealed class SimpleSegment : ISegment
    {
        public SimpleSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }

        public int EndOffset => Offset + Length;
    }
}
