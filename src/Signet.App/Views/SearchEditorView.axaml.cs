using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// "Saved Searches" panel view — a tree (Name, Find, Replace, Controls, edited in place) over
/// <see cref="SearchEditorViewModel"/>. Synchronizes the selection, handles double-click (Load Search) and the
/// import/export file pickers.
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> built here (a TreeDataGrid source is bound to the UI thread) over the
/// visible nodes (<see cref="VisibleItemsView{T}"/> — the filter sets <see cref="SearchEntryNodeViewModel.IsVisible"/>);
/// its multi-selection and the view model's selected nodes follow each other.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class SearchEditorView : UserControl
{
    private static FilePickerFileType JsonType => new(Strings.Get("SearchEditor_JsonFileType"))
    {
        Patterns = new[] { "*.json" },
    };

    private static FilePickerFileType TsvType => new(Strings.Get("SearchEditor_TsvFileType"))
    {
        Patterns = new[] { "*.txt" },
    };

    private static readonly FilePickerFileType CsvType = new("CSV (*.csv)")
    {
        Patterns = new[] { "*.csv" },
    };

    private SearchEditorViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<SearchEntryNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<SearchEntryNodeViewModel>? _source;
    private TreeMultiSelectionSync<SearchEntryNodeViewModel>? _selection;

    /// <summary>Initializes the view.</summary>
    public SearchEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.DoubleTapped += OnDoubleTapped;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportRequested -= OnImportRequested;
            _bound.ExportRequested -= OnExportRequested;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as SearchEditorViewModel;

        if (_bound is not null)
        {
            _bound.ImportRequested += OnImportRequested;
            _bound.ExportRequested += OnExportRequested;
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            SearchEditorViewModel vm = _bound;
            _columns = new LocalizedColumns<SearchEntryNodeViewModel>();
            VisibleItemsView<SearchEntryNodeViewModel> roots = Visible(vm.Nodes);
            _source = new HierarchicalTreeDataGridSource<SearchEntryNodeViewModel>(roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("ReportsWindow_Name", "NameCellTemplate", new GridLength(200)),
                        n => Visible(n.Children),
                        n => n.Children.Count > 0,
                        n => n.IsExpanded),
                    _columns.Template("SearchEditorView_ColumnFind", "FindCellTemplate", new GridLength(1, GridUnitType.Star)),
                    _columns.Template("FindReplaceView_ReplaceButton", "ReplaceCellTemplate", new GridLength(1, GridUnitType.Star)),
                    _columns.Template("SearchEditorView_Controls", "ControlsCellTemplate", new GridLength(110)),
                },
            };
            _selection = new TreeMultiSelectionSync<SearchEntryNodeViewModel>(
                _source, roots, n => Visible(n.Children), () => vm.SelectedNodes, vm.SetSelectedNodes,
                n => Contains(vm.Nodes, n));
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    // The visible nodes of a level (the filter's result).
    private static VisibleItemsView<SearchEntryNodeViewModel> Visible(ObservableCollection<SearchEntryNodeViewModel> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(SearchEntryNodeViewModel.IsVisible));

    // Whether a node is in the tree (also when the filter hides it).
    private static bool Contains(IEnumerable<SearchEntryNodeViewModel> nodes, SearchEntryNodeViewModel node) =>
        nodes.Any(n => ReferenceEquals(n, node) || Contains(n.Children, node));

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchEditorViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound?.LoadSearchCommand.CanExecute(null) == true)
        {
            _bound.LoadSearchCommand.Execute(null);
        }
    }

    private async void OnImportRequested(object? sender, EventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _bound is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = Strings.Get("SearchEditor_ImportTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { JsonType, TsvType, CsvType },
            });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            _bound.ImportFile(path);
        }
    }

    private async void OnExportRequested(object? sender, bool all)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _bound is null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = all ? Strings.Get("SearchEditor_ExportAllTitle") : Strings.Get("SearchEditor_ExportTitle"),
                DefaultExtension = "json",
                SuggestedFileName = "saved_searches.json",
                FileTypeChoices = new[] { JsonType, TsvType, CsvType },
            });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            _bound.ExportFile(path, all);
        }
    }
}
