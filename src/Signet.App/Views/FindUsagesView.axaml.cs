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
/// <remarks>
/// The tree is a flattened, virtualizing list (<see cref="FindUsagesViewModel.Rows"/>), so the keys of a tree view are
/// handled here: Right / NumPad + expand the selected node (Right on an expanded one goes to its first child), Left /
/// NumPad - collapse it (Left on a collapsed node or a usage goes to the parent); double-clicking a file or the root
/// expands / collapses it.
/// </remarks>
public partial class FindUsagesView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FindUsagesView()
    {
        InitializeComponent();

        // Tunnel: the list handles NumPad +/- itself (expanding / collapsing the selected node).
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);

        // Tunnel: the list box itself consumes the arrows and Enter before a bubbling handler sees them.
        UsageList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private FindUsagesNode? SelectedNode => UsageList.SelectedItem as FindUsagesNode;

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { HasChildren: true } node)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        ActivateSelected();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || SelectedNode is not { } node)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = ActivateSelected();
                break;
            case Key.Right or Key.Add when node.HasChildren:
                if (node.IsExpanded && e.Key == Key.Right)
                {
                    Select(node.Children[0]);
                }

                node.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Left or Key.Subtract:
                if (node is { HasChildren: true, IsExpanded: true })
                {
                    node.IsExpanded = false;
                }
                else if (e.Key == Key.Left && node.Parent is { } parent)
                {
                    Select(parent);
                }

                e.Handled = true;
                break;
        }
    }

    private void Select(FindUsagesNode node)
    {
        UsageList.SelectedItem = node;
        UsageList.ScrollIntoView(node);

        // Keyboard focus follows, so that Up / Down continue from the new row.
        UsageList.ContainerFromItem(node)?.Focus(NavigationMethod.Directional);
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
        if (DataContext is FindUsagesViewModel vm && SelectedNode is { Usage: not null } node)
        {
            vm.Activate(node);
            return true;
        }

        return false;
    }
}
