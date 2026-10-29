using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed on detachment and before rebuilding; the view can be reattached.")]
public partial class CleanupClassTreeView : UserControl
{
    private LocalizedColumns<CleanupClassTreeNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<CleanupClassTreeNodeViewModel>? _source;
    private CleanupSectionViewModel? _section;

    public CleanupClassTreeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
        AttachedToVisualTree += (_, _) => BuildTree();
        DetachedFromVisualTree += (_, _) => DisposeTree();
        ClassTree.DoubleTapped += OnTreeDoubleTapped;
    }

    private void BuildTree()
    {
        DisposeTree();
        _section = DataContext as CleanupSectionViewModel;
        if (_section is not { UsesClassTree: true } section)
        {
            return;
        }

        _columns = new LocalizedColumns<CleanupClassTreeNodeViewModel>();
        _source = new HierarchicalTreeDataGridSource<CleanupClassTreeNodeViewModel>(section.ClassGroups)
        {
            Columns =
            {
                _columns.Expander(
                    _columns.Template(
                        "Cleanup_UndefinedClasses_ClassFilesHeader",
                        "CleanupClassTreeCellTemplate",
                        new GridLength(1, GridUnitType.Star)),
                    node => node.Children,
                    node => node.HasChildren,
                    node => node.IsExpanded),
            },
        };
        ClassTree.Source = _source;
    }

    private void DisposeTree()
    {
        ClassTree.Source = null;
        _source?.Dispose();
        _source = null;
        _columns = null;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_source?.RowSelection?.SelectedItem is not { } node)
        {
            return;
        }

        if (node.HasChildren)
        {
            if (e.Source is not CheckBox)
            {
                node.IsExpanded = !node.IsExpanded;
                e.Handled = true;
            }

            return;
        }

        if (node.Item is { } item && _section is { } section)
        {
            section.Navigate(item);
            e.Handled = true;
        }
    }
}
