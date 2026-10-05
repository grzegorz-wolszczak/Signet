using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Find Usages" panel view (<see cref="FindUsagesViewModel"/>): a left toolbar (Refresh, Group By, Expand All, Collapse
/// All) and the tree of the usages; double-clicking (or Enter on) a usage opens the file with the caret on it.
/// Ctrl+NumPad + / Ctrl+NumPad - in the panel expand / collapse all, as in the JetBrains IDEs.
/// </summary>
public partial class FindUsagesView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FindUsagesView()
    {
        InitializeComponent();

        // Tunnel: the tree handles NumPad +/- itself (expanding / collapsing the selected node).
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e) => ActivateSelected();

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = ActivateSelected();
        }
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Add or Key.Subtract
            && DataContext is FindUsagesViewModel vm)
        {
            IRelayCommand command = e.Key == Key.Add ? vm.ExpandAllCommand : vm.CollapseAllCommand;
            command.Execute(null);
            e.Handled = true;
        }
    }

    private bool ActivateSelected()
    {
        if (DataContext is FindUsagesViewModel vm && Tree.SelectedItem is FindUsagesNode { Usage: not null } node)
        {
            vm.Activate(node);
            return true;
        }

        return false;
    }
}
