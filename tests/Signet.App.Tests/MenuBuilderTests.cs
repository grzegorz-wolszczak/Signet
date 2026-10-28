using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="MenuBuilder"/>: structure, action binding, the "Toolbars" submenu.</summary>
public sealed class MenuBuilderTests
{
    private static IReadOnlyList<MenuItemViewModel> Build(TestHost host)
    {
        MenuBuilder builder = new(host.Registry, host.Toolbars, new RelayCommand(() => { }));
        return builder.Build();
    }

    private static IEnumerable<MenuItemViewModel> Flatten(MenuItemViewModel item)
    {
        yield return item;
        foreach (MenuItemViewModel child in item.Items ?? Enumerable.Empty<MenuItemViewModel>())
        {
            foreach (MenuItemViewModel descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    // The submenu holding the action.
    private static MenuItemViewModel SubmenuOf(IReadOnlyList<MenuItemViewModel> menus, string actionId) =>
        menus.SelectMany(Flatten).Single(m => m.Items?.Any(i => i.ActionId == actionId) ?? false);

    [Fact]
    public void A_submenu_is_enabled_only_while_one_of_its_items_is()
    {
        // Arrange
        using TestHost host = new();
        foreach (AppAction action in host.Registry.Actions)
        {
            action.IsEnabled = true;
        }

        // Act
        IReadOnlyList<MenuItemViewModel> menus = Build(host);
        MenuItemViewModel[] bookOnly =
        {
            SubmenuOf(menus, AppActionIds.AddExistingFile), SubmenuOf(menus, AppActionIds.CasingLowercase),
            SubmenuOf(menus, AppActionIds.Heading1), SubmenuOf(menus, AppActionIds.FindNextInFile),
            SubmenuOf(menus, AppActionIds.GenerateToc), SubmenuOf(menus, AppActionIds.SpellcheckEditor),
            SubmenuOf(menus, AppActionIds.MendHtml), SubmenuOf(menus, AppActionIds.UpgradeToEpub3),
        };

        // Assert
        bookOnly.Should().OnlyContain(m => m.IsEnabled);

        // Act
        host.Registry.SetBookOpen(false);

        // Assert
        bookOnly.Should().OnlyContain(m => !m.IsEnabled, "all their actions need a book");
        SubmenuOf(menus, AppActionIds.NewDefault).IsEnabled.Should().BeTrue("File → New works without a book");
        menus.Should().OnlyContain(m => m.IsEnabled, "the top-level menus stay enabled");

        // Act
        host.Registry.SetBookOpen(true);

        // Assert
        bookOnly.Should().OnlyContain(m => m.IsEnabled);
    }

    [Fact]
    public void Builds_nine_top_level_menus()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();

        // Act & Assert
        Build(host).Select(m => m.Header).Should().Equal(
            "_File", "_Edit", "_Insert", "For_mat", "_Search", "_Tools", "_View", "_Window", "_Help");
    }

    [Fact]
    public void Every_leaf_has_a_command_and_every_separator_is_flagged()
    {
        // Arrange
        using TestHost host = new();

        // Act & Assert
        foreach (MenuItemViewModel node in Build(host).SelectMany(Flatten))
        {
            if (node.IsSeparator)
            {
                node.Header.Should().Be("-");
            }
            else if (node.Items is null)
            {
                node.Command.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public void Toolbars_submenu_lists_toggles_plus_customize()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuItemViewModel view = Build(host).Single(m => m.Header == "_View");
        MenuItemViewModel toolbars = view.Items!.Single(m => m.Header == "_Toolbars");

        // Act & Assert
        toolbars.Items!.Count(i => i.IsCheckable).Should().Be(Signet.App.Toolbars.ToolbarManager.AllToolbars.Count);
        toolbars.Items!.Should().Contain(i => i.Header == "_Customize Toolbars...");
    }

    [Fact]
    public void Toolbars_toggle_menu_item_tracks_manager_visibility()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuItemViewModel view = Build(host).Single(m => m.Header == "_View");
        MenuItemViewModel toolbars = view.Items!.Single(m => m.Header == "_Toolbars");
        MenuItemViewModel directionToggle = toolbars.Items!.Single(i => i.Header == "Text Direction");

        // Act & Assert
        directionToggle.IsChecked.Should().BeFalse();

        // Act
        host.Toolbars.SetVisible(Signet.App.Toolbars.ToolbarId.TextDirection, true);

        // Assert
        directionToggle.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Menu_headers_and_action_texts_follow_the_ui_language()
    {
        // Arrange
        using UiCultureScope culture = new("pl");
        using TestHost host = new();

        // Act
        IReadOnlyList<MenuItemViewModel> menu = Build(host);

        // Assert
        menu.Select(m => m.Header).Should().Equal(
            "_Plik", "_Edycja", "_Wstaw", "_Format", "_Szukaj", "Nar_zędzia", "Wi_dok", "_Okno", "Pomo_c");
        menu[0].Items!.Should().Contain(m => m.Header == "_Zapisz");
    }

    [Fact]
    public void Every_action_and_menu_header_has_a_polish_and_an_english_text()
    {
        foreach (string lang in new[] { "pl", "en" })
        {
            // Arrange
            using UiCultureScope culture = new(lang);
            foreach (AppActionDescriptor descriptor in AppActionCatalog.All.Where(d => !AppActionIds.TryGetClipSlot(d.Id, out _)))
            {
                // Act & Assert
                Strings.TryGet("Action_" + descriptor.Id.Replace('.', '_')).Should().NotBeNullOrEmpty(descriptor.Id);
                Strings.TryGet("ActionCategory_" + descriptor.Category.Replace(" ", string.Empty)).Should().NotBeNullOrEmpty(descriptor.Category);
            }
        }
    }

    private static readonly string[] StandardizedTidyingActions =
    {
        "Restructure Epub to Signet Norm", "Use Standard File Extensions",
        "Rebase OPF Manifest IDs on Current Filenames", "Update OPF Manifest Media Types",
    };

    [Fact]
    public void Tools_menu_has_one_Standardize_entry_instead_of_the_four_tidying_actions()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();

        // Act
        List<string> tools = Build(host).Single(m => m.Header == "_Tools").Items!.Select(i => i.Header).ToList();

        // Assert
        tools.Should().Contain("Standardize EPUB...");
        tools.Should().NotContain(StandardizedTidyingActions);
    }

    [Fact]
    public void Help_menu_contains_only_about()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();

        // Act & Assert
        Build(host).Single(m => m.Header == "_Help").Items!.Select(i => i.Header)
            .Should().Equal("_About...");
    }

    [Fact]
    public void Recent_files_are_a_flat_list_above_close()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new() { Files = new[] { "/books/a_b.epub", "/books/c.epub" } };

        // Act
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        List<string> headers = file.Items!.Select(i => i.Header).ToList();
        int print = headers.IndexOf("_Print...");

        // Assert
        headers.Skip(print + 1).Take(6).Should().Equal(
            "-", "_1. a__b.epub", "_2. c.epub", "Clear List", "-", "Close");
        file.Items!.Should().NotContain(i => i.Items != null && i.Header.Contains("Recent"));
    }

    [Fact]
    public void A_recent_file_shows_its_full_path_in_the_tooltip()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        using TempDir temp = new();
        string first = MakeFile(temp, "one", "a.epub");
        string second = MakeFile(temp, "two", "a.epub");
        FakeRecentFiles recent = new() { Files = new[] { first, second } };

        // Act
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");

        // Assert
        file.Items!.Where(i => i.Header.EndsWith(". a.epub", StringComparison.Ordinal)).Select(i => i.ToolTip)
            .Should().Equal(first, second);
        file.Items!.Where(i => i.Header.EndsWith(". a.epub", StringComparison.Ordinal)).Should().OnlyContain(i => !i.IsDimmed);
        file.Items!.Single(i => i.Header == "Close").ToolTip.Should().BeNull();
    }

    /// <summary>
    /// A file of the list that is no longer on disk is dimmed and its tooltip says why, but it stays enabled: the click
    /// is what offers to take it off the list.
    /// </summary>
    [Fact]
    public void A_recent_file_that_is_gone_is_dimmed_and_says_so_in_its_tooltip()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        using TempDir temp = new();
        string here = MakeFile(temp, "books", "here.epub");
        string gone = Path.Combine(temp.Path, "books", "gone.epub");
        FakeRecentFiles recent = new() { Files = new[] { here, gone } };

        // Act
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        MenuItemViewModel present = file.Items!.Single(i => i.Header.EndsWith(". here.epub", StringComparison.Ordinal));
        MenuItemViewModel missing = file.Items!.Single(i => i.Header.EndsWith(". gone.epub", StringComparison.Ordinal));

        // Assert
        present.IsDimmed.Should().BeFalse();
        present.Opacity.Should().Be(1.0);
        missing.IsDimmed.Should().BeTrue();
        missing.Opacity.Should().Be(0.5);
        missing.IsEnabled.Should().BeTrue("the click offers to remove the entry from the list");
        missing.ToolTip.Should().Be(gone + Environment.NewLine + Strings.Get("RecentFiles_FileMissingTooltip"));
    }

    [Fact]
    public void RefreshRecentFiles_notices_a_file_deleted_while_the_menu_was_already_built()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        using TempDir temp = new();
        string path = MakeFile(temp, "books", "a.epub");
        FakeRecentFiles recent = new() { Files = new[] { path } };
        MenuBuilder builder = new(host.Registry, host.Toolbars, new RelayCommand(() => { }), recent);
        MenuItemViewModel file = builder.Build().Single(m => m.Header == "_File");

        // Assert
        file.Items!.Single(i => i.Header.EndsWith(". a.epub", StringComparison.Ordinal)).IsDimmed.Should().BeFalse();

        // Act
        File.Delete(path);
        builder.RefreshRecentFiles();

        // Assert
        file.Items!.Single(i => i.Header.EndsWith(". a.epub", StringComparison.Ordinal)).IsDimmed.Should().BeTrue();
    }

    private static string MakeFile(TempDir temp, string folder, string name)
    {
        string dir = Path.Combine(temp.Path, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void A_long_recent_file_name_is_shortened_with_an_ellipsis_and_keeps_its_extension()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new()
        {
            Files = new[] { "/books/Kroki w nieznane 3 (Iskry) (1972) - Rozni.epub", "/books/exactly_forty_characters_long_names.epub" },
        };

        // Act
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        List<string> headers = file.Items!.Select(i => i.Header).ToList();

        // Assert
        headers.Should().Contain("_1. Kroki w nieznane 3 (Iskry) (1972)….epub");
        headers.Should().Contain("_2. exactly__forty__characters__long__names.epub");
    }

    [Fact]
    public void Recent_files_list_is_rebuilt_in_place_and_disappears_when_empty()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new() { Files = new[] { "a.epub" } };
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        int countWithOne = file.Items!.Count;

        // Act
        recent.Files = new[] { "a.epub", "b.epub", "c.epub" };
        recent.RaiseChanged();

        // Assert
        file.Items!.Count.Should().Be(countWithOne + 2);

        // Act
        recent.Files = System.Array.Empty<string>();
        recent.RaiseChanged();

        // Assert
        file.Items!.Count.Should().Be(countWithOne - 3);
        file.Items!.Select(i => i.Header).Should().NotContain(h => h.EndsWith(".epub"));
    }

    [Fact]
    public void A_recent_file_has_context_items_to_open_its_location_or_remove_it()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        string existingPath = typeof(MenuBuilderTests).Assembly.Location;
        FakeRecentFiles recent = new() { Files = new[] { existingPath, "/books/b.epub" } };
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");

        MenuItemViewModel first = file.Items!.Single(i => i.Header == $"_1. {Path.GetFileName(existingPath)}");
        MenuItemViewModel[] firstContextItems = first.ContextItems!.ToArray();
        firstContextItems.Select(i => i.Header).Should().Equal("Open File Location", "Remove from Recent Files");
        firstContextItems[0].Command!.CanExecute(null).Should().BeTrue("the file exists");

        MenuItemViewModel second = file.Items!.Single(i => i.Header == "_2. b.epub");
        MenuItemViewModel remove = second.ContextItems!.Should().ContainSingle().Subject;
        remove.Header.Should().Be("Remove from Recent Files");

        // Act
        remove.Command!.Execute(null);

        // Assert
        recent.Removed.Should().Equal("/books/b.epub");
        file.Items!.Single(i => i.Header == "Clear List").ContextItems.Should().BeNull();
    }

    private static IReadOnlyList<MenuItemViewModel> BuildWith(TestHost host, IRecentFilesMenu recent) =>
        new MenuBuilder(host.Registry, host.Toolbars, new RelayCommand(() => { }), recent).Build();

    private sealed class FakeRecentFiles : IRecentFilesMenu
    {
        public IReadOnlyList<string> Files { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> RecentFiles => Files;

        public event System.EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, System.EventArgs.Empty);

        public List<string> Removed { get; } = new();

        public void Open(string path)
        {
        }

        public void Remove(string path) => Removed.Add(path);

        public void ClearAll()
        {
        }
    }
}
