using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>One font setting of a "Fonts" page of the Preferences window (see <see cref="FontsPageView"/>).</summary>
public partial class FontSettingView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FontSettingView()
    {
        InitializeComponent();
    }

    // The link to the parent setting: the Preferences window goes to it.
    private void OnParentLinkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FontSettingViewModel { Parent: { } parent } && this.FindAncestorOfType<PreferencesWindow>() is { } window)
        {
            window.RevealFont(parent);
        }
    }
}
