using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The keymap editor: the tree (Main Menu with its submenus, panel groups, Other), the search and "Find Shortcut"
/// filters, adding shortcuts with or without taking them away from conflicting actions, removing and resetting.
/// </summary>
public sealed class KeymapViewModelTests
{
    private static (TestHost Host, KeymapViewModel Keymap) New()
    {
        TestHost host = new();
        return (host, new KeymapViewModel(host.Registry, host.Shortcuts));
    }

    private static KeymapNode Action(KeymapViewModel keymap, string id) => keymap.ActionNodes.Single(n => n.ActionId == id);

    private static IEnumerable<KeymapNode> All(IEnumerable<KeymapNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

    [Fact]
    public void The_tree_has_the_main_menu_the_panel_groups_and_other_actions_once_each()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act & Assert
        keymap.Roots[0].Name.Should().Be("Main Menu");
        keymap.Roots[0].Children.Select(c => c.Name).Take(2).Should().Equal("File", "Edit");
        keymap.Roots.Select(r => r.Name).Should().Contain("Book Browser").And.Contain("Clip Editor");
        keymap.ActionNodes.Select(n => n.ActionId).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(host.Registry.Actions.Select(a => a.Id));
        Action(keymap, AppActionIds.Save).Path.Should().Be("Main Menu | File");
    }

    [Fact]
    public void Search_all_files_of_same_type_action_is_available_under_Other_without_default_shortcut()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act
        KeymapNode action = Action(keymap, AppActionIds.SearchAllFilesOfSameType);

        // Assert
        action.Path.Should().Be("Other");
        action.Name.Should().Be("Search in All Files of Same Type");
        action.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void Search_all_text_files_action_is_available_under_Other_without_default_shortcut()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act
        KeymapNode action = Action(keymap, AppActionIds.SearchAllTextFiles);

        // Assert
        action.Path.Should().Be("Other");
        action.Name.Should().Be("Search in All Text Files");
        action.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void Clip_slots_are_labelled_with_their_slot_number()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act & Assert
        Action(keymap, AppActionIds.Clip(1)).Name.Should().Be("Clip 1");
        Action(keymap, AppActionIds.Clip(60)).Name.Should().Be("Clip 60");
    }

    [Fact]
    public void The_search_matches_names_and_shortcuts_and_hides_empty_groups()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act
        keymap.Filter = "ctrl + s";

        // Assert
        Action(keymap, AppActionIds.Save).IsVisible.Should().BeTrue();
        keymap.Roots[0].IsExpanded.Should().BeTrue();
        All(keymap.Roots).Where(n => !n.IsGroup && n.IsVisible)
            .Should().OnlyContain(n => n.ShortcutsText.Replace(" ", string.Empty).Contains("Ctrl+S", System.StringComparison.OrdinalIgnoreCase)
                || n.Name.Contains("ctrl + s", System.StringComparison.OrdinalIgnoreCase));

        // Act
        keymap.Filter = "no action has this name";

        // Assert
        keymap.Roots.Should().OnlyContain(r => !r.IsVisible);

        // Act
        keymap.Filter = string.Empty;

        // Assert
        keymap.Roots.Should().OnlyContain(r => r.IsVisible);
    }

    [Fact]
    public void Find_shortcut_shows_only_the_actions_having_it()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act
        keymap.ShortcutFilter = new KeyStrokeShortcut(new KeyGesture(Key.S, KeyModifiers.Control));

        // Assert
        All(keymap.Roots).Where(n => !n.IsGroup && n.IsVisible).Select(n => n.ActionId).Should().Equal(AppActionIds.Save);
        keymap.HasShortcutFilter.Should().BeTrue();
    }

    [Fact]
    public void Typing_in_the_search_box_drops_the_shortcut_filter()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;
        string typed = new Fixture().Create<string>();
        keymap.ShortcutFilter = new KeyStrokeShortcut(new KeyGesture(Key.S, KeyModifiers.Control));

        // Act
        keymap.Filter = typed;

        // Assert
        keymap.ShortcutFilter.Should().BeNull();
        keymap.HasShortcutFilter.Should().BeFalse();
        keymap.Filter.Should().Be(typed);
    }

    [Fact]
    public void Setting_the_shortcut_filter_clears_the_search_box()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;
        keymap.Filter = new Fixture().Create<string>();

        // Act
        keymap.ShortcutFilter = new KeyStrokeShortcut(new KeyGesture(Key.S, KeyModifiers.Control));

        // Assert
        keymap.Filter.Should().BeEmpty();
        All(keymap.Roots).Where(n => !n.IsGroup && n.IsVisible).Select(n => n.ActionId).Should().Equal(AppActionIds.Save);
    }

    [Fact]
    public void ClearFilters_drops_both_filters_and_shows_every_action()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;
        keymap.ShortcutFilter = new KeyStrokeShortcut(new KeyGesture(Key.S, KeyModifiers.Control));

        // Act
        keymap.ClearFilters();

        // Assert
        keymap.Filter.Should().BeEmpty();
        keymap.ShortcutFilter.Should().BeNull();
        All(keymap.Roots).Should().OnlyContain(n => n.IsVisible);

        // Act
        keymap.Filter = new Fixture().Create<string>();
        keymap.ClearFilters();

        // Assert
        keymap.Filter.Should().BeEmpty();
        All(keymap.Roots).Should().OnlyContain(n => n.IsVisible);
    }

    [Fact]
    public void Adding_a_conflicting_shortcut_lists_the_conflicts_and_can_take_it_away()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;
        KeymapNode find = Action(keymap, AppActionIds.Find);
        KeyStrokeShortcut save = new(new KeyGesture(Key.S, KeyModifiers.Control));

        // Act & Assert
        keymap.ConflictsFor(find, save).Should().ContainSingle().Which.Should().StartWith(Action(keymap, AppActionIds.Save).Name)
            .And.EndWith("(Main Menu | File)");

        // Act
        keymap.AddShortcut(find, save, removeConflicts: false);

        // Assert
        Action(keymap, AppActionIds.Save).Shortcuts.Should().Contain(save, "Leave keeps the shortcut on both actions");

        // Act
        keymap.RemoveShortcut(find, save);
        keymap.AddShortcut(find, save, removeConflicts: true);

        // Assert
        Action(keymap, AppActionIds.Save).Shortcuts.Should().NotContain(save);
        find.Shortcuts.Should().Contain(save);
        find.IsModified.Should().BeTrue();
        find.CanReset.Should().BeTrue();
    }

    [Fact]
    public void Reset_restores_the_defaults_of_one_or_all_actions()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;
        KeymapNode save = Action(keymap, AppActionIds.Save);
        IReadOnlyList<Shortcut> defaults = save.Shortcuts;
        MouseShortcut mouse = new(MouseShortcutButton.Forward, KeyModifiers.Shift);

        // Act
        keymap.AddShortcut(save, mouse, removeConflicts: false);

        // Assert
        save.ShortcutsText.Should().Contain(mouse.DisplayText);

        // Act
        keymap.ResetShortcuts(save);

        // Assert
        save.Shortcuts.Should().Equal(defaults);
        save.IsModified.Should().BeFalse();

        // Act
        keymap.RemoveAllShortcuts(save);
        keymap.ResetAll();

        // Assert
        save.Shortcuts.Should().Equal(defaults);
    }

    [Fact]
    public void ExpandAll_and_CollapseAll_change_every_group()
    {
        // Arrange
        (TestHost host, KeymapViewModel keymap) = New();
        using TestHost _ = host;

        // Act
        keymap.ExpandAll();

        // Assert
        All(keymap.Roots).Where(n => n.IsGroup).Should().OnlyContain(n => n.IsExpanded);

        // Act
        keymap.CollapseAll();

        // Assert
        All(keymap.Roots).Where(n => n.IsGroup).Should().OnlyContain(n => !n.IsExpanded);
    }
}
