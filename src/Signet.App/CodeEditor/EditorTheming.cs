using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// Puts the look of the editors from the theme on an AvaloniaEdit editor: the text and the background
/// (<c>editor.text</c>), the current line, the selection and the line numbers with their background
/// (<c>editor.lineNumbers</c>, through <see cref="ThemedLineNumberMargin"/>).
/// </summary>
public static class EditorTheming
{
    /// <summary>
    /// Replaces the line numbers of AvaloniaEdit with <see cref="ThemedLineNumberMargin"/> (the stock margin cannot have
    /// a background). Call instead of setting <see cref="TextEditor.ShowLineNumbers"/>.
    /// </summary>
    public static void ShowThemedLineNumbers(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.ShowLineNumbers = false;
        if (editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Any())
        {
            return;
        }

        editor.TextArea.LeftMargins.Insert(0, new ThemedLineNumberMargin());
        editor.TextArea.LeftMargins.Insert(1, DottedLineMargin.Create());
    }

    /// <summary>Applies the theme to the editor.</summary>
    public static void Apply(TextEditor editor, ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(theme);
        TextAttributes text = theme.Attributes(ThemeKeys.EditorText);
        editor.Foreground = (IBrush?)Brush(text.Foreground) ?? Brushes.Black;
        editor.Background = (IBrush?)Brush(text.Background) ?? Brushes.White;

        TextView view = editor.TextArea.TextView;
        view.CurrentLineBackground = theme.Color(ThemeKeys.EditorCurrentLine) is { } line ? new SolidColorBrush(line) : null;
        view.CurrentLineBorder = null;

        TextAttributes selection = theme.Attributes(ThemeKeys.EditorSelection);
        editor.TextArea.SelectionBrush = (IBrush?)Brush(selection.Background) ?? Brushes.LightBlue;
        editor.TextArea.SelectionForeground = Brush(selection.Foreground);

        TextAttributes numbers = theme.Attributes(ThemeKeys.EditorLineNumbers);
        IBrush numbersForeground = (IBrush?)Brush(numbers.Foreground) ?? Brushes.Gray;
        editor.LineNumbersForeground = numbersForeground;
        foreach (Control margin in editor.TextArea.LeftMargins)
        {
            switch (margin)
            {
                case ThemedLineNumberMargin themed:
                    themed.SetValue(TextBlock.ForegroundProperty, numbersForeground);
                    themed.Background = Brush(numbers.Background);
                    break;
                case Line dotted:
                    dotted.Stroke = numbersForeground;
                    break;
            }
        }

        view.InvalidateLayer(KnownLayer.Selection);
        view.Redraw();
    }

    private static SolidColorBrush? Brush(ThemeColor? color) => color is { } c ? new SolidColorBrush(ResolvedTheme.ToColor(c)) : null;
}

/// <summary>
/// AvaloniaEdit's line numbers on a background of their own (<c>editor.lineNumbers</c>), with a little room to the right
/// of the numbers (the stock margin ends right at the last digit, while the left side has some).
/// </summary>
public sealed class ThemedLineNumberMargin : LineNumberMargin
{
    /// <summary>The room between the numbers and the right edge of the margin (px).</summary>
    public const double RightPadding = 4;

    /// <summary>The background of the line numbers; <c>null</c> = the editor's.</summary>
    public IBrush? Background { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Size size = base.MeasureOverride(availableSize);
        return new Size(size.Width + RightPadding, size.Height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Background is { } background)
        {
            drawingContext.FillRectangle(background, new Rect(Bounds.Size));
        }

        // The stock margin right-aligns the numbers to its edge: shift them left by the padding.
        using (drawingContext.PushTransform(Matrix.CreateTranslation(-RightPadding, 0)))
        {
            base.Render(drawingContext);
        }
    }
}
