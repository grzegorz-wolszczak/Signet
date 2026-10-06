using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Semantics;

namespace Signet.App.Views;

/// <summary>Input of the "Insert Special Character" window.</summary>
/// <param name="Appearance">Font of the character column (Preferences).</param>
/// <param name="Recents">Recently used characters (newest first), read-only.</param>
/// <param name="Favorites">Current favorite characters (in the order they were added).</param>
/// <param name="SearchAll">Initial state of "Search all characters".</param>
/// <param name="DocumentDefinesHtmlEntities">
/// Whether the target document has an XHTML 1.x DOCTYPE (<see cref="XhtmlEntities.HasXhtml1Doctype"/>) —
/// otherwise inserting a named HTML entity asks for confirmation.
/// </param>
public sealed record SpecialCharacterDialogContext(
    SpecialCharacterAppearance Appearance,
    IReadOnlyList<string> Recents,
    IReadOnlyList<string> Favorites,
    bool SearchAll,
    bool DocumentDefinesHtmlEntities)
{
    /// <summary>Invoked immediately after a character is added to favorites.</summary>
    public Action<string>? OnAddFavorite { get; init; }

    /// <summary>Invoked immediately after a character is removed from favorites.</summary>
    public Action<string>? OnRemoveFavorite { get; init; }

    /// <summary>Invoked when "Search all characters" is toggled (to remember it).</summary>
    public Action<bool>? OnSearchAllChanged { get; init; }

    /// <summary>Initial state of "Double-click inserts the cell content".</summary>
    public bool DoubleClickInsertsCell { get; init; }

    /// <summary>Invoked when "Double-click inserts the cell content" is toggled (to remember it).</summary>
    public Action<bool>? OnDoubleClickInsertsCellChanged { get; init; }

    /// <summary>
    /// Inserts a chosen text into the document — invoked for every insertion; the window stays open
    /// (it is closed only by "Close" / Esc).
    /// </summary>
    public Action<SpecialCharacterChoice>? OnInsert { get; init; }
}

/// <summary>What is inserted from a row of the "Insert Special Character" table.</summary>
public enum SpecialCharacterInsertForm
{
    /// <summary>The character itself (<see cref="SpecialCharacterWindow.CharacterText"/>).</summary>
    Character,

    /// <summary>The named entity (confirmed when the document does not define it).</summary>
    Entity,

    /// <summary>The plain text <c>U+XXXX</c>.</summary>
    Code,

    /// <summary>The hexadecimal character reference <c>&amp;#xXXXX;</c>.</summary>
    Hex,

    /// <summary>The decimal character reference <c>&amp;#NNNN;</c>.</summary>
    Dec,
}

/// <summary>An insertion from the "Insert Special Character" window.</summary>
/// <param name="Text">The text to insert (the character, its code or its entity).</param>
/// <param name="Character">The character itself (as it is inserted by "Insert Character") — the key of the recently used list.</param>
public sealed record SpecialCharacterChoice(string Text, string Character);

/// <summary>
/// "Insert Special Character" dialog with search by Unicode name (<see cref="CodepointNames.Search"/>),
/// the categories "Popular" (the curated set, <see cref="SpecialCharacters"/>), "Recently used" (selected
/// on opening when not empty) and "Favorites" (persistent, manually managed —
/// <c>SettingsStore.FavoriteSpecialCharacters</c>, via the caller), followed by the Unicode blocks
/// (<see cref="UnicodeBlocks"/>). The search box searches all characters, or — with "Search all characters"
/// off — only the rows of the selected category.
/// The results are a table (character / entity / code / hex / decimal / name / favorite mark; a double-click
/// on the mark toggles it); the character column uses the font from Preferences (<see cref="SpecialCharacterAppearance"/>).
/// The character can be inserted as itself, as a code (<see cref="InsertCodeWindow"/>) or as its entity;
/// with "Double-click inserts the cell content" a double-click inserts the clicked cell's form directly
/// (<see cref="DoubleClickForm"/>). An entity the document does not define asks for confirmation first.
/// Inserting does not close the window (<see cref="SpecialCharacterDialogContext.OnInsert"/>); the recently
/// used list shown in it is the one from when it was opened.
/// </summary>
/// <remarks>
/// The table is a <see cref="TreeDataGrid"/>; its source (whose items are replaced by every search / category change)
/// and columns are built here. The character, code, hexadecimal and decimal columns sort by the code point, the
/// favorite column by the mark.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source lives as long as the window and observes only the window's own row lists.")]
public partial class SpecialCharacterWindow : Window
{
    // Room for the drop-down arrow and the inner padding next to the longest category name.
    private const double CategoryChromeWidth = 56;

    private static readonly Dictionary<int, string> EntityByCodepoint = SpecialCharacters.Entries
        .Select(e => (Cp: SingleCodepoint(WebUtility.HtmlDecode(e.Insert)), e.Entity))
        .Where(p => p.Cp >= 0)
        .GroupBy(p => p.Cp)
        .ToDictionary(g => g.Key, g => g.First().Entity);

    private static readonly Dictionary<string, SpecialCharacterEntry> CuratedByInsert = SpecialCharacters.Entries
        .GroupBy(e => e.Insert, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static string PopularCategoryLabel => Strings.Get("SpecialCharacterWindow_PopularCategory");

    private static string RecentLabel => Strings.Get("SpecialCharacterWindow_RecentCategory");

    private static string FavoritesLabel => Strings.Get("SpecialCharacterWindow_FavoritesCategory");

    private SpecialCharacterDialogContext _context = new(
        SpecialCharacterAppearance.Default, Array.Empty<string>(), Array.Empty<string>(), true, false);

    private List<string> _favorites = new();
    private List<CharRow> _rows = new();
    private Style? _symbolStyle;
    private bool _initializing = true;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private readonly LocalizedColumns<CharRow> _columns = new();
    private readonly FlatTreeDataGridSource<CharRow> _source;

    /// <summary>Initializes the window.</summary>
    public SpecialCharacterWindow()
    {
        InitializeComponent();
        _source = BuildSource();
        Results.Source = _source;
        Filter.TextChanged += (_, _) => Populate();
        Category.SelectionChanged += (_, _) => Populate();
        SearchAll.IsCheckedChanged += (_, _) => OnSearchAllChanged();
        DoubleClickInsertsCell.IsCheckedChanged += (_, _) => OnDoubleClickInsertsCellChanged();
        Results.DoubleTapped += OnResultsDoubleTapped;
        _source.RowSelection!.SelectionChanged += (_, _) => UpdateButtons();
        // Enter in the table inserts, like the default button (handled before the grid's own key handling).
        Results.AddHandler(KeyDownEvent, OnResultsKeyDown, RoutingStrategies.Tunnel);
        OkButton.Click += (_, _) => Insert(SpecialCharacterInsertForm.Character);
        InsertCodeButton.Click += (_, _) => InsertCodeFromDialog();
        InsertEntityButton.Click += (_, _) => Insert(SpecialCharacterInsertForm.Entity);
        CancelButton.Click += (_, _) => Close();
        FavoriteButton.Click += (_, _) =>
        {
            if (SelectedRow is { } row)
            {
                ToggleFavorite(row);
            }
        };
        Opened += (_, _) => FitCategoryWidth();

        var categories = new List<string> { PopularCategoryLabel, RecentLabel, FavoritesLabel };
        categories.AddRange(UnicodeBlocks.Blocks.Select(b => b.DisplayName));
        Category.ItemsSource = categories;
        Category.SelectedIndex = 0;
        ApplySymbolFont(SpecialCharacterAppearance.Default);
    }

    /// <summary>The rows currently shown (in the order they were built, before any column sorting).</summary>
    public IReadOnlyList<CharRow> Rows => _rows;

    /// <summary>The "Favorite" column (a double-click on its cell toggles the mark).</summary>
    private IColumn FavoriteColumn => _source.Columns[^1];

    /// <summary>The selected row of the table, if any.</summary>
    private CharRow? SelectedRow => _source.RowSelection?.SelectedItem;

    /// <summary>
    /// Shows the window until it is closed; every insertion goes through
    /// <see cref="SpecialCharacterDialogContext.OnInsert"/> and leaves the window open.
    /// </summary>
    public static Task ShowAsync(Window owner, SpecialCharacterDialogContext context) =>
        Build(context).ShowDialog(owner);

    /// <summary>Configures the window without showing it (for render tests without a blocking <c>ShowDialog</c>).</summary>
    public static SpecialCharacterWindow Build(SpecialCharacterDialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpecialCharacterWindow window = new()
        {
            _context = context,
            _favorites = new List<string>(context.Favorites),
        };
        window.ApplySymbolFont(context.Appearance);
        window.SearchAll.IsChecked = context.SearchAll;
        window.DoubleClickInsertsCell.IsChecked = context.DoubleClickInsertsCell;
        // Opens on the recently used characters, or on the popular ones when there are none yet.
        window.Category.SelectedItem = context.Recents.Count > 0 ? RecentLabel : PopularCategoryLabel;
        window._initializing = false;
        window.Populate();
        return window;
    }

    /// <summary>
    /// The text "Insert Character" inserts for a row: the character itself, with <c>&amp;</c>, <c>&lt;</c> and
    /// <c>&gt;</c> escaped (a raw one would break the XHTML). Also the key of the recent/favorite lists.
    /// </summary>
    public static string CharacterText(CharRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Insert switch
        {
            "&" => "&amp;",
            "<" => "&lt;",
            ">" => "&gt;",
            _ => row.Insert,
        };
    }

    /// <summary>
    /// The text a form of a row inserts, or <c>null</c> when the row has no such form (no entity, or not a
    /// single character). An entity is returned as is — the confirmation for an undefined one is separate.
    /// </summary>
    public static string? FormText(CharRow row, SpecialCharacterInsertForm form)
    {
        ArgumentNullException.ThrowIfNull(row);
        string text = form switch
        {
            SpecialCharacterInsertForm.Character => CharacterText(row),
            SpecialCharacterInsertForm.Entity => row.Entity,
            SpecialCharacterInsertForm.Code => row.Code,
            SpecialCharacterInsertForm.Hex => row.Hex,
            SpecialCharacterInsertForm.Dec => row.Dec,
            _ => string.Empty,
        };
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// What a double-click on a cell of <paramref name="column"/> inserts: the character, or — with
    /// "Double-click inserts the cell content" — the content of the character, entity, code, hexadecimal
    /// and decimal columns and nothing for the name column. The favorite column never inserts (<c>null</c>).
    /// </summary>
    public SpecialCharacterInsertForm? DoubleClickForm(IColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        // A double-click on the favorite column toggles the mark — it never inserts.
        if (ReferenceEquals(column, FavoriteColumn))
        {
            return null;
        }

        if (DoubleClickInsertsCell.IsChecked != true)
        {
            return SpecialCharacterInsertForm.Character;
        }

        int index = 0;
        while (index < _source.Columns.Count && !ReferenceEquals(_source.Columns[index], column))
        {
            index++;
        }

        return index switch
        {
            0 => SpecialCharacterInsertForm.Character,
            1 => SpecialCharacterInsertForm.Entity,
            2 => SpecialCharacterInsertForm.Code,
            3 => SpecialCharacterInsertForm.Hex,
            4 => SpecialCharacterInsertForm.Dec,
            _ => null,
        };
    }

    /// <summary>Toggles the favorite mark of a row in place (the row stays in the list until the view changes).</summary>
    public void ToggleFavorite(CharRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string key = CharacterText(row);
        if (row.IsFavorite)
        {
            _favorites.RemoveAll(s => string.Equals(s, key, StringComparison.Ordinal));
            _context.OnRemoveFavorite?.Invoke(key);
        }
        else if (!_favorites.Contains(key, StringComparer.Ordinal))
        {
            _favorites.Add(key);
            _context.OnAddFavorite?.Invoke(key);
        }

        bool isFavorite = !row.IsFavorite;
        foreach (CharRow same in _rows.Where(r => string.Equals(CharacterText(r), key, StringComparison.Ordinal)))
        {
            same.IsFavorite = isFavorite;
        }

        UpdateButtons();
    }

    private void ApplySymbolFont(SpecialCharacterAppearance appearance)
    {
        Style style = new(x => x.OfType<TextBlock>().Class("symbol"));
        if (!string.IsNullOrWhiteSpace(appearance.FontFamily))
        {
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(appearance.FontFamily)));
        }

        if (appearance.FontSize > 0)
        {
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, (double)appearance.FontSize));
        }

        if (_symbolStyle is not null)
        {
            Styles.Remove(_symbolStyle);
        }

        _symbolStyle = style;
        Styles.Add(style);
    }

    // The ComboBox sizes itself to the selected item only — fix its width to the longest category name
    // so that none of them is cut off and the box does not jump when the selection changes.
    private void FitCategoryWidth()
    {
        Typeface typeface = new(Category.FontFamily, Category.FontStyle, Category.FontWeight);
        double longest = Category.Items.OfType<string>()
            .Select(text => new FormattedText(
                text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Category.FontSize, null).Width)
            .DefaultIfEmpty(0)
            .Max();
        Category.Width = Math.Ceiling(longest + CategoryChromeWidth);
    }

    private void OnSearchAllChanged()
    {
        if (_initializing)
        {
            return;
        }

        _context.OnSearchAllChanged?.Invoke(SearchAll.IsChecked == true);
        Populate();
    }

    private void OnDoubleClickInsertsCellChanged()
    {
        if (!_initializing)
        {
            _context.OnDoubleClickInsertsCellChanged?.Invoke(DoubleClickInsertsCell.IsChecked == true);
        }
    }

    // The columns of the table, in the order DoubleClickForm relies on.
    private FlatTreeDataGridSource<CharRow> BuildSource()
    {
        static TOptions ByCodePoint<TOptions>(TOptions options)
            where TOptions : ColumnOptions<CharRow>
        {
            options.CompareAscending = (a, b) => (a?.CodePoint ?? -1).CompareTo(b?.CodePoint ?? -1);
            options.CompareDescending = (a, b) => (b?.CodePoint ?? -1).CompareTo(a?.CodePoint ?? -1);
            return options;
        }

        return new FlatTreeDataGridSource<CharRow>(Array.Empty<CharRow>())
        {
            Columns =
            {
                _columns.Template("SpecialCharacterWindow_ColumnCharacter", "CharacterCellTemplate", GridLength.Auto,
                    ByCodePoint(new TemplateColumnOptions<CharRow> { MinWidth = new GridLength(70) })),
                _columns.Text("SpecialCharacterWindow_ColumnEntity", r => r.Entity, new GridLength(110)),
                _columns.Text("SpecialCharacterWindow_ColumnCode", r => r.Code, new GridLength(90),
                    ByCodePoint(new TextColumnOptions<CharRow>())),
                _columns.Text("SpecialCharacterWindow_ColumnHex", r => r.Hex, new GridLength(100),
                    ByCodePoint(new TextColumnOptions<CharRow>())),
                _columns.Text("SpecialCharacterWindow_ColumnDecimal", r => r.Dec, new GridLength(100),
                    ByCodePoint(new TextColumnOptions<CharRow>())),
                _columns.Text("SpecialCharacterWindow_ColumnName", r => r.Name, new GridLength(1, GridUnitType.Star)),
                // A fixed width, not Auto: TreeDataGrid virtualizes columns too, and an Auto column after a star
                // column never gets realized (its width is unknown, so the star column takes all the room). The
                // header sizing still widens it to its header.
                _columns.Template("SpecialCharacterWindow_ColumnFavorite", "FavoriteCellTemplate", new GridLength(80),
                    new TemplateColumnOptions<CharRow>
                    {
                        CompareAscending = (a, b) => string.CompareOrdinal(a?.FavoriteMark, b?.FavoriteMark),
                        CompareDescending = (a, b) => string.CompareOrdinal(b?.FavoriteMark, a?.FavoriteMark),
                    }),
            },
        };
    }

    // Only a double-click on a cell acts (not one on a column header): in the favorite column it toggles
    // the mark, elsewhere it inserts. The pressed cell tells the column; its row is the double-clicked one.
    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source
            || source.FindAncestorOfType<TreeDataGridCell>(includeSelf: true) is not { ColumnIndex: >= 0 } cell
            || cell.ColumnIndex >= _source.Columns.Count)
        {
            return;
        }

        IColumn column = _source.Columns[cell.ColumnIndex];
        if (ReferenceEquals(column, FavoriteColumn))
        {
            if (cell.FindAncestorOfType<TreeDataGridRow>()?.Model is CharRow row)
            {
                ToggleFavorite(row);
            }
        }
        else if (DoubleClickForm(column) is { } form)
        {
            Insert(form);
        }
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Insert(SpecialCharacterInsertForm.Character);
            e.Handled = true;
        }
    }

    private async void Insert(SpecialCharacterInsertForm form)
    {
        if (SelectedRow is not { } row || FormText(row, form) is not { } text)
        {
            return;
        }

        if (form == SpecialCharacterInsertForm.Entity && !await ConfirmEntityAsync(text))
        {
            return;
        }

        _context.OnInsert?.Invoke(new SpecialCharacterChoice(text, CharacterText(row)));
    }

    private async void InsertCodeFromDialog()
    {
        if (SelectedRow is not { CodePoint: >= 0 } row)
        {
            return;
        }

        if (await InsertCodeWindow.AskAsync(this, row.CodePoint) is { } text)
        {
            _context.OnInsert?.Invoke(new SpecialCharacterChoice(text, CharacterText(row)));
        }
    }

    private async Task<bool> ConfirmEntityAsync(string entity) =>
        XhtmlEntities.IsDefined(entity, _context.DocumentDefinesHtmlEntities)
        || await ConfirmWindow.AskAsync(
            this,
            Strings.Get("SpecialCharacterWindow_EntityWarningTitle"),
            Strings.Format("SpecialCharacterWindow_EntityWarning", entity),
            Strings.Get("SpecialCharacterWindow_InsertAnyway"));

    private void UpdateButtons()
    {
        CharRow? row = SelectedRow;
        FavoriteButton.Content = Strings.Get(row is { IsFavorite: true } ? "SpecialCharacterWindow_RemoveFavorite" : "SpecialCharacterWindow_AddFavorite");
        FavoriteButton.IsEnabled = row is not null;
        OkButton.IsEnabled = row is not null;
        InsertCodeButton.IsEnabled = row is { CodePoint: >= 0 };
        InsertEntityButton.IsEnabled = row is { Entity.Length: > 0 };
    }

    private void Populate()
    {
        if (_initializing)
        {
            return;
        }

        string query = Filter.Text?.Trim() ?? string.Empty;
        List<CharRow> rows;
        if (query.Length == 0)
        {
            rows = CategoryRows();
        }
        else if (SearchAll.IsChecked == true)
        {
            rows = SpecialCharacters.Entries
                .Where(e => e.Entity.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.DisplayDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(FromEntry)
                .Concat(CodepointNames.Search(query, 300).Select(m => FromCodepoint(m.Codepoint, m.Name)))
                .ToList();
        }
        else
        {
            rows = CategoryRows().Where(r => Matches(r, query)).ToList();
        }

        foreach (CharRow row in rows)
        {
            row.IsFavorite = _favorites.Contains(CharacterText(row), StringComparer.Ordinal);
        }

        _rows = rows;
        _source.Items = rows;
        if (rows.Count > 0)
        {
            _source.RowSelection!.SelectedIndex = new IndexPath(0);
        }

        UpdateButtons();
    }

    // The rows of the selected category (without the search filter).
    private List<CharRow> CategoryRows()
    {
        string? category = Category.SelectedItem as string;
        if (category == RecentLabel)
        {
            return _context.Recents.Select(FromInsert).ToList();
        }

        if (category == FavoritesLabel)
        {
            return _favorites.Select(FromInsert).ToList();
        }

        if (category is not null && category != PopularCategoryLabel)
        {
            UnicodeBlock block = UnicodeBlocks.Blocks.First(b => b.DisplayName == category);
            return UnicodeBlocks.GetAssignedCodepoints(block).Select(cp => FromCodepoint(cp, CodepointNames.GetName(cp))).ToList();
        }

        return SpecialCharacters.Entries.Select(FromEntry).ToList();
    }

    private static bool Matches(CharRow row, string query) =>
        row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Entity.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Hex.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Dec.Contains(query, StringComparison.Ordinal)
        || string.Equals(row.Display, query, StringComparison.Ordinal);

    private static CharRow FromEntry(SpecialCharacterEntry e)
    {
        int cp = SingleCodepoint(WebUtility.HtmlDecode(e.Insert));
        return new CharRow(e.Insert, e.DisplayText, cp, FormatCode(cp), e.Entity, e.DisplayDescription);
    }

    private static CharRow FromCodepoint(int cp, string name)
    {
        string ch = char.ConvertFromUtf32(cp);
        return new CharRow(ch, ch, cp, FormatCode(cp), EntityByCodepoint.GetValueOrDefault(cp, string.Empty), name);
    }

    // A recent/favorite item: a curated entry keeps its display text and entity.
    private static CharRow FromInsert(string insert)
    {
        int cp = SingleCodepoint(WebUtility.HtmlDecode(insert));
        bool curated = CuratedByInsert.TryGetValue(insert, out SpecialCharacterEntry entry);
        string display = curated ? entry.DisplayText : insert;
        string entity = cp >= 0 ? EntityByCodepoint.GetValueOrDefault(cp, string.Empty) : string.Empty;
        string name = cp >= 0 ? CodepointNames.GetName(cp) : curated ? entry.DisplayDescription : string.Empty;
        return new CharRow(insert, display, cp, FormatCode(cp), entity, name);
    }

    private static string FormatCode(int cp) => cp >= 0 ? $"U+{cp:X4}" : string.Empty;

    // The code point of a text that is exactly one Unicode scalar value, otherwise -1.
    private static int SingleCodepoint(string text)
    {
        if (string.IsNullOrEmpty(text) || !Rune.TryGetRuneAt(text, 0, out Rune rune) || rune.Utf16SequenceLength != text.Length)
        {
            return -1;
        }

        return rune.Value;
    }
}

/// <summary>A row of the "Insert Special Character" table.</summary>
public sealed partial class CharRow : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="insert">The text to insert.</param>
    /// <param name="display">The appearance in the "Character" column.</param>
    /// <param name="codePoint">The code point, or <c>-1</c> when the insert is not a single character (used for sorting).</param>
    /// <param name="code">The code point as <c>U+XXXX</c> (empty when not a single character).</param>
    /// <param name="entity">The HTML entity, when known (empty otherwise).</param>
    /// <param name="name">The Unicode name or description.</param>
    public CharRow(string insert, string display, int codePoint, string code, string entity, string name)
    {
        Insert = insert;
        Display = display;
        CodePoint = codePoint;
        Code = code;
        Entity = entity;
        Name = name;
    }

    /// <summary>The text to insert.</summary>
    public string Insert { get; }

    /// <summary>The appearance in the "Character" column.</summary>
    public string Display { get; }

    /// <summary>The code point, or <c>-1</c> when the insert is not a single character.</summary>
    public int CodePoint { get; }

    /// <summary>The code point as <c>U+XXXX</c> (empty when not a single character).</summary>
    public string Code { get; }

    /// <summary>The HTML entity, when known (empty otherwise).</summary>
    public string Entity { get; }

    /// <summary>The hexadecimal character reference <c>&amp;#xXXXX;</c> (empty when not a single character).</summary>
    public string Hex => CodePoint >= 0 ? InsertCodeWindow.Forms(CodePoint).Hex : string.Empty;

    /// <summary>The decimal character reference <c>&amp;#NNNN;</c> (empty when not a single character).</summary>
    public string Dec => CodePoint >= 0 ? InsertCodeWindow.Forms(CodePoint).Decimal : string.Empty;

    /// <summary>The Unicode name or description.</summary>
    public string Name { get; }

    /// <summary>Whether the character is on the favorites list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteMark))]
    private bool _isFavorite;

    /// <summary>The "Favorite" column: a check mark for a favorite character, otherwise empty.</summary>
    public string FavoriteMark => IsFavorite ? "✓" : string.Empty;
}
