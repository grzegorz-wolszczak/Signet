using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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

        ThemedLineNumberMargin margin = new();
        editor.TextArea.LeftMargins.Insert(0, margin);
        editor.TextArea.LeftMargins.Insert(1, DottedLineMargin.Create());

        // The number of the line with the caret has a look of its own (editor.currentLineNumber).
        editor.TextArea.Caret.PositionChanged += (_, _) => margin.InvalidateVisual();
    }

    /// <summary>
    /// Gives the line numbers a typeface and a size of their own; an empty typeface / a size of 0 leave them the editor's
    /// (they follow it again); so does a weight of 0.
    /// </summary>
    public static void ApplyLineNumberFont(TextEditor editor, string family, double size, int weight = 0)
    {
        ArgumentNullException.ThrowIfNull(editor);
        foreach (ThemedLineNumberMargin margin in editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>())
        {
            if (!string.IsNullOrWhiteSpace(family))
            {
                margin.SetValue(TextBlock.FontFamilyProperty, new FontFamily(family));
            }
            else
            {
                margin.ClearValue(TextBlock.FontFamilyProperty);
            }

            if (size > 0)
            {
                margin.SetValue(TextBlock.FontSizeProperty, size);
            }
            else
            {
                margin.ClearValue(TextBlock.FontSizeProperty);
            }

            if (weight > 0)
            {
                margin.SetValue(TextBlock.FontWeightProperty, FontWeightOptions.ToAvalonia(weight));
            }
            else
            {
                margin.ClearValue(TextBlock.FontWeightProperty);
            }

            margin.InvalidateMeasure();
            margin.InvalidateVisual();
        }
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
                    TextAttributes current = theme.Attributes(ThemeKeys.EditorCurrentLineNumber);
                    themed.CurrentForeground = Brush(current.Foreground);
                    themed.CurrentBackground = Brush(current.Background);
                    themed.CurrentBold = current.Bold;
                    themed.CurrentItalic = current.Italic;
                    themed.InvalidateVisual();
                    break;
                case FoldingGutterMargin folding:
                    folding.Background = Brush(numbers.Background);
                    folding.ChevronBrush = numbersForeground;
                    folding.HoverBrush = Brush(theme.Attributes(ThemeKeys.EditorCurrentLineNumber).Foreground) ?? numbersForeground;
                    folding.InvalidateVisual();
                    break;
                case BookmarkMargin bookmarks:
                    bookmarks.Background = Brush(numbers.Background);
                    bookmarks.InvalidateVisual();
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
/// of the numbers (the stock margin ends right at the last digit, while the left side has some), and the number of the
/// line with the caret in a look of its own (<c>editor.currentLineNumber</c>: color, background, bold, italic).
/// </summary>
public sealed class ThemedLineNumberMargin : LineNumberMargin
{
    /// <summary>The room between the left edge of the margin and the numbers (px).</summary>
    public const double LeftPadding = 4;

    /// <summary>The room between the numbers and the right edge of the margin, where the icon column starts (px).</summary>
    public const double RightPadding = 4;

    // The margin has room for at least this many digits, so that it does not change width as the file grows past 99 lines.
    private const int MinimumDigits = 3;

    /// <summary>The background of the line numbers; <c>null</c> = the editor's.</summary>
    public IBrush? Background { get; set; }

    /// <summary>The color of the number of the current line; <c>null</c> = the color of the other numbers.</summary>
    public IBrush? CurrentForeground { get; set; }

    /// <summary>The background of the number of the current line; <c>null</c> = the one of the margin.</summary>
    public IBrush? CurrentBackground { get; set; }

    /// <summary>Whether the number of the current line is bold.</summary>
    public bool CurrentBold { get; set; }

    /// <summary>Whether the number of the current line is italic.</summary>
    public bool CurrentItalic { get; set; }

    /// <summary>The number of the current line when it is not the caret's (the preview of a theme); <c>null</c> = the caret's.</summary>
    public int? CurrentLineOverride { get; set; }

    /// <summary>Draws a frame round the number of the current line (the blink of its key in the theme editor).</summary>
    public bool FrameCurrent { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The base measure keeps the margin informed of the number of lines; the width is that of the widest number.
        Size size = base.MeasureOverride(availableSize);
        int digits = Math.Max(MinimumDigits, (Document?.LineCount ?? 1).ToString(CultureInfo.InvariantCulture).Length);
        FormattedText widest = new(
            new string('9', digits), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, EmSize, Brushes.Black);
        return new Size(widest.Width + LeftPadding + RightPadding, size.Height);
    }

    /// <summary>
    /// A click on a number puts the caret at the start of that line, like a click in the bookmark column next to it. The
    /// margin of AvaloniaEdit selects the whole line instead, with its line break, which leaves the caret at the start of
    /// the line below; and it does so for the right button too, whose click opens the menu of the gutter.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TextView is not { } view || TextArea is not { } area)
        {
            return;
        }

        e.Handled = true;
        area.Focus();
        if (BookmarkMargin.LineAt(view, e.GetPosition(view).Y) is { } line)
        {
            area.ClearSelection();
            area.Caret.Line = line;
            area.Caret.Column = 1;
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Size size = Bounds.Size;
        if (Background is { } background)
        {
            drawingContext.FillRectangle(background, new Rect(size));
        }

        TextView? textView = TextView;
        if (textView is not { VisualLinesValid: true })
        {
            return;
        }

        IBrush foreground = GetValue(TextBlock.ForegroundProperty) ?? Brushes.Gray;
        int currentLine = CurrentLineOverride ?? TextArea?.Caret.Line ?? -1;
        Typeface typeface = Typeface;
        foreach (VisualLine line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            bool current = number == currentLine;
            Typeface face = current && (CurrentBold || CurrentItalic)
                ? new Typeface(typeface.FontFamily, CurrentItalic ? FontStyle.Italic : FontStyle.Normal,
                    CurrentBold ? FontWeight.Bold : FontWeight.Normal, typeface.Stretch)
                : typeface;
            FormattedText text = new(
                number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, EmSize,
                current ? CurrentForeground ?? foreground : foreground);

            // The numbers are aligned to the left (like in the IntelliJ editors); the room that is left is for the icons.
            double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
            if (current)
            {
                Rect cell = new(0, line.VisualTop - textView.VerticalOffset, size.Width, line.Height);
                if (CurrentBackground is { } fill)
                {
                    drawingContext.FillRectangle(fill, cell);
                }

                if (FrameCurrent)
                {
                    drawingContext.DrawRectangle(null, new Pen(current ? CurrentForeground ?? foreground : foreground, 1.5), cell.Deflate(1));
                }
            }

            drawingContext.DrawText(text, new Point(LeftPadding, top));
        }
    }
}
