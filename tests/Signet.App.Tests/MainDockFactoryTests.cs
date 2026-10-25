using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.Core.Misc;
using Signet.App.Tests.TestSupport;
using Signet.Core.Resources;
using Signet.Core.BookManipulation;
using Signet.App.ViewModels;
using Signet.App.Services;
using Signet.Core.Tests.TestSupport;
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

    /// <summary>
    /// The Preview panel is one while the file it shows follows the active tab, so its title says which file that is —
    /// and goes back to the bare name of the panel when the preview is emptied.
    /// </summary>
    [Fact]
    public void The_preview_panel_says_in_its_title_which_file_it_shows()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        using PreviewViewModel preview = new(new StatusBarService()) { DebounceInterval = TimeSpan.Zero };
        MainDockFactory factory = new(preview: preview);
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);
        HtmlResource chapter = book.GetHtmlResources().First();

        // Assert
        PreviewTitleOf(root).Should().Be("Preview", "nothing is shown yet");

        // Act
        preview.SetBook(book);
        preview.ShowResource(chapter);

        // Assert
        string name = chapter.BookPath[(chapter.BookPath.LastIndexOf('/') + 1)..];
        factory.PreviewTitle.Should().Be("Preview [" + name + "]");
        PreviewTitleOf(root).Should().Be("Preview [" + name + "]");

        // Act
        preview.Clear();

        // Assert
        PreviewTitleOf(root).Should().Be("Preview");
    }

    [Fact]
    public void The_preview_panel_keeps_the_name_of_its_file_after_a_language_change()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        using PreviewViewModel preview = new(new StatusBarService()) { DebounceInterval = TimeSpan.Zero };
        MainDockFactory factory = new(preview: preview);
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);
        preview.SetBook(book);
        preview.ShowResource(book.GetHtmlResources().First());

        // Act
        factory.RefreshTitles();

        // Assert
        PreviewTitleOf(root).Should().StartWith("Preview [").And.EndWith("]");
    }

    // The title the Preview panel shows in the layout (its tab header / title bar).
    private static string? PreviewTitleOf(IDock root) =>
        Flatten(root).OfType<Signet.App.Docking.PreviewTool>().Single().Title;

    private static IEnumerable<IDockable> Flatten(IDockable dockable)
    {
        yield return dockable;
        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                foreach (IDockable descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    [Fact]
    public void Saved_side_widths_survive_a_settings_round_trip_into_a_fresh_layout()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        MainDockFactory before = NewFactory();

        // Act
        before.ApplySideRegionWidths(Saved(0.22, 0.33));
        SettingsStore settings = new(path);
        settings.MainWindowDockLayout = before.CaptureLayoutState();
        settings.Save();

        MainDockFactory after = NewFactory();
        after.ApplySideRegionWidths(new SettingsStore(path).MainWindowDockLayout!);

        // Assert
        Width(after, "LeftDock").Should().BeApproximately(0.22, 1e-9);
        Width(after, "RightDock").Should().BeApproximately(0.33, 1e-9);
    }

    [Fact]
    public void The_dock_layout_is_stored_as_structure_in_the_settings_file()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        DockLayoutState state = Saved(0.22, 0.33);
        SettingsStore settings = new(path);

        // Act
        settings.MainWindowDockLayout = state;
        settings.Save();

        DockLayoutState reloaded = new SettingsStore(path).MainWindowDockLayout!;

        // Assert
        reloaded.Should().BeEquivalentTo(state, o => o.WithStrictOrdering());
        System.IO.File.ReadAllText(path).Should().Contain("<object key=\"main_window_dock_layout\">").And.NotContain("{");
    }

    [Fact]
    public void Bottom_region_spans_the_whole_width_under_the_side_regions()
    {
        // Collapsed ("Auto Hide") bottom panels slide out along the whole window, so the docked bottom region is
        // as wide too — pinning a panel back does not move it into the middle column.
        // Arrange
        MainDockFactory factory = new();

        // Act
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);

        IProportionalDock layout = root.VisibleDockables!.OfType<IProportionalDock>().Single();

        // Assert
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
        // Act
        DockLayoutState state = NewFactory().CaptureLayoutState();

        // Assert
        double.IsNaN(state.DocumentsProportion).Should().BeFalse();
        state.Regions.Should().OnlyContain(r => !double.IsNaN(r.Proportion));
    }

    [Fact]
    public void Vertical_proportions_are_left_at_defaults_even_when_bottom_was_saved_collapsed()
    {
        // Arrange
        MainDockFactory sut = NewFactory();
        double defaultDocuments = sut.DocumentDock!.Proportion;

        // State saved with Validation Results hidden: documents ≈ 1, bottom = 0.
        // Act
        sut.ApplySideRegionWidths(Saved(0.2, 0.3, documents: 0.9999999999999999, bottom: 0));

        // Assert
        sut.DocumentDock!.Proportion.Should().Be(defaultDocuments);
    }

    [Fact]
    public void Degenerate_or_missing_widths_keep_the_default()
    {
        // Arrange
        MainDockFactory sut = NewFactory();
        double defaultLeft = Width(sut, "LeftDock");
        double defaultRight = Width(sut, "RightDock");

        // Act
        sut.ApplySideRegionWidths(Saved(0, double.NaN));

        // Assert
        Width(sut, "LeftDock").Should().Be(defaultLeft);
        Width(sut, "RightDock").Should().Be(defaultRight);
    }

    [Fact]
    public void Extreme_widths_are_clamped_so_the_center_column_keeps_room()
    {
        // Arrange
        MainDockFactory sut = NewFactory();

        // Act
        sut.ApplySideRegionWidths(Saved(0.001, 0.95));

        // Assert
        Width(sut, "LeftDock").Should().BeGreaterThanOrEqualTo(0.08);
        Width(sut, "RightDock").Should().BeLessThanOrEqualTo(0.6);

        // Act
        sut.ApplySideRegionWidths(Saved(0.55, 0.55));

        // Assert
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
        // Arrange
        (_, IRootDock root) = NewWithRoot();

        // Act & Assert
        root.PinnedDockDisplayMode.Should().Be(PinnedDockDisplayMode.Inline);
    }

    [Fact]
    public void Auto_hidden_panel_is_captured_as_pinned_not_as_visible()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        // Act
        sut.PinDockable(Tool(sut, root, DockableIds.TableOfContents));

        // Assert
        sut.IsToolPinned(DockableIds.TableOfContents).Should().BeTrue();
        sut.CaptureToolVisibility()[DockableIds.TableOfContents].Should().Be(MainDockFactory.PinnedState);
        sut.CaptureToolVisibility()[DockableIds.Preview].Should().Be("1");
    }

    [Fact]
    public void Closing_a_panel_hides_it_and_the_view_menu_toggle_shows_it_again()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        IDockable toc = Tool(sut, root, DockableIds.TableOfContents);

        // Act
        sut.CloseDockable(toc);

        // Assert
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse();
        sut.CaptureToolVisibility()[DockableIds.TableOfContents].Should().Be("0");

        // Act
        sut.ToggleTool(DockableIds.TableOfContents).Should().BeTrue();

        // Assert
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
    }

    [Fact]
    public void Panels_can_be_closed()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        // Act & Assert
        sut.ToolIds.Where(sut.IsToolVisible)
            .Select(id => Tool(sut, root, id))
            .Should().OnlyContain(t => t.CanClose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Showing_a_panel_collapsed_in_auto_hide_slides_it_out(bool viaExpand)
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        sut.PinDockable(Tool(sut, root, DockableIds.TableOfContents));

        sut.IsToolPinned(DockableIds.TableOfContents).Should().BeTrue();
        root.PinnedDock.Should().BeNull("a collapsed panel is not slid out");

        // Act
        if (viaExpand)
        {
            sut.ExpandTool(DockableIds.TableOfContents);
        }
        else
        {
            sut.FocusTool(DockableIds.TableOfContents);
        }

        // Assert
        root.PinnedDock.Should().NotBeNull("focusing a collapsed panel slides it out");
    }

    [Fact]
    public void Pinned_state_is_restored_on_next_start()
    {
        // Arrange
        (MainDockFactory before, IRootDock beforeRoot) = NewWithRoot();

        // Act
        before.PinDockable(Tool(before, beforeRoot, DockableIds.TableOfContents));
        IReadOnlyDictionary<string, string> saved = before.CaptureToolVisibility();

        (MainDockFactory after, IRootDock afterRoot) = NewWithRoot();
        after.ApplyToolVisibility(saved);

        // Assert
        after.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
        after.IsToolPinned(DockableIds.TableOfContents).Should().BeTrue();
        afterRoot.RightPinnedDockables!.Select(d => d.Id).Should().Contain(DockableIds.TableOfContents);
        after.IsToolPinned(DockableIds.Preview).Should().BeFalse();
    }

    [Fact]
    public void Slid_out_size_of_an_auto_hidden_panel_is_restored_on_next_start()
    {
        // Arrange
        (MainDockFactory before, IRootDock beforeRoot) = NewWithRoot();
        IDockable preview = Tool(before, beforeRoot, DockableIds.Preview);

        // Act
        before.PinDockable(preview);
        preview.SetPinnedBounds(0, 0, 640.5, 900);
        IReadOnlyDictionary<string, string> saved = before.CapturePinnedSizes();

        (MainDockFactory after, IRootDock afterRoot) = NewWithRoot();
        IDockable restored = Tool(after, afterRoot, DockableIds.Preview);
        after.ApplyToolVisibility(before.CaptureToolVisibility());
        after.ApplyPinnedSizes(saved);

        // Assert
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
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        // Act
        sut.ApplyPinnedSizes(new Dictionary<string, string> { [DockableIds.Preview] = stored });

        // Assert
        sut.CapturePinnedSizes().Should().BeEmpty();
        Tool(sut, root, DockableIds.Preview).GetPinnedBounds(out _, out _, out double width, out _);
        (double.IsFinite(width) && width >= 50).Should().BeFalse();
    }

    [Theory]
    [InlineData("1")]
    [InlineData(MainDockFactory.PinnedState)]
    public void Validation_results_always_start_hidden_whatever_was_saved(string saved)
    {
        // Arrange
        (MainDockFactory sut, _) = NewWithRoot();

        // Act
        sut.ApplyToolVisibility(new Dictionary<string, string> { [DockableIds.ValidationResults] = saved });

        // Assert
        sut.IsToolVisible(DockableIds.ValidationResults).Should().BeFalse();
        sut.IsToolPinned(DockableIds.ValidationResults).Should().BeFalse();
        sut.CaptureToolVisibility()[DockableIds.ValidationResults].Should().Be("0");
    }

    [Fact]
    public void Toggling_an_auto_hidden_panel_hides_it_and_toggling_again_docks_it_back()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        // Act
        sut.ToggleTool(DockableIds.ValidationResults).Should().BeTrue();
        sut.PinDockable(Tool(sut, root, DockableIds.ValidationResults));

        // Act & Assert
        sut.ToggleTool(DockableIds.ValidationResults).Should().BeFalse();

        // Assert
        sut.CaptureToolVisibility()[DockableIds.ValidationResults].Should().Be("0");
        root.BottomPinnedDockables.Should().BeNullOrEmpty();

        // Act
        sut.ToggleTool(DockableIds.ValidationResults).Should().BeTrue();

        // Assert
        sut.IsToolPinned(DockableIds.ValidationResults).Should().BeFalse();
        Tool(sut, root, DockableIds.ValidationResults).Owner!.Id.Should().Be("BottomDock");
    }
}
