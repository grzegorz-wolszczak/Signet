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
/// The modal "Change Footnote Placement" dialog (<see cref="FootnotePlacementViewModel"/>): the footnotes of Signet of the book
/// as a list with check boxes and the kind (local or global), a filter for each kind and the file of the global notes. "Change"
/// moves the checked footnotes to the other placement and closes the dialog; a double click opens the reference in the main window.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The list source is disposed when the list is rebuilt and when the window closes, like in the other TreeDataGrid views.")]
public partial class FootnotePlacementWindow : Window
{
    private FootnotePlacementViewModel? _bound;
    private LocalizedColumns<FootnotePlacementRow>? _columns;
    private FlatTreeDataGridSource<FootnotePlacementRow>? _source;

    /// <summary>Initializes the window.</summary>
    public FootnotePlacementWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) =>
        {
            Unbind();
            List.Source = null;
            _source?.Dispose();
            _source = null;
        };
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>.</summary>
    public static Task ShowAsync(Window owner, FootnotePlacementViewModel viewModel) =>
        new FootnotePlacementWindow { DataContext = viewModel }.ShowDialog(owner);

    private void Bind()
    {
        Unbind();
        _bound = DataContext as FootnotePlacementViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnPropertyChanged;
            _bound.Done += OnDone;
        }

        BuildList();
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnPropertyChanged;
            _bound.Done -= OnDone;
            _bound = null;
        }
    }

    private void OnDone(object? sender, EventArgs e) => Close();

    // The filter gives another list of rows: the source is made again.
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FootnotePlacementViewModel.Rows))
        {
            BuildList();
        }
    }

    // The source of the list (TreeDataGrid sources belong to the views).
    private void BuildList()
    {
        FlatTreeDataGridSource<FootnotePlacementRow>? previous = _source;
        _source = null;
        if (_bound is { } vm)
        {
            _columns ??= new LocalizedColumns<FootnotePlacementRow>();
            _source = new FlatTreeDataGridSource<FootnotePlacementRow>(vm.Rows)
            {
                Columns =
                {
                    _columns.Template("FootnotePlacementWindow_ColumnFootnote", "FootnotePlacementMarkerCellTemplate", new GridLength(110)),
                    _columns.Template("FootnotePlacementWindow_ColumnKind", "FootnotePlacementKindCellTemplate", new GridLength(80)),
                    _columns.Template("FootnotePlacementWindow_ColumnText", "FootnotePlacementTextCellTemplate", new GridLength(1, GridUnitType.Star)),
                    _columns.Template("FootnotePlacementWindow_ColumnFile", "FootnotePlacementFileCellTemplate", new GridLength(180)),
                    _columns.Template("FootnotePlacementWindow_ColumnRemark", "FootnotePlacementRemarkCellTemplate", new GridLength(230)),
                },
            };
        }

        List.Source = _source;
        previous?.Dispose();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FootnotePlacementRow row })
        {
            row.Open();
            e.Handled = true;
        }
    }
}
