using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Core.Parsers;
using Signet.Core.Reports;

namespace Signet.App.Views;

/// <summary>
/// The non-modal "Reports" dialog.
/// Unlike most other dialogs, this window is non-modal (<c>Show</c>, not
/// <c>ShowDialog</c>) — MainWindow keeps a single instance and on every open replaces its
/// <c>DataContext</c> with a freshly computed <see cref="ReportsViewModel"/>.
/// </summary>
/// <remarks>
/// Every report is a <see cref="TreeDataGrid"/>; the sources and columns are built here for each new view model (a
/// TreeDataGrid source is bound to the UI thread) and the previous ones disposed.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The report sources are disposed when the data context changes; the current ones live as long as "
        + "the window and the view model's collections they observe.")]
public partial class ReportsWindow : Window
{
    private static readonly FilePickerFileType CsvType = new("CSV") { Patterns = new[] { "*.csv" } };

    private readonly List<IDisposable> _sources = new();

    // The column factories keep the headers in the current UI language (held weakly by Strings).
    private readonly List<object> _columnFactories = new();

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

        BuildTables(_bound);
    }

    // The same columns (headers, widths) as the former DataGrids. Without a view model the grids are emptied.
    private void BuildTables(ReportsViewModel? vm)
    {
        foreach (IDisposable source in _sources)
        {
            source.Dispose();
        }

        _sources.Clear();
        _columnFactories.Clear();
        GridLength star = new(1, GridUnitType.Star);

        Table(AllFilesGrid, vm?.AllFiles, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_Name", r => r.Name, star));
            cols.Add(c.Text("ReportsWindow_Type", r => r.TypeName, new GridLength(110)));
            cols.Add(c.Text("ReportsWindow_SizeBytes", r => r.SizeBytes, new GridLength(120)));
            cols.Add(c.CheckBox("ReportsWindow_InSpine", r => r.InSpine, width: new GridLength(90)));
        });
        Table(HtmlFilesGrid, vm?.HtmlFiles, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_Name", r => r.Name, star));
            cols.Add(c.Text("ReportsWindow_SizeBytes", r => r.SizeBytes, new GridLength(120)));
            cols.Add(c.Text("ReportsWindow_AllWords", r => r.WordCount, new GridLength(100)));
            cols.Add(c.CheckBox("ReportsWindow_WellFormed", r => r.WellFormed, width: new GridLength(100)));
        });
        Table(ImageFilesGrid, vm?.ImageFiles, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_Name", r => r.Name, star));
            cols.Add(c.Text("ReportsWindow_Format", r => r.Format, new GridLength(80)));
            cols.Add(c.Text("ReportsWindow_SizeBytes", r => r.SizeBytes, new GridLength(110)));
            cols.Add(c.Text("ImageResizeWindow_Width", r => r.Width, new GridLength(70)));
            cols.Add(c.Text("ImageResizeWindow_Height", r => r.Height, new GridLength(70)));
            cols.Add(c.Text("ReportsWindow_UsedIn", r => r.UsedInDisplay, new GridLength(200)));
        });
        Table(CssFilesGrid, vm?.CssFiles, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_Name", r => r.Name, star));
            cols.Add(c.Text("ReportsWindow_SizeBytes", r => r.SizeBytes, new GridLength(120)));
            cols.Add(c.Text("ReportsWindow_SelectorCount", r => r.SelectorCount, new GridLength(120)));
        });
        Table(ClassesGrid, vm?.Classes, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_HtmlFile", r => r.HtmlBookPath, star));
            cols.Add(c.Text("ReportsWindow_Element", r => r.ElementName, new GridLength(90)));
            cols.Add(c.Text("ReportsWindow_Class", r => r.ClassName, new GridLength(140)));
            cols.Add(c.Text("ReportsWindow_MatchedSelector", r => r.SelectorText, new GridLength(180)));
            cols.Add(c.CheckBox("ReportsWindow_UsedInCss", r => r.IsUsed, width: new GridLength(100)));
        });
        Table(StylesGrid, vm?.Styles, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_CssFile", r => r.CssBookPath, star));
            cols.Add(c.Text("ReportsWindow_CssSelector", r => r.SelectorText, new GridLength(220)));
            cols.Add(c.Text("ReportsWindow_UsedInHtmlFile", r => r.UsedInHtmlBookPath, new GridLength(220)));
        });
        Table(LinksGrid, vm?.Links, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_File", r => r.HtmlBookPath, star));
            cols.Add(c.Text("ReportsWindow_Text", r => r.Text, new GridLength(160)));
            cols.Add(c.Text("ReportsWindow_Target", r => r.TargetHref, new GridLength(220)));
            cols.Add(c.CheckBox("ReportsWindow_Internal", r => r.IsInternal, width: new GridLength(80)));
            cols.Add(c.ThreeStateCheckBox("ReportsWindow_TargetExists", r => r.TargetExists, new GridLength(110)));
        });
        Table(CharactersGrid, vm?.Characters, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_Character", r => r.Character, new GridLength(80)));
            // The decimal code is shown as text but sorted by its value.
            cols.Add(c.Text("ReportsWindow_Decimal", r => r.DecimalValue, new GridLength(80),
                new TextColumnOptions<CharacterDisplayRow>().SortedBy(r => r.CodePoint)));
            cols.Add(c.Text("ReportsWindow_Hexadecimal", r => r.Hexadecimal, new GridLength(90)));
            cols.Add(c.Text("ReportsWindow_Name", r => r.EntityName, star));
            cols.Add(c.Text("ReportsWindow_Count", r => r.Count, new GridLength(80)));
            cols.Add(c.Text("ReportsWindow_FoundIn", r => r.FoundInDisplay, new GridLength(220)));
        });
        Table(WordCountsGrid, vm?.WordCounts, (c, cols) =>
        {
            cols.Add(c.Text("ReportsWindow_File", r => r.BookPath, star));
            cols.Add(c.Text("ReportsWindow_Words", r => r.Words, new GridLength(100)));
            cols.Add(c.Text("ReportsWindow_Characters", r => r.Characters, new GridLength(100)));
        });
    }

    private void Table<T>(
        TreeDataGrid grid,
        ObservableCollection<T>? rows,
        Action<LocalizedColumns<T>, ColumnList<T>> addColumns)
        where T : class
    {
        if (rows is null)
        {
            grid.Source = null;
            return;
        }

        LocalizedColumns<T> columns = new();
        FlatTreeDataGridSource<T> source = new(rows);
        addColumns(columns, source.Columns);
        _columnFactories.Add(columns);
        _sources.Add(source);
        grid.Source = source;
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
        if (DataContext is ReportsViewModel vm && AllFilesGrid.RowSelection?.SelectedItem is AllFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnHtmlFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && HtmlFilesGrid.RowSelection?.SelectedItem is HtmlFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnImageFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && ImageFilesGrid.RowSelection?.SelectedItem is ImageFilesDisplayRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnCssFilesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && CssFilesGrid.RowSelection?.SelectedItem is CssFilesRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }

    private void OnClassesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && ClassesGrid.RowSelection?.SelectedItem is HtmlClassUsageRow row)
        {
            vm.NavigateToOffset(row.HtmlBookPath, row.Position);
        }
    }

    private void OnStylesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && StylesGrid.RowSelection?.SelectedItem is CssSelectorUsage row)
        {
            vm.NavigateToOffset(row.CssBookPath, row.Position);
        }
    }

    private void OnLinksDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && LinksGrid.RowSelection?.SelectedItem is LinkRow row)
        {
            vm.NavigateToOffset(row.HtmlBookPath, row.Position);
        }
    }

    private void OnWordCountsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && WordCountsGrid.RowSelection?.SelectedItem is FileWordCountRow row)
        {
            vm.NavigateToFile(row.BookPath);
        }
    }
}
