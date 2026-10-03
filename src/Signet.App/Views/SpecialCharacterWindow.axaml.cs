using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.Core.MainUI;
using Signet.Core.Semantics;

namespace Signet.App.Views;

/// <summary>
/// "Insert Special Character" dialog with search by Unicode name (<see cref="CodepointNames.Search"/>),
/// a recently-used list, browsing by Unicode category/block (<see cref="UnicodeBlocks"/>) and a
/// persistent, manually managed favorites list (<c>SettingsStore.FavoriteSpecialCharacters</c>, via the
/// caller). An empty filter with the "All" category shows the recently used characters followed by
/// the curated set (<see cref="SpecialCharacters"/>).
/// </summary>
public partial class SpecialCharacterWindow : Window
{
    private static string AllCategoriesLabel => Strings.Get("SpecialCharacterWindow_AllCategories");

    private static string FavoritesLabel => Strings.Get("SpecialCharacterWindow_FavoritesCategory");

    private IReadOnlyList<string> _recents = Array.Empty<string>();
    private List<string> _favorites = new();
    private Action<string>? _onAddFavorite;
    private Action<string>? _onRemoveFavorite;

    /// <summary>Initializes the window.</summary>
    public SpecialCharacterWindow()
    {
        InitializeComponent();
        Filter.TextChanged += (_, _) => Populate();
        Category.SelectionChanged += (_, _) => Populate();
        Results.DoubleTapped += (_, _) => Accept();
        Results.SelectionChanged += (_, _) => UpdateFavoriteButtonText();
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(null);
        FavoriteButton.Click += (_, _) => ToggleFavorite();

        var categories = new List<string> { AllCategoriesLabel, FavoritesLabel };
        categories.AddRange(UnicodeBlocks.Blocks.Select(b => b.DisplayName));
        Category.ItemsSource = categories;
        Category.SelectedIndex = 0;
    }

    /// <summary>Shows the window; returns the text to insert or <c>null</c> when cancelled.</summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="recents">Recently used characters (newest first), read-only.</param>
    /// <param name="favorites">Current favorite characters (in the order they were added).</param>
    /// <param name="onAddFavorite">Invoked immediately after a character is added to favorites.</param>
    /// <param name="onRemoveFavorite">Invoked immediately after a character is removed from favorites.</param>
    public static async Task<string?> AskAsync(
        Window owner,
        IReadOnlyList<string> recents,
        IReadOnlyList<string> favorites,
        Action<string> onAddFavorite,
        Action<string> onRemoveFavorite)
    {
        SpecialCharacterWindow window = new()
        {
            _recents = recents,
            _favorites = new List<string>(favorites),
            _onAddFavorite = onAddFavorite,
            _onRemoveFavorite = onRemoveFavorite,
        };
        window.Populate();
        return await window.ShowDialog<string?>(owner);
    }

    private void Accept()
    {
        if (Results.SelectedItem is CharRow row)
        {
            Close(row.Insert);
        }
    }

    private void ToggleFavorite()
    {
        if (Results.SelectedItem is not CharRow row)
        {
            return;
        }

        if (_favorites.Contains(row.Insert, StringComparer.Ordinal))
        {
            _favorites.RemoveAll(s => string.Equals(s, row.Insert, StringComparison.Ordinal));
            _onRemoveFavorite?.Invoke(row.Insert);
        }
        else
        {
            _favorites.Add(row.Insert);
            _onAddFavorite?.Invoke(row.Insert);
        }

        int selectedIndex = Results.SelectedIndex;
        Populate();
        if (selectedIndex >= 0 && selectedIndex < Results.ItemCount)
        {
            Results.SelectedIndex = selectedIndex;
        }

        UpdateFavoriteButtonText();
    }

    private void UpdateFavoriteButtonText()
    {
        bool isFavorite = Results.SelectedItem is CharRow row && _favorites.Contains(row.Insert, StringComparer.Ordinal);
        FavoriteButton.Content = Strings.Get(isFavorite ? "SpecialCharacterWindow_RemoveFavorite" : "SpecialCharacterWindow_AddFavorite");
        FavoriteButton.IsEnabled = Results.SelectedItem is CharRow;
    }

    private void Populate()
    {
        string query = Filter.Text?.Trim() ?? string.Empty;
        string? category = Category.SelectedItem as string;
        var rows = new List<CharRow>();

        if (query.Length == 0 && category == FavoritesLabel)
        {
            foreach (string f in _favorites)
            {
                rows.Add(new CharRow(f, f, DescribeInsert(f, Strings.Get("SpecialCharacterWindow_FavoriteNote"))));
            }
        }
        else if (query.Length == 0 && category is not null && category != AllCategoriesLabel)
        {
            UnicodeBlock block = UnicodeBlocks.Blocks.First(b => b.DisplayName == category);
            foreach (int cp in UnicodeBlocks.GetAssignedCodepoints(block))
            {
                string ch = char.ConvertFromUtf32(cp);
                rows.Add(new CharRow(ch, ch, $"U+{cp:X4}  {CodepointNames.GetName(cp)}"));
            }
        }
        else if (query.Length == 0)
        {
            foreach (string r in _recents)
            {
                rows.Add(new CharRow(r, r, DescribeInsert(r, Strings.Get("SpecialCharacterWindow_RecentlyUsed"))));
            }

            foreach (SpecialCharacterEntry e in SpecialCharacters.Entries)
            {
                rows.Add(new CharRow(e.Insert, e.DisplayText, $"{e.Entity}  {e.DisplayDescription}"));
            }
        }
        else
        {
            foreach (SpecialCharacterEntry e in SpecialCharacters.Entries)
            {
                if (e.Entity.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.DisplayDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(new CharRow(e.Insert, e.DisplayText, $"{e.Entity}  {e.DisplayDescription}"));
                }
            }

            foreach ((int cp, string name) in CodepointNames.Search(query, 300))
            {
                string ch = char.ConvertFromUtf32(cp);
                rows.Add(new CharRow(ch, ch, $"U+{cp:X4}  {name}"));
            }
        }

        Results.ItemsSource = rows;
        if (rows.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        UpdateFavoriteButtonText();
    }

    private static string DescribeInsert(string insert, string note)
    {
        try
        {
            if (new System.Globalization.StringInfo(insert).LengthInTextElements == 1)
            {
                int cp = char.ConvertToUtf32(insert, 0);
                return $"U+{cp:X4}  {CodepointNames.GetName(cp)}  ({note})";
            }
        }
        catch (ArgumentException)
        {
            // The insert is an entity (e.g. "&amp;") — it has no single code point.
        }

        return note;
    }

}

/// <summary>A row of the "Insert Special Character" list (Insert = text to insert, Display = appearance, Detail = description).</summary>
public sealed record CharRow(string Insert, string Display, string Detail);
