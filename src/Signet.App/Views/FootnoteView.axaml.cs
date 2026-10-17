using System.ComponentModel;
using Avalonia.Controls;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.MainUI;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The content of the footnote window (<see cref="FootnoteViewModel"/>): the text around the reference, the text of the footnote and
/// its placement, and the preview (the markup Signet writes in a read-only editor; the "Appearance" tab is filled by the window,
/// which owns its web view). The window adds its buttons; the preview of Preferences shows this view alone.
/// </summary>
public partial class FootnoteView : UserControl
{
    private FootnoteViewModel? _viewModel;

    /// <summary>Initializes the view; the markup of the preview is highlighted as HTML in the colors of the theme.</summary>
    public FootnoteView()
    {
        InitializeComponent();
        if (CodeEditorTextMate.CreateColorizer(CodeEditorSyntax.Html, extended: false, linkExists: null, AppTheme.Current) is { } colorizer)
        {
            PreviewCodeEditor.TextArea.TextView.LineTransformers.Add(colorizer);
        }

        EditorTheming.Apply(PreviewCodeEditor, AppTheme.Current);
        DataContextChanged += (_, _) => Bind(DataContext as FootnoteViewModel);
    }

    /// <summary>Gives the markup of the preview the font of Code Editor.</summary>
    public void UseEditorFont(CodeEditorAppearance appearance)
    {
        if (!string.IsNullOrWhiteSpace(appearance.FontFamily))
        {
            PreviewCodeEditor.FontFamily = new Avalonia.Media.FontFamily(appearance.FontFamily);
        }
    }

    private void Bind(FootnoteViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        PreviewCodeEditor.Text = _viewModel?.PreviewCode ?? string.Empty;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FootnoteViewModel.PreviewCode))
        {
            PreviewCodeEditor.Text = _viewModel?.PreviewCode ?? string.Empty;
        }
    }
}
