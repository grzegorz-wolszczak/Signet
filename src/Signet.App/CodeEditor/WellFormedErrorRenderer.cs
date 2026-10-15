using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Marks the text range reported as a well-formedness error (<c>errors.wellFormed</c>, a red squiggle by default) and
/// the range of a non-blocking structural warning, e.g. a missing DOCTYPE (<c>errors.wellFormedWarning</c>, an amber
/// one). At most one error and one warning range at a time.
/// </summary>
internal sealed class WellFormedErrorRenderer : IBackgroundRenderer
{
    private ErrorSegment? _segment;
    private ErrorSegment? _warningSegment;

    /// <summary>The theme the marks are drawn in.</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

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

        Color text = Theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
        if (_warningSegment is { } warning)
        {
            ThemePainter.Paint(textView, drawingContext, warning.Offset, warning.Length, Theme.Attributes(ThemeKeys.ErrorsWellFormedWarning), text);
        }

        if (_segment is { } segment)
        {
            ThemePainter.Paint(textView, drawingContext, segment.Offset, segment.Length, Theme.Attributes(ThemeKeys.ErrorsWellFormed), text);
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
