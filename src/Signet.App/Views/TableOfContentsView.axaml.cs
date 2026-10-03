using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Table Of Contents" panel view — the TOC tree; double-clicking or pressing Enter on an entry
/// navigates to the target file and fragment.
/// </summary>
public partial class TableOfContentsView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public TableOfContentsView()
    {
        InitializeComponent();
        Tree.DoubleTapped += OnActivate;
        Tree.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnActivate(sender, e);
        }
    }

    private void OnActivate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TableOfContentsViewModel vm && Tree.SelectedItem is TocEntryViewModel node)
        {
            vm.Activate(node);
        }
    }
}
