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
    public void Rows_break_before_Heading_and_the_clip_bars()
    {
        ToolbarManager.Rows.Select(r => r[0]).Should().Equal(
            ToolbarId.New, ToolbarId.Heading, ToolbarId.Clips, ToolbarId.Clips2);
        ToolbarManager.Rows[0].Should().EndWith(ToolbarId.Tools);
        ToolbarManager.Rows[1].Should().EndWith(ToolbarId.TextDirection);
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
