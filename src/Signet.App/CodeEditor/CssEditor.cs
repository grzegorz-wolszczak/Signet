using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit;
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

    private SyntaxColorizer? _colorizer;
    private bool _syncing;

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
        ApplyTheme(AppTheme.Current);
        Document.TextChanged += (_, _) =>
        {
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
    }
}
