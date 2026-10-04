using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
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

    [Fact]
    public void Builds_nine_top_level_menus()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();

        Build(host).Select(m => m.Header).Should().Equal(
            "_File", "_Edit", "_Insert", "For_mat", "_Search", "_Tools", "_View", "_Window", "_Help");
    }

    [Fact]
    public void Every_leaf_has_a_command_and_every_separator_is_flagged()
    {
        using TestHost host = new();

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
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuItemViewModel view = Build(host).Single(m => m.Header == "_View");
        MenuItemViewModel toolbars = view.Items!.Single(m => m.Header == "_Toolbars");

        toolbars.Items!.Count(i => i.IsCheckable).Should().Be(Signet.App.Toolbars.ToolbarManager.AllToolbars.Count);
        toolbars.Items!.Should().Contain(i => i.Header == "_Customize Toolbars...");
    }

    [Fact]
    public void Toolbars_toggle_menu_item_tracks_manager_visibility()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuItemViewModel view = Build(host).Single(m => m.Header == "_View");
        MenuItemViewModel toolbars = view.Items!.Single(m => m.Header == "_Toolbars");
        MenuItemViewModel directionToggle = toolbars.Items!.Single(i => i.Header == "Text Direction");

        directionToggle.IsChecked.Should().BeFalse();
        host.Toolbars.SetVisible(Signet.App.Toolbars.ToolbarId.TextDirection, true);
        directionToggle.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Menu_headers_and_action_texts_follow_the_ui_language()
    {
        using UiCultureScope culture = new("pl");
        using TestHost host = new();

        IReadOnlyList<MenuItemViewModel> menu = Build(host);

        menu.Select(m => m.Header).Should().Equal(
            "_Plik", "_Edycja", "_Wstaw", "_Format", "_Szukaj", "Nar_zędzia", "Wi_dok", "_Okno", "Pomo_c");
        menu[0].Items!.Should().Contain(m => m.Header == "_Zapisz");
    }

    [Fact]
    public void Every_action_and_menu_header_has_a_polish_and_an_english_text()
    {
        foreach (string lang in new[] { "pl", "en" })
        {
            using UiCultureScope culture = new(lang);
            foreach (AppActionDescriptor descriptor in AppActionCatalog.All.Where(d => !AppActionIds.TryGetClipSlot(d.Id, out _)))
            {
                Strings.TryGet("Action_" + descriptor.Id.Replace('.', '_')).Should().NotBeNullOrEmpty(descriptor.Id);
                Strings.TryGet("ActionCategory_" + descriptor.Category.Replace(" ", string.Empty)).Should().NotBeNullOrEmpty(descriptor.Category);
            }
        }
    }

    [Fact]
    public void Help_menu_contains_only_about()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();

        Build(host).Single(m => m.Header == "_Help").Items!.Select(i => i.Header)
            .Should().Equal("_About...");
    }

    [Fact]
    public void Recent_files_are_a_flat_list_above_close()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new() { Files = new[] { "/books/a_b.epub", "/books/c.epub" } };

        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        List<string> headers = file.Items!.Select(i => i.Header).ToList();
        int print = headers.IndexOf("_Print...");

        headers.Skip(print + 1).Take(6).Should().Equal(
            "-", "_1 a__b.epub", "_2 c.epub", "Clear List", "-", "Close");
        file.Items!.Should().NotContain(i => i.Items != null && i.Header.Contains("Recent"));
    }

    [Fact]
    public void A_long_recent_file_name_is_shortened_with_an_ellipsis_and_keeps_its_extension()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new()
        {
            Files = new[] { "/books/Kroki w nieznane 3 (Iskry) (1972) - Rozni.epub", "/books/exactly_forty_characters_long_names.epub" },
        };

        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        List<string> headers = file.Items!.Select(i => i.Header).ToList();

        headers.Should().Contain("_1 Kroki w nieznane 3 (Iskry) (1972)….epub");
        headers.Should().Contain("_2 exactly__forty__characters__long__names.epub");
    }

    [Fact]
    public void Recent_files_list_is_rebuilt_in_place_and_disappears_when_empty()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        FakeRecentFiles recent = new() { Files = new[] { "a.epub" } };
        MenuItemViewModel file = BuildWith(host, recent).Single(m => m.Header == "_File");
        int countWithOne = file.Items!.Count;

        recent.Files = new[] { "a.epub", "b.epub", "c.epub" };
        recent.RaiseChanged();
        file.Items!.Count.Should().Be(countWithOne + 2);

        recent.Files = System.Array.Empty<string>();
        recent.RaiseChanged();
        file.Items!.Count.Should().Be(countWithOne - 3);
        file.Items!.Select(i => i.Header).Should().NotContain(h => h.EndsWith(".epub"));
    }

    private static IReadOnlyList<MenuItemViewModel> BuildWith(TestHost host, IRecentFilesMenu recent) =>
        new MenuBuilder(host.Registry, host.Toolbars, new RelayCommand(() => { }), recent).Build();

    private sealed class FakeRecentFiles : IRecentFilesMenu
    {
        public IReadOnlyList<string> Files { get; set; } = System.Array.Empty<string>();

        public IReadOnlyList<string> RecentFiles => Files;

        public event System.EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, System.EventArgs.Empty);

        public void Open(string path)
        {
        }

        public void ClearAll()
        {
        }
    }
}
