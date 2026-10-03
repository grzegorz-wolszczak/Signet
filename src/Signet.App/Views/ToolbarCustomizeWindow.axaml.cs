using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Signet.App.Views;

/// <summary>Window for customizing the configurable toolbars (View → Customize Toolbars).</summary>
public partial class ToolbarCustomizeWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ToolbarCustomizeWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
