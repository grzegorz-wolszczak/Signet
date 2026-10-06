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

namespace Signet.App.UiTests;

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

    [AvaloniaFact]
    public void Closing_the_floating_window_docks_the_panel_back_into_its_region()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        IDock rightDock = (IDock)toc.Owner!;

        host.Factory.FloatDockable(toc);
        Settle(host.Window);
        FloatingWindow(host).Close();
        Settle(host.Window);

        toc.Owner.Should().BeSameAs(rightDock);
        host.Factory.IsToolFloating(DockableIds.TableOfContents).Should().BeFalse();
        host.Factory.IsToolVisible(DockableIds.TableOfContents).Should().BeTrue();
        host.Root.Windows.Should().BeNullOrEmpty();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void The_only_panel_of_a_region_comes_back_too()
    {
        Host host = Show();
        IDockable bookBrowser = Panel(host, DockableIds.BookBrowser);

        host.Factory.FloatDockable(bookBrowser);
        Settle(host.Window);
        FloatingWindow(host).Close();
        Settle(host.Window);

        host.Factory.FindDockable(host.Root, d => d.Id == DockableIds.BookBrowser).Should().BeSameAs(bookBrowser,
            "the panel is back in the main layout, not in a detached (collapsed) region");
        host.Factory.IsToolFloating(DockableIds.BookBrowser).Should().BeFalse();
        host.Window.Close();
    }

    [AvaloniaFact]
    public void Hiding_a_floating_panel_from_the_View_menu_and_showing_it_again_docks_it()
    {
        Host host = Show();
        IDockable toc = Panel(host, DockableIds.TableOfContents);
        IDock rightDock = (IDock)toc.Owner!;
        host.Factory.FloatDockable(toc);
        Settle(host.Window);

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeFalse("hidden");
        Settle(host.Window);
        host.Root.Windows.Should().BeNullOrEmpty("the emptied floating window is closed");

        host.Factory.ToggleTool(DockableIds.TableOfContents).Should().BeTrue("shown again");
        Settle(host.Window);
        toc.Owner.Should().BeSameAs(rightDock);
        host.Window.Close();
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
