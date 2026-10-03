using System.IO;
using System.Text;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Parsers;
using Signet.Core.Reports;

namespace Signet.App.Views;

/// <summary>
/// The non-modal "Reports" dialog.
/// Unlike most other dialogs, this window is non-modal (<c>Show</c>, not
/// <c>ShowDialog</c>) — MainWindow keeps a single instance and on every open replaces its
/// <c>DataContext</c> with a freshly computed <see cref="ReportsViewModel"/>.
/// </summary>
public partial class ReportsWindow : Window
{
    private static readonly FilePickerFileType CsvType = new("CSV") { Patterns = new[] { "*.csv" } };

    private ReportsViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public ReportsWindow()
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

        _bound = DataContext as ReportsViewModel;
        if (_bound is not null)
        {
            _bound.ExportCsvRequested += OnExportCsvRequested;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnExportCsvRequested(object? sender, int tabIndex)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not ReportsViewModel viewModel)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("ReportsWindow_ExportCsvTitle"),
            DefaultExtension = "csv",
            SuggestedFileName = "report.csv",
            FileTypeChoices = new[] { CsvType },
        });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, viewModel.BuildCsv(tabIndex), Encoding.UTF8);
        }
    }

    private void OnAllFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && AllFilesGrid.SelectedItem is AllFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnHtmlFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && HtmlFilesGrid.SelectedItem is HtmlFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnImageFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && ImageFilesGrid.SelectedItem is ImageFilesDisplayRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnCssFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && CssFilesGrid.SelectedItem is CssFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnClassesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && ClassesGrid.SelectedItem is HtmlClassUsageRow row)
        {
            vm.NavigateToOffset(row.HtmlBookPath, row.Position);
        }
    }

    private void OnStylesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && StylesGrid.SelectedItem is CssSelectorUsage row)
        {
            vm.NavigateToOffset(row.CssBookPath, row.Position);
        }
    }

    private void OnLinksDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && LinksGrid.SelectedItem is LinkRow row)
        {
            vm.NavigateToOffset(row.HtmlBookPath, row.Position);
        }
    }

    private void OnWordCountsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && WordCountsGrid.SelectedItem is FileWordCountRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }
}
