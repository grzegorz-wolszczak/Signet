using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The modal "Convert Footnotes of Other Origin" dialog (<see cref="FootnoteConversionViewModel"/>): the footnotes of
/// other origin as a tree (files, footnotes, their other links) with checkboxes, the details of the selected row, the
/// options and the notes file of the global ones. "Convert" converts the checked ones and the dialog stays open with
/// the footnotes left; "Close" closes it. A double click in the reference or the note column (or "Go to" in the
/// details) opens that place in the main window behind the dialog.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the tree is rebuilt and when the window closes, like in the other TreeDataGrid views.")]
public partial class FootnoteConversionWindow : Window
{
    private FootnoteConversionViewModel? _bound;
    private LocalizedColumns<FootnoteConversionNode>? _columns;
    private HierarchicalTreeDataGridSource<FootnoteConversionNode>? _source;
    private TreeSelectionSync<FootnoteConversionNode>? _selection;

    /// <summary>Initializes the window.</summary>
    public FootnoteConversionWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) =>
        {
            Unbind();
            Tree.Source = null;
            _source?.Dispose();
            _source = null;
        };
    }

    /// <summary>The height of the details under the tree (pixels); the splitter above them changes it.</summary>
    public double DetailsHeight
    {
        get => Split.RowDefinitions[2].Height.Value;
        set => Split.RowDefinitions[2].Height = new GridLength(Math.Max(Split.RowDefinitions[2].MinHeight, value));
    }

    /// <summary>
    /// Asks whether to convert although the checkpoint before the conversion could not be created (then a failed
    /// conversion cannot be undone).
    /// </summary>
    public static async Task<bool> ConfirmWithoutCheckpointAsync(Window owner) =>
        await ChoiceWindow.AskAsync(
            owner,
            Strings.Get("FootnoteConversionWindow_Title"),
            Strings.Get("FootnoteConversion_NoCheckpoint"),
            Strings.Get("FootnoteConversion_ConvertAnyway"),
            Strings.Get("Common_Cancel")) == 0;

    private void Bind()
    {
        Unbind();
        _bound = DataContext as FootnoteConversionViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnViewModelPropertyChanged;
        }

        BuildTree();
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _bound = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FootnoteConversionViewModel.Roots))
        {
            BuildTree();
        }
        else if (e.PropertyName == nameof(FootnoteConversionViewModel.Selected))
        {
            _selection?.SelectFromViewModel();
        }
    }

    // The source of the tree (TreeDataGrid sources belong to the views), built again when the filter changes the rows.
    private void BuildTree()
    {
        HierarchicalTreeDataGridSource<FootnoteConversionNode>? previous = _source;
        _source = null;
        _selection = null;
        if (_bound is { } vm)
        {
            _columns ??= new LocalizedColumns<FootnoteConversionNode>();
            _source = new HierarchicalTreeDataGridSource<FootnoteConversionNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("FootnoteConversionWindow_ColumnFootnote", "FootnoteConversionTitleCellTemplate", new GridLength(300)),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Template("FootnoteConversionWindow_ColumnKind", "FootnoteConversionKindCellTemplate", new GridLength(80)),
                    _columns.Template("FootnoteConversionWindow_ColumnReference", "FootnoteConversionReferenceCellTemplate", new GridLength(1, GridUnitType.Star)),
                    _columns.Template("FootnoteConversionWindow_ColumnNote", "FootnoteConversionNoteCellTemplate", new GridLength(1, GridUnitType.Star)),
                },
            };
            _selection = new TreeSelectionSync<FootnoteConversionNode>(_source, vm.Roots, n => n.Children, () => vm.Selected, n => vm.Selected = n);
        }

        Tree.Source = _source;
        previous?.Dispose();
        _selection?.SelectFromViewModel();
    }

    private void OnReferenceDoubleTapped(object? sender, TappedEventArgs e) => Navigate(sender, e, n => n.GoToReference());

    private void OnNoteDoubleTapped(object? sender, TappedEventArgs e) => Navigate(sender, e, n => n.GoToNote());

    private static void Navigate(object? sender, TappedEventArgs e, Action<FootnoteConversionNode> go)
    {
        if (sender is Control { DataContext: FootnoteConversionNode node })
        {
            go(node);
            e.Handled = true;
        }
    }
}
