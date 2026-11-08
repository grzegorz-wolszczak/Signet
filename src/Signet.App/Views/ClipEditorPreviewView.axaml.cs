using System;
using Avalonia.Controls;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The preview of the Clip Editor window on the Preferences › Clips pages (see <c>ClipEditorPreviewView.axaml</c>): it
/// follows the fonts of the settings (<see cref="ClipsViewModel.Appearance"/>) as they are changed.
/// </summary>
public partial class ClipEditorPreviewView : UserControl
{
    private ClipsViewModel? _bound;

    /// <summary>Initializes the view.</summary>
    public ClipEditorPreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.AppearanceChanged -= OnAppearanceChanged;
        }

        _bound = DataContext as ClipsViewModel;
        if (_bound is not null)
        {
            _bound.AppearanceChanged += OnAppearanceChanged;
        }

        ApplyAppearance();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    // The fonts of the window, as ClipEditorWindow applies them; the tree's own (headers, entries) are the panel view's.
    private void ApplyAppearance()
    {
        ClipEditorAppearance look = _bound?.Appearance ?? ClipEditorAppearance.Default;
        ClipsLook.SetFont(Body, look.OtherFontFamily, look.OtherFontSize, look.OtherFontWeight);
        ClipsLook.SetFont(ClipTextBox, look.TextFontFamily, look.TextFontSize, look.TextFontWeight);
    }
}
