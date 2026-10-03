using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The docked "Clips" panel. Browsing the tree of groups/clips;
/// a double click pastes the clip content into the active document (a double click, so that
/// keyboard navigation / a single selecting click do not paste by accident, consistent
/// with the "Load Search" convention in <see cref="SearchEditorView"/>).
/// </summary>
public partial class ClipsPanelView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public ClipsPanelView()
    {
        InitializeComponent();
        Tree.SelectionChanged += OnSelectionChanged;
        Tree.DoubleTapped += OnDoubleTapped;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        IEnumerable<ClipNodeViewModel> selected =
            Tree.SelectedItems?.OfType<ClipNodeViewModel>() ?? Enumerable.Empty<ClipNodeViewModel>();
        (DataContext as ClipsViewModel)?.SetSelectedNodes(selected);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ClipsViewModel vm && vm.PasteCommand.CanExecute(null))
        {
            vm.PasteCommand.Execute(null);
        }
    }
}
