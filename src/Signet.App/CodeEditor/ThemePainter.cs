using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Draws a range of text in the look a theme key gives it, behind the text (the renderers' layer): the background and
/// the effect of the <see cref="TextAttributes"/> — a line under or through the text, a wave, a frame.
/// </summary>
internal static class ThemePainter
{
    /// <summary>Draws the background and the effect of <paramref name="attributes"/> under the range.</summary>
    /// <param name="textView">The view.</param>
    /// <param name="context">Where to draw.</param>
    /// <param name="offset">The start of the range (document offset).</param>
    /// <param name="length">The length of the range.</param>
    /// <param name="attributes">The look.</param>
    /// <param name="text">The color of the text around — for an effect without a color of its own and without a text color.</param>
    /// <param name="rounded">Rounded corners slightly wider than the text (search matches, the tag pair).</param>
    public static void Paint(
        TextView textView, DrawingContext context, int offset, int length, TextAttributes attributes, Color text, bool rounded = false)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attributes);
        if (length <= 0 || (attributes.Background is null && attributes.Effect == EffectType.None))
        {
            return;
        }

        IBrush? background = attributes.Background is { } bg ? new SolidColorBrush(ResolvedTheme.ToColor(bg)) : null;
        Color effectColor = attributes.EffectColor is { } ec ? ResolvedTheme.ToColor(ec)
            : attributes.Foreground is { } fg ? ResolvedTheme.ToColor(fg)
            : text;
        foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = offset, Length = length }))
        {
            if (background is not null)
            {
                if (rounded)
                {
                    context.DrawRectangle(background, null, rect.Inflate(new Thickness(0.5, 0)), 2, 2);
                }
                else
                {
                    context.DrawRectangle(background, null, rect);
                }
            }

            DrawEffect(context, rect, attributes.Effect, effectColor);
        }
    }

    /// <summary>Draws an effect over a rectangle of text.</summary>
    public static void DrawEffect(DrawingContext context, Rect rect, EffectType effect, Color color)
    {
        ArgumentNullException.ThrowIfNull(context);
        SolidColorBrush brush = new(color);
        double under = rect.Bottom - 0.5;
        switch (effect)
        {
            case EffectType.Underline:
                context.DrawLine(new Pen(brush, 1), new Point(rect.Left, under), new Point(rect.Right, under));
                break;
            case EffectType.BoldUnderline:
                context.DrawLine(new Pen(brush, 2), new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
                break;
            case EffectType.DottedUnderline:
                context.DrawLine(new Pen(brush, 1, new DashStyle(new AvaloniaList<double> { 1, 2 }, 0)), new Point(rect.Left, under), new Point(rect.Right, under));
                break;
            case EffectType.DashedUnderline:
                context.DrawLine(new Pen(brush, 1, new DashStyle(new AvaloniaList<double> { 2, 2 }, 0)), new Point(rect.Left, under), new Point(rect.Right, under));
                break;
            case EffectType.Wave:
                DrawWave(context, new Pen(brush, 1), rect);
                break;
            case EffectType.Box:
                context.DrawRectangle(null, new Pen(brush, 1), rect.Deflate(0.5));
                break;
            case EffectType.Strikeout:
                double middle = rect.Top + (rect.Height / 2);
                context.DrawLine(new Pen(brush, 1), new Point(rect.Left, middle), new Point(rect.Right, middle));
                break;
        }
    }

    // A zigzag 2 px high under the text, as the spelling and error squiggles always were.
    private static void DrawWave(DrawingContext context, Pen pen, Rect rect)
    {
        double baseline = rect.Bottom - 1;
        const double step = 2.0;
        List<Point> points = new();
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
