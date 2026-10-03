using System;
using Avalonia.Controls;
using Avalonia.Styling;
using Signet.App.Resources;
using Signet.App.ViewModels.Tabs;

namespace Signet.App.Views.Tabs;

/// <summary>
/// Font preview tab view: metadata, a sample rendered from the font file, and the obfuscation
/// method selector.
/// </summary>
public partial class FontTabView : UserControl
{
    private static string[] ObfuscationLabels =>
    [
        Strings.Get("FontTabView_ObfuscationNone"), "IDPF", "Adobe",
    ];

    private FontTabViewModel? _boundViewModel;
    private bool _syncing;

    /// <summary>Initializes the view.</summary>
    public FontTabView()
    {
        InitializeComponent();

        ObfuscationBox.ItemsSource = ObfuscationLabels;
        ObfuscationBox.SelectionChanged += OnObfuscationChanged;
        DataContextChanged += OnDataContextChanged;
        ActualThemeVariantChanged += (_, _) => ApplyPreviewTheme();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _boundViewModel = DataContext as FontTabViewModel;
        if (_boundViewModel is null)
        {
            return;
        }

        _syncing = true;
        ObfuscationBox.SelectedIndex = (int)_boundViewModel.ObfuscationMethod;
        _syncing = false;

        ApplyPreviewTheme();
    }

    private void OnObfuscationChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _boundViewModel is null || ObfuscationBox.SelectedIndex < 0)
        {
            return;
        }

        _boundViewModel.ObfuscationMethod = (FontObfuscationMethod)ObfuscationBox.SelectedIndex;
    }

    private void ApplyPreviewTheme() =>
        _boundViewModel?.SetPreviewTheme(ActualThemeVariant == ThemeVariant.Dark);
}
