using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;

namespace Signet.App.CodeEditor;

/// <summary>
/// An editable field for CSS (declarations or rules): the text is highlighted as in Code Editor, in the colors of the
/// current theme, wraps long lines and zooms with Ctrl + wheel (<see cref="EditorZoom"/>). Used instead of a plain
/// text box where the user writes CSS, e.g. the footnote styles in the Preferences window. <see cref="CssText"/> binds
/// two-way (AvaloniaEdit's own <see cref="TextEditor.Text"/> is not a bindable property).
/// </summary>
public sealed class CssEditor : TextEditor
{
    /// <summary>The CSS in the editor; binds two-way.</summary>
    public static readonly StyledProperty<string> CssTextProperty =
        AvaloniaProperty.Register<CssEditor, string>(nameof(CssText), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    private readonly ErrorRenderer _errorRenderer = new();
    private SyntaxColorizer? _colorizer;
    private bool _syncing;
    private Func<string, (int Start, int Length)?>? _errorLocator;

    /// <summary>Creates the editor (editable, wrapping, without line numbers).</summary>
    public CssEditor()
    {
        FontFamily = new FontFamily("Cascadia Code,Consolas,monospace");
        FontSize = 13;
        WordWrap = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Padding = new Thickness(4, 2);
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = false;
        EditorZoom.Attach(this);
        TextArea.TextView.BackgroundRenderers.Add(_errorRenderer);
        ApplyTheme(AppTheme.Current);
        Document.TextChanged += (_, _) =>
        {
            UpdateError();
            if (!_syncing)
            {
                _syncing = true;
                try
                {
                    CssText = Document.Text;
                }
                finally
                {
                    _syncing = false;
                }
            }
        };
    }

    /// <summary>
    /// Finds what is wrong in the text: the start and the length of the first problem, or <see langword="null"/> when the
    /// text is fine. It is asked after every change of the text; the problem is underlined with the look of the theme's
    /// syntax errors (a squiggle by default), as Code Editor underlines its errors.
    /// </summary>
    public Func<string, (int Start, int Length)?>? ErrorLocator
    {
        get => _errorLocator;
        set
        {
            _errorLocator = value;
            UpdateError();
        }
    }

    /// <summary>The underlined range (start, length); <see langword="null"/> when nothing is underlined.</summary>
    public (int Start, int Length)? ErrorRange => _errorRenderer.Range;

    private void UpdateError()
    {
        (int Start, int Length)? range = _errorLocator?.Invoke(Document.Text);
        if (range is { } r && (r.Length <= 0 || r.Start < 0 || r.Start >= Document.TextLength))
        {
            range = null;
        }

        _errorRenderer.Range = range is { } valid ? (valid.Start, Math.Min(valid.Length, Document.TextLength - valid.Start)) : null;
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>Gets or sets <see cref="CssTextProperty"/>.</summary>
    public string CssText
    {
        get => GetValue(CssTextProperty);
        set => SetValue(CssTextProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CssTextProperty && !_syncing)
        {
            _syncing = true;
            try
            {
                string text = CssText ?? string.Empty;
                if (Document.Text != text)
                {
                    Document.Text = text;
                }
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed += OnThemeChanged;
        ApplyTheme(AppTheme.Current);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppTheme.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme(AppTheme.Current);

    private void ApplyTheme(ResolvedTheme theme)
    {
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Remove(_colorizer);
        }

        _colorizer = CodeEditorTextMate.CreateColorizer(CodeEditorSyntax.Css, extended: true, linkExists: null, theme);
        if (_colorizer is not null)
        {
            TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        EditorTheming.Apply(this, theme);
        _errorRenderer.Colorizer = _colorizer;
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    // Underlines the problem with the look of the theme's syntax errors.
    private sealed class ErrorRenderer : IBackgroundRenderer
    {
        public (int Start, int Length)? Range { get; set; }

        public SyntaxColorizer? Colorizer { get; set; }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Range is not { } range || Colorizer is not { } colorizer || !textView.VisualLinesValid)
            {
                return;
            }

            ThemePainter.Paint(textView, drawingContext, range.Start, range.Length, colorizer.ErrorLook, colorizer.TextColor);
        }
    }
}
