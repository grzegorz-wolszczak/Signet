using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// Document tabs dragged out of the tab strip into their own windows: such a window has its own tab strip, accepts
/// more tabs, and closing it (or the application) attaches the tabs to the main tab strip again. A tab can only dock
/// into a tab strip, never into a panel region.
/// </summary>
public sealed class FloatingDocumentTests
{
    // The six places of the panels (each an area of tabs of its own).
    private static readonly string[] RegionIds = PanelPlaces.All.Select(PanelPlaces.DockId).ToArray();

    // Created inside each test: the constructor and Dispose of a test class do not run on the UI thread.
    private sealed class Scenario : IDisposable
    {
        private readonly TempDir _temp = new();
        private readonly Book _book;
        private readonly MainWindow _window;
        public readonly MainWindowViewModel _vm;

        public Scenario()
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
            // CloseBook disposes the book and detaches the view model from it: a view model that stays reachable gets
            // the language-change notification of later tests.
            ((IBookWorkspace)_vm).CloseBook();
            _window.Close();
            _temp.Dispose();
        }

        public MainDockFactory Factory => (MainDockFactory)_vm.DockFactory;

        public IDocumentDock MainTabs => Factory.DocumentDock!;

        public List<ContentTabViewModel> Tabs => MainTabs.VisibleDockables!.OfType<ContentTabViewModel>().ToList();

        public IList<IDockWindow> Windows => _vm.Layout.Windows ?? new List<IDockWindow>();

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            _window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static IDocument[] TabsOf(IDockWindow window) =>
        (window.Layout as IDock)?.VisibleDockables?.OfType<IDock>().SelectMany(d => d.VisibleDockables ?? new List<IDockable>())
            .OfType<IDocument>().ToArray() ?? Array.Empty<IDocument>();

    [AvaloniaFact]
    public void A_tab_floated_out_of_the_tab_strip_gets_its_own_window()
    {
        // Arrange
        using Scenario s = new();
        int before = s.Tabs.Count;
        ContentTabViewModel tab = s.Tabs[1];

        // Act
        s.Factory.FloatDockable(tab);
        s.Settle();

        // Assert
        s.Windows.Should().ContainSingle();
        TabsOf(s.Windows[0]).Should().Equal(tab);
        s.Tabs.Should().NotContain(tab).And.HaveCount(before - 1);
    }

    [AvaloniaFact]
    public void The_tab_floated_out_of_the_tab_strip_stays_the_active_one_so_the_preview_follows_it()
    {
        // Arrange
        using Scenario s = new();
        ContentTabViewModel tab = s.Tabs[1];

        // Act & Assert
        s._vm.ActiveTab.Should().NotBeSameAs(tab, "another tab is active before");

        // Act
        s.Factory.FloatDockable(tab);
        s.Settle();

        // Assert
        s._vm.ActiveTab.Should().BeSameAs(tab, "the tab that was taken out is the one being worked on");
    }

    [AvaloniaFact]
    public void Focusing_a_floated_tab_makes_it_the_active_one_again()
    {
        // Arrange
        using Scenario s = new();
        ContentTabViewModel floated = s.Tabs[1];
        ContentTabViewModel other = s.Tabs[0];

        // Act
        s.Factory.FloatDockable(floated);
        s.Settle();
        IDocumentDock windowTabs = (IDocumentDock)((IDock)s.Windows[0].Layout!).VisibleDockables!.Single();
        s._vm.NavigateToBookPathAtOffset(other.Resource.BookPath, 0);
        s.Settle();

        // Assert
        s._vm.ActiveTab.Should().BeSameAs(other);

        // Act
        s.Factory.SetFocusedDockable(windowTabs, floated);
        s.Settle();

        // Assert
        s._vm.ActiveTab.Should().BeSameAs(floated, "a click in the floated tab shows its file in the preview");
    }

    [AvaloniaFact]
    public void The_book_browser_selects_the_file_of_the_tab_that_became_active_also_a_floated_one()
    {
        // Arrange
        using Scenario s = new();
        ContentTabViewModel floated = s.Tabs[1];
        ContentTabViewModel other = s.Tabs[0];

        // Act
        s.Factory.FloatDockable(floated);
        s.Settle();
        IDocumentDock windowTabs = (IDocumentDock)((IDock)s.Windows[0].Layout!).VisibleDockables!.Single();

        // Act & Assert
        s._vm.BookBrowser.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(floated.Resource, "the tab taken out is the active one");

        // Act
        s._vm.NavigateToBookPathAtOffset(other.Resource.BookPath, 0);
        s.Settle();

        // Assert
        s._vm.BookBrowser.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(other.Resource);

        // Act
        s.Factory.SetFocusedDockable(windowTabs, floated);
        s.Settle();

        // Assert
        s._vm.BookBrowser.SelectedResources.Should().ContainSingle().Which.Should().BeSameAs(floated.Resource, "the focused tab is shown in the Book Browser");
    }

    [AvaloniaFact]
    public void The_window_of_a_floated_tab_has_no_dialog_frame()
    {
        // Arrange
        using Scenario s = new();

        // Act
        s.Factory.FloatDockable(s.Tabs[1]);
        s.Settle();
        HostWindow host = (HostWindow)s.Windows[0].Host!;
        host.Show();
        s.Settle();

        Avalonia.Controls.Presenters.ContentPresenter[] presenters = host.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.ContentPresenter>().Where(p => p.Name == "PART_ContentPresenter").ToArray();

        // Assert
        presenters.Should().NotBeEmpty();
        presenters.Take(1).Should().OnlyContain(p => p.BorderBrush == null && p.Margin == default, "only dialogs have the frame");
    }

    [AvaloniaFact]
    public void Closing_the_window_of_a_floated_tab_attaches_it_to_the_tab_strip_again()
    {
        // Arrange
        using Scenario s = new();
        int before = s.Tabs.Count;
        ContentTabViewModel tab = s.Tabs[1];

        // Act
        s.Factory.FloatDockable(tab);
        s.Settle();

        ((HostWindow)s.Windows[0].Host!).Close();
        s.Settle();

        // Assert
        s.Windows.Should().BeEmpty();
        s.Tabs.Should().Contain(tab).And.HaveCount(before, "closing the window does not close the file");
        s.MainTabs.ActiveDockable.Should().BeSameAs(tab);
    }

    [AvaloniaFact]
    public void Closing_a_floated_tab_closes_it_and_its_window()
    {
        // Arrange
        using Scenario s = new();
        int before = s.Tabs.Count;
        ContentTabViewModel tab = s.Tabs[1];

        // Act
        s.Factory.FloatDockable(tab);
        s.Settle();

        s.Factory.CloseDockable(tab);
        s.Settle();

        // Assert
        s.Windows.Should().BeEmpty();
        s.Tabs.Should().NotContain(tab).And.HaveCount(before - 1);
    }

    [AvaloniaFact]
    public void A_floated_tab_can_be_dropped_into_the_window_of_another_one_and_floated_out_again()
    {
        // Arrange
        using Scenario s = new();
        ContentTabViewModel first = s.Tabs[0];
        ContentTabViewModel second = s.Tabs[1];

        // Act
        s.Factory.FloatDockable(first);
        s.Settle();
        IDocumentDock windowTabs = (IDocumentDock)((IDock)s.Windows[0].Layout!).VisibleDockables!.Single();

        s.Factory.MoveDockable(s.MainTabs, windowTabs, second, null);
        s.Settle();

        // Assert
        s.Windows.Should().ContainSingle();
        TabsOf(s.Windows[0]).Should().BeEquivalentTo(new IDocument[] { first, second });

        // Act
        s.Factory.FloatDockable(second);
        s.Settle();

        // Assert
        s.Windows.Should().HaveCount(2);
        s.Windows.Select(TabsOf).Should().OnlyContain(tabs => tabs.Length == 1);
    }

    [AvaloniaFact]
    public void Closing_the_application_windows_attaches_all_floated_tabs_again()
    {
        // Arrange
        using Scenario s = new();
        List<ContentTabViewModel> all = s.Tabs;

        // Act
        s.Factory.FloatDockable(all[0]);
        s.Settle();
        s.Factory.FloatDockable(all[1]);
        s.Settle();

        // Assert
        s.Windows.Should().HaveCount(2);

        // Act
        s.Factory.CloseFloatingWindows();
        s.Settle();

        // Assert
        s.Windows.Should().BeEmpty();
        s.Tabs.Should().BeEquivalentTo(all);
    }

    [AvaloniaFact]
    public void A_tab_docks_only_into_a_tab_strip_never_into_a_panel_region()
    {
        // Arrange
        using Scenario s = new();
        DockManager manager = new(new DockService());
        ContentTabViewModel tab = s.Tabs[0];
        IToolDock[] regions = RegionIds
            .Select(id => (IToolDock)s.Factory.FindDockable(s._vm.Layout, d => d.Id == id)!)
            .ToArray();

        foreach (IToolDock region in regions)
        {
            foreach (DockOperation operation in new[] { DockOperation.Fill, DockOperation.Left, DockOperation.Window })
            {
                // Act
                manager.ValidateDockable(tab, region, DragAction.Move, operation, false)

                    // Assert
                    .Should().BeFalse($"{region.Id} is a panel region ({operation})");
            }
        }

        // Act & Assert
        manager.ValidateDockable(tab, s.MainTabs, DragAction.Move, DockOperation.Fill, false).Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_window_of_floated_tabs_has_the_keyboard_shortcuts_of_the_main_window()
    {
        // Arrange
        using Scenario s = new();

        // Act
        s.Factory.FloatDockable(s.Tabs[0]);
        s.Settle();

        // Assert
        ((HostWindow)s.Windows[0].Host!).KeyBindings.Should().NotBeEmpty();
    }
}
