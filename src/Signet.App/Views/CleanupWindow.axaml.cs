using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// The modal "Cleanup" dialog (<see cref="CleanupViewModel"/>): the cleanup steps as sections with a preview of
/// their changes; OK applies the current plan, Cancel changes nothing. A double click on a report item (or on a
/// consequence of a risky item) opens the file at that place in the main window behind the dialog.
/// </summary>
public partial class CleanupWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public CleanupWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>Shows the dialog; returns <c>true</c> when the user confirmed with OK.</summary>
    public static async Task<bool> AskAsync(Window owner, CleanupViewModel viewModel)
    {
        CleanupWindow window = new() { DataContext = viewModel };
        return await window.ShowDialog<bool>(owner);
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
