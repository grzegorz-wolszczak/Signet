using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Signet.App.Views;

/// <summary>
/// Modeless "Saved Searches" window (does not block the main window).
/// A single persistent instance is kept (Show/Activate, same pattern as <see cref="LiveCssPanelWindow"/>).
/// </summary>
public partial class SearchEditorWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public SearchEditorWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
