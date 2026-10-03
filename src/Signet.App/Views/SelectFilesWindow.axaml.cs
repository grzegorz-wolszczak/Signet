using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core;

namespace Signet.App.Views;

/// <summary>Selection mode of the <see cref="SelectFilesWindow"/>.</summary>
public enum SelectFilesMode
{
    /// <summary>Exactly one file (Add Cover). The returned list has at most one element.</summary>
    SingleFile,

    /// <summary>Any number of files, in selection order (Insert File).</summary>
    Multiple,

    /// <summary>
    /// An "included" checkbox plus reordering of the whole list (Link Stylesheets /
    /// Link Javascripts) — returns the book paths of the checked rows in list order.
    /// </summary>
    MultipleOrdered,
}

/// <summary>An entry offered in the <see cref="SelectFilesWindow"/>.</summary>
public sealed record SelectFilesEntry(
    string BookPath,
    string DisplayName,
    ResourceType Type,
    string? ThumbnailPath,
    bool InitiallyIncluded = false);

/// <summary>Result of the <see cref="SelectFilesWindow"/>: either "from disk" or a list of book paths.</summary>
public sealed record SelectFilesResult(bool FromDisk, IReadOnlyList<string> BookPaths);

/// <summary>
/// Shared "Select Files" dialog: a filterable list of the book's files with image thumbnails,
/// an optional "Insert from disk" and (in <see cref="SelectFilesMode.MultipleOrdered"/> mode)
/// reordering. Used by Add Cover, Insert File, Link Stylesheets and Link Javascripts.
/// </summary>
/// <remarks>
/// There is no audio/video preview (only raster image thumbnails via <see cref="Bitmap"/> — SVG and
/// audio/video show just the name, without a WebView), and thumbnails cannot be resized on the fly.
/// </remarks>
public partial class SelectFilesWindow : Window
{
    private static readonly (string Label, ResourceType? Filter)[] TypeFilters =
    {
        (Strings.Get("SelectFilesWindow_FilterAll"), null),
        (Strings.Get("SelectFilesWindow_FilterImages"), ResourceType.Image | ResourceType.Svg),
        (Strings.Get("SelectFilesWindow_FilterVideo"), ResourceType.Video),
        (Strings.Get("SelectFilesWindow_FilterAudio"), ResourceType.Audio),
    };

    private readonly ObservableCollection<SelectFilesRow> _allRows = new();
    private SelectFilesMode _mode = SelectFilesMode.Multiple;

    /// <summary>Initializes the window.</summary>
    public SelectFilesWindow()
    {
        InitializeComponent();
        TypeFilterList.ItemsSource = TypeFilters.Select(f => f.Label).ToList();
        TypeFilterList.SelectedIndex = 0;
        TypeFilterList.SelectionChanged += (_, _) => ApplyFilter();
        FilterBox.TextChanged += (_, _) => ApplyFilter();
        MoveUpButton.Click += (_, _) => Move(-1);
        MoveDownButton.Click += (_, _) => Move(1);
        OkButton.Click += (_, _) => AcceptSelection();
        PlainList.DoubleTapped += (_, _) => AcceptSelection();
        FromDiskButton.Click += (_, _) => Close(new SelectFilesResult(true, Array.Empty<string>()));
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Shows the window; returns the choice (book paths or "from disk") or <c>null</c> when cancelled.
    /// </summary>
    public static async Task<SelectFilesResult?> AskAsync(
        Window owner,
        string title,
        string prompt,
        SelectFilesMode mode,
        IReadOnlyList<SelectFilesEntry> entries,
        bool allowInsertFromDisk = false,
        bool showTypeFilter = false,
        string? defaultSelectedBookPath = null)
    {
        SelectFilesWindow window = Build(title, prompt, mode, entries, allowInsertFromDisk, showTypeFilter, defaultSelectedBookPath);
        return await window.ShowDialog<SelectFilesResult?>(owner);
    }

    /// <summary>
    /// Configures the window without showing it (split out of <see cref="AskAsync"/> so rendering
    /// can be tested without a blocking <c>ShowDialog</c>, see <c>CommonDialogsTests</c>).
    /// </summary>
    public static SelectFilesWindow Build(
        string title,
        string prompt,
        SelectFilesMode mode,
        IReadOnlyList<SelectFilesEntry> entries,
        bool allowInsertFromDisk = false,
        bool showTypeFilter = false,
        string? defaultSelectedBookPath = null)
    {
        bool ordered = mode == SelectFilesMode.MultipleOrdered;
        SelectFilesWindow window = new() { Title = title, _mode = mode };
        window.PromptText.Text = prompt;
        window.FromDiskButton.IsVisible = allowInsertFromDisk;
        window.TypeFilterList.IsVisible = showTypeFilter;
        // The text filter rebuilds the list from _allRows in its original order — in
        // MultipleOrdered mode that would undo Move Up/Down, so the box is hidden here.
        window.FilterBox.IsVisible = !ordered;
        window.ReorderButtons.IsVisible = ordered;
        window.OrderedList.IsVisible = ordered;
        window.PlainList.IsVisible = !ordered;
        window.PlainList.SelectionMode = mode == SelectFilesMode.SingleFile ? SelectionMode.Single : SelectionMode.Multiple;

        foreach (SelectFilesEntry entry in entries)
        {
            window._allRows.Add(new SelectFilesRow(entry));
        }

        window.ApplyFilter();

        if (mode == SelectFilesMode.SingleFile && defaultSelectedBookPath is { Length: > 0 })
        {
            SelectFilesRow? match = window._allRows.FirstOrDefault(
                r => string.Equals(r.Entry.BookPath, defaultSelectedBookPath, StringComparison.Ordinal));
            if (match is not null)
            {
                window.PlainList.SelectedItem = match;
            }
        }
        else if (mode == SelectFilesMode.SingleFile && window.PlainList.ItemsSource is IEnumerable<SelectFilesRow> rows)
        {
            window.PlainList.SelectedItem = rows.FirstOrDefault();
        }

        return window;
    }

    private void ApplyFilter()
    {
        ResourceType? typeFilter = TypeFilterList.SelectedIndex is >= 0 and < 4
            ? TypeFilters[TypeFilterList.SelectedIndex].Filter
            : null;
        string text = FilterBox.Text?.Trim() ?? string.Empty;

        IEnumerable<SelectFilesRow> filtered = _allRows.Where(r =>
            (typeFilter is null || (r.Entry.Type & typeFilter.Value) != 0) &&
            (text.Length == 0 || r.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)));

        List<SelectFilesRow> list = filtered.ToList();
        PlainList.ItemsSource = list;
        OrderedList.ItemsSource = list;
    }

    private void Move(int delta)
    {
        if (OrderedList.ItemsSource is not IList<SelectFilesRow> rows)
        {
            return;
        }

        int index = OrderedList.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= rows.Count)
        {
            return;
        }

        (rows[index], rows[target]) = (rows[target], rows[index]);
        OrderedList.ItemsSource = null;
        OrderedList.ItemsSource = rows;
        OrderedList.SelectedIndex = target;
    }

    private void AcceptSelection()
    {
        if (_mode == SelectFilesMode.MultipleOrdered)
        {
            List<string> ordered = (OrderedList.ItemsSource as IEnumerable<SelectFilesRow> ?? Array.Empty<SelectFilesRow>())
                .Where(r => r.Included)
                .Select(r => r.Entry.BookPath)
                .ToList();
            Close(new SelectFilesResult(false, ordered));
            return;
        }

        List<string> chosen = PlainList.SelectedItems?
            .OfType<SelectFilesRow>()
            .Select(r => r.Entry.BookPath)
            .ToList() ?? new List<string>();

        if (chosen.Count > 0)
        {
            Close(new SelectFilesResult(false, chosen));
        }
    }
}

/// <summary>A row of the <see cref="SelectFilesWindow"/> list.</summary>
public sealed partial class SelectFilesRow : ObservableObject
{
    /// <summary>Creates a row for <paramref name="entry"/> and tries to load its thumbnail (raster images).</summary>
    public SelectFilesRow(SelectFilesEntry entry)
    {
        Entry = entry;
        _included = entry.InitiallyIncluded;
        Thumbnail = TryLoadThumbnail(entry);
    }

    /// <summary>The source entry.</summary>
    public SelectFilesEntry Entry { get; }

    /// <summary>Display name.</summary>
    public string DisplayName => Entry.DisplayName;

    /// <summary>Thumbnail (raster images only) or <c>null</c>.</summary>
    public Bitmap? Thumbnail { get; }

    /// <summary>Whether a thumbnail was loaded.</summary>
    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Whether the row is checked as "included" (<see cref="SelectFilesMode.MultipleOrdered"/> mode).</summary>
    [ObservableProperty]
    private bool _included;

    private static Bitmap? TryLoadThumbnail(SelectFilesEntry entry)
    {
        if (entry.Type != ResourceType.Image || entry.ThumbnailPath is not { Length: > 0 } path)
        {
            return null;
        }

        try
        {
            using System.IO.FileStream stream = System.IO.File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
