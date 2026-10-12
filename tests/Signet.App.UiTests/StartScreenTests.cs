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

namespace Signet.App.UiTests;

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
        MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        StartScreenView start = window.GetVisualDescendants().OfType<StartScreenView>().Single();
        start.GetVisualDescendants().OfType<ScrollViewer>().First().IsVisible.Should().BeTrue();
        start.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenButton").IsEffectivelyEnabled.Should().BeTrue();

        BookBrowserView browser = window.GetVisualDescendants().OfType<BookBrowserView>().Single();
        browser.IsEffectivelyEnabled.Should().BeFalse("the Book Browser has nothing to show without a book");
        window.Close();
    }

    [AvaloniaFact]
    public void The_recent_files_open_on_click()
    {
        List<string> opened = new();
        StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }),
            () => RecentFiles, opened.Add)
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        vm.HasRecentFiles.Should().BeTrue();
        vm.RecentFileItems.Select(f => f.Name).Should().Equal("First.epub", "Second.epub");
        Button[] entries = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentFile")).ToArray();
        entries.Should().HaveCount(2);

        entries[1].Command!.Execute(entries[1].CommandParameter);

        opened.Should().Equal(RecentFiles[1]);
        window.Close();
    }
}
