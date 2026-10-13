using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// A page of the keys of Appearance › Color Scheme (<see cref="ColorSchemePageViewModel"/>). The tree's source is built
/// here (TreeDataGrid sources belong to the views).
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the DataContext changes, like in the other TreeDataGrid views.")]
public partial class ColorSchemeKeysView : UserControl
{
    private ColorSchemePageViewModel? _bound;
    private LocalizedColumns<ThemeKeyNode>? _columns;
    private HierarchicalTreeDataGridSource<ThemeKeyNode>? _source;
    private TreeSelectionSync<ThemeKeyNode>? _selection;

    /// <summary>Initializes the view.</summary>
    public ColorSchemeKeysView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
    }

    private void BuildTree()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as ColorSchemePageViewModel;
        if (_bound is not null)
        {
            ColorSchemePageViewModel vm = _bound;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _columns = new LocalizedColumns<ThemeKeyNode>();
            _source = new HierarchicalTreeDataGridSource<ThemeKeyNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("ColorScheme_KeysColumn", "ThemeKeyNameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => n.Children,
                        n => n.IsGroup,
                        n => n.IsExpanded),
                },
            };
            _selection = new TreeSelectionSync<ThemeKeyNode>(_source, vm.Roots, n => n.Children, () => vm.Selected, n => vm.Selected = n);
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColorSchemePageViewModel.Selected))
        {
            _selection?.SelectFromViewModel();
        }
    }
}
