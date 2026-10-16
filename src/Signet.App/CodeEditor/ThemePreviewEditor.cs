using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;
using Signet.Core.Themes;

namespace Signet.App.CodeEditor;

/// <summary>
/// The preview of a page of the theme editor (IntelliJ's <c>SimpleEditorPreview</c>): a read-only editor showing a
/// <see cref="ThemeSample"/> in the colors of a theme — the syntax through the highlighting of Code Editor, the other
/// keys (the current line, the selection, the search matches, the errors, the lines of a diff…) painted over it. A
/// click reports the key under the mouse (<see cref="KeyClicked"/>), and <see cref="Blink"/> makes the fragments of a
/// key blink.
/// </summary>
public sealed class ThemePreviewEditor : TextEditor
{
    private const int BlinkTicks = 6;

    private readonly MarksColorizer _marks;
    private readonly MarksRenderer _renderer;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private ThemeSample? _sample;
    private ResolvedTheme _theme = ResolvedTheme.Defaults;
    private SyntaxColorizer? _colorizer;
    private TextMate.Installation? _textMate;
    private ThemeKey? _blinkKey;

    // Whether the editor is in a visual tree (TextMate is installed only then).
    private bool _attached;
    private int _blinkTick;

    /// <summary>Creates the editor (read-only, not focusable, with line numbers).</summary>
    public ThemePreviewEditor()
    {
        IsReadOnly = true;
        Focusable = false;
        EditorTheming.ShowThemedLineNumbers(this);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Options.HighlightCurrentLine = false;
        Document = new TextDocument();
        _marks = new MarksColorizer(this);
        _renderer = new MarksRenderer(this);
        TextArea.TextView.LineTransformers.Add(_marks);
        TextArea.TextView.BackgroundRenderers.Add(_renderer);
        TextArea.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _blinkTimer.Tick += (_, _) => OnBlinkTick();
        EditorZoom.Attach(this);
    }

    /// <summary>Raised by a click on a fragment shown in the look of a key (on the line numbers: <c>editor.lineNumbers</c>, on the one of the current line <c>editor.currentLineNumber</c>).</summary>
    public event EventHandler<ThemeKey>? KeyClicked;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_sample is not null && _textMate is null && _colorizer is null)
        {
            InstallSyntax(_sample);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Nothing may outlive the shown preview (e.g. a page of the Preferences window left or the window closed): the
        // blink stops, and TextMate goes with its tokenizer thread (installed again when the preview is shown).
        _attached = false;
        _blinkTimer.Stop();
        _textMate?.Dispose();
        _textMate = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Shows a sample in the colors of a theme.</summary>
    public void Show(ThemeSample sample, ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!ReferenceEquals(sample, _sample))
        {
            _sample = sample;
            Document.Text = sample.Text;
            InstallSyntax(sample);
        }

        SetTheme(theme);
        UpdateNumberFrame();
    }

    /// <summary>Changes the colors.</summary>
    public void SetTheme(ResolvedTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _colorizer?.SetTheme(theme);
        _textMate?.SetTheme(CodeEditorTextMate.ThemeFor(theme));
        EditorTheming.Apply(this, theme);
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        TextArea.TextView.Redraw();
    }

    /// <summary>Makes the fragments of the key blink a few times (nothing for a key the sample does not show).</summary>
    public void Blink(ThemeKey? key)
    {
        _blinkKey = key;
        _blinkTick = 0;
        _blinkTimer.Stop();
        if (key is not null && _sample is not null
            && (_sample.RangesOf(key).Any() || key == ThemeKeys.EditorCurrentLine || key == ThemeKeys.EditorCurrentLineNumber))
        {
            _blinkTimer.Start();
        }

        UpdateNumberFrame();

        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    // The fragments of the key blinking now (every other tick shows them framed).
    private bool IsBlinking(ThemeKey key) => _blinkTimer.IsEnabled && key == _blinkKey && _blinkTick % 2 == 0;

    // The frame round the number of the current line while its key blinks.
    private void UpdateNumberFrame()
    {
        foreach (ThemedLineNumberMargin margin in TextArea.LeftMargins.OfType<ThemedLineNumberMargin>())
        {
            margin.CurrentLineOverride = _sample?.CaretLine > 0 ? _sample.CaretLine : null;
            margin.FrameCurrent = IsBlinking(ThemeKeys.EditorCurrentLineNumber);
            margin.InvalidateVisual();
        }
    }

    private void OnBlinkTick()
    {
        if (++_blinkTick >= BlinkTicks)
        {
            _blinkTimer.Stop();
        }

        UpdateNumberFrame();

        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void InstallSyntax(ThemeSample sample)
    {
        CodeEditorSyntax syntax = sample.Syntax;
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }

        _textMate?.Dispose();
        _textMate = null;
        _colorizer = CodeEditorTextMate.CreateColorizer(syntax, sample.IsExtended, ThemeSample.LinkExists, _theme);
        if (_colorizer is not null)
        {
            // Before the marks, so that a mark (e.g. the selection) wins over the syntax.
            TextArea.TextView.LineTransformers.Insert(0, _colorizer);
        }
        else if (CodeEditorTextMate.ScopeFor(syntax) is { } scope && _attached)
        {
            // The document is already the final one (see the TextMate note in CodeTabView.ApplyGrammar).
            _textMate = this.InstallTextMate(CodeEditorTextMate.Options);
            _textMate.SetTheme(CodeEditorTextMate.ThemeFor(_theme));
            _textMate.SetGrammar(scope);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_sample is null)
        {
            return;
        }

        e.Handled = true;
        TextView view = TextArea.TextView;
        Point point = e.GetPosition(view);
        if (point.X < 0)
        {
            // The number of the line with the caret has a key of its own.
            VisualLine? clicked = view.GetVisualLineFromVisualTop(point.Y + view.VerticalOffset);
            KeyClicked?.Invoke(this, clicked is not null && clicked.FirstDocumentLine.LineNumber == _sample.CaretLine
                ? ThemeKeys.EditorCurrentLineNumber
                : ThemeKeys.EditorLineNumbers);
            return;
        }

        if (view.GetPositionFloor(point + view.ScrollOffset) is not { } position)
        {
            return;
        }

        int offset = Document.GetOffset(position.Location);
        DocumentLine line = Document.GetLineByNumber(position.Line);
        ThemeKey key = (offset < line.EndOffset ? _sample.KeyAt(offset) : null)
            ?? (position.Line == _sample.CaretLine ? ThemeKeys.EditorCurrentLine : ThemeKeys.EditorText);
        KeyClicked?.Invoke(this, key);
    }

    // Applies the text color, bold and italic of the marks (after the syntax).
    private sealed class MarksColorizer(ThemePreviewEditor owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (owner._sample is not { } sample)
            {
                return;
            }

            foreach (KeyRange mark in sample.Marks.Where(m => m.Key.Kind == ThemeKeyKind.TextAttributes).OrderByDescending(m => m.Length))
            {
                int start = Math.Max(mark.Offset, line.Offset);
                int end = Math.Min(mark.End, line.EndOffset);
                if (end <= start)
                {
                    continue;
                }

                TextAttributes look = owner._theme.Attributes(mark.Key);
                if (look.Foreground is { } foreground)
                {
                    IBrush brush = new SolidColorBrush(ResolvedTheme.ToColor(foreground));
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
                }

                if (look.Bold || look.Italic)
                {
                    ChangeLinePart(start, end, e =>
                    {
                        Typeface current = e.TextRunProperties.Typeface;
                        e.TextRunProperties.SetTypeface(new Typeface(
                            current.FontFamily, look.Italic ? FontStyle.Italic : FontStyle.Normal,
                            look.Bold ? FontWeight.Bold : FontWeight.Normal, current.Stretch));
                    });
                }
            }
        }
    }

    // Paints under the text: the current line, the backgrounds and effects of the marks, the effects of the syntax and
    // the frame of a blinking key.
    private sealed class MarksRenderer(ThemePreviewEditor owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner._sample is not { } sample || !textView.VisualLinesValid)
            {
                return;
            }

            ResolvedTheme theme = owner._theme;
            Color text = theme.Foreground(ThemeKeys.EditorText) ?? Colors.Black;
            if (sample.CaretLine > 0 && theme.Color(ThemeKeys.EditorCurrentLine) is { } currentLine)
            {
                FillLines(textView, drawingContext, sample.CaretLine, sample.CaretLine, new SolidColorBrush(currentLine),
                    owner.IsBlinking(ThemeKeys.EditorCurrentLine) ? text : null);
            }

            foreach (KeyRange mark in sample.Marks.OrderByDescending(m => m.Length))
            {
                TextAttributes look = mark.Key.Kind == ThemeKeyKind.TextAttributes
                    ? theme.Attributes(mark.Key)
                    : new TextAttributes { Background = theme.Resolver.Color(theme.Theme, mark.Key) };
                if (ThemeSample.IsWholeLine(mark.Key))
                {
                    DocumentLine first = owner.Document.GetLineByOffset(mark.Offset);
                    DocumentLine last = owner.Document.GetLineByOffset(Math.Max(mark.Offset, mark.End - 1));
                    IBrush? fill = look.Background is { } bg ? new SolidColorBrush(ResolvedTheme.ToColor(bg)) : null;
                    FillLines(textView, drawingContext, first.LineNumber, last.LineNumber, fill, owner.IsBlinking(mark.Key) ? text : null);
                    continue;
                }

                bool rounded = mark.Key == ThemeKeys.EditorTagPair || mark.Key == ThemeKeys.SearchMatch || mark.Key == ThemeKeys.SearchMarked;
                ThemePainter.Paint(textView, drawingContext, mark.Offset, mark.Length, look, text, rounded);
                Frame(textView, drawingContext, mark, text);
            }

            foreach (KeyRange range in sample.SyntaxRanges)
            {
                TextAttributes look = theme.Attributes(range.Key);
                if (look.Effect != EffectType.None)
                {
                    ThemePainter.Paint(textView, drawingContext, range.Offset, range.Length, look with { Background = null }, text);
                }

                Frame(textView, drawingContext, range, text);
            }
        }

        private void Frame(TextView textView, DrawingContext context, KeyRange range, Color color)
        {
            if (!owner.IsBlinking(range.Key))
            {
                return;
            }

            Pen pen = new(new SolidColorBrush(color), 1.5);
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = range.Offset, Length = range.Length }))
            {
                context.DrawRectangle(null, pen, rect.Inflate(1));
            }
        }

        // Fills whole lines (first..last, 1-based) over the width of the view, with a frame while blinking.
        private static void FillLines(TextView textView, DrawingContext context, int first, int last, IBrush? fill, Color? frame)
        {
            foreach (VisualLine visual in textView.VisualLines)
            {
                int number = visual.FirstDocumentLine.LineNumber;
                if (number < first || number > last)
                {
                    continue;
                }

                Rect rect = new(0, visual.VisualTop - textView.ScrollOffset.Y, textView.Bounds.Width + textView.ScrollOffset.X, visual.Height);
                if (fill is not null)
                {
                    context.FillRectangle(fill, rect);
                }

                if (frame is { } color)
                {
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(color), 1.5), rect.Deflate(1));
                }
            }
        }
    }
}
