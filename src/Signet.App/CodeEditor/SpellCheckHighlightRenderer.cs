using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Marks every misspelled word (<c>errors.spelling</c>, a red squiggle by default). Many ranges at once (like
/// <see cref="SearchHighlightRenderer"/>).
/// </summary>
internal sealed class SpellCheckHighlightRenderer : IBackgroundRenderer
{
    private readonly List<(int Start, int End)> _words = new();

    /// <summary>The theme the marks are drawn in.</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

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

        TextAttributes look = Theme.Attributes(ThemeKeys.ErrorsSpelling);
        Color text = Theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
        foreach ((int start, int end) in _words)
        {
            ThemePainter.Paint(textView, drawingContext, start, end - start, look, text);
        }
    }
}
