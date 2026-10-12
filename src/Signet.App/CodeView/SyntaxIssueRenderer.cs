using System;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Avalonia.Media;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// What the colorizer cannot put on the text itself, for the visible lines (<see cref="SyntaxColorizer"/>): the look of
/// <c>errors.syntax</c> (a squiggle by default) under the syntax errors and bad links of the extended highlighting, and
/// the effects of the syntax keys (e.g. the dashed line under a special space).
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

        foreach (VisualLine visualLine in textView.VisualLines)
        {
            DocumentLine line = visualLine.FirstDocumentLine;
            if (line.IsDeleted)
            {
                continue;
            }

            foreach ((int start, int length, TextAttributes look) in colorizer.EffectsOf(line))
            {
                Paint(textView, drawingContext, line, start, length, look, colorizer);
            }

            foreach (SyntaxSpan span in colorizer.IssuesOf(line))
            {
                if (span.Format is SyntaxFormat.SyntaxError or SyntaxFormat.BadLink)
                {
                    Paint(textView, drawingContext, line, span.Start, span.Length, colorizer.ErrorLook, colorizer);
                }
            }
        }
    }

    private static void Paint(
        TextView textView, DrawingContext context, DocumentLine line, int start, int length, TextAttributes look, SyntaxColorizer colorizer)
    {
        int offset = line.Offset + start;
        ThemePainter.Paint(textView, context, offset, Math.Min(length, line.EndOffset - offset), look, colorizer.TextColor);
    }
}
