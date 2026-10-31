using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// The stripes of the panels along the sides of the main window (IntelliJ's tool window bars): a button for every
/// panel, grouped by its place; a click opens, brings to the front or closes the panel; the menu of a button hides it,
/// moves its panel or shows the names; a button dragged to another group moves its panel there.
/// </summary>
public sealed class PanelStripeTests
{
    private sealed class Host : IDisposable
    {
        public Host()
        {
            Vm = new MainWindowViewModel();
            Window = new MainWindow { DataContext = Vm, Width = 1200, Height = 800 };
            Window.Show();
            Settle();
        }

        public MainWindowViewModel Vm { get; }

        public MainWindow Window { get; }

        public MainDockFactory Factory => (MainDockFactory)Vm.DockFactory;

        public PanelStripeView Left => Window.FindControl<PanelStripeView>("LeftPanelStripe")!;

        public PanelStripeView Right => Window.FindControl<PanelStripeView>("RightPanelStripe")!;

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string[] Ids(PanelStripeView stripe, string group) =>
        stripe.FindControl<ItemsControl>(group)!.Items.OfType<PanelStripeButtonViewModel>().Select(b => b.Id).ToArray();

    private static Button ButtonOf(PanelStripeView stripe, string id) =>
        stripe.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is PanelStripeButtonViewModel vm && vm.Id == id);

    [AvaloniaFact]
    public void Every_panel_has_a_button_on_the_stripe_of_its_place()
    {
        // Arrange
        using Host host = new();

        // Act & Assert
        Ids(host.Left, "TopGroup").Should().Equal(DockableIds.BookBrowser, DockableIds.Clips);
        Ids(host.Left, "MiddleGroup").Should().BeEmpty();
        Ids(host.Left, "BottomGroup").Should().Equal(DockableIds.ValidationResults, DockableIds.Notifications, DockableIds.FindUsages);
        Ids(host.Right, "TopGroup").Should().Equal(DockableIds.Preview, DockableIds.TableOfContents, DockableIds.Checkpoints);
        Ids(host.Right, "BottomGroup").Should().BeEmpty();
        host.Left.FindControl<Border>("GroupSeparator")!.IsVisible.Should().BeFalse("the second half has no buttons");
        host.Left.FindControl<Button>("MoreButton")!.IsVisible.Should().BeTrue("\"More Tool Windows\" is always on the left stripe");
        host.Right.FindControl<Button>("MoreButton")!.IsVisible.Should().BeFalse();
        host.Vm.PanelStripes.MoreItems.Should().HaveCount(host.Factory.ToolIds.Count, "it lists every panel");
        ButtonOf(host.Right, DockableIds.Preview).Background.Should().Be(ButtonOf(host.Left, DockableIds.Clips).Background,
            "the buttons are not toggles: an open panel's button looks like a closed one's");
    }

    [AvaloniaFact]
    public void Unread_errors_put_a_red_dot_on_the_notifications_button()
    {
        // Arrange
        using Host host = new();
        Ellipse Dot() => ButtonOf(host.Left, DockableIds.Notifications).GetVisualDescendants().OfType<Ellipse>()
            .Single(e => e.Name == "AttentionDot");

        // Act & Assert
        Dot().IsVisible.Should().BeFalse();

        // Act
        host.Factory.SetToolAttention(DockableIds.Notifications, 2);
        host.Settle();

        // Assert
        Dot().IsVisible.Should().BeTrue();

        // Act
        host.Factory.SetToolAttention(DockableIds.Notifications, 0);
        host.Settle();

        // Assert
        Dot().IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_click_on_a_button_opens_and_closes_its_panel()
    {
        // Arrange
        using Host host = new();
        Button clips = ButtonOf(host.Left, DockableIds.Clips);

        // Act
        clips.Command!.Execute(null);
        host.Settle();

        // Assert
        host.Factory.IsToolInFront(DockableIds.Clips).Should().BeTrue();

        // Act
        ButtonOf(host.Left, DockableIds.Clips).Command!.Execute(null);
        host.Settle();

        // Assert
        host.Factory.IsToolVisible(DockableIds.Clips).Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_half_with_no_shown_panel_takes_no_room_and_a_column_with_none_neither()
    {
        // Arrange
        using Host host = new();
        double before = DocumentsWidth(host);

        // Act
        host.Factory.MoveTool(DockableIds.TableOfContents, PanelPlace.RightBottom, 0);
        host.Settle();

        // Assert
        Ids(host.Right, "MiddleGroup").Should().Equal(DockableIds.TableOfContents);
        host.Right.FindControl<Border>("GroupSeparator")!.IsVisible.Should().BeTrue();
        PlaceHeight(host, PanelPlace.RightBottom).Should().BeGreaterThan(50, "the bottom half shows its panel");
        PlaceHeight(host, PanelPlace.RightTop).Should().BeGreaterThan(50);

        // Act
        foreach (string id in new[] { DockableIds.Preview, DockableIds.TableOfContents, DockableIds.Checkpoints })
        {
            if (host.Factory.IsToolVisible(id))
            {
                host.Factory.ToggleTool(id);
            }
        }

        host.Settle();

        // Assert
        DocumentsWidth(host).Should().BeGreaterThan(before + 100, "the emptied right column gives its room to the documents");
    }

    /// <summary>
    /// A panel opened from its stripe in a half that had no shown panel gets room: Dock left such a half with no share
    /// and the bottom panels opened as an empty white area. Also when the other half of the row shows a panel.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DockableIds.Notifications)]
    [InlineData(DockableIds.ValidationResults)]
    [InlineData(DockableIds.FindUsages)]
    [InlineData(DockableIds.Clips)]
    public void A_panel_opened_in_an_empty_half_gets_room(string id)
    {
        // Arrange
        using Host host = new();
        if (host.Factory.PlaceOf(id) == PanelPlace.LeftTop)
        {
            host.Factory.MoveTool(id, PanelPlace.LeftBottom, 0);
            host.Settle();
        }

        // Act
        host.Factory.ClickStripeButton(id);
        host.Settle();

        // Assert
        PanelSize(host, id).Width.Should().BeGreaterThan(50);
        PanelSize(host, id).Height.Should().BeGreaterThan(50);

        // Act
        string other = id == DockableIds.FindUsages ? DockableIds.Notifications : DockableIds.FindUsages;
        host.Factory.MoveTool(other, PanelPlace.BottomRight, 0);
        host.Factory.ClickStripeButton(other);
        host.Settle();

        // Assert
        PanelSize(host, id).Width.Should().BeGreaterThan(50, "the two halves share the room");
        PanelSize(host, other).Width.Should().BeGreaterThan(50);
    }

    /// <summary>
    /// A splitter is in the layout only with something shown on both its sides: the one next to an empty half cut a gap
    /// into the frame of the panel above it. It comes back once the half shows a panel.
    /// </summary>
    [AvaloniaFact]
    public void A_splitter_is_shown_only_between_two_shown_parts()
    {
        // Arrange
        using Host host = new();

        // Act
        host.Factory.ClickStripeButton(DockableIds.Clips);
        host.Settle();

        // Assert
        Splitters(host).Should().HaveCount(2, "only those beside the documents: the left column has one half shown, the bottom row none");

        // Act
        host.Factory.ClickStripeButton(DockableIds.Notifications);
        host.Factory.MoveTool(DockableIds.TableOfContents, PanelPlace.RightBottom, 0);
        host.Settle();

        // Assert
        Splitters(host).Should().HaveCount(4, "and the one over the bottom row, and the one between the halves of the right column");
        PanelSize(host, DockableIds.TableOfContents).Height.Should().BeGreaterThan(50);
    }

    private static Control[] Splitters(Host host) =>
        host.Window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.DataContext is IProportionalDockSplitter && c.GetType().Name.Contains("Splitter", StringComparison.Ordinal))
            .ToArray();

    [AvaloniaFact]
    public void With_the_names_a_stripe_is_as_wide_as_the_longest_name_or_as_set_or_as_dragged()
    {
        // Arrange
        using Host host = new();
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        PanelStripesViewModel stripes = new(host.Factory, settings, host.Vm.Actions);
        host.Left.DataContext = stripes.Left;
        host.Right.DataContext = stripes.Right;
        TextBlock Label(string id) => ButtonOf(host.Left, id).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("stripeLabel"));

        // Act
        stripes.ShowNames = true;
        host.Settle();

        // Assert
        Label(DockableIds.Notifications).Bounds.Width.Should().BeGreaterThanOrEqualTo(
            Label(DockableIds.Notifications).DesiredSize.Width - 0.5, "Dynamic: no name is cut");
        host.Left.FindControl<Thumb>("ResizeGrip")!.IsVisible.Should().BeFalse();

        // Act
        settings.PanelStripeAppearance = settings.PanelStripeAppearance with { WidthMode = PanelStripeWidthMode.Fixed, FixedWidth = 50 };
        host.Settle();

        // Assert
        host.Left.Bounds.Width.Should().Be(50);
        host.Right.Bounds.Width.Should().Be(50);

        // Act
        settings.PanelStripeAppearance = settings.PanelStripeAppearance with { WidthMode = PanelStripeWidthMode.Resizable, ResizedWidth = 60 };
        host.Settle();
        stripes.ResizeBy(15);
        host.Settle();

        // Assert
        host.Left.FindControl<Thumb>("ResizeGrip")!.IsVisible.Should().BeTrue();
        host.Left.Bounds.Width.Should().Be(75);
        settings.PanelStripeAppearance.ResizedWidth.Should().Be(60, "saved when the drag ends");

        // Act
        stripes.ResizeBy(500);
        stripes.EndResize();
        host.Settle();

        // Assert
        host.Left.Bounds.Width.Should().Be(PanelStripeAppearance.MaxWidth);
        settings.PanelStripeAppearance.ResizedWidth.Should().Be(PanelStripeAppearance.MaxWidth);

        // Act
        stripes.ShowNames = false;
        host.Settle();

        // Assert
        host.Left.Bounds.Width.Should().Be(PanelStripeAppearance.MinWidth, "without the names a stripe is one icon wide");
    }

    private static Size PanelSize(Host host, string id)
    {
        IDockable panel = host.Factory.FindDockable(host.Vm.Layout, d => d.Id == id)!;
        return host.Window.GetVisualDescendants().OfType<ContentPresenter>()
            .Where(c => ReferenceEquals(c.DataContext, panel) && c.IsEffectivelyVisible)
            .Select(c => c.Bounds.Size)
            .DefaultIfEmpty(default)
            .MaxBy(s => s.Width);
    }

    private static double DocumentsWidth(Host host) =>
        host.Window.GetVisualDescendants().OfType<Control>()
            .Where(c => ReferenceEquals(c.DataContext, host.Factory.DocumentDock) && c.IsEffectivelyVisible)
            .Max(c => c.Bounds.Width);

    private static double PlaceHeight(Host host, PanelPlace place)
    {
        IToolDock dock = (IToolDock)host.Factory.FindDockable(host.Vm.Layout, d => d.Id == PanelPlaces.DockId(place))!;
        return host.Window.GetVisualDescendants().OfType<Control>()
            .Where(c => ReferenceEquals(c.DataContext, dock) && c.IsEffectivelyVisible)
            .Select(c => c.Bounds.Height)
            .DefaultIfEmpty(0)
            .Max();
    }

    [AvaloniaFact]
    public void The_menu_of_a_button_names_its_panel_and_moves_it_hides_it_and_shows_the_names()
    {
        // Arrange
        using Host host = new();
        Button toc = ButtonOf(host.Right, DockableIds.TableOfContents);
        ContextMenu menu = toc.ContextMenu!;

        // Act
        menu.Open(toc);
        host.Settle();

        // Assert
        MenuItem title = menu.Items.OfType<MenuItem>().First();
        title.Classes.Should().Contain(MenuTitle.TitleClass);
        title.Header.Should().Be(MenuTitle.Escape(Strings.Get("Panel_" + DockableIds.TableOfContents)));
        PanelStripeButtonViewModel button = (PanelStripeButtonViewModel)toc.DataContext!;
        button.MoveToItems.Single(i => i.Place == PanelPlace.RightTop).MoveCommand.CanExecute(null)
            .Should().BeFalse("the panel is there already");
        menu.Close();

        // Act
        button.MoveToItems.Single(i => i.Place == PanelPlace.LeftBottom).MoveCommand.Execute(null);
        host.Settle();

        // Assert
        Ids(host.Left, "MiddleGroup").Should().Equal(DockableIds.TableOfContents);
        Ids(host.Right, "TopGroup").Should().NotContain(DockableIds.TableOfContents);

        // Act
        double narrow = host.Left.Bounds.Width;
        button.ToggleShowNamesCommand.Execute(null);
        host.Settle();

        // Assert
        host.Left.Bounds.Width.Should().BeGreaterThan(narrow, "the names under the icons need room");
        host.Left.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("stripeLabel"))
            .Should().OnlyContain(t => t.IsEffectivelyVisible);

        // Act
        button.HideFromStripeCommand.Execute(null);
        host.Settle();

        // Assert
        Ids(host.Left, "MiddleGroup").Should().BeEmpty();
        host.Factory.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse();
        host.Vm.PanelStripes.MoreItems.Select(b => b.Id).Should().Contain(DockableIds.TableOfContents, "\"More Tool Windows\" brings it back");

        // Act
        host.Vm.PanelStripes.MoreItems.Single(b => b.Id == DockableIds.TableOfContents).RestoreCommand.Execute(null);
        host.Settle();

        // Assert
        Ids(host.Left, "MiddleGroup").Should().Equal(DockableIds.TableOfContents);
        host.Factory.IsToolInFront(DockableIds.TableOfContents).Should().BeTrue();
        button.ToggleShowNamesCommand.Execute(null);
    }

    [AvaloniaFact]
    public void A_button_dragged_to_the_other_stripe_moves_its_panel_there_and_is_not_clicked()
    {
        // Arrange
        using Host host = new();
        Button clips = ButtonOf(host.Left, DockableIds.Clips);
        Point start = Center(host, clips);
        Button checkpoints = ButtonOf(host.Right, DockableIds.Checkpoints);
        // The lower half of the last button of the group: after it.
        Point target = Center(host, checkpoints) + new Vector(0, checkpoints.Bounds.Height * 0.3);

        // Act
        host.Window.MouseDown(start, MouseButton.Left);
        host.Window.MouseMove(start + new Vector(0, 20));
        host.Window.MouseMove(target);
        host.Settle();

        // Assert
        host.Right.FindControl<Border>("DropIndicator")!.IsVisible.Should().BeTrue("the line shows where it goes");

        // Act
        host.Window.MouseUp(target, MouseButton.Left);
        host.Settle();

        // Assert
        host.Factory.PanelsAt(PanelPlace.RightTop).Should().Equal(
            DockableIds.Preview, DockableIds.TableOfContents, DockableIds.Checkpoints, DockableIds.Clips);
        Ids(host.Left, "TopGroup").Should().Equal(DockableIds.BookBrowser);
        host.Factory.IsToolVisible(DockableIds.Clips).Should().BeFalse("the drop is not a click: the closed panel stays closed");
        host.Right.FindControl<Border>("DropIndicator")!.IsVisible.Should().BeFalse();

        // Act: below the group, where the empty second half gets room while a button is dragged.
        Button moved = ButtonOf(host.Right, DockableIds.Clips);
        Point from = Center(host, moved);
        Point below = from + new Vector(0, moved.Bounds.Height * 1.2);
        host.Window.MouseDown(from, MouseButton.Left);
        host.Window.MouseMove(from + new Vector(0, 10));
        host.Settle();
        host.Window.MouseMove(below);
        host.Window.MouseUp(below, MouseButton.Left);
        host.Settle();

        // Assert
        host.Factory.PlaceOf(DockableIds.Clips).Should().Be(PanelPlace.RightBottom);
        Ids(host.Right, "MiddleGroup").Should().Equal(DockableIds.Clips);
    }

    private static Point Center(Host host, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host.Window)!.Value;
}
