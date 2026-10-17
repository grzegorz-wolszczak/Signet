using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// The content of the "Cleanup" dialog (<see cref="CleanupViewModel"/>): the cleanup steps grouped into tabs, as sections with a
/// preview of their changes. A double click on a report item (or on a consequence of a risky item) opens the file at that place.
/// The dialog (<see cref="CleanupWindow"/>) adds its Close button; the preview of Preferences shows this view alone.
/// </summary>
public partial class CleanupView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public CleanupView()
    {
        InitializeComponent();
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
