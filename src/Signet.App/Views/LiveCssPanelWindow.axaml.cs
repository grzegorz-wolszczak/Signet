using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Signet.App.Views;

/// <summary>
/// The non-modal "Live CSS Panel" window — there is only a single persistent instance
/// (like <see cref="ClipEditorWindow"/>/<see cref="SpellcheckEditorWindow"/>: Show/Activate)
/// whose <c>DataContext</c> is refreshed on every subsequent invocation of the "Live CSS Panel"
/// action (the host in <c>MainWindow</c> calls <c>LiveCssPanelViewModel.Update</c> instead of
/// creating a new window).
/// </summary>
public partial class LiveCssPanelWindow : Window
{
    private CssPropertyReferenceWindow? _cssPropertiesWindow;

    /// <summary>Initializes the window.</summary>
    public LiveCssPanelWindow() => InitializeComponent();

    private void OnCssReferenceClick(object? sender, RoutedEventArgs e)
    {
        // The main window keeps the one reference window shared with Help; without it (a panel shown on its own, as in
        // the tests) the panel keeps its own.
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.ShowCssPropertyReferenceWindow();
        }
        else
        {
            _cssPropertiesWindow = CssPropertyReferenceWindow.ShowOrActivate(this, _cssPropertiesWindow);
        }
    }
}
