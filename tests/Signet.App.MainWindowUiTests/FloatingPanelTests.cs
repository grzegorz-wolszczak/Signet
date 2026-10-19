using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// "Float" on a dockable panel: the panel opens in its own window (it used to vanish — the factory had no host window
/// locator); closing that window, or hiding the panel from the View menu, docks it back where it came from.
/// </summary>
public sealed class FloatingPanelTests
{
    private sealed record Host(Window Window, MainDockFactory Factory, IRootDock Root);

    private static Host Show()
    {
        MainDockFactory factory = new();
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);
        Window window = new()
        {
            Width = 1000,
            Height = 700,
            Content = new DockControl { Layout = root, Factory = factory, InitializeLayout = false },
        };
        window.Show();
        Settle(window);
        return new Host(window, factory, root);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static IDockable Panel(Host host, string id) => host.Factory.FindDockable(host.Root, d => d.Id == id)!;

    private static HostWindow FloatingWindow(Host host) =>
        host.Root.Windows!.Select(w => w.Host).OfType<HostWindow>().Should().ContainSingle().Subject;

    // Opens the ▾ menu of the panel's title bar in the given window; returns whether "Float" and "Dock" are enabled.
    private static (bool Float, bool Dock) PanelMenuEnabled(TopLevel window, IDockable panel)
    {
        ToolChromeControl chrome = window.GetVisualDescendants().OfType<ToolChromeControl>()
            .Single(c => c.DataContext is IToolDock dock && ReferenceEquals(dock.ActiveDockable, panel));
        MenuFlyout menu = chrome.ToolFlyout.Should().BeOfType<MenuFlyout>().Subject;
        menu.ShowAt(chrome);
        Dispatcher.UIThread.RunJobs();
        MenuItem[] items = menu.Items.Cast<MenuItem>().ToArray();
        MenuItem Item(string key) => items.Single(i => Equals(i.Header, Application.Current!.FindResource(key)));
        (bool, bool) enabled = (Item("ToolChromeControlFloatString").IsEnabled, Item("ToolChromeControlDockString").IsEnabled);
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
        return enabled;
    }

    [AvaloniaFact]
    public void Float_opens_the_panel_in_a_visible_window()
    {
        Host host = Show();

        host.Factory.FloatDockable(Panel(host, DockableIds.TableOfContents));
        Settle(host.Window);

        HostWindow floating = FloatingWindow(host);
        floating.IsVisible.Should().BeTrue("the floating panel must be shown, not vanish");
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeTrue();
        host.Factory.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
        floating.Close();
        host.Window.Close();
    }

    private static IEnumerable<IDockable> Flatten(IDockable dockable) =>
        new[] { dockable }.Concat(dockable is IDock { VisibleDockables: { } children } ? children.SelectMany(Flatten) : Enumerable.Empty<IDockable>());

    // The region (the dock of the left, right or bottom panels) with the given panel.
    private static IToolDock RegionOf(Host host, string panelId) =>
        (IToolDock)host.Factory.FindDockable(host.Root, d => d is IToolDock dock && dock.VisibleDockables!.Any(v => v.Id == panelId))!;

    /// <summary>
    /// Regression: dragging the title bar of a panel out of the window makes Dock float the whole region. The region
    /// has to stay in the main layout and the panel it shows floats instead (as with "Float"): otherwise the panel still
    /// counted as docked, "Dock" in its menu collapsed it inside the floating window, and docking back again recursed
    /// until the stack overflowed — the application died without a word in the log.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_the_title_bar_floats_the_panel_not_its_region_and_Dock_brings_it_back()
    {
        Host host = Show();
        IToolDock region = RegionOf(host, DockableIds.BookBrowser);
        IDockable bookBrowser = Panel(host, DockableIds.BookBrowser);

        host.Factory.FloatDockable(region); // what the drag of the title bar asks Dock for
        Settle(host.Window);

        host.Factory.IsToolFloating(DockableIds.BookBrowser).Should().BeTrue("the panel floats");
        Flatten(host.Root).Should().Contain(region, "the region itself stays in the main layout");
        host.Root.Windows!.Select(w => w.Layout).OfType<IDockable>().SelectMany(Flatten).Should().NotContain(region,
            "no floating window holds the region");
        host.Factory.DescribePlaces().Should().Contain(DockableIds.BookBrowser + "=floating");

        host.Factory.PinDockable(bookBrowser); // "Dock" in the panel's menu
        Settle(host.Window);

        host.Factory.IsToolFloating(DockableIds.BookBrowser).Should().BeFalse();
        region.VisibleDockables.Should().Contain(bookBrowser, "it is back in its region");
        host.Root.Windows.Should().BeNullOrEmpty("the emptied window is closed");
        host.Factory.DescribePlaces().Should().Contain(DockableIds.BookBrowser + "=" + region.Id);
        host.Window.Close();
    }

    /// <summary>"Dock" on a floating panel docks it back; it must not start docking back again from Dock's own unpin call.</summary>
    [AvaloniaFact]
    public void Dock_on_a_floating_panel_docks_it_once_without_recursion()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        IToolDock region = RegionOf(host, DockableIds.TableOfContents);
        host.Factory.FloatDockable(toc);
        Settle(host.Window);

        host.Factory.PinDockable(toc);
        Settle(host.Window);

        region.VisibleDockables.Should().Contain(toc);
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeFalse();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void Closing_the_floating_window_hides_the_panel_and_showing_it_again_floats_it()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);

        host.Factory.FloatDockable(toc);
        Settle(host.Window);
        FloatingWindow(host).Close();
        Settle(host.Window);

        host.Factory.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse("the window's × closes its panels");
        host.Root.Windows.Should().BeNullOrEmpty();

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeTrue("shown again");
        Settle(host.Window);
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeTrue("it was closed floating");
        FloatingWindow(host).IsVisible.Should().BeTrue();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void The_only_panel_of_a_region_comes_back_floating_too()
    {
        // The Book Browser is alone in its region: closed while floating, the View menu must bring it back (it used
        // to dock back on the window's × — and the next View → Book Browser hid it).
        Host host = Show();
        IDockable bookBrowser = Panel(host, DockableIds.BookBrowser);

        host.Factory.FloatDockable(bookBrowser);
        Settle(host.Window);
        FloatingWindow(host).Close();
        Settle(host.Window);
        host.Factory.ToggleTool(DockableIds.BookBrowser).Should().BeTrue("shown again");
        Settle(host.Window);

        host.Factory.FindDockable(host.Root, d => d.Id == DockableIds.BookBrowser).Should().BeSameAs(bookBrowser);
        host.Factory.IsToolFloating(DockableIds.BookBrowser).Should().BeTrue();
        FloatingWindow(host).GetVisualDescendants().OfType<BookBrowserView>().Should().ContainSingle()
            .Which.IsEffectivelyVisible.Should().BeTrue();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void A_floating_panel_closed_with_its_own_close_comes_back_floating_at_the_same_place()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        host.Factory.FloatDockable(toc);
        Settle(host.Window);
        IDockWindow window = host.Root.Windows!.Single();
        window.Host!.SetPosition(120, 80);
        window.Host.SetSize(300, 400);
        Settle(host.Window);

        host.Factory.CloseDockable(toc);
        Settle(host.Window);
        host.Root.Windows.Should().BeNullOrEmpty("the emptied floating window is closed");

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeTrue("shown again");
        Settle(host.Window);
        IDockWindow reopened = host.Root.Windows!.Single();
        reopened.Save();
        (reopened.Width, reopened.Height).Should().Be((300d, 400d));
        host.Window.Close();
    }

    [AvaloniaFact]
    public void Hiding_a_floating_panel_from_the_View_menu_and_showing_it_again_floats_it()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        host.Factory.FloatDockable(toc);
        Settle(host.Window);

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeFalse("hidden");
        Settle(host.Window);
        host.Root.Windows.Should().BeNullOrEmpty("the emptied floating window is closed");

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeTrue("shown again");
        Settle(host.Window);
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeTrue();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void A_panel_hidden_while_floating_is_saved_and_restored_as_floating_when_shown()
    {
        Host host = Show();
        host.Factory.FloatDockable(Panel(host, DockableIds.TableOfContents));
        Settle(host.Window);
        FloatingWindow(host).Close();
        Settle(host.Window);
        IReadOnlyDictionary<string, string> visibility = host.Factory.CaptureToolVisibility();
        IReadOnlyDictionary<string, string> floating = host.Factory.CaptureFloatingPanels();
        host.Window.Close();
        floating.Should().ContainKey(DockableIds.TableOfContents);

        Host restarted = Show();
        restarted.Factory.ApplyToolVisibility(visibility);
        restarted.Factory.ApplyFloatingPanels(floating);
        Settle(restarted.Window);
        restarted.Factory.IsToolVisible(DockableIds.TableOfContents).Should().BeFalse("it stays closed");
        restarted.Root.Windows.Should().BeNullOrEmpty();

        restarted.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeTrue();
        Settle(restarted.Window);
        restarted.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeTrue();
        restarted.Window.Close();
    }

    [AvaloniaFact]
    public void A_floating_panel_is_saved_in_the_region_it_came_from()
    {
        Host host = Show();
        host.Factory.FloatDockable(Panel(host, DockableIds.TableOfContents));
        Settle(host.Window);

        host.Factory.CaptureLayoutState().Regions.Single(r => r.DockId == "RightDock").ToolIds
            .Should().Contain(DockableIds.TableOfContents);
        FloatingWindow(host).Close();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void The_panel_menu_offers_Dock_instead_of_Float_in_a_floating_window()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);

        host.Factory.SetActiveDockable(toc);
        Settle(host.Window);

        PanelMenuEnabled(host.Window, toc).Should().Be((true, false), "a docked panel can float, it is docked already");

        host.Factory.FloatDockable(toc);
        Settle(host.Window);
        HostWindow floating = FloatingWindow(host);
        Settle(floating);

        PanelMenuEnabled(floating, toc).Should().Be((false, true), "a floating panel can only be docked back");
        host.Window.Close();
    }

    [AvaloniaFact]
    public void Dock_docks_a_floating_panel_back_and_closes_its_window()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        IDock rightDock = (IDock)toc.Owner!;
        host.Factory.FloatDockable(toc);
        Settle(host.Window);

        host.Factory.PinDockable(toc);
        Settle(host.Window);

        toc.Owner.Should().BeSameAs(rightDock);
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeFalse();
        host.Factory.IsToolPinned(DockableIds.TableOfContents).Should().BeFalse("\"Dock\" must not switch the panel to Auto Hide");
        host.Root.Windows.Should().BeNullOrEmpty("the emptied floating window is closed");
        host.Window.Close();
    }

    [AvaloniaFact]
    public void A_floating_panel_uses_the_UI_font_of_the_main_window()
    {
        Host host = Show();
        host.Factory.FloatDockable(Panel(host, DockableIds.TableOfContents));
        Settle(host.Window);
        HostWindow floating = FloatingWindow(host);

        floating.FontSize.Should().Be(host.Window.FontSize, "a floated panel must not fall back to Avalonia's 12 px");
        floating.FontFamily.Should().Be(host.Window.FontFamily);

        // The UI font chosen in Preferences applies to the floating window too.
        ResourceDictionary uiFont = UiDensityManager.CreateFontResources("", 17)!;
        Application.Current!.Resources.MergedDictionaries.Add(uiFont);
        try
        {
            Settle(host.Window);
            floating.FontSize.Should().Be(17);
            host.Window.FontSize.Should().Be(17);
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(uiFont);
            floating.Close();
            host.Window.Close();
        }
    }

    [AvaloniaFact]
    public void Closing_the_main_window_closes_the_floating_windows_too()
    {
        MainWindowViewModel vm = new();
        MainWindow main = new() { DataContext = vm };
        main.Show();
        Settle(main);
        MainDockFactory factory = (MainDockFactory)vm.DockFactory;
        factory.FloatDockable(factory.FindDockable(vm.Layout, d => d.Id == DockableIds.TableOfContents)!);
        factory.FloatDockable(factory.FindDockable(vm.Layout, d => d.Id == DockableIds.BookBrowser)!);
        Settle(main);
        HostWindow[] floating = vm.Layout.Windows!.Select(w => w.Host).OfType<HostWindow>().ToArray();
        floating.Should().HaveCount(2).And.OnlyContain(w => w.IsVisible);

        main.Close();
        Dispatcher.UIThread.RunJobs();

        main.IsVisible.Should().BeFalse();
        floating.Should().OnlyContain(w => !w.IsVisible, "a floating window must not outlive the application");
        vm.Layout.Windows.Should().BeNullOrEmpty();
    }
}
