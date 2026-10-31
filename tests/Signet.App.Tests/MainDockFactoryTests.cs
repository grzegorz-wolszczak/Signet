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
/// The dock layout of the main window (<see cref="MainDockFactory"/>): the six places of the panels, moving panels
/// between them, the stripe buttons, and restoring the layout after a restart (the side widths, the places).
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
        Width(after, MainDockFactory.LeftColumnId).Should().BeApproximately(0.22, 1e-9);
        Width(after, MainDockFactory.RightColumnId).Should().BeApproximately(0.33, 1e-9);
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
    public void The_panels_have_six_places_around_the_documents_and_the_bottom_row_spans_the_whole_width()
    {
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
            .Should().Equal(MainDockFactory.LeftColumnId, DockableIds.Documents, MainDockFactory.RightColumnId);
        parts[1].Id.Should().Be(MainDockFactory.BottomRowId, "the bottom row is under the side columns too");
        Halves(row, MainDockFactory.LeftColumnId).Should().Equal(PanelPlaces.DockId(PanelPlace.LeftTop), PanelPlaces.DockId(PanelPlace.LeftBottom));
        Halves(row, MainDockFactory.RightColumnId).Should().Equal(PanelPlaces.DockId(PanelPlace.RightTop), PanelPlaces.DockId(PanelPlace.RightBottom));
        Halves(layout, MainDockFactory.BottomRowId).Should().Equal(PanelPlaces.DockId(PanelPlace.BottomLeft), PanelPlaces.DockId(PanelPlace.BottomRight));
        factory.PanelsAt(PanelPlace.LeftTop).Should().Equal(DockableIds.BookBrowser, DockableIds.Clips);
        factory.PanelsAt(PanelPlace.RightTop).Should().Equal(DockableIds.Preview, DockableIds.TableOfContents, DockableIds.Checkpoints);
        factory.PanelsAt(PanelPlace.BottomLeft).Should().Equal(DockableIds.ValidationResults, DockableIds.Notifications, DockableIds.FindUsages);
        new[] { PanelPlace.LeftBottom, PanelPlace.RightBottom, PanelPlace.BottomRight }
            .Should().OnlyContain(p => factory.PanelsAt(p).Count == 0, "the second halves start empty");
    }

    private static string[] Halves(IProportionalDock parent, string id) =>
        parent.VisibleDockables!.OfType<IProportionalDock>().Single(d => d.Id == id).VisibleDockables!
            .Where(d => d is not IProportionalDockSplitter).Select(d => d.Id).ToArray();

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
        double defaultLeft = Width(sut, MainDockFactory.LeftColumnId);
        double defaultRight = Width(sut, MainDockFactory.RightColumnId);

        // Act
        sut.ApplySideRegionWidths(Saved(0, double.NaN));

        // Assert
        Width(sut, MainDockFactory.LeftColumnId).Should().Be(defaultLeft);
        Width(sut, MainDockFactory.RightColumnId).Should().Be(defaultRight);
    }

    [Fact]
    public void Extreme_widths_are_clamped_so_the_center_column_keeps_room()
    {
        // Arrange
        MainDockFactory sut = NewFactory();

        // Act
        sut.ApplySideRegionWidths(Saved(0.001, 0.95));

        // Assert
        Width(sut, MainDockFactory.LeftColumnId).Should().BeGreaterThanOrEqualTo(0.08);
        Width(sut, MainDockFactory.RightColumnId).Should().BeLessThanOrEqualTo(0.6);

        // Act
        sut.ApplySideRegionWidths(Saved(0.55, 0.55));

        // Assert
        (Width(sut, MainDockFactory.LeftColumnId) + Width(sut, MainDockFactory.RightColumnId)).Should().BeLessThanOrEqualTo(0.8 + 1e-9);
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
    public void A_panel_saved_as_collapsed_by_auto_hide_starts_closed_with_its_stripe_button()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();

        // Act
        sut.ApplyToolVisibility(new Dictionary<string, string> { [DockableIds.TableOfContents] = MainDockFactory.PinnedState });

        // Assert
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse();
        sut.IsOnStripe(DockableIds.TableOfContents).Should().BeTrue();
        sut.CaptureToolVisibility()[DockableIds.TableOfContents].Should().Be("0");
        root.RightPinnedDockables.Should().BeNullOrEmpty();
    }

    [Fact]
    public void A_docked_panel_is_never_collapsed_by_auto_hide()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        IDockable toc = Tool(sut, root, DockableIds.TableOfContents);

        // Act
        sut.PinDockable(toc);

        // Assert
        toc.CanPin.Should().BeFalse();
        root.RightPinnedDockables.Should().BeNullOrEmpty();
        toc.Owner!.Id.Should().Be(PanelPlaces.DockId(PanelPlace.RightTop));
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

    [Fact]
    public void Moving_a_shown_panel_puts_it_in_the_other_place_in_front_and_keeps_the_order()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        int changes = 0;
        sut.PanelsChanged += (_, _) => changes++;

        // Act
        sut.MoveTool(DockableIds.TableOfContents, PanelPlace.LeftBottom, 0);

        // Assert
        sut.PlaceOf(DockableIds.TableOfContents).Should().Be(PanelPlace.LeftBottom);
        sut.PanelsAt(PanelPlace.RightTop).Should().Equal(DockableIds.Preview, DockableIds.Checkpoints);
        IToolDock leftBottom = (IToolDock)Tool(sut, root, DockableIds.TableOfContents).Owner!;
        leftBottom.Id.Should().Be(PanelPlaces.DockId(PanelPlace.LeftBottom));
        leftBottom.ActiveDockable!.Id.Should().Be(DockableIds.TableOfContents);
        changes.Should().Be(1, "one move is one notice for the stripes");

        // Act
        sut.MoveTool(DockableIds.Clips, PanelPlace.LeftTop, 0);

        // Assert
        sut.PanelsAt(PanelPlace.LeftTop).Should().Equal(DockableIds.Clips, DockableIds.BookBrowser);
    }

    [Fact]
    public void A_closed_panel_moved_to_another_place_shows_there_next_time()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        sut.IsToolVisible(DockableIds.Notifications).Should().BeFalse();

        // Act
        sut.MoveTool(DockableIds.Notifications, PanelPlace.BottomRight, 0);

        // Assert
        sut.IsToolVisible(DockableIds.Notifications).Should().BeFalse("moving does not open it");

        // Act
        sut.ToggleTool(DockableIds.Notifications);

        // Assert
        Tool(sut, root, DockableIds.Notifications).Owner!.Id.Should().Be(PanelPlaces.DockId(PanelPlace.BottomRight));
    }

    [Fact]
    public void The_places_of_the_panels_survive_a_restart()
    {
        // Arrange
        (MainDockFactory before, _) = NewWithRoot();
        before.MoveTool(DockableIds.TableOfContents, PanelPlace.RightBottom, 0);
        before.MoveTool(DockableIds.Notifications, PanelPlace.BottomRight, 0);
        before.MoveTool(DockableIds.Clips, PanelPlace.LeftTop, 0);
        DockLayoutState saved = before.CaptureLayoutState();

        // Act
        (MainDockFactory after, IRootDock afterRoot) = NewWithRoot();
        after.ApplyPanelPlaces(saved);
        after.ApplyToolVisibility(before.CaptureToolVisibility());

        // Assert
        foreach (PanelPlace place in PanelPlaces.All)
        {
            after.PanelsAt(place).Should().Equal(before.PanelsAt(place), "the panels of {0}", place);
        }

        Tool(after, afterRoot, DockableIds.TableOfContents).Owner!.Id.Should().Be(PanelPlaces.DockId(PanelPlace.RightBottom));
    }

    [Fact]
    public void A_layout_saved_before_the_places_were_split_puts_the_panels_in_the_first_halves()
    {
        // Arrange
        (MainDockFactory sut, IRootDock root) = NewWithRoot();
        DockLayoutState legacy = new(
            new[]
            {
                new DockRegionState(PanelPlaces.LegacyLeftDock, 0.2, new[] { DockableIds.BookBrowser, DockableIds.TableOfContents }, DockableIds.BookBrowser),
                new DockRegionState(PanelPlaces.LegacyRightDock, 0.25, new[] { DockableIds.Preview }, DockableIds.Preview),
                new DockRegionState(PanelPlaces.LegacyBottomDock, 0.3, new[] { DockableIds.Checkpoints }, null),
            },
            0.72);

        // Act
        sut.ApplyPanelPlaces(legacy);

        // Assert
        sut.PlaceOf(DockableIds.TableOfContents).Should().Be(PanelPlace.LeftTop);
        sut.PlaceOf(DockableIds.Checkpoints).Should().Be(PanelPlace.BottomLeft);
        sut.PanelsAt(PanelPlace.LeftTop).Take(2).Should().Equal(DockableIds.BookBrowser, DockableIds.TableOfContents);
        Tool(sut, root, DockableIds.TableOfContents).Owner!.Id.Should().Be(PanelPlaces.DockId(PanelPlace.LeftTop));
    }

    [Fact]
    public void A_click_on_the_stripe_button_of_a_closed_panel_opens_it_and_makes_it_active()
    {
        // Arrange
        (MainDockFactory sut, _) = NewWithRoot();
        sut.ClickStripeButton(DockableIds.TableOfContents);
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse();

        // Act
        sut.ClickStripeButton(DockableIds.TableOfContents);

        // Assert
        sut.IsToolInFront(DockableIds.TableOfContents).Should().BeTrue();
        sut.IsToolInFront(DockableIds.Preview).Should().BeFalse();
    }

    [Fact]
    public void A_click_on_the_stripe_button_of_a_panel_behind_another_tab_closes_it_and_the_tab_in_front_stays()
    {
        // Arrange
        (MainDockFactory sut, _) = NewWithRoot();
        sut.IsToolInFront(DockableIds.Preview).Should().BeTrue();
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
        sut.IsToolInFront(DockableIds.TableOfContents).Should().BeFalse("it is behind the Preview tab");

        // Act
        sut.ClickStripeButton(DockableIds.TableOfContents);

        // Assert
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse("a click on an open panel closes it, also behind another tab");
        sut.IsToolInFront(DockableIds.Preview).Should().BeTrue();
    }

    [Fact]
    public void A_click_on_the_stripe_button_of_the_panel_in_front_closes_it_and_another_tab_comes_forward()
    {
        // Arrange
        (MainDockFactory sut, _) = NewWithRoot();
        sut.IsToolInFront(DockableIds.Preview).Should().BeTrue();
        sut.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();

        // Act
        sut.ClickStripeButton(DockableIds.Preview);

        // Assert
        sut.IsToolVisible(DockableIds.Preview).Should().BeFalse();
        sut.IsToolInFront(DockableIds.TableOfContents).Should().BeTrue("Dock brings forward the tab left in the place");
    }

    [Fact]
    public void Hiding_a_stripe_button_closes_its_panel_and_showing_the_panel_brings_the_button_back()
    {
        // Arrange
        (MainDockFactory sut, _) = NewWithRoot();

        // Act
        sut.SetOnStripe(DockableIds.Preview, false);

        // Assert
        sut.IsOnStripe(DockableIds.Preview).Should().BeFalse();
        sut.IsToolVisible(DockableIds.Preview).Should().BeFalse();
        sut.CaptureOffStripe().Should().Equal(new Dictionary<string, string> { [DockableIds.Preview] = "0" });

        // Act
        (MainDockFactory restarted, _) = NewWithRoot();
        restarted.ApplyOffStripe(sut.CaptureOffStripe());

        // Assert
        restarted.IsOnStripe(DockableIds.Preview).Should().BeFalse();

        // Act
        sut.ToggleTool(DockableIds.Preview);

        // Assert
        sut.IsOnStripe(DockableIds.Preview).Should().BeTrue("a panel shown in any way gets its button back");
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
        sut.CaptureToolVisibility()[DockableIds.ValidationResults].Should().Be("0");
    }
}
