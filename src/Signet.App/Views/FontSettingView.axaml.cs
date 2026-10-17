using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// One font setting of a "Fonts" page of the Preferences window (see <see cref="FontsPageView"/>): the font as a line, "Choose…"
/// and the preview of the font, which is one or two lines tall to start with and which the user can make taller.
/// </summary>
public partial class FontSettingView : UserControl
{
    private FontSettingViewModel? _font;
    private bool _userHeight;

    /// <summary>Initializes the view.</summary>
    public FontSettingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Track(DataContext as FontSettingViewModel);
        PreviewGrip.Resized += (_, _) =>
        {
            _userHeight = true;
            PreviewHeightChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Whether the row has the preview of its font under it (default: yes). A page with a preview of its own for all the
    /// fonts (<see cref="FontsPageView.HasSharedPreview"/>) switches it off.
    /// </summary>
    public bool ShowPreview
    {
        get => PreviewFrame.IsVisible;
        set => PreviewFrame.IsVisible = value;
    }

    /// <summary>Raised when the user has dragged the grip: <see cref="UserPreviewHeight"/> is the new height.</summary>
    public event EventHandler? PreviewHeightChanged;

    /// <summary>The height the user gave the preview; <c>null</c> while it has the default one (two lines of the font).</summary>
    public double? UserPreviewHeight => _userHeight ? PreviewArea.Height : null;

    /// <summary>Gives the preview the height the user chose before (the remembered one).</summary>
    public void SetUserPreviewHeight(double height)
    {
        _userHeight = true;
        PreviewArea.Height = Math.Max(PreviewGrip.MinTargetHeight, height);
    }

    // The link to the parent setting: the Preferences window goes to it.
    private void OnParentLinkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FontSettingViewModel { Parent: { } parent } && this.FindAncestorOfType<PreferencesWindow>() is { } window)
        {
            window.RevealFont(parent);
        }
    }

    // Follows the setting, to update what the bindings cannot: the code sample, the wrapping and the height.
    private void Track(FontSettingViewModel? font)
    {
        if (_font is not null)
        {
            _font.PropertyChanged -= OnFontChanged;
        }

        _font = font;
        if (_font is not null)
        {
            _font.PropertyChanged += OnFontChanged;
        }

        UpdatePreview();
    }

    private void OnFontChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        bool wrap = _font?.WrapPreview ?? false;
        bool code = _font?.IsCodePreview ?? false;
        PlainPreview.IsVisible = !code;
        CodePreview.IsVisible = code;
        CodePreview.WordWrap = wrap;
        PlainPreview.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PreviewText.TextWrapping = wrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
        if (_font is null)
        {
            return;
        }

        if (code)
        {
            CodePreview.Show(
                _font.PreviewText, _font.EffectiveFamily, _font.EffectiveSize, AppTheme.Current, _font.CodePreviewExtended,
                _font.EffectiveWeight);
        }

        if (!_userHeight)
        {
            // Two lines of the font, and the room of the scroll bar (the sample of code scrolls sideways).
            double line = Math.Ceiling(Math.Max(_font.EffectiveSize, 8) * 1.45);
            PreviewArea.Height = (2 * line) + (code ? 22 : 14);
        }
    }
}
