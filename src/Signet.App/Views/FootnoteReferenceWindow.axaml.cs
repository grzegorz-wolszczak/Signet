using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Footnotes;

namespace Signet.App.Views;

/// <summary>
/// "Add Reference to Footnote": chooses the note a new reference at the caret leads to
/// (<see cref="FootnoteReferenceViewModel"/>). The table's source and columns are built here (a TreeDataGrid source is
/// bound to the UI thread); a double click or Enter on a row accepts it.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes or the window closes.")]
public partial class FootnoteReferenceWindow : Window
{
    private FootnoteReferenceViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<FootnoteReferenceRow>? _columns;
    private FlatTreeDataGridSource<FootnoteReferenceRow>? _source;
    private bool _selecting;

    /// <summary>Initializes the window.</summary>
    public FootnoteReferenceWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as FootnoteReferenceViewModel);
        NotesGrid.DoubleTapped += (_, _) => Accept();
        NotesGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        };
        Opened += (_, _) => FilterBox.Focus();
        Closed += (_, _) => Bind(null);
    }

    /// <summary>Shows the window; returns the chosen note (its first reference), or <c>null</c>.</summary>
    public static Task<Footnote?> AskAsync(Window owner, FootnoteReferenceViewModel viewModel)
    {
        FootnoteReferenceWindow window = new() { DataContext = viewModel };
        return window.ShowDialog<Footnote?>(owner);
    }

    private void Bind(FootnoteReferenceViewModel? viewModel)
    {
        if (_source is not null)
        {
            _source.RowSelection!.SelectionChanged -= OnSelectionChanged;
            _source.Dispose();
            _source = null;
        }

        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _bound = viewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            _columns = new LocalizedColumns<FootnoteReferenceRow>();
            _source = new FlatTreeDataGridSource<FootnoteReferenceRow>(_bound.Visible)
            {
                Columns =
                {
                    _columns.Text("FootnoteReferenceWindow_Number", r => r.Number, new GridLength(70)),
                    _columns.Text("FootnoteReferenceWindow_Kind", r => r.Kind, new GridLength(90)),
                    _columns.Text("FootnoteReferenceWindow_File", r => r.File, new GridLength(150)),
                    _columns.Text("FootnoteReferenceWindow_Text", r => r.Text, new GridLength(1, GridUnitType.Star)),
                },
            };
            _source.RowSelection!.SelectionChanged += OnSelectionChanged;
        }

        NotesGrid.Source = _source;
        SelectFromViewModel();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (!_selecting && _bound is not null && _source?.RowSelection?.SelectedItem is { } row)
        {
            _bound.Selected = row;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FootnoteReferenceViewModel.Selected))
        {
            SelectFromViewModel();
        }
    }

    // Selects the view model's note in the table (after the filter rebuilt the rows, or at the start).
    private void SelectFromViewModel()
    {
        if (_bound is null || _source?.RowSelection is not { } selection)
        {
            return;
        }

        int index = _bound.Selected is { } row ? _bound.Visible.IndexOf(row) : -1;
        if (index == selection.SelectedIndex.FirstOrDefault(-1))
        {
            return;
        }

        _selecting = true;
        try
        {
            selection.Clear();
            if (index >= 0)
            {
                selection.Select(new IndexPath(index));
                NotesGrid.RowsPresenter?.BringIntoView(index);
            }
        }
        finally
        {
            _selecting = false;
        }
    }

    private void Accept()
    {
        if (_bound?.Selected is { } row)
        {
            Close(row.Target.Footnote);
        }
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
