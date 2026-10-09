using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.CodeView;

/// <summary>
/// A read-only editor showing the snippet of a Recent Locations entry (the <see cref="RecentLocationItem"/> in its
/// <c>DataContext</c>): highlighted like Code View, with the file's own line numbers.
/// </summary>
public sealed class LocationSnippetEditor : TextEditor
{
    private const string CodeFont = "Cascadia Code,Cascadia Mono,Consolas,Menlo,Monospace";

    private readonly SnippetLineNumberMargin _lineNumbers;
    private SyntaxColorizer? _colorizer;

    /// <summary>Creates the editor (read-only, not focusable, without its own line numbers).</summary>
    public LocationSnippetEditor()
    {
        FontFamily = new FontFamily(CodeFont);
        FontSize = 13;
        IsReadOnly = true;
        ShowLineNumbers = false;
        WordWrap = false;
        Focusable = false;
        IsHitTestVisible = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = new TextDocument();
        _lineNumbers = new SnippetLineNumberMargin(FontFamily, FontSize);
        TextArea.LeftMargins.Insert(0, _lineNumbers);
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not RecentLocationItem item)
        {
            return;
        }

        Document.Text = item.Snippet.Text;
        _lineNumbers.FirstLineNumber = item.Snippet.FirstLine;
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Remove(_colorizer);
        }

        _colorizer = CodeViewTextMate.CreateColorizer(item.Syntax, extended: false, linkExists: null, Appearance());
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        TextArea.TextView.Redraw();
    }

    // The Code View colours of the current theme variant.
    private CodeViewAppearance Appearance()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        return App.Services?.GetService<SettingsStore>() is { } settings
            ? dark ? settings.CodeViewDarkAppearance : settings.CodeViewAppearance
            : dark ? CodeViewAppearance.DarkDefault : CodeViewAppearance.LightDefault;
    }

    // Line numbers starting at the snippet's first line (AvaloniaEdit's own margin always starts at 1).
    private sealed class SnippetLineNumberMargin(FontFamily fontFamily, double fontSize) : AbstractMargin
    {
        private const double Padding = 8;

        private int _firstLineNumber = 1;

        public int FirstLineNumber
        {
            get => _firstLineNumber;
            set
            {
                _firstLineNumber = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int lastLine = _firstLineNumber + (Document?.LineCount ?? 1);
            return new Size(Format(new string('9', Math.Max(2, lastLine.ToString(CultureInfo.InvariantCulture).Length))).Width + 2 * Padding, 0);
        }

        public override void Render(DrawingContext context)
        {
            if (TextView is not { VisualLinesValid: true } view)
            {
                return;
            }

            foreach (VisualLine line in view.VisualLines)
            {
                int number = line.FirstDocumentLine.LineNumber + _firstLineNumber - 1;
                FormattedText text = Format(number.ToString(CultureInfo.InvariantCulture));
                double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset;
                context.DrawText(text, new Point(Bounds.Width - Padding - text.Width, y));
            }
        }

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null)
            {
                oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            }

            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null)
            {
                newTextView.VisualLinesChanged += OnVisualLinesChanged;
            }
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        private FormattedText Format(string text) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(fontFamily), fontSize, Brushes.Gray);
    }
}
