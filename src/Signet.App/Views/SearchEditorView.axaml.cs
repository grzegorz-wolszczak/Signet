using System.Collections.Generic;
using System.Linq;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Signet.App.Resources;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Saved Searches" panel view — a <c>TreeView</c> over <see cref="SearchEditorViewModel"/>.
/// Synchronizes the selection, handles double-click (Load Search) and the import/export file pickers.
/// </summary>
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

    /// <summary>Initializes the view.</summary>
    public SearchEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.SelectionChanged += OnSelectionChanged;
        Tree.DoubleTapped += OnDoubleTapped;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportRequested -= OnImportRequested;
            _bound.ExportRequested -= OnExportRequested;
        }

        _bound = DataContext as SearchEditorViewModel;

        if (_bound is not null)
        {
            _bound.ImportRequested += OnImportRequested;
            _bound.ExportRequested += OnExportRequested;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        IEnumerable<SearchEntryNodeViewModel> selected =
            Tree.SelectedItems?.OfType<SearchEntryNodeViewModel>() ?? Enumerable.Empty<SearchEntryNodeViewModel>();
        _bound?.SetSelectedNodes(selected);
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
