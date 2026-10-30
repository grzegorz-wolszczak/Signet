using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ToolbarManager"/>: default layout, editing, visibility, persistence, reset.</summary>
public sealed class ToolbarManagerTests
{
    [Fact]
    public void Default_toolbar_layout()
    {
        // Arrange
        using TestHost host = new();

        // Act & Assert
        ToolbarManager.AllToolbars.Should().HaveCount(18);
        host.Toolbars.GetItems(ToolbarId.UndoRedo).Should().Equal(AppActionIds.Undo, AppActionIds.Redo);
        host.Toolbars.GetItems(ToolbarId.Edit).Should().Equal(
            AppActionIds.Cut, AppActionIds.Copy, AppActionIds.Paste, AppActionIds.RemoveTagPair);
        host.Toolbars.GetItems(ToolbarId.Heading).Should().Equal(ToolbarManager.HeadingsMenu);
        host.Toolbars.GetItems(ToolbarId.ChangeCase).Should().Equal(ToolbarManager.CaseMenu);
        host.Toolbars.IsVisible(ToolbarId.Format).Should().BeTrue();
        host.Toolbars.IsVisible(ToolbarId.TextDirection).Should().BeFalse("the Text Direction toolbar is hidden by default");
        host.Toolbars.IsVisible(ToolbarId.Clips).Should().BeFalse("the Clips toolbar is hidden by default");
    }

    [Fact]
    public void Default_rows_break_before_Heading_and_the_clip_bars()
    {
        // Act & Assert
        ToolbarManager.DefaultRows.Select(r => r[0]).Should().Equal(
            ToolbarId.New, ToolbarId.Heading, ToolbarId.Clips, ToolbarId.Clips2);
        ToolbarManager.DefaultRows[0].Should().EndWith(ToolbarId.Tools);
        ToolbarManager.DefaultRows[1].Should().EndWith(ToolbarId.TextDirection);
    }

    [Fact]
    public void All_toolbars_start_in_the_top_area_in_the_default_rows()
    {
        // Arrange
        using TestHost host = new();

        // Act & Assert
        host.Toolbars.GetRows(ToolbarArea.Top).Should().BeEquivalentTo(ToolbarManager.DefaultRows, o => o.WithStrictOrdering());
        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().BeEmpty();
    }

    [Fact]
    public void Moving_a_toolbar_within_a_row_puts_it_before_the_given_one()
    {
        // Arrange
        using TestHost host = new();

        // Act
        bool moved = host.Toolbars.MoveToolbar(ToolbarId.Tools, ToolbarArea.Top, 0, ToolbarId.File);

        // Assert
        moved.Should().BeTrue();
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Take(3).Should().Equal(ToolbarId.New, ToolbarId.Tools, ToolbarId.File);
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Should().HaveCount(ToolbarManager.DefaultRows[0].Count);
    }

    [Fact]
    public void Moving_a_toolbar_without_an_anchor_appends_it_to_the_row()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.MoveToolbar(ToolbarId.New, ToolbarArea.Top, 1, before: null);

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Top)[0][0].Should().Be(ToolbarId.File);
        host.Toolbars.GetRows(ToolbarArea.Top)[1].Should().EndWith(ToolbarId.New);
    }

    [Fact]
    public void Moving_a_toolbar_before_itself_changes_nothing()
    {
        // Arrange
        using TestHost host = new();
        int raised = 0;
        host.Toolbars.PlacementChanged += (_, _) => raised++;

        // Act & Assert
        host.Toolbars.MoveToolbar(ToolbarId.File, ToolbarArea.Top, 0, ToolbarId.File).Should().BeFalse();
        host.Toolbars.MoveToolbar(ToolbarId.File, ToolbarArea.Top, 0, ToolbarId.AddExisting).Should().BeFalse("it already sits right before it");

        // Assert
        raised.Should().Be(0);
    }

    [Fact]
    public void Moving_a_toolbar_to_a_new_row_in_the_empty_bottom_area_creates_it()
    {
        // Arrange
        using TestHost host = new();

        // Act & Assert
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0).Should().BeTrue();

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().HaveCount(1);
        host.Toolbars.GetRows(ToolbarArea.Bottom)[0].Should().Equal(ToolbarId.Format);
        host.Toolbars.GetRows(ToolbarArea.Top).SelectMany(r => r).Should().NotContain(ToolbarId.Format);
    }

    [Fact]
    public void A_new_row_can_be_inserted_above_between_and_below_existing_rows()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Top, 0);

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Should().Equal(ToolbarId.Format);

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Align, ToolbarArea.Top, 2);

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Top)[2].Should().Equal(ToolbarId.Align);

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.List, ToolbarArea.Top, 99);

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Top)[^1].Should().Equal(ToolbarId.List);
    }

    [Fact]
    public void A_row_left_empty_by_a_move_disappears()
    {
        // Arrange
        using TestHost host = new();
        int rows = host.Toolbars.GetRows(ToolbarArea.Top).Count;

        // The Clip Bar is alone in its default row — moving it away empties that row.
        // Act
        host.Toolbars.MoveToolbar(ToolbarId.Clips, ToolbarArea.Top, 0, before: null);

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Top).Should().HaveCount(rows - 1);
        host.Toolbars.GetRows(ToolbarArea.Top).SelectMany(r => r).Should().OnlyHaveUniqueItems().And.HaveCount(ToolbarManager.AllToolbars.Count);
    }

    [Fact]
    public void Dragging_a_lone_toolbar_to_a_new_row_next_to_itself_is_not_a_change()
    {
        // Arrange
        using TestHost host = new();
        // After the Clip Bar leaves its row, the Clip Bar2 is the lone toolbar of the last row.
        host.Toolbars.MoveToolbar(ToolbarId.Clips, ToolbarArea.Top, 0, before: null);
        int last = host.Toolbars.GetRows(ToolbarArea.Top).Count - 1;

        host.Toolbars.GetRows(ToolbarArea.Top)[last].Should().Equal(ToolbarId.Clips2);

        // Act & Assert
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Clips2, ToolbarArea.Top, last).Should().BeFalse();
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Clips2, ToolbarArea.Top, last + 1).Should().BeFalse();
    }

    [Fact]
    public void Placement_survives_reopen()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        host.Toolbars.MoveToolbar(ToolbarId.Align, ToolbarArea.Bottom, 0, before: ToolbarId.Format);
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Find, ToolbarArea.Top, 0);

        ToolbarManager reopened = new(host.ReopenSettings());

        // Assert
        reopened.GetRows(ToolbarArea.Bottom).Should().BeEquivalentTo(
            host.Toolbars.GetRows(ToolbarArea.Bottom), o => o.WithStrictOrdering());
        reopened.GetRows(ToolbarArea.Top).Should().BeEquivalentTo(
            host.Toolbars.GetRows(ToolbarArea.Top), o => o.WithStrictOrdering());
        reopened.GetRows(ToolbarArea.Bottom)[0].Should().Equal(ToolbarId.Align, ToolbarId.Format);
        reopened.GetRows(ToolbarArea.Top)[0].Should().Equal(ToolbarId.Find);
    }

    [Fact]
    public void Default_placement_is_not_stored()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        host.Toolbars.MoveToolbar(ToolbarId.Format, ToolbarArea.Top, 1, ToolbarId.Align);

        // Act & Assert
        host.ReopenSettings().GetStringMap(ToolbarManager.PlacementGroup).Should().BeEmpty();
    }

    [Fact]
    public void Placement_with_a_missing_toolbar_gets_it_back_in_the_last_top_row()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.SetStringMap(ToolbarManager.PlacementGroup, new Dictionary<string, string>
        {
            ["Top"] = "New,File;Heading,Bogus,New",
            ["Bottom"] = "Format,Format",
        });

        // Act
        ToolbarManager sut = new(host.Settings);

        // Assert
        sut.GetRows(ToolbarArea.Top)[0].Should().Equal(ToolbarId.New, ToolbarId.File);
        sut.GetRows(ToolbarArea.Bottom).Should().HaveCount(1);
        sut.GetRows(ToolbarArea.Bottom)[0].Should().Equal(ToolbarId.Format);
        sut.GetRows(ToolbarArea.Top)[1].Take(1).Should().Equal(ToolbarId.Heading);
        sut.GetRows(ToolbarArea.Top).Concat(sut.GetRows(ToolbarArea.Bottom)).SelectMany(r => r)
            .Should().OnlyHaveUniqueItems().And.HaveCount(ToolbarManager.AllToolbars.Count);
    }

    [Fact]
    public void Reset_restores_the_default_placement()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        int raised = 0;
        host.Toolbars.PlacementChanged += (_, _) => raised++;

        host.Toolbars.ResetToDefaults();

        // Assert
        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().BeEmpty();
        host.Toolbars.GetRows(ToolbarArea.Top).Should().BeEquivalentTo(ToolbarManager.DefaultRows, o => o.WithStrictOrdering());
        raised.Should().Be(1);
        host.ReopenSettings().GetStringMap(ToolbarManager.PlacementGroup).Should().BeEmpty();
    }

    [Fact]
    public void Every_default_layout_entry_references_a_real_action_or_separator()
    {
        // Arrange
        using TestHost host = new();

        // Act
        foreach (ToolbarId toolbar in ToolbarManager.AllToolbars)
        {
            foreach (string entry in ToolbarManager.GetDefaultItems(toolbar))
            {
                if (entry is ToolbarManager.Separator or ToolbarManager.HeadingsMenu or ToolbarManager.CaseMenu)
                {
                    continue;
                }

                // Assert
                host.Registry.Get(entry).Should().NotBeNull($"\"{entry}\" in toolbar {toolbar} must be a known action");
            }
        }
    }

    [Fact]
    public void Custom_layout_and_visibility_survive_reopen()
    {
        // Arrange
        using TestHost host = new();

        // Act
        host.Toolbars.SetItems(ToolbarId.Insert, new[] { AppActionIds.InsertId, ToolbarManager.Separator, AppActionIds.InsertHyperlink });
        host.Toolbars.SetVisible(ToolbarId.Format, false);

        ToolbarManager reopened = new(host.ReopenSettings());

        // Assert
        reopened.GetItems(ToolbarId.Insert).Should().Equal(AppActionIds.InsertId, ToolbarManager.Separator, AppActionIds.InsertHyperlink);
        reopened.IsVisible(ToolbarId.Format).Should().BeFalse();
    }

    [Fact]
    public void Reset_restores_defaults_and_clears_stored_overrides()
    {
        // Arrange
        using TestHost host = new();
        host.Toolbars.SetItems(ToolbarId.File, new[] { AppActionIds.Save });
        host.Toolbars.SetVisible(ToolbarId.Clips, true);

        // Act
        host.Toolbars.ResetToDefaults();

        // Assert
        host.Toolbars.GetItems(ToolbarId.File).Should().Equal(ToolbarManager.GetDefaultItems(ToolbarId.File));
        host.Toolbars.IsVisible(ToolbarId.Clips).Should().BeFalse();
        host.ReopenSettings().GetStringMap(ToolbarManager.LayoutGroup).Should().BeEmpty();
    }

    [Fact]
    public void Changed_event_fires_on_edit()
    {
        // Arrange
        using TestHost host = new();
        ToolbarId? changed = null;
        host.Toolbars.Changed += (_, id) => changed = id;

        // Act
        host.Toolbars.ToggleVisible(ToolbarId.Find);

        // Assert
        changed.Should().Be(ToolbarId.Find);
    }

    [Fact]
    public void Hiding_all_toolbars_is_saved_raises_one_change_and_keeps_each_toolbars_visibility()
    {
        // Arrange
        using TestHost host = new();
        int raised = 0;
        host.Toolbars.AllHiddenChanged += (_, _) => raised++;

        // Act
        host.Toolbars.AllHidden = true;
        host.Toolbars.AllHidden = true;

        // Assert
        raised.Should().Be(1);
        host.ReopenSettings().ToolbarsAllHidden.Should().BeTrue();
        new ToolbarManager(host.ReopenSettings()).AllHidden.Should().BeTrue();
        host.Toolbars.IsVisible(ToolbarId.Format).Should().BeTrue("the toolbar's own visibility does not change");
    }

    [Fact]
    public void Changing_the_look_saves_it_and_raises_AppearanceChanged_once()
    {
        // Arrange
        using TestHost host = new();
        int raised = 0;
        host.Toolbars.AppearanceChanged += (_, _) => raised++;

        // Act
        host.Toolbars.SetIconSize(ToolbarIconSize.Small);

        // Assert
        raised.Should().Be(1);
        host.Toolbars.Appearance.IconSize.Should().Be(ToolbarIconSize.Small);
        host.ReopenSettings().ToolbarAppearance.IconSize.Should().Be(ToolbarIconSize.Small);

        // Act
        host.Toolbars.SetIconSize(ToolbarIconSize.Small);

        // Assert
        raised.Should().Be(1, "setting the same value changes nothing");
    }

    [Fact]
    public void A_look_committed_from_the_preferences_draft_raises_AppearanceChanged()
    {
        // Arrange
        using TestHost host = new();
        SettingsStore draft = host.Settings.CreateDraft();
        draft.ToolbarAppearance = draft.ToolbarAppearance with { FontSize = 15, TextPosition = ToolbarTextPosition.TextBesideIcons };
        int raised = 0;
        host.Toolbars.AppearanceChanged += (_, _) => raised++;

        // Act
        host.Settings.CommitDraft(draft);

        // Assert
        raised.Should().Be(1);
        host.Toolbars.Appearance.FontSize.Should().Be(15);
        host.Toolbars.Appearance.TextPosition.Should().Be(ToolbarTextPosition.TextBesideIcons);
    }

    [Fact]
    public void Restoring_the_appearance_defaults_raises_AppearanceChanged()
    {
        // Arrange
        using TestHost host = new();
        host.Toolbars.SetPositionsLocked(true);
        int raised = 0;
        host.Toolbars.AppearanceChanged += (_, _) => raised++;

        // Act
        host.Settings.ClearAppearanceSettings();

        // Assert
        raised.Should().Be(1);
        host.Toolbars.Appearance.Should().Be(ToolbarAppearance.Default);
    }
}
