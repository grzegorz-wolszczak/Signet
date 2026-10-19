using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Input;
using Signet.App.ViewModels;
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
        start.GetVisualDescendants().OfType<ScrollViewer>().First().IsVisible.Should().BeTrue();
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
        MenuItem remove = first.ContextMenu.Items.OfType<MenuItem>().Single();
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
    public void A_long_list_of_recent_files_scrolls_when_it_does_not_fit_the_window()
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
        ScrollViewer scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();

        // Assert
        vm.RecentFileItems.Should().HaveCount(60);
        scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height, "the entries do not fit the window");
        scroll.ScrollBarMaximum.Y.Should().BeGreaterThan(0, "so the list can be scrolled");
        window.Close();
    }
}
