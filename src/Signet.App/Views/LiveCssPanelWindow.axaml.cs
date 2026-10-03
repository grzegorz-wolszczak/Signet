using Avalonia.Controls;

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
    /// <summary>Initializes the window.</summary>
    public LiveCssPanelWindow() => InitializeComponent();
}
