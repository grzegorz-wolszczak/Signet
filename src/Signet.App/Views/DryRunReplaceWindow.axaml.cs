using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;
using Signet.Core.Search;

namespace Signet.App.Views;

/// <summary>
/// The non-modal "Dry Run Replace All" window — a read-only preview of all matches
/// and proposed replacements.
/// </summary>
public partial class DryRunReplaceWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public DryRunReplaceWindow()
    {
        InitializeComponent();
        RowsList.DoubleTapped += OnRowDoubleTapped;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DryRunReplaceViewModel vm && RowsList.SelectedItem is ReplacePreviewRow row)
        {
            vm.OpenRowCommand.Execute(row);
        }
    }
}
