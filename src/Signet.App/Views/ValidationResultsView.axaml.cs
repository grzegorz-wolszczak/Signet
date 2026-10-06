using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Validation Results" panel view — a results table; double-clicking a row navigates to the
/// file/line.
/// </summary>
public partial class ValidationResultsView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public ValidationResultsView()
    {
        InitializeComponent();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ValidationResultsViewModel vm && vm.Source.RowSelection?.SelectedItem is { } row)
        {
            vm.Activate(row);
        }
    }
}
