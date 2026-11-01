using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Views;

/// <summary>
/// The modal "Dictionary manager" window (<see cref="DictionaryManagerViewModel"/>): the installed Hunspell
/// dictionaries and the files of the selected one, in tables built here (a TreeDataGrid source is bound to the UI
/// thread). Adding a dictionary opens a multi-select file picker; deleting one asks first and lists the files it will
/// remove.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table sources are disposed when the window closes; they live as long as the window and the "
        + "view model's collections they observe.")]
public partial class DictionaryManagerWindow : Window
{
    private readonly List<IDisposable> _sources = new();

    // The column factories keep the headers in the current UI language (held weakly by Strings).
    private readonly List<object> _columnFactories = new();

    private DictionaryManagerViewModel? _bound;
    private FlatTreeDataGridSource<DictionaryRow>? _dictionaries;
    private bool _syncingSelection;

    /// <summary>Initializes the window.</summary>
    public DictionaryManagerWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => Detach();
    }

    /// <summary>
    /// Shows the manager over <paramref name="owner"/> and waits until it is closed; returns whether a dictionary was
    /// installed or deleted (the caller rebuilds what lists the dictionaries then).
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner, DictionaryManagerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        await Create(viewModel).ShowDialog(owner);
        return viewModel.Changed;
    }

    /// <summary>Creates the window for <paramref name="viewModel"/> (the tables are built right away).</summary>
    public static DictionaryManagerWindow Create(DictionaryManagerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DictionaryManagerWindow window = new();
        window.Attach(viewModel);
        return window;
    }

    private void Attach(DictionaryManagerViewModel vm)
    {
        _bound = vm;
        DataContext = vm;
        vm.AddFilesRequested = PickDictionaryFilesAsync;
        vm.ConfirmReplaceRequested = ConfirmReplaceAsync;
        vm.ConfirmDeleteRequested = ConfirmDeleteAsync;
        vm.ProblemReported = ReportProblemAsync;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        GridLength star = new(1, GridUnitType.Star);
        _dictionaries = Table(DictionariesGrid, vm.Dictionaries, (c, cols) =>
        {
            cols.Add(c.Text("DictionaryManager_ColumnName", r => r.DisplayName, new GridLength(180)));
            cols.Add(c.Text("DictionaryManager_ColumnFiles", r => r.FileCount, new GridLength(70)));
            cols.Add(c.Text("DictionaryManager_ColumnState", r => r.StateText, star));
        });
        _dictionaries.RowSelection!.SingleSelect = true;
        _dictionaries.RowSelection.SelectionChanged += OnDictionarySelectionChanged;

        Table(FilesGrid, vm.Files, (c, cols) =>
        {
            cols.Add(c.Text("DictionaryManager_ColumnFile", r => r.FileName, new GridLength(200)));
            cols.Add(c.Text("DictionaryManager_ColumnRole", r => r.RoleText, star));
        });
        SelectDictionaryInGrid();
    }

    private void Detach()
    {
        if (_bound is not null)
        {
            _bound.AddFilesRequested = null;
            _bound.ConfirmReplaceRequested = null;
            _bound.ConfirmDeleteRequested = null;
            _bound.ProblemReported = null;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        foreach (IDisposable source in _sources)
        {
            source.Dispose();
        }

        _sources.Clear();
    }

    private FlatTreeDataGridSource<T> Table<T>(
        TreeDataGrid grid,
        System.Collections.ObjectModel.ObservableCollection<T> rows,
        Action<LocalizedColumns<T>, ColumnList<T>> addColumns)
        where T : class
    {
        LocalizedColumns<T> columns = new();
        FlatTreeDataGridSource<T> source = new(rows);
        addColumns(columns, source.Columns);
        _columnFactories.Add(columns);
        _sources.Add(source);
        grid.Source = source;
        return source;
    }

    private void OnDictionarySelectionChanged(object? sender, EventArgs e)
    {
        if (_bound is not null && !_syncingSelection && _dictionaries?.RowSelection?.SelectedItem is DictionaryRow row)
        {
            _bound.SelectedDictionary = row;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DictionaryManagerViewModel.SelectedDictionary))
        {
            SelectDictionaryInGrid();
        }
    }

    // The dictionary selected in the view model (after a refresh) is selected in the table too.
    private void SelectDictionaryInGrid()
    {
        if (_bound is null || _dictionaries?.RowSelection is not { } selection)
        {
            return;
        }

        int index = _bound.SelectedDictionary is { } row ? _bound.Dictionaries.IndexOf(row) : -1;
        if (selection.SelectedIndex.Count == 1 && selection.SelectedIndex[0] == index)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            selection.Clear();
            if (index >= 0)
            {
                selection.Select(new IndexPath(index));
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // The files a dictionary package may be made of; everything else is refused by the installer anyway, and the
    // filter only saves the user from picking it.
    private static readonly string[] DictionaryFilePatterns =
    {
        "*.aff", "*.dic", "*.dat", "*.idx", "*.xcu", "*.tex", "*.txt",
    };

    private async Task<IReadOnlyList<string>> PickDictionaryFilesAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("DictionaryManager_AddTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Strings.Get("DictionaryManager_FileTypeName")) { Patterns = DictionaryFilePatterns },
                FilePickerFileTypes.All,
            },
        });
        return files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    private Task<bool> ConfirmReplaceAsync(string displayName) =>
        ConfirmWindow.AskAsync(
            this,
            Strings.Get("DictionaryManager_ReplaceTitle"),
            Strings.Format("DictionaryManager_ReplacePrompt", displayName),
            Strings.Get("DictionaryManager_Replace"),
            destructive: true);

    private Task<bool> ConfirmDeleteAsync(string displayName, IReadOnlyList<string> files) =>
        ConfirmWindow.AskAsync(
            this,
            Strings.Get("DictionaryManager_DeleteTitle"),
            Strings.Format("DictionaryManager_DeletePrompt", displayName, string.Join(Environment.NewLine, files)),
            Strings.Get("Common_Delete"),
            destructive: true);

    private Task ReportProblemAsync(string problem) =>
        MessageDialog.ShowAsync(this, Strings.Get("DictionaryManager_ProblemTitle"), problem);
}
