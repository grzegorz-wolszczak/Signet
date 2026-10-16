using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// Document tabs dragged out of the tab strip into their own windows: such a window has its own tab strip, accepts
/// more tabs, and closing it (or the application) attaches the tabs to the main tab strip again. A tab can only dock
/// into a tab strip, never into a panel region.
/// </summary>
public sealed class FloatingDocumentTests : IDisposable
{
    private static readonly string[] RegionIds = { "LeftDock", "RightDock", "BottomDock" };

    private readonly TempDir _temp = new();
    private readonly Book _book;
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _vm;

    public FloatingDocumentTests()
    {
        _book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, _temp)).GetBook();
        _window = new MainWindow { DataContext = _vm = new MainWindowViewModel() };
        _window.Show();
        Settle();
        _vm.LoadBook(_book, string.Empty);
        foreach (TextResource resource in _book.GetAllResources().OfType<TextResource>().Take(3))
        {
            _vm.NavigateToBookPathAtOffset(resource.BookPath, 0);
        }

        Settle();
    }

    public void Dispose()
    {
        _window.Close();
        _book.Dispose();
        _temp.Dispose();
    }

    private MainDockFactory Factory => (MainDockFactory)_vm.DockFactory;

    private IDocumentDock MainTabs => Factory.DocumentDock!;

    private List<ContentTabViewModel> Tabs => MainTabs.VisibleDockables!.OfType<ContentTabViewModel>().ToList();

    private IList<IDockWindow> Windows => _vm.Layout.Windows ?? new List<IDockWindow>();

    private void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static IDocument[] TabsOf(IDockWindow window) =>
        (window.Layout as IDock)?.VisibleDockables?.OfType<IDock>().SelectMany(d => d.VisibleDockables ?? new List<IDockable>())
            .OfType<IDocument>().ToArray() ?? Array.Empty<IDocument>();

    [AvaloniaFact]
    public void A_tab_floated_out_of_the_tab_strip_gets_its_own_window()
    {
        int before = Tabs.Count;
        ContentTabViewModel tab = Tabs[1];

        Factory.FloatDockable(tab);
        Settle();

        Windows.Should().ContainSingle();
        TabsOf(Windows[0]).Should().Equal(tab);
        Tabs.Should().NotContain(tab).And.HaveCount(before - 1);
    }

    [AvaloniaFact]
    public void Closing_the_window_of_a_floated_tab_attaches_it_to_the_tab_strip_again()
    {
        int before = Tabs.Count;
        ContentTabViewModel tab = Tabs[1];
        Factory.FloatDockable(tab);
        Settle();

        ((HostWindow)Windows[0].Host!).Close();
        Settle();

        Windows.Should().BeEmpty();
        Tabs.Should().Contain(tab).And.HaveCount(before, "closing the window does not close the file");
        MainTabs.ActiveDockable.Should().BeSameAs(tab);
    }

    [AvaloniaFact]
    public void Closing_a_floated_tab_closes_it_and_its_window()
    {
        int before = Tabs.Count;
        ContentTabViewModel tab = Tabs[1];
        Factory.FloatDockable(tab);
        Settle();

        Factory.CloseDockable(tab);
        Settle();

        Windows.Should().BeEmpty();
        Tabs.Should().NotContain(tab).And.HaveCount(before - 1);
    }

    [AvaloniaFact]
    public void A_floated_tab_can_be_dropped_into_the_window_of_another_one_and_floated_out_again()
    {
        ContentTabViewModel first = Tabs[0];
        ContentTabViewModel second = Tabs[1];
        Factory.FloatDockable(first);
        Settle();
        IDocumentDock windowTabs = (IDocumentDock)((IDock)Windows[0].Layout!).VisibleDockables!.Single();

        Factory.MoveDockable(MainTabs, windowTabs, second, null);
        Settle();

        Windows.Should().ContainSingle();
        TabsOf(Windows[0]).Should().BeEquivalentTo(new IDocument[] { first, second });

        Factory.FloatDockable(second);
        Settle();

        Windows.Should().HaveCount(2);
        Windows.Select(TabsOf).Should().OnlyContain(tabs => tabs.Length == 1);
    }

    [AvaloniaFact]
    public void Closing_the_application_windows_attaches_all_floated_tabs_again()
    {
        List<ContentTabViewModel> all = Tabs;
        Factory.FloatDockable(all[0]);
        Settle();
        Factory.FloatDockable(all[1]);
        Settle();
        Windows.Should().HaveCount(2);

        Factory.CloseFloatingWindows();
        Settle();

        Windows.Should().BeEmpty();
        Tabs.Should().BeEquivalentTo(all);
    }

    [AvaloniaFact]
    public void A_tab_docks_only_into_a_tab_strip_never_into_a_panel_region()
    {
        DockManager manager = new(new DockService());
        ContentTabViewModel tab = Tabs[0];
        IToolDock[] regions = RegionIds
            .Select(id => (IToolDock)Factory.FindDockable(_vm.Layout, d => d.Id == id)!)
            .ToArray();

        foreach (IToolDock region in regions)
        {
            foreach (DockOperation operation in new[] { DockOperation.Fill, DockOperation.Left, DockOperation.Window })
            {
                manager.ValidateDockable(tab, region, DragAction.Move, operation, false)
                    .Should().BeFalse($"{region.Id} is a panel region ({operation})");
            }
        }

        manager.ValidateDockable(tab, MainTabs, DragAction.Move, DockOperation.Fill, false).Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_window_of_floated_tabs_has_the_keyboard_shortcuts_of_the_main_window()
    {
        Factory.FloatDockable(Tabs[0]);
        Settle();

        ((HostWindow)Windows[0].Host!).KeyBindings.Should().NotBeEmpty();
    }
}
