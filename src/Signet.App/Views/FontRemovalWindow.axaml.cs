using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal window shown before font files are deleted (<see cref="FontRemovalViewModel"/>): what happens to the
/// stylesheets, with each change to uncheck. "Delete" confirms; a double click on an item opens its place in the main
/// window behind the dialog.
/// </summary>
public partial class FontRemovalWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public FontRemovalWindow()
    {
        InitializeComponent();
        DeleteButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>Shows the window; returns whether the user confirmed the deletion.</summary>
    public static Task<bool> ShowAsync(Window owner, FontRemovalViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return new FontRemovalWindow { DataContext = viewModel }.ShowDialog<bool>(owner);
    }

    private void OnItemsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FontRemovalViewModel vm && sender is ListBox { SelectedItem: FontRemovalItemViewModel item })
        {
            vm.Navigate(item);
        }
    }
}
