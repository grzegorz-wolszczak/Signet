using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Core.Fonts;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// The modal "Manage Fonts" dialog (<see cref="ManageFontsViewModel"/>): the font files and the families of the
/// book in tables built here (a TreeDataGrid source is bound to the UI thread). The actions that change the book open
/// their dialogs over this one and the tables are read again afterwards; a double click on a place opens it in the
/// main window behind the dialog.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table sources are disposed when the window closes; they live as long as the window and the "
        + "view model's collections they observe.")]
public partial class ManageFontsWindow : Window
{
    private readonly List<IDisposable> _sources = new();

    // The column factories keep the headers in the current UI language (held weakly by Strings).
    private readonly List<object> _columnFactories = new();

    private ManageFontsViewModel? _bound;
    private IFontManagementHost? _host;
    private FlatTreeDataGridSource<FontFileRow>? _files;
    private FlatTreeDataGridSource<FontFamilyRow>? _families;
    private FlatTreeDataGridSource<FontLocationRow>? _details;
    private bool _syncingFamily;

    /// <summary>Initializes the window.</summary>
    public ManageFontsWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => Detach();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and waits until it is closed.</summary>
    public static async Task ShowAsync(Window owner, ManageFontsViewModel viewModel, IFontManagementHost host)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(host);
        await Create(viewModel, host).ShowDialog(owner);
    }

    /// <summary>Creates the window for <paramref name="viewModel"/> (the tables are built right away).</summary>
    public static ManageFontsWindow Create(ManageFontsViewModel viewModel, IFontManagementHost host)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(host);
        ManageFontsWindow window = new() { _host = host };
        window.Attach(viewModel);
        return window;
    }

    private void Attach(ManageFontsViewModel vm)
    {
        _bound = vm;
        DataContext = vm;
        vm.AddFontsRequested += OnAddFontsRequested;
        vm.CreateRulesRequested += OnCreateRulesRequested;
        vm.DeleteRequested += OnDeleteRequested;
        vm.RenameFamilyRequested += OnRenameFamilyRequested;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        GridLength star = new(1, GridUnitType.Star);
        _files = Table(FilesGrid, vm.Files, (c, cols) =>
        {
            cols.Add(c.Text("ManageFonts_ColumnFile", r => r.FileName, star));
            cols.Add(c.Text("ManageFonts_ColumnFamily", r => r.Family, new GridLength(190)));
            cols.Add(c.Text("ManageFonts_ColumnFace", r => r.Face, new GridLength(110)));
            cols.Add(c.Text("ManageFonts_ColumnFormat", r => r.Format, new GridLength(120)));
            cols.Add(c.Text("ManageFonts_ColumnSize", r => r.SizeText, new GridLength(80)));
            cols.Add(c.Text("ManageFonts_ColumnRules", r => r.RuleCount, new GridLength(90)));
            cols.Add(c.Text("ManageFonts_ColumnStatus", r => r.StatusText, new GridLength(260)));
        });
        _files.RowSelection!.SingleSelect = false;
        _files.RowSelection.SelectionChanged += OnFilesSelectionChanged;

        _families = Table(FamiliesGrid, vm.Families, (c, cols) =>
        {
            cols.Add(c.Text("ManageFonts_ColumnFamily", r => r.Family, star));
            cols.Add(c.Text("ManageFonts_ColumnFaces", r => r.Faces, new GridLength(240)));
            cols.Add(c.Text("ManageFonts_ColumnDeclarations", r => r.Declarations, new GridLength(110)));
            cols.Add(c.Text("ManageFonts_ColumnElements", r => r.Elements, new GridLength(100)));
            cols.Add(c.Text("ManageFonts_ColumnStatus", r => r.StatusText, new GridLength(300)));
        });
        _families.RowSelection!.SelectionChanged += OnFamiliesSelectionChanged;

        _details = Table(DetailsGrid, vm.Details, (c, cols) =>
        {
            cols.Add(c.Text("ManageFonts_ColumnKind", r => r.Kind, new GridLength(130)));
            cols.Add(c.Text("ManageFonts_ColumnPlace", r => r.Text, star));
            cols.Add(c.Text("ManageFonts_ColumnFile", r => r.FileText, new GridLength(180)));
        });
        SelectFamilyInGrid();
    }

    private void Detach()
    {
        if (_bound is not null)
        {
            _bound.AddFontsRequested -= OnAddFontsRequested;
            _bound.CreateRulesRequested -= OnCreateRulesRequested;
            _bound.DeleteRequested -= OnDeleteRequested;
            _bound.RenameFamilyRequested -= OnRenameFamilyRequested;
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

    private void OnFilesSelectionChanged(object? sender, EventArgs e)
    {
        if (_bound is not null && _files is not null)
        {
            _bound.SelectedFiles = _files.RowSelection!.SelectedItems.OfType<FontFileRow>().ToList();
        }
    }

    private void OnFamiliesSelectionChanged(object? sender, EventArgs e)
    {
        if (_bound is not null && !_syncingFamily && _families?.RowSelection?.SelectedItem is FontFamilyRow row)
        {
            _bound.SelectedFamily = row;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManageFontsViewModel.SelectedFamily))
        {
            SelectFamilyInGrid();
        }
    }

    // The family selected in the view model (after a refresh) is selected in the table too.
    private void SelectFamilyInGrid()
    {
        if (_bound is null || _families?.RowSelection is not { } selection)
        {
            return;
        }

        int index = _bound.SelectedFamily is { } family ? _bound.Families.IndexOf(family) : -1;
        if (selection.SelectedIndex.Count == 1 && selection.SelectedIndex[0] == index)
        {
            return;
        }

        _syncingFamily = true;
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
            _syncingFamily = false;
        }
    }

    private void OnFilesDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_bound?.OpenFilesCommand.CanExecute(null) == true)
        {
            _bound.OpenFilesCommand.Execute(null);
        }
    }

    private void OnDetailsDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_bound is not null && _details?.RowSelection?.SelectedItem is FontLocationRow row)
        {
            _bound.Navigate(row);
        }
    }

    private async void OnAddFontsRequested(object? sender, EventArgs e)
    {
        if (_host is not null && await FontDialogs.PickAndAddFontsAsync(this, _host))
        {
            _bound?.Refresh();
        }
    }

    private async void OnCreateRulesRequested(object? sender, IReadOnlyList<FontResource> fonts)
    {
        if (_host is not null && await FontDialogs.OfferFontFacesAsync(this, _host, fonts))
        {
            _bound?.Refresh();
        }
    }

    private async void OnDeleteRequested(object? sender, IReadOnlyList<FontResource> fonts)
    {
        if (_host is not null && await FontDialogs.DeleteAsync(this, _host, fonts.Cast<Resource>().ToList()))
        {
            _bound?.Refresh();
        }
    }

    private async void OnRenameFamilyRequested(object? sender, string family)
    {
        if (_host is null || _bound is null)
        {
            return;
        }

        string? newName = await TextPromptWindow.AskAsync(
            this, Strings.Get("ManageFonts_RenameTitle"), Strings.Format("ManageFonts_RenamePrompt", family), family);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        // Renaming to a family that already exists merges the two — confirmed separately.
        string newKey = FontFaceDescriptors.FamilyKey(newName);
        if (!string.Equals(newKey, FontFaceDescriptors.FamilyKey(family), StringComparison.Ordinal)
            && _bound.Families.Any(f => f.Report.IsEmbedded && string.Equals(f.Report.Key, newKey, StringComparison.Ordinal))
            && !await ConfirmWindow.AskAsync(
                this, Strings.Get("ManageFonts_RenameTitle"), Strings.Format("ManageFonts_RenameMerge", family, newName.Trim())))
        {
            return;
        }

        if (await _host.RenameFontFamilyAsync(family, newName))
        {
            _bound.Refresh();
        }
    }
}
