using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// Modeless "Spellcheck Editor" window. Like <see cref="ReportsWindow"/>, MainWindow keeps a single
/// window instance AND a single persistent <see cref="SpellcheckEditorViewModel"/> instance (not
/// rebuilt on every open — see the remarks in that class), so this window's <c>DataContext</c>
/// does not change.
/// </summary>
/// <remarks>
/// The words table is a <see cref="TreeDataGrid"/> whose source and columns are built here, over the view model's
/// <see cref="SpellcheckEditorViewModel.Words"/> (a TreeDataGrid source is bound to the UI thread, so it does not
/// belong in the view model). Several rows can be selected; the selection is passed to
/// <see cref="SpellcheckEditorViewModel.SetSelectedWords"/>.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes; the current one lives as long as the "
        + "window and the view model's Words it observes.")]
public partial class SpellcheckEditorWindow : Window
{
    private static readonly FilePickerFileType CsvType = new("CSV") { Patterns = new[] { "*.csv" } };

    private SpellcheckEditorViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<SpellcheckWordRow>? _columns;
    private FlatTreeDataGridSource<SpellcheckWordRow>? _source;

    /// <summary>Initializes the window.</summary>
    public SpellcheckEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        WordsGrid.DoubleTapped += OnWordDoubleTapped;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_source is not null)
        {
            _source.RowSelection!.SelectionChanged -= OnSelectionChanged;
            _source.Dispose();
            _source = null;
        }

        if (_bound is not null)
        {
            _bound.SelectRowRequested -= OnSelectRowRequested;
            _bound.ExportCsvRequested -= OnExportCsvRequested;
        }

        _bound = DataContext as SpellcheckEditorViewModel;
        if (_bound is not null)
        {
            _bound.SelectRowRequested += OnSelectRowRequested;
            _bound.ExportCsvRequested += OnExportCsvRequested;
            _columns = new LocalizedColumns<SpellcheckWordRow>();
            _source = new FlatTreeDataGridSource<SpellcheckWordRow>(_bound.Words)
            {
                Columns =
                {
                    _columns.Text("SpellcheckEditorWindow_Word", r => r.Word, new GridLength(1, GridUnitType.Star)),
                    _columns.Text("ReportsWindow_Count", r => r.Count, new GridLength(70)),
                    _columns.Text("SpellcheckEditorWindow_Language", r => r.LanguageName, new GridLength(110)),
                    _columns.Text("SpellcheckEditorWindow_Misspelled", r => r.MisspelledDisplay, new GridLength(90)),
                },
            };
            _source.RowSelection!.SingleSelect = false;
            _source.RowSelection.SelectionChanged += OnSelectionChanged;
        }

        WordsGrid.Source = _source;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnExportCsvRequested(object? sender, EventArgs e)
    {
        if (_bound is not { } vm)
        {
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("SpellcheckEditorWindow_ExportCsvTitle"),
            DefaultExtension = "csv",
            SuggestedFileName = "words.csv",
            FileTypeChoices = new[] { CsvType },
        });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, vm.BuildCsv(), Encoding.UTF8);
        }
    }

    private void OnWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound is not null && _source?.RowSelection?.SelectedItem is { } row)
        {
            _bound.RequestNavigation(row);
        }
    }

    private void OnSelectRowRequested(object? sender, int index)
    {
        if (_source?.RowSelection is not { } selection)
        {
            return;
        }

        selection.Clear();
        selection.Select(new IndexPath(index));
        WordsGrid.RowsPresenter?.BringIntoView(index);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_source is not null)
        {
            _bound?.SetSelectedWords(_source.RowSelection!.SelectedItems.OfType<SpellcheckWordRow>());
        }
    }
}
