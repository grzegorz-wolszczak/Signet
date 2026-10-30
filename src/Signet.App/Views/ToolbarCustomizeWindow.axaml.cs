using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>Window for customizing the configurable toolbars (View → Customize Toolbars).</summary>
public partial class ToolbarCustomizeWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ToolbarCustomizeWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // A double click on an available action adds it, as Add does (the first click of it selected the action).
    private void OnAvailableDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OnEntry(e) && DataContext is ToolbarCustomizeViewModel vm)
        {
            vm.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    // A double click on an item of the current layout removes it, as Remove does.
    private void OnCurrentDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OnEntry(e) && DataContext is ToolbarCustomizeViewModel vm)
        {
            vm.RemoveCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Whether the double click was on an item of the list, not on its empty part or its scroll bar.
    private static bool OnEntry(TappedEventArgs e) =>
        e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null;
}
