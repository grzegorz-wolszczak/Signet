using Avalonia.Controls;
using Avalonia.Interactivity;
using Signet.App.ViewModels;

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
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.ShowCssPropertyReferenceWindow();
            return;
        }

        if (_cssPropertiesWindow is { IsVisible: true })
        {
            _cssPropertiesWindow.Activate();
            return;
        }

        CssPropertyReferenceWindow window = new() { DataContext = new CssPropertyReferenceViewModel() };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_cssPropertiesWindow, window))
            {
                _cssPropertiesWindow = null;
            }
        };
        _cssPropertiesWindow = window;
        MainWindow.RememberPlacement(window);
        window.Show(this);
    }
}
