using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Reports;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the modeless "Reports" dialog. Unlike the other dialogs it is modeless: the data
/// is a snapshot taken when the model is created (<see cref="Refresh"/> is called each time the
/// window is opened by <c>MainWindowViewModel.CreateReportsViewModel</c>), not updated live while
/// editing.
/// </summary>
public sealed class ReportsViewModel : ViewModelBase
{
    private readonly Book _book;

    /// <summary>Creates the view model and immediately computes all reports for the given publication.</summary>
    public ReportsViewModel(Book book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));

        RefreshCommand = new RelayCommand(Refresh);
        ExportCsvCommand = new RelayCommand<int?>(tab => ExportCsvRequested?.Invoke(this, tab ?? SelectedTabIndex));

        Refresh();
    }

    /// <summary>Refreshes all reports from the current state of the book.</summary>
    public void Refresh()
    {
        AllFiles.ReplaceWith(BookReportEngine.GetAllFiles(_book));
        HtmlFiles.ReplaceWith(BookReportEngine.GetHtmlFiles(_book));
        ImageFiles.ReplaceWith(BookReportEngine.GetImageFiles(_book).Select(ImageFilesDisplayRow.From));
        CssFiles.ReplaceWith(BookReportEngine.GetCssFiles(_book));
        Classes.ReplaceWith(BookReportEngine.GetHtmlClassUsage(_book));
        Styles.ReplaceWith(BookReportEngine.GetStylesInCss(_book));
        Links.ReplaceWith(BookReportEngine.GetLinks(_book));
        Characters.ReplaceWith(BookReportEngine.GetCharacterUsage(_book).Select(CharacterDisplayRow.From));

        WordCharacterCountsReport counts = BookReportEngine.GetWordCharacterCounts(_book);
        WordCounts.ReplaceWith(counts.Files);
        SetProperty(ref _totalWords, counts.TotalWords, nameof(TotalWords));
        SetProperty(ref _totalCharacters, counts.TotalCharacters, nameof(TotalCharacters));
    }

    /// <summary>"All Files" report.</summary>
    public ObservableCollection<AllFilesRow> AllFiles { get; } = new();

    /// <summary>"HTML Files" report.</summary>
    public ObservableCollection<HtmlFilesRow> HtmlFiles { get; } = new();

    /// <summary>"Image Files" report.</summary>
    public ObservableCollection<ImageFilesDisplayRow> ImageFiles { get; } = new();

    /// <summary>"CSS Files" report.</summary>
    public ObservableCollection<CssFilesRow> CssFiles { get; } = new();

    /// <summary>"Classes in HTML" report.</summary>
    public ObservableCollection<HtmlClassUsageRow> Classes { get; } = new();

    /// <summary>"Styles in CSS" report.</summary>
    public ObservableCollection<CssSelectorUsage> Styles { get; } = new();

    /// <summary>"Links" report.</summary>
    public ObservableCollection<LinkRow> Links { get; } = new();

    /// <summary>"Characters in HTML" report (rows extended with the display text of the character).</summary>
    public ObservableCollection<CharacterDisplayRow> Characters { get; } = new();

    /// <summary>"Word &amp; Character Counts" report — see the note on <see cref="WordCharacterCountsReport"/>.</summary>
    public ObservableCollection<FileWordCountRow> WordCounts { get; } = new();

    private int _totalWords;

    /// <summary>Total words across all (X)HTML files.</summary>
    public int TotalWords => _totalWords;

    private int _totalCharacters;

    /// <summary>Total characters across all (X)HTML files.</summary>
    public int TotalCharacters => _totalCharacters;

    private int _selectedTabIndex;

    /// <summary>Index of the currently selected report tab (0..8) — for CSV export of the current view.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    /// <summary>Refreshes all reports (the "Refresh" button).</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>Requests a CSV export of the current (or given) tab — the view shows a file picker.</summary>
    public RelayCommand<int?> ExportCsvCommand { get; }

    /// <summary>Raised by <see cref="ExportCsvCommand"/> — the argument is the index of the tab to export.</summary>
    public event EventHandler<int>? ExportCsvRequested;

    /// <summary>
    /// Raised on "go to" (double-click on a row) — open <c>BookPath</c> in Code View and scroll to
    /// the character offset.
    /// </summary>
    public event Action<string, int>? NavigationRequested;

    /// <summary>Navigates to the start of the file (offset 0) — All Files / HTML Files / CSS Files / Image Files / Word Counts.</summary>
    public void NavigateToFile(string bookPath) => NavigationRequested?.Invoke(bookPath, 0);

    /// <summary>Navigates to a specific offset in the file — Classes in HTML / Styles in CSS / Links.</summary>
    public void NavigateToOffset(string bookPath, int offset) => NavigationRequested?.Invoke(bookPath, Math.Max(0, offset));

    private static string[] AllFilesHeader => [Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_Type"), Strings.Get("ReportsWindow_SizeBytes"), Strings.Get("ReportsWindow_InSpine")];
    private static string[] HtmlFilesHeader => [Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_SizeBytes"), Strings.Get("ReportsWindow_AllWords"), Strings.Get("ReportsWindow_WellFormed")];
    private static string[] ImageFilesHeader => [Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_Format"), Strings.Get("ReportsWindow_SizeBytes"), Strings.Get("ReportsWindow_Width"), Strings.Get("ReportsWindow_Height"), Strings.Get("ReportsWindow_UsedIn")];
    private static string[] CssFilesHeader => [Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_SizeBytes"), Strings.Get("ReportsWindow_SelectorCount")];
    private static string[] ClassesHeader => [Strings.Get("ReportsWindow_HtmlFile"), Strings.Get("ReportsWindow_Element"), Strings.Get("ReportsWindow_Class"), Strings.Get("ReportsWindow_MatchedSelector"), Strings.Get("ReportsWindow_DefinedIn")];
    private static string[] StylesHeader => [Strings.Get("ReportsWindow_CssFile"), Strings.Get("ReportsWindow_CssSelector"), Strings.Get("ReportsWindow_UsedInHtmlFile")];
    private static string[] LinksHeader => [Strings.Get("ReportsWindow_File"), Strings.Get("ReportsWindow_Text"), Strings.Get("ReportsWindow_Target"), Strings.Get("ReportsWindow_Internal"), Strings.Get("ReportsWindow_TargetExists")];
    private static string[] CharactersHeader => [Strings.Get("ReportsWindow_Character"), Strings.Get("ReportsWindow_Decimal"), Strings.Get("ReportsWindow_Hexadecimal"), Strings.Get("ReportsWindow_EntityName"), Strings.Get("ReportsWindow_Count"), Strings.Get("ReportsWindow_FoundIn")];
    private static string[] WordCountsHeader => [Strings.Get("ReportsWindow_File"), Strings.Get("ReportsWindow_WordsColumn"), Strings.Get("ReportsWindow_CharactersColumn")];

    /// <summary>Builds the CSV text (header + rows) for the given report tab.</summary>
    public string BuildCsv(int tabIndex)
    {
        return tabIndex switch
        {
            0 => BuildCsvLines(
                AllFilesHeader,
                AllFiles.Select(r => new[] { r.Name, r.TypeName, r.SizeBytes.ToString(CultureInfo.InvariantCulture), YesNo(r.InSpine) })),
            1 => BuildCsvLines(
                HtmlFilesHeader,
                HtmlFiles.Select(r => new[] { r.Name, r.SizeBytes.ToString(CultureInfo.InvariantCulture), r.WordCount.ToString(CultureInfo.InvariantCulture), YesNo(r.WellFormed) })),
            2 => BuildCsvLines(
                ImageFilesHeader,
                ImageFiles.Select(r => new[] { r.Name, r.Format, r.SizeBytes.ToString(CultureInfo.InvariantCulture), r.Width.ToString(CultureInfo.InvariantCulture), r.Height.ToString(CultureInfo.InvariantCulture), string.Join("; ", r.UsedIn) })),
            3 => BuildCsvLines(
                CssFilesHeader,
                CssFiles.Select(r => new[] { r.Name, r.SizeBytes.ToString(CultureInfo.InvariantCulture), r.SelectorCount.ToString(CultureInfo.InvariantCulture) })),
            4 => BuildCsvLines(
                ClassesHeader,
                Classes.Select(r => new[] { r.HtmlBookPath, r.ElementName, r.ClassName, r.SelectorText ?? string.Empty, r.CssBookPath ?? string.Empty })),
            5 => BuildCsvLines(
                StylesHeader,
                Styles.Select(r => new[] { r.CssBookPath, r.SelectorText, r.UsedInHtmlBookPath ?? string.Empty })),
            6 => BuildCsvLines(
                LinksHeader,
                Links.Select(r => new[] { r.HtmlBookPath, r.Text, r.TargetHref, YesNo(r.IsInternal), r.TargetExists is null ? Strings.Get("Common_NotApplicable") : YesNo(r.TargetExists.Value) })),
            7 => BuildCsvLines(
                CharactersHeader,
                Characters.Select(r => new[] { r.Character, r.DecimalValue, r.Hexadecimal, r.EntityName, r.Count.ToString(CultureInfo.InvariantCulture), string.Join("; ", r.FoundIn) })),
            8 => BuildCsvLines(
                WordCountsHeader,
                WordCounts.Select(r => new[] { r.BookPath, r.Words.ToString(CultureInfo.InvariantCulture), r.Characters.ToString(CultureInfo.InvariantCulture) })
                    .Append(new[] { Strings.Get("ReportsWindow_TotalRow"), TotalWords.ToString(CultureInfo.InvariantCulture), TotalCharacters.ToString(CultureInfo.InvariantCulture) })),
            _ => string.Empty,
        };
    }

    private static string YesNo(bool value) => Strings.Get(value ? "Common_Yes" : "Common_No");

    private static string BuildCsvLines(string[] header, System.Collections.Generic.IEnumerable<string[]> rows)
    {
        System.Text.StringBuilder sb = new();
        sb.Append(ReportCsv.WriteLine(header)).Append('\n');
        foreach (string[] row in rows)
        {
            sb.Append(ReportCsv.WriteLine(row)).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>"Image Files" report row extended with the display text of the "Used In" list.</summary>
public sealed record ImageFilesDisplayRow(string BookPath, string Name, string Format, long SizeBytes, int Width, int Height, System.Collections.Generic.IReadOnlyList<string> UsedIn)
{
    /// <summary>Book paths of the files using this image, joined into a single column text.</summary>
    public string UsedInDisplay => string.Join("; ", UsedIn);

    /// <summary>Builds the display row from the raw <see cref="ImageFilesRow"/>.</summary>
    public static ImageFilesDisplayRow From(ImageFilesRow row) =>
        new(row.BookPath, row.Name, row.Format, row.SizeBytes, row.Width, row.Height, row.UsedIn);
}

/// <summary>"Characters in HTML" report row extended with ready-to-display text fields.</summary>
/// <remarks>
/// The character name comes from <see cref="Signet.Core.Semantics.CodepointNames"/> (Unicode code
/// point names), not from a table of named XML/HTML entities, which would not cover most non-ASCII
/// characters.
/// </remarks>
public sealed record CharacterDisplayRow(string Character, string DecimalValue, string Hexadecimal, string EntityName, int Count, System.Collections.Generic.IReadOnlyList<string> FoundIn)
{
    /// <summary>Book paths of the files containing this character, joined into a single column text.</summary>
    public string FoundInDisplay => string.Join("; ", FoundIn);

    /// <summary>Builds the display row from the raw <see cref="CharacterUsageRow"/>.</summary>
    public static CharacterDisplayRow From(CharacterUsageRow row)
    {
        string character = char.ConvertFromUtf32(row.CodePoint);
        string name = Signet.Core.Semantics.CodepointNames.GetName(row.CodePoint);
        return new CharacterDisplayRow(
            character,
            row.CodePoint.ToString(CultureInfo.InvariantCulture),
            row.CodePoint.ToString("X4", CultureInfo.InvariantCulture),
            name,
            row.Count,
            row.FoundIn);
    }
}

/// <summary>Helper: replaces the contents of an <see cref="ObservableCollection{T}"/> without breaking bindings.</summary>
internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, System.Collections.Generic.IEnumerable<T> items)
    {
        collection.Clear();
        foreach (T item in items)
        {
            collection.Add(item);
        }
    }
}
