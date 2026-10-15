using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Highlights all Find matches and — in a separate style — the "marked text" area
/// to which the search is restricted.
/// </summary>
internal sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    private readonly List<(int Start, int End)> _matches = new();
    private (int Start, int End)? _marked;

    /// <summary>The theme the matches (<c>search.match</c>) and the marked area (<c>search.marked</c>) are drawn in.</summary>
    public ResolvedTheme Theme { get; set; } = AppTheme.Current;

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

        Color text = Theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
        if (_marked is { } marked && marked.End > marked.Start)
        {
            ThemePainter.Paint(textView, drawingContext, marked.Start, marked.End - marked.Start, Theme.Attributes(ThemeKeys.SearchMarked), text);
        }

        // Opaque by default: the Selection layer lies below the text, and the selection of the current match is drawn above it.
        TextAttributes match = Theme.Attributes(ThemeKeys.SearchMatch);
        foreach ((int start, int end) in _matches)
        {
            ThemePainter.Paint(textView, drawingContext, start, end - start, match, text, rounded: true);
        }
    }
}
