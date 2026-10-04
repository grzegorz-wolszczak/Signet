using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// The modal "Cleanup" dialog (<see cref="CleanupViewModel"/>): the cleanup steps grouped into tabs, as sections
/// with a preview of their changes. Each tab's "Clean" button applies that tab's plan and the dialog stays open;
/// "Close" closes it. A double click on a report item (or on a consequence of a risky item) opens the file at that
/// place in the main window behind the dialog.
/// </summary>
public partial class CleanupWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public CleanupWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Shows the dialog and waits until it is closed.</summary>
    public static async Task ShowAsync(Window owner, CleanupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        CleanupWindow window = new() { DataContext = viewModel };
        void OnCloseRequested(object? sender, EventArgs e) => window.Close();
        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }
    }

    private void OnConsequenceDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CleanupViewModel vm && sender is Control { DataContext: CleanupConsequence consequence })
        {
            vm.Navigate(consequence);
            e.Handled = true;
        }
    }

    private void OnItemsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CleanupViewModel vm && sender is ListBox { SelectedItem: CleanupItemViewModel item })
        {
            vm.Navigate(item);
        }
    }
}
