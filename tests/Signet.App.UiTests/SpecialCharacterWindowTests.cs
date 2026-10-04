using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Views;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Semantics;

namespace Signet.App.UiTests;

/// <summary>
/// Headless tests of <see cref="SpecialCharacterWindow"/>: the results table (character / code / entity /
/// name / favorite), the character column font from Preferences, the width of the category list,
/// toggling favorites in place and the search scope.
/// </summary>
public sealed class SpecialCharacterWindowTests
{
    private static readonly string[] Recents = { "—" };

    private static readonly string[] Favorites = { "©" };

    private static SpecialCharacterDialogContext Context(
        SpecialCharacterAppearance? appearance = null,
        IReadOnlyList<string>? favorites = null,
        bool searchAll = true) =>
        new(appearance ?? SpecialCharacterAppearance.Default, Recents, favorites ?? Array.Empty<string>(), searchAll, false);

    private static SpecialCharacterWindow Show(SpecialCharacterDialogContext context)
    {
        SpecialCharacterWindow window = SpecialCharacterWindow.Build(context);
        window.Show();
        Render(window);
        return window;
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static ComboBox CategoryBox(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single();

    private static TextBox FilterBox(Window window) => window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Filter");

    private static CheckBox SearchAllBox(Window window) => window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "SearchAll");

    [AvaloniaFact]
    public void Results_are_a_table_with_entity_code_hex_decimal_name_and_favorite_columns()
    {
        SpecialCharacterWindow window = Show(Context());

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.Columns.Select(c => c.Header as string).Should().Equal(
            Strings.Get("SpecialCharacterWindow_ColumnCharacter"),
            Strings.Get("SpecialCharacterWindow_ColumnEntity"),
            Strings.Get("SpecialCharacterWindow_ColumnCode"),
            Strings.Get("SpecialCharacterWindow_ColumnHex"),
            Strings.Get("SpecialCharacterWindow_ColumnDecimal"),
            Strings.Get("SpecialCharacterWindow_ColumnName"),
            Strings.Get("SpecialCharacterWindow_ColumnFavorite"));

        CharRow recent = window.Rows.Should().ContainSingle().Which;
        (recent.Insert, recent.Entity, recent.Code, recent.Hex, recent.Dec, recent.Name)
            .Should().Be(("—", "&mdash;", "U+2014", "&#x2014;", "&#8212;", CodepointNames.GetName(0x2014)));

        CategoryBox(window).SelectedItem = Strings.Get("SpecialCharacterWindow_PopularCategory");
        Dispatcher.UIThread.RunJobs();
        window.Rows.Should().Contain(r => r.Insert == "&amp;" && r.Display == "&" && r.Code == "U+0026" && r.Entity == "&amp;");
    }

    [AvaloniaFact]
    public void Recently_used_is_a_separate_category_selected_on_opening()
    {
        SpecialCharacterWindow window = Show(Context());

        CategoryBox(window).Items.OfType<string>().Take(3).Should().Equal(
            Strings.Get("SpecialCharacterWindow_PopularCategory"),
            Strings.Get("SpecialCharacterWindow_RecentCategory"),
            Strings.Get("SpecialCharacterWindow_FavoritesCategory"));
        CategoryBox(window).SelectedItem.Should().Be(Strings.Get("SpecialCharacterWindow_RecentCategory"));
        window.Rows.Select(r => r.Insert).Should().Equal(Recents);

        CategoryBox(window).SelectedItem = Strings.Get("SpecialCharacterWindow_PopularCategory");
        Dispatcher.UIThread.RunJobs();
        window.Rows.Select(r => r.Insert).Should().Equal(SpecialCharacters.Entries.Select(e => e.Insert),
            "the popular category holds only the curated set, without the recently used characters");
    }

    [AvaloniaFact]
    public void Opens_on_the_popular_category_when_nothing_was_used_yet()
    {
        SpecialCharacterWindow window = Show(Context() with { Recents = Array.Empty<string>() });

        CategoryBox(window).SelectedItem.Should().Be(Strings.Get("SpecialCharacterWindow_PopularCategory"));
        window.Rows.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Character_column_uses_the_preferences_font()
    {
        SpecialCharacterWindow window = Show(Context(new SpecialCharacterAppearance("Georgia", 30)));

        TextBlock symbol = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("symbol"));
        symbol.FontSize.Should().Be(30);
        symbol.FontFamily.Name.Should().Be("Georgia");
        SpecialCharacterAppearance.Default.FontSize.Should().Be(24);
    }

    [AvaloniaFact]
    public void Category_list_is_wide_enough_for_its_longest_name()
    {
        SpecialCharacterWindow window = Show(Context());

        ComboBox category = CategoryBox(window);
        Typeface typeface = new(category.FontFamily, category.FontStyle, category.FontWeight);
        double longest = category.Items.OfType<string>()
            .Max(t => new FormattedText(t, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, category.FontSize, null).Width);

        category.Bounds.Width.Should().BeGreaterThan(longest);
        category.Items.OfType<string>().First().Should().Be(Strings.Get("SpecialCharacterWindow_PopularCategory"));
    }

    [AvaloniaFact]
    public void Unicode_block_rows_get_the_entity_when_one_is_known()
    {
        SpecialCharacterWindow window = Show(Context());

        CategoryBox(window).SelectedItem = UnicodeBlocks.Blocks.Single(b => b.Name == "General Punctuation").DisplayName;
        Dispatcher.UIThread.RunJobs();

        window.Rows.Should().Contain(r => r.Code == "U+2014" && r.Entity == "&mdash;");
        window.Rows.Should().OnlyContain(r => r.CodePoint >= 0x2000 && r.CodePoint <= 0x206F);
    }

    [AvaloniaFact]
    public void Favorites_are_check_marked_and_toggling_keeps_the_row_until_the_view_changes()
    {
        List<string> added = new();
        List<string> removed = new();
        SpecialCharacterWindow window = Show(Context(favorites: Favorites) with
        {
            OnAddFavorite = added.Add,
            OnRemoveFavorite = removed.Add,
        });
        CategoryBox(window).SelectedItem = Strings.Get("SpecialCharacterWindow_FavoritesCategory");
        Render(window);

        CharRow copyright = window.Rows.Should().ContainSingle().Which;
        copyright.FavoriteMark.Should().Be("✓");
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("✓");

        window.ToggleFavorite(copyright);

        copyright.FavoriteMark.Should().BeEmpty();
        window.Rows.Should().ContainSingle("the row stays so that it can be checked back");
        removed.Should().Equal("©");

        window.ToggleFavorite(copyright);

        copyright.FavoriteMark.Should().Be("✓");
        added.Should().Equal("©");
    }

    [AvaloniaFact]
    public void Favorite_marks_follow_the_favorites_in_every_category()
    {
        SpecialCharacterWindow window = Show(Context(favorites: Favorites));
        CategoryBox(window).SelectedItem = Strings.Get("SpecialCharacterWindow_PopularCategory");
        Dispatcher.UIThread.RunJobs();

        window.Rows.Where(r => r.Insert == "©").Should().NotBeEmpty().And.OnlyContain(r => r.IsFavorite);
        window.Rows.Where(r => r.Insert != "©").Should().OnlyContain(r => !r.IsFavorite && r.FavoriteMark.Length == 0);
    }

    [AvaloniaFact]
    public void Without_search_all_the_filter_searches_only_the_current_category()
    {
        List<bool> remembered = new();
        SpecialCharacterWindow window = Show(Context(favorites: Favorites, searchAll: false) with
        {
            OnSearchAllChanged = remembered.Add,
        });
        CategoryBox(window).SelectedItem = Strings.Get("SpecialCharacterWindow_FavoritesCategory");
        SearchAllBox(window).IsChecked.Should().BeFalse();

        FilterBox(window).Text = "dash";
        Dispatcher.UIThread.RunJobs();
        window.Rows.Should().BeEmpty("the only favorite is ©");

        FilterBox(window).Text = "copy";
        Dispatcher.UIThread.RunJobs();
        window.Rows.Should().ContainSingle().Which.Insert.Should().Be("©");

        SearchAllBox(window).IsChecked = true;
        FilterBox(window).Text = "dash";
        Dispatcher.UIThread.RunJobs();
        window.Rows.Should().Contain(r => r.Insert == "—");
        remembered.Should().Equal(true);
    }

    [AvaloniaFact]
    public void Search_all_check_box_sits_under_the_filter()
    {
        SpecialCharacterWindow window = Show(Context());

        TextBox filter = FilterBox(window);
        CheckBox searchAll = SearchAllBox(window);
        Point filterTopLeft = filter.TranslatePoint(default, window)!.Value;
        Point checkTopLeft = searchAll.TranslatePoint(default, window)!.Value;

        checkTopLeft.Y.Should().BeGreaterThanOrEqualTo(filterTopLeft.Y + filter.Bounds.Height);
        checkTopLeft.X.Should().BeApproximately(filterTopLeft.X, 0.5);
    }

    [AvaloniaFact]
    public void Insert_buttons_follow_what_the_selected_row_has()
    {
        SpecialCharacterWindow window = Show(Context());
        Button code = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "InsertCodeButton");
        Button entity = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "InsertEntityButton");
        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single();

        grid.SelectedItem = window.Rows.First(r => r.Insert == "—");
        Dispatcher.UIThread.RunJobs();
        (code.IsEnabled, entity.IsEnabled).Should().Be((true, true));

        CategoryBox(window).SelectedItem = UnicodeBlocks.Blocks.Single(b => b.Name == "Arrows").DisplayName;
        Dispatcher.UIThread.RunJobs();
        grid.SelectedItem = window.Rows.First(r => r.Entity.Length == 0);
        Dispatcher.UIThread.RunJobs();
        (code.IsEnabled, entity.IsEnabled).Should().Be((true, false));
    }

    [AvaloniaFact]
    public void Double_click_inserts_the_character_unless_the_cell_content_option_is_on()
    {
        List<bool> remembered = new();
        SpecialCharacterWindow window = Show(Context() with { OnDoubleClickInsertsCellChanged = remembered.Add });
        var columns =window.GetVisualDescendants().OfType<DataGrid>().Single().Columns;
        CheckBox option = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "DoubleClickInsertsCell");

        option.IsChecked.Should().BeFalse();
        columns.Take(columns.Count - 1).Select(window.DoubleClickForm).Should().OnlyContain(f => f == SpecialCharacterInsertForm.Character);
        window.DoubleClickForm(columns[^1]).Should().BeNull("the favorite column only toggles the mark");

        option.IsChecked = true;

        remembered.Should().Equal(true);
        columns.Select(window.DoubleClickForm).Should().Equal(
            SpecialCharacterInsertForm.Character,
            SpecialCharacterInsertForm.Entity,
            SpecialCharacterInsertForm.Code,
            SpecialCharacterInsertForm.Hex,
            SpecialCharacterInsertForm.Dec,
            null,
            null);
    }

    [AvaloniaFact]
    public void Only_a_double_click_on_a_favorite_cell_toggles_the_mark_and_it_does_not_insert()
    {
        List<string> added = new();
        List<string> removed = new();
        List<SpecialCharacterChoice> inserted = new();
        SpecialCharacterWindow window = Show(Context() with
        {
            OnAddFavorite = added.Add,
            OnRemoveFavorite = removed.Add,
            OnInsert = inserted.Add,
        });
        Border mark = window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Cursor is not null && b.GetVisualDescendants().OfType<TextBlock>().Any());
        Point center = mark.TranslatePoint(new Point(mark.Bounds.Width / 2, mark.Bounds.Height / 2), window)!.Value;

        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        added.Should().BeEmpty("a single click does not toggle");

        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        added.Should().Equal("—");
        removed.Should().BeEmpty();
        window.Rows[0].FavoriteMark.Should().Be("✓");
        inserted.Should().BeEmpty("a double-click on the favorite column does not insert");
        window.DoubleClickForm(window.GetVisualDescendants().OfType<DataGrid>().Single().Columns[^1]).Should().BeNull();
    }

    [AvaloniaFact]
    public void Inserting_keeps_the_window_open()
    {
        List<SpecialCharacterChoice> inserted = new();
        SpecialCharacterWindow window = Show(Context() with { OnInsert = inserted.Add });
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = window.Rows.First(r => r.Insert == "—");
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        TextBlock symbol = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("symbol") && t.Text == "—");
        Point center = symbol.TranslatePoint(new Point(symbol.Bounds.Width / 2, symbol.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        inserted.Should().Equal(new SpecialCharacterChoice("—", "—"), new SpecialCharacterChoice("—", "—"));
        closed.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Each_form_inserts_the_matching_cell_content()
    {
        CharRow dash = new("—", "—", 0x2014, "U+2014", "&mdash;", "EM DASH");
        CharRow arrow = new("↔", "↔", 0x2194, "U+2194", string.Empty, "LEFT RIGHT ARROW");

        SpecialCharacterWindow.FormText(dash, SpecialCharacterInsertForm.Character).Should().Be("—");
        SpecialCharacterWindow.FormText(dash, SpecialCharacterInsertForm.Entity).Should().Be("&mdash;");
        SpecialCharacterWindow.FormText(dash, SpecialCharacterInsertForm.Code).Should().Be("U+2014");
        SpecialCharacterWindow.FormText(dash, SpecialCharacterInsertForm.Hex).Should().Be("&#x2014;");
        SpecialCharacterWindow.FormText(dash, SpecialCharacterInsertForm.Dec).Should().Be("&#8212;");
        SpecialCharacterWindow.FormText(arrow, SpecialCharacterInsertForm.Entity).Should().BeNull("the row has no entity");
    }

    [AvaloniaFact]
    public void Insert_character_escapes_markup_characters()
    {
        SpecialCharacterWindow.CharacterText(new CharRow("&", "&", 0x26, "U+0026", "&amp;", "AMPERSAND")).Should().Be("&amp;");
        SpecialCharacterWindow.CharacterText(new CharRow("<", "<", 0x3C, "U+003C", "&lt;", "LESS-THAN SIGN")).Should().Be("&lt;");
        SpecialCharacterWindow.CharacterText(new CharRow("—", "—", 0x2014, "U+2014", "&mdash;", "EM DASH")).Should().Be("—");
    }

    [AvaloniaFact]
    public void Insert_code_window_offers_hex_decimal_and_text_forms()
    {
        InsertCodeWindow.Forms(0x2014).Should().Be(("&#x2014;", "&#8212;", "U+2014"));

        InsertCodeWindow window = InsertCodeWindow.Build(0x2014);
        window.Show();
        Render(window);

        window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).Should().Contain(new[]
        {
            Strings.Format("InsertCodeWindow_Hex", "&#x2014;"),
            Strings.Format("InsertCodeWindow_Decimal", "&#8212;"),
            Strings.Format("InsertCodeWindow_Text", "U+2014"),
            Strings.Get("Common_Cancel"),
        });
    }
}
