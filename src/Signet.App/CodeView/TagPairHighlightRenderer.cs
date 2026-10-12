using System;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;
using Signet.Core.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// Marks the opening and closing tag of the (X)HTML/XML pair at the current
/// caret position (setting <c>HighlightOpenCloseTags</c>).
/// </summary>
internal sealed class TagPairHighlightRenderer : IBackgroundRenderer
{
    /// <summary>The theme the pair is drawn in (<c>editor.tagPair</c>).</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

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

        TextAttributes look = Theme.Attributes(ThemeKeys.EditorTagPair);
        Color text = Theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
        foreach ((int Offset, int Length)? range in new[] { pair.Open, pair.Close })
        {
            if (range is { } r)
            {
                ThemePainter.Paint(textView, drawingContext, r.Offset, r.Length, look, text, rounded: true);
            }
        }
    }
}
