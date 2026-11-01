using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Signet.App.Resources;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// Modeless "Spellcheck Editor" window. Like <see cref="ReportsWindow"/>, MainWindow keeps a single
/// window instance AND a single persistent <see cref="SpellcheckEditorViewModel"/> instance (not
/// rebuilt on every open — see the remarks in that class), so this window's <c>DataContext</c>
/// does not change.
/// </summary>
/// <remarks>
/// The words table itself is <see cref="SpellcheckWordsView"/> — the same view the preview of
/// Preferences › Appearance › Spelling Editor shows. What is left here belongs to the window: the buttons beside the
/// table and writing the CSV the view model builds.
/// </remarks>
public partial class SpellcheckEditorWindow : Window
{
    private static readonly FilePickerFileType CsvType = new("CSV") { Patterns = new[] { "*.csv" } };

    private SpellcheckEditorViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public SpellcheckEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ExportCsvRequested -= OnExportCsvRequested;
        }

        _bound = DataContext as SpellcheckEditorViewModel;
        if (_bound is not null)
        {
            _bound.ExportCsvRequested += OnExportCsvRequested;
        }
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
}
