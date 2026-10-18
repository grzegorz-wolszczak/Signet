using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="ToolbarManager"/>: default layout, editing, visibility, persistence, reset.</summary>
public sealed class ToolbarManagerTests
{
    [Fact]
    public void Default_toolbar_layout()
    {
        using TestHost host = new();

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
        ToolbarManager.DefaultRows.Select(r => r[0]).Should().Equal(
            ToolbarId.New, ToolbarId.Heading, ToolbarId.Clips, ToolbarId.Clips2);
        ToolbarManager.DefaultRows[0].Should().EndWith(ToolbarId.Tools);
        ToolbarManager.DefaultRows[1].Should().EndWith(ToolbarId.TextDirection);
    }

    [Fact]
    public void All_toolbars_start_in_the_top_area_in_the_default_rows()
    {
        using TestHost host = new();

        host.Toolbars.GetRows(ToolbarArea.Top).Should().BeEquivalentTo(ToolbarManager.DefaultRows, o => o.WithStrictOrdering());
        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().BeEmpty();
    }

    [Fact]
    public void Moving_a_toolbar_within_a_row_puts_it_before_the_given_one()
    {
        using TestHost host = new();

        bool moved = host.Toolbars.MoveToolbar(ToolbarId.Tools, ToolbarArea.Top, 0, ToolbarId.File);

        moved.Should().BeTrue();
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Take(3).Should().Equal(ToolbarId.New, ToolbarId.Tools, ToolbarId.File);
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Should().HaveCount(ToolbarManager.DefaultRows[0].Count);
    }

    [Fact]
    public void Moving_a_toolbar_without_an_anchor_appends_it_to_the_row()
    {
        using TestHost host = new();

        host.Toolbars.MoveToolbar(ToolbarId.New, ToolbarArea.Top, 1, before: null);

        host.Toolbars.GetRows(ToolbarArea.Top)[0][0].Should().Be(ToolbarId.File);
        host.Toolbars.GetRows(ToolbarArea.Top)[1].Should().EndWith(ToolbarId.New);
    }

    [Fact]
    public void Moving_a_toolbar_before_itself_changes_nothing()
    {
        using TestHost host = new();
        int raised = 0;
        host.Toolbars.PlacementChanged += (_, _) => raised++;

        host.Toolbars.MoveToolbar(ToolbarId.File, ToolbarArea.Top, 0, ToolbarId.File).Should().BeFalse();
        host.Toolbars.MoveToolbar(ToolbarId.File, ToolbarArea.Top, 0, ToolbarId.AddExisting).Should().BeFalse("it already sits right before it");

        raised.Should().Be(0);
    }

    [Fact]
    public void Moving_a_toolbar_to_a_new_row_in_the_empty_bottom_area_creates_it()
    {
        using TestHost host = new();

        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0).Should().BeTrue();

        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().HaveCount(1);
        host.Toolbars.GetRows(ToolbarArea.Bottom)[0].Should().Equal(ToolbarId.Format);
        host.Toolbars.GetRows(ToolbarArea.Top).SelectMany(r => r).Should().NotContain(ToolbarId.Format);
    }

    [Fact]
    public void A_new_row_can_be_inserted_above_between_and_below_existing_rows()
    {
        using TestHost host = new();

        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Top, 0);
        host.Toolbars.GetRows(ToolbarArea.Top)[0].Should().Equal(ToolbarId.Format);

        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Align, ToolbarArea.Top, 2);
        host.Toolbars.GetRows(ToolbarArea.Top)[2].Should().Equal(ToolbarId.Align);

        host.Toolbars.MoveToolbarToNewRow(ToolbarId.List, ToolbarArea.Top, 99);
        host.Toolbars.GetRows(ToolbarArea.Top)[^1].Should().Equal(ToolbarId.List);
    }

    [Fact]
    public void A_row_left_empty_by_a_move_disappears()
    {
        using TestHost host = new();
        int rows = host.Toolbars.GetRows(ToolbarArea.Top).Count;

        // The Clip Bar is alone in its default row — moving it away empties that row.
        host.Toolbars.MoveToolbar(ToolbarId.Clips, ToolbarArea.Top, 0, before: null);

        host.Toolbars.GetRows(ToolbarArea.Top).Should().HaveCount(rows - 1);
        host.Toolbars.GetRows(ToolbarArea.Top).SelectMany(r => r).Should().OnlyHaveUniqueItems().And.HaveCount(ToolbarManager.AllToolbars.Count);
    }

    [Fact]
    public void Dragging_a_lone_toolbar_to_a_new_row_next_to_itself_is_not_a_change()
    {
        using TestHost host = new();
        // After the Clip Bar leaves its row, the Clip Bar2 is the lone toolbar of the last row.
        host.Toolbars.MoveToolbar(ToolbarId.Clips, ToolbarArea.Top, 0, before: null);
        int last = host.Toolbars.GetRows(ToolbarArea.Top).Count - 1;
        host.Toolbars.GetRows(ToolbarArea.Top)[last].Should().Equal(ToolbarId.Clips2);

        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Clips2, ToolbarArea.Top, last).Should().BeFalse();
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Clips2, ToolbarArea.Top, last + 1).Should().BeFalse();
    }

    [Fact]
    public void Placement_survives_reopen()
    {
        using TestHost host = new();
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        host.Toolbars.MoveToolbar(ToolbarId.Align, ToolbarArea.Bottom, 0, before: ToolbarId.Format);
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Find, ToolbarArea.Top, 0);

        ToolbarManager reopened = new(host.ReopenSettings());

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
        using TestHost host = new();
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        host.Toolbars.MoveToolbar(ToolbarId.Format, ToolbarArea.Top, 1, ToolbarId.Align);

        host.ReopenSettings().GetStringMap(ToolbarManager.PlacementGroup).Should().BeEmpty();
    }

    [Fact]
    public void Placement_with_a_missing_toolbar_gets_it_back_in_the_last_top_row()
    {
        using TestHost host = new();
        host.Settings.SetStringMap(ToolbarManager.PlacementGroup, new Dictionary<string, string>
        {
            ["Top"] = "New,File;Heading,Bogus,New",
            ["Bottom"] = "Format,Format",
        });

        ToolbarManager sut = new(host.Settings);

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
        using TestHost host = new();
        host.Toolbars.MoveToolbarToNewRow(ToolbarId.Format, ToolbarArea.Bottom, 0);
        int raised = 0;
        host.Toolbars.PlacementChanged += (_, _) => raised++;

        host.Toolbars.ResetToDefaults();

        host.Toolbars.GetRows(ToolbarArea.Bottom).Should().BeEmpty();
        host.Toolbars.GetRows(ToolbarArea.Top).Should().BeEquivalentTo(ToolbarManager.DefaultRows, o => o.WithStrictOrdering());
        raised.Should().Be(1);
        host.ReopenSettings().GetStringMap(ToolbarManager.PlacementGroup).Should().BeEmpty();
    }

    [Fact]
    public void Every_default_layout_entry_references_a_real_action_or_separator()
    {
        using TestHost host = new();

        foreach (ToolbarId toolbar in ToolbarManager.AllToolbars)
        {
            foreach (string entry in ToolbarManager.GetDefaultItems(toolbar))
            {
                if (entry is ToolbarManager.Separator or ToolbarManager.HeadingsMenu or ToolbarManager.CaseMenu)
                {
                    continue;
                }

                host.Registry.Get(entry).Should().NotBeNull($"\"{entry}\" in toolbar {toolbar} must be a known action");
            }
        }
    }

    [Fact]
    public void Custom_layout_and_visibility_survive_reopen()
    {
        using TestHost host = new();
        host.Toolbars.SetItems(ToolbarId.Insert, new[] { AppActionIds.InsertId, ToolbarManager.Separator, AppActionIds.InsertHyperlink });
        host.Toolbars.SetVisible(ToolbarId.Format, false);

        ToolbarManager reopened = new(host.ReopenSettings());
        reopened.GetItems(ToolbarId.Insert).Should().Equal(AppActionIds.InsertId, ToolbarManager.Separator, AppActionIds.InsertHyperlink);
        reopened.IsVisible(ToolbarId.Format).Should().BeFalse();
    }

    [Fact]
    public void Reset_restores_defaults_and_clears_stored_overrides()
    {
        using TestHost host = new();
        host.Toolbars.SetItems(ToolbarId.File, new[] { AppActionIds.Save });
        host.Toolbars.SetVisible(ToolbarId.Clips, true);

        host.Toolbars.ResetToDefaults();

        host.Toolbars.GetItems(ToolbarId.File).Should().Equal(ToolbarManager.GetDefaultItems(ToolbarId.File));
        host.Toolbars.IsVisible(ToolbarId.Clips).Should().BeFalse();
        host.ReopenSettings().GetStringMap(ToolbarManager.LayoutGroup).Should().BeEmpty();
    }

    [Fact]
    public void Changed_event_fires_on_edit()
    {
        using TestHost host = new();
        ToolbarId? changed = null;
        host.Toolbars.Changed += (_, id) => changed = id;

        host.Toolbars.ToggleVisible(ToolbarId.Find);

        changed.Should().Be(ToolbarId.Find);
    }
}
