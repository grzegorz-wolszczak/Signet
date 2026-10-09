using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The Recent Locations popup (<see cref="RecentLocationsViewModel"/>). The filter box keeps the focus: typing filters,
/// Up / Down move the selection, Enter or a click goes to the entry, Delete forgets it, Esc or a click outside closes
/// the popup, and the Recent Locations shortcut toggles "Show edited only".
/// </summary>
public partial class RecentLocationsWindow : Window
{
    private readonly KeyGesture? _toggleGesture;
    private bool _closing;

    /// <summary>Initializes the window (designer).</summary>
    public RecentLocationsWindow()
        : this(null)
    {
    }

    /// <summary>Initializes the window.</summary>
    /// <param name="toggleGesture">The Recent Locations shortcut — toggles "Show edited only" while the popup is open.</param>
    public RecentLocationsWindow(KeyGesture? toggleGesture)
    {
        _toggleGesture = toggleGesture;
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Locations.AddHandler(TappedEvent, OnLocationTapped);
        Opened += (_, _) => FilterBox.Focus();
        Deactivated += (_, _) => CloseOnce();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RecentLocationsViewModel vm)
        {
            vm.CloseRequested += (_, _) => CloseOnce();
        }
    }

    private RecentLocationsViewModel? ViewModel => DataContext as RecentLocationsViewModel;

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (_toggleGesture is not null && _toggleGesture.Matches(e))
        {
            vm.ShowEditedOnly = !vm.ShowEditedOnly;
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                CloseOnce();
                break;
            case Key.Up or Key.Down:
                vm.MoveSelection(e.Key == Key.Up ? -1 : 1);
                ScrollToSelected(vm);
                break;
            case Key.PageUp or Key.PageDown:
                vm.MoveSelection(e.Key == Key.PageUp ? -5 : 5);
                ScrollToSelected(vm);
                break;
            case Key.Enter when vm.Selected is { } selected:
                vm.Navigate(selected);
                break;
            case Key.Delete:
                vm.RemoveSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ScrollToSelected(RecentLocationsViewModel vm)
    {
        if (vm.Selected is { } selected)
        {
            Locations.ScrollIntoView(selected);
        }
    }

    private void OnLocationTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { Selected: { } selected } vm)
        {
            vm.Navigate(selected);
        }
    }

    private void CloseOnce()
    {
        if (!_closing)
        {
            _closing = true;
            Close();
        }
    }
}
