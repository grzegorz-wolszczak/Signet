using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.App.ViewModels;
using Signet.Core.Tests.TestSupport;
using Signet.App.Views;

namespace Signet.App.MainWindowUiTests;

/// <summary>The window without a book: the start screen in the document area and the book's panels grayed out.</summary>
public sealed class StartScreenTests
{
    // Paths of the platform the tests run on (a Windows path is not split into folders on Linux).
    private static readonly string Books = Path.Combine(Path.GetTempPath(), "Books");

    private static readonly string[] RecentFiles =
    {
        Path.Combine(Books, "First.epub"), Path.Combine(Books, "Other", "Second.epub"),
    };

    [AvaloniaFact]
    public void Without_a_book_the_document_area_shows_the_start_screen_and_the_book_panels_are_disabled()
    {
        // Arrange
        MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        StartScreenView start = window.GetVisualDescendants().OfType<StartScreenView>().Single();

        // Assert
        start.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenButton").IsEffectivelyVisible.Should().BeTrue();
        start.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenButton").IsEffectivelyEnabled.Should().BeTrue();

        BookBrowserView browser = window.GetVisualDescendants().OfType<BookBrowserView>().Single();
        browser.IsEffectivelyEnabled.Should().BeFalse("the Book Browser has nothing to show without a book");
        window.Close();
    }

    [AvaloniaFact]
    public void The_recent_files_open_on_click()
    {
        // Arrange
        List<string> opened = new();
        StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }),
            () => RecentFiles, opened.Add)
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        vm.HasRecentFiles.Should().BeTrue();
        vm.RecentFileItems.Select(f => f.Name).Should().Equal("First.epub", "Second.epub");
        Button[] entries = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentFile")).ToArray();
        entries.Should().HaveCount(2);

        // Act
        entries[1].Command!.Execute(entries[1].CommandParameter);

        // Assert
        opened.Should().Equal(RecentFiles[1]);
        window.Close();
    }

    /// <summary>
    /// A book that is no longer on disk is drawn dimmed (the "missing" class) but stays clickable — the click is what
    /// offers to take it off the list.
    /// </summary>
    [AvaloniaFact]
    public void An_entry_whose_file_is_gone_is_dimmed_and_still_clickable()
    {
        // Arrange
        using TempDir temp = new();
        string here = temp.Combine("Here.epub");
        File.WriteAllText(here, string.Empty);
        string gone = temp.Combine("Gone.epub");
        StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }),
            () => new[] { here, gone }, _ => { })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Button[] entries = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentFile")).ToArray();

        // Assert
        entries.Should().HaveCount(2);
        entries[0].Classes.Contains("missing").Should().BeFalse();
        entries[1].Classes.Contains("missing").Should().BeTrue();
        entries[1].Opacity.Should().Be(0.5);
        entries[1].IsEffectivelyEnabled.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void The_context_menu_of_an_entry_removes_it_from_the_list_of_recent_files()
    {
        // Arrange
        List<string> files = new(RecentFiles);
        StartScreenViewModel? vm = null;
        vm = new StartScreenViewModel(
            new RelayCommand(() => { }), new RelayCommand(() => { }),
            () => files.ToList(), _ => { },
            path =>
            {
                files.Remove(path);
                vm!.Refresh();
            })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Button first = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("recentFile"));

        // Act
        first.ContextMenu!.Open(first); // the items bind to the entry once the menu is in the visual tree
        Dispatcher.UIThread.RunJobs();
        MenuItem remove = first.ContextMenu.Items.OfType<MenuItem>().Last();
        remove.Command!.Execute(remove.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        files.Should().Equal(RecentFiles[1]);
        vm.RecentFileItems.Select(f => f.Name).Should().Equal("Second.epub");
        window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("recentFile")).Should().Be(1);
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_list_of_recent_files_scrolls_alone_while_the_buttons_and_the_heading_stay_in_place()
    {
        // Arrange
        string[] many = Enumerable.Range(0, 60).Select(i => Path.Combine(Books, "Book" + i + ".epub")).ToArray();
        StartScreenViewModel vm = new(new RelayCommand(() => { }), new RelayCommand(() => { }), () => many, _ => { })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        ScrollViewer scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "RecentFilesScroll");
        Button open = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenButton");
        TextBlock heading = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "RecentFilesHeading");
        double openTop = open.Bounds.Y;
        double headingTop = heading.Bounds.Y;
        scroll.Offset = new Avalonia.Vector(0, 300);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        vm.RecentFileItems.Should().HaveCount(60);
        scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height, "the entries do not fit the window");
        scroll.Offset.Y.Should().BeGreaterThan(0, "the list scrolled");
        scroll.GetVisualDescendants().OfType<Button>().Should().NotContain(b => b.Name == "OpenButton", "the buttons are not in the scrolled part");
        open.Bounds.Y.Should().Be(openTop, "the buttons did not move");
        heading.Bounds.Y.Should().Be(headingTop, "the heading did not move");
        (scroll.Bounds.Bottom + 32).Should().BeLessThanOrEqualTo(window.Bounds.Height + 1, "the list ends within the window");
        window.Close();
    }

    [AvaloniaFact]
    public void RecentFileContextMenu_WhenOpened_ShowsExpectedIcons()
    {
        // Arrange
        using TempDir temp = new();
        string path = temp.Combine("Book.epub");
        File.WriteAllText(path, string.Empty);
        new IconThemeManager(new SettingsStore(temp.Combine("icon-theme-settings.xml")), NullLogger<IconThemeManager>.Instance).ApplySaved();
        using StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }),
            () => new[] { path }, _ => { })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Button entry = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("recentFile"));

        // Act
        entry.ContextMenu!.Open(entry);
        Dispatcher.UIThread.RunJobs();
        MenuItem title = entry.ContextMenu.Items.OfType<MenuItem>().First();
        MenuItem[] items = entry.ContextMenu.Items.OfType<MenuItem>().Skip(1).ToArray();

        // Assert
        title.Classes.Should().Contain("menuTitle");
        title.Header.Should().Be("Book.epub", "the title names the right-clicked file");
        items.Select(i => i.Tag).Should().Equal(ActionIconKeys.Open, ActionIconKeys.Delete);
        foreach (MenuItem item in items)
        {
            Control icon = item.Icon.Should().BeAssignableTo<Control>().Subject;
            icon.GetVisualDescendants().Should().Contain(
                c => c.IsVisible && ((c is Image && ((Image)c).Source != null) || (c is PathIcon && ((PathIcon)c).Data != null)),
                "the icon of {0} is drawn from its Tag", item.Header);
        }

        entry.ContextMenu.Close();
        window.Close();
    }
}
