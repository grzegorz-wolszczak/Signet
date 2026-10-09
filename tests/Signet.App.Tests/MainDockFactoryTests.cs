using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Restoring the side panel widths (Book Browser / Preview) after a restart:
/// <see cref="MainDockFactory.ApplySideRegionWidths"/>.
/// </summary>
public sealed class MainDockFactoryTests
{
    private static MainDockFactory NewFactory()
    {
        MainDockFactory factory = new();
        factory.InitLayout(factory.CreateLayout());
        return factory;
    }

    private static double Width(MainDockFactory factory, string dockId) =>
        factory.CaptureLayoutState().Regions.Single(r => r.DockId == dockId).Proportion;

    private static DockLayoutState Saved(double left, double right, double documents = 0.72, double bottom = 0.28) =>
        new(
            new[]
            {
                new DockRegionState("LeftDock", left, new[] { DockableIds.BookBrowser }, DockableIds.BookBrowser),
                new DockRegionState("RightDock", right, new[] { DockableIds.Preview }, DockableIds.Preview),
                new DockRegionState("BottomDock", bottom, Array.Empty<string>(), null),
            },
            documents);

    [Fact]
    public void Saved_side_widths_survive_a_json_round_trip_into_a_fresh_layout()
    {
        MainDockFactory before = NewFactory();
        before.ApplySideRegionWidths(Saved(0.22, 0.33));
        string json = JsonSerializer.Serialize(before.CaptureLayoutState());

        MainDockFactory after = NewFactory();
        after.ApplySideRegionWidths(JsonSerializer.Deserialize<DockLayoutState>(json)!);

        Width(after, "LeftDock").Should().BeApproximately(0.22, 1e-9);
        Width(after, "RightDock").Should().BeApproximately(0.33, 1e-9);
    }

    [Fact]
    public void Bottom_region_spans_the_whole_width_under_the_side_regions()
    {
        // Collapsed ("Auto Hide") bottom panels slide out along the whole window, so the docked bottom region is
        // as wide too — pinning a panel back does not move it into the middle column.
        MainDockFactory factory = new();
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);

        IProportionalDock layout = root.VisibleDockables!.OfType<IProportionalDock>().Single();
        layout.Orientation.Should().Be(Orientation.Vertical);
        IDockable[] parts = layout.VisibleDockables!.Where(d => d is not IProportionalDockSplitter).ToArray();
        parts.Should().HaveCount(2);
        IProportionalDock row = parts[0].Should().BeAssignableTo<IProportionalDock>().Subject;
        row.Orientation.Should().Be(Orientation.Horizontal);
        row.VisibleDockables!.Where(d => d is not IProportionalDockSplitter).Select(d => d.Id)
            .Should().Equal("LeftDock", DockableIds.Documents, "RightDock");
        parts[1].Id.Should().Be("BottomDock");
    }

    [Fact]
    public void Captured_layout_state_has_no_unset_proportions()
    {
        // The document area has no proportion of its own (NaN) — the snapshot must still be valid JSON.
        DockLayoutState state = NewFactory().CaptureLayoutState();

        double.IsNaN(state.DocumentsProportion).Should().BeFalse();
        state.Regions.Should().OnlyContain(r => !double.IsNaN(r.Proportion));
    }

    [Fact]
    public void Vertical_proportions_are_left_at_defaults_even_when_bottom_was_saved_collapsed()
    {
        MainDockFactory sut = NewFactory();
        double defaultDocuments = sut.DocumentDock!.Proportion;

        // State saved with Validation Results hidden: documents ≈ 1, bottom = 0.
        sut.ApplySideRegionWidths(Saved(0.2, 0.3, documents: 0.9999999999999999, bottom: 0));

        sut.DocumentDock!.Proportion.Should().Be(defaultDocuments);
    }

    [Fact]
    public void Degenerate_or_missing_widths_keep_the_default()
    {
        MainDockFactory sut = NewFactory();
        double defaultLeft = Width(sut, "LeftDock");
        double defaultRight = Width(sut, "RightDock");

        sut.ApplySideRegionWidths(Saved(0, double.NaN));

        Width(sut, "LeftDock").Should().Be(defaultLeft);
        Width(sut, "RightDock").Should().Be(defaultRight);
    }

    [Fact]
    public void Extreme_widths_are_clamped_so_the_center_column_keeps_room()
    {
        MainDockFactory sut = NewFactory();

        sut.ApplySideRegionWidths(Saved(0.001, 0.95));
        Width(sut, "LeftDock").Should().BeGreaterThanOrEqualTo(0.08);
        Width(sut, "RightDock").Should().BeLessThanOrEqualTo(0.6);

        sut.ApplySideRegionWidths(Saved(0.55, 0.55));
        (Width(sut, "LeftDock") + Width(sut, "RightDock")).Should().BeLessThanOrEqualTo(0.8 + 1e-9);
    }

    private static (MainDockFactory Factory, IRootDock Root) NewWithRoot()
    {
        MainDockFactory factory = new();
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);
        return (factory, root);
    }

    private static IDockable Tool(MainDockFactory factory, IRootDock root, string id) =>
        factory.FindDockable(root, d => d.Id == id)!;

    [Fact]
    public void Auto_hidden_panel_opens_inline_instead_of_overlaying_the_native_preview()
    {
        (_, IRootDock root) = NewWithRoot();

        root.PinnedDockDisplayMode.Should().Be(PinnedDockDisplayMode.Inline);
    }

    [Fact]
    public void Auto_hidden_panel_is_captured_as_pinned_not_as_visible()
    {
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        sut.PinDockable(Tool(sut, root, DockableIds.TableOfContents));

        sut.IsToolPinned(DockableIds.TableOfContents).Should().BeTrue();
        sut.CaptureToolVisibility()[DockableIds.TableOfContents].Should().Be(MainDockFactory.PinnedState);
        sut.CaptureToolVisibility()[DockableIds.Preview].Should().Be("1");
    }

    [Fact]
    public void Closing_a_panel_hides_it_and_the_view_menu_toggle_shows_it_again()
    {
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        IDockable toc = Tool(sut, root, DockableIds.TableOfContents);

        sut.CloseDockable(toc);

        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse();
        sut.CaptureToolVisibility()[DockableIds.TableOfContents].Should().Be("0");
        sut.ToggleTool(DockableIds.TableOfContents).Should().BeTrue();
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
    }

    [Fact]
    public void Panels_can_be_closed()
    {
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        sut.ToolIds.Where(sut.IsToolVisible)
            .Select(id => Tool(sut, root, id))
            .Should().OnlyContain(t => t.CanClose);
    }

    [Fact]
    public void Pinned_state_is_restored_on_next_start()
    {
        (MainDockFactory before, IRootDock beforeRoot) = NewWithRoot();
        before.PinDockable(Tool(before, beforeRoot, DockableIds.TableOfContents));
        IReadOnlyDictionary<string, string> saved = before.CaptureToolVisibility();

        (MainDockFactory after, IRootDock afterRoot) = NewWithRoot();
        after.ApplyToolVisibility(saved);

        after.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
        after.IsToolPinned(DockableIds.TableOfContents).Should().BeTrue();
        afterRoot.RightPinnedDockables!.Select(d => d.Id).Should().Contain(DockableIds.TableOfContents);
        after.IsToolPinned(DockableIds.Preview).Should().BeFalse();
    }

    [Fact]
    public void Slid_out_size_of_an_auto_hidden_panel_is_restored_on_next_start()
    {
        (MainDockFactory before, IRootDock beforeRoot) = NewWithRoot();
        IDockable preview = Tool(before, beforeRoot, DockableIds.Preview);
        before.PinDockable(preview);
        preview.SetPinnedBounds(0, 0, 640.5, 900);
        IReadOnlyDictionary<string, string> saved = before.CapturePinnedSizes();

        (MainDockFactory after, IRootDock afterRoot) = NewWithRoot();
        IDockable restored = Tool(after, afterRoot, DockableIds.Preview);
        after.ApplyToolVisibility(before.CaptureToolVisibility());
        after.ApplyPinnedSizes(saved);

        after.IsToolPinned(DockableIds.Preview).Should().BeTrue();
        restored.GetPinnedBounds(out _, out _, out double width, out double height);
        (width, height).Should().Be((640.5, 900));
        saved.Should().ContainSingle("only panels with a known slid-out size are saved");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("10;900")]
    [InlineData("640;NaN")]
    [InlineData("640")]
    public void Unusable_saved_slid_out_sizes_are_ignored(string stored)
    {
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        sut.ApplyPinnedSizes(new Dictionary<string, string> { [DockableIds.Preview] = stored });

        sut.CapturePinnedSizes().Should().BeEmpty();
        Tool(sut, root, DockableIds.Preview).GetPinnedBounds(out _, out _, out double width, out _);
        (double.IsFinite(width) && width >= 50).Should().BeFalse();
    }

    [Theory]
    [InlineData("1")]
    [InlineData(MainDockFactory.PinnedState)]
    public void Validation_results_always_start_hidden_whatever_was_saved(string saved)
    {
        (MainDockFactory sut, _) = NewWithRoot();

        sut.ApplyToolVisibility(new Dictionary<string, string> { [DockableIds.ValidationResults] = saved });

        sut.IsToolVisible(DockableIds.ValidationResults).Should().BeFalse();
        sut.IsToolPinned(DockableIds.ValidationResults).Should().BeFalse();
        sut.CaptureToolVisibility()[DockableIds.ValidationResults].Should().Be("0");
    }

    [Fact]
    public void Toggling_an_auto_hidden_panel_hides_it_and_toggling_again_docks_it_back()
    {
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        sut.ToggleTool(DockableIds.ValidationResults).Should().BeTrue();
        sut.PinDockable(Tool(sut, root, DockableIds.ValidationResults));

        sut.ToggleTool(DockableIds.ValidationResults).Should().BeFalse();
        sut.CaptureToolVisibility()[DockableIds.ValidationResults].Should().Be("0");
        root.BottomPinnedDockables.Should().BeNullOrEmpty();

        sut.ToggleTool(DockableIds.ValidationResults).Should().BeTrue();
        sut.IsToolPinned(DockableIds.ValidationResults).Should().BeFalse();
        Tool(sut, root, DockableIds.ValidationResults).Owner!.Id.Should().Be("BottomDock");
    }
}
