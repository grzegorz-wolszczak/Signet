using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The modal "Hyphenation" dialog (<see cref="SoftHyphensViewModel"/>): the switch between adding and removing soft
/// hyphens, the tree of languages, files and places with their check boxes, the buttons that check or uncheck all, and
/// Add / Remove, which apply and leave the dialog open. A double click on a file or a place opens it in the main window
/// behind the dialog.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed before it is rebuilt and when the window closes.")]
public partial class SoftHyphensWindow : Window
{
    private HierarchicalTreeDataGridSource<SoftHyphensNode>? _source;
    private SoftHyphensViewModel? _viewModel;

    /// <summary>Initializes the window.</summary>
    public SoftHyphensWindow()
    {
        InitializeComponent();
        Tree.DoubleTapped += OnTreeDoubleTapped;
        Closed += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            DisposeTree();
        };
    }

    /// <summary>
    /// Shows the dialog over <paramref name="owner"/> and waits until it is closed. <paramref name="dictionaryManager"/>
    /// opens the dictionary manager over the dialog (it answers whether the dictionaries changed);
    /// <paramref name="created"/> gets the window before it is shown (the progress of its operations goes over it).
    /// </summary>
    public static async Task ShowAsync(
        Window owner, SoftHyphensViewModel viewModel, Func<Window, Task<bool>>? dictionaryManager = null, Action<SoftHyphensWindow>? created = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(viewModel);
        SoftHyphensWindow window = Create(viewModel);
        created?.Invoke(window);
        if (dictionaryManager is not null)
        {
            viewModel.DictionaryManager = () => dictionaryManager(window);
        }

        void OnCloseRequested(object? sender, EventArgs e) => window.Close();
        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.DictionaryManager = null;
        }
    }

    /// <summary>Creates the window for <paramref name="viewModel"/> (the tree is built right away).</summary>
    public static SoftHyphensWindow Create(SoftHyphensViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        SoftHyphensWindow window = new() { DataContext = viewModel };
        window.Attach(viewModel);
        return window;
    }

    private void Attach(SoftHyphensViewModel viewModel)
    {
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildTree();
    }

    // The other side of the switch has other rows and columns.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoftHyphensViewModel.Mode))
        {
            BuildTree();
        }
    }

    // The columns: the name (with the check box) under the expander, then the patterns (adding), where the language
    // comes from, the count, and the line around a place outside the text (removing).
    private void BuildTree()
    {
        DisposeTree();
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        LocalizedColumns<SoftHyphensNode> columns = new();
        _source = new HierarchicalTreeDataGridSource<SoftHyphensNode>(viewModel.Roots);
        _source.Columns.Add(columns.Expander(
            columns.Template("SoftHyphens_ColumnName", "SoftHyphensTitleCellTemplate", new GridLength(300)),
            node => node.Children,
            node => node.HasChildren,
            node => node.IsExpanded));
        if (viewModel.IsAddMode)
        {
            _source.Columns.Add(columns.Template("SoftHyphens_ColumnPatterns", "SoftHyphensPatternsCellTemplate", new GridLength(230)));
        }

        _source.Columns.Add(columns.Template("SoftHyphens_ColumnSource", "SoftHyphensSourceCellTemplate", new GridLength(220)));
        _source.Columns.Add(columns.Template(viewModel.CountHeaderKey, "SoftHyphensCountCellTemplate", new GridLength(120)));
        if (!viewModel.IsAddMode)
        {
            _source.Columns.Add(columns.Template("SoftHyphens_ColumnContext", "SoftHyphensContextCellTemplate", new GridLength(1, GridUnitType.Star)));
        }

        Tree.Source = _source;
    }

    private void DisposeTree()
    {
        Tree.Source = null;
        _source?.Dispose();
        _source = null;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is CheckBox || _source?.RowSelection?.SelectedItem is not { } node)
        {
            return;
        }

        if (node.Navigate())
        {
            e.Handled = true;
        }
        else if (node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            e.Handled = true;
        }
    }
}
