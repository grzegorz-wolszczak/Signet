using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
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
    public void The_menu_of_an_entry_removes_it_from_the_list_of_recent_files()
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
        MenuFlyout menu = EntryMenu(first);
        menu.ShowAt(first); // the items bind to the entry once the menu is in the visual tree
        Dispatcher.UIThread.RunJobs();
        MenuItem remove = menu.Items.OfType<MenuItem>().Last();
        remove.Command!.Execute(remove.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        files.Should().Equal(RecentFiles[1]);
        vm.RecentFileItems.Select(f => f.Name).Should().Equal("Second.epub");
        window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("recentFile")).Should().Be(1);
        menu.Hide();
        window.Close();
    }

    /// <summary>
    /// A file that is no longer on disk has no menu: it has one button, the cross, which takes it off the list
    /// straight away (there is nothing to open, to show in the file manager, or to decide).
    /// </summary>
    [AvaloniaFact]
    public void A_file_that_is_gone_is_removed_from_the_list_by_its_own_button()
    {
        // Arrange
        using TempDir temp = new();
        string here = temp.Combine("Here.epub");
        File.WriteAllText(here, string.Empty);
        List<string> files = new() { here, temp.Combine("Gone.epub") };
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

        Button[] entries = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentFile")).ToArray();

        // Assert
        vm.RecentFileItems[1].IsMissing.Should().BeTrue();
        Shown(entries[0]).Content.Should().Be(MenuGlyph, "a file that is there gets the menu button");
        Shown(entries[1]).Content.Should().Be(RemoveGlyph, "a file that is gone gets the cross");

        // Act
        Button remove = Shown(entries[1]);
        remove.Command!.Execute(remove.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        files.Should().Equal(here);
        vm.RecentFileItems.Select(f => f.Name).Should().Equal("Here.epub");
        window.Close();
    }

    /// <summary>
    /// The button of an entry shows up only under the pointer, is pushed to the right edge of the entry and keeps a
    /// few pixels off it (a long list has a scroll bar there). It is hidden with Opacity, not IsVisible, so the width
    /// of the entry does not change when the pointer arrives.
    /// </summary>
    [AvaloniaFact]
    public void The_button_of_an_entry_appears_under_the_pointer_and_stays_off_the_right_edge()
    {
        // Arrange
        using StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }), () => RecentFiles, _ => { })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Button entry = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("recentFile"));
        Button actions = Shown(entry);

        // Assert
        actions.Opacity.Should().Be(0, "nothing is on the entry yet");
        actions.IsHitTestVisible.Should().BeFalse();
        actions.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
        actions.Margin.Right.Should().BeGreaterThan(0, "the button does not touch the scroll bar of a long list");
        actions.Bounds.Right.Should().BeLessThan(entry.Bounds.Width);

        // Act
        ((IPseudoClasses)entry.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        actions.Opacity.Should().Be(1);
        actions.IsHitTestVisible.Should().BeTrue();

        // Act
        ((IPseudoClasses)entry.Classes).Set(":pointerover", false);
        Dispatcher.UIThread.RunJobs();

        // Assert
        actions.Opacity.Should().Be(0);
        window.Close();
    }

    // The icon of each item of the menu; "Open Selected" has none (the open icon belongs to "Show in Explorer").
    private static readonly object?[] MenuIconKeys =
    {
        null, ActionIconKeys.Open, ActionIconKeys.Copy, ActionIconKeys.Delete,
    };

    // The glyphs of the two buttons of an entry: symbols written in the XAML, not text of the resources.
    private const string MenuGlyph = "⋮";

    private const string RemoveGlyph = "✕";

    // The buttons of an entry: the one that opens its menu and the cross of a file that is gone (only one is shown).
    private static Button[] EntryButtons(Button entry) =>
        entry.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("entryMenu")).ToArray();

    private static Button Shown(Button entry) => EntryButtons(entry).Single(b => b.IsVisible);

    private static MenuFlyout EntryMenu(Button entry) =>
        (MenuFlyout)EntryButtons(entry).Single(b => b.Flyout is MenuFlyout).Flyout!;

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
    public void The_menu_of_an_entry_shows_its_four_actions_with_their_icons()
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
        MenuFlyout menu = EntryMenu(entry);
        menu.ShowAt(entry);
        Dispatcher.UIThread.RunJobs();
        MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();

        // Assert
        items.Should().HaveCount(4);
        items.Select(i => i.Header).Should().Equal(
            Strings.Get("StartScreen_OpenSelected"), Strings.Get("Menu_OpenFileLocation"),
            Strings.Get("StartScreen_CopyPath"), Strings.Get("Menu_RemoveFromRecentFiles"));
        items.Select(i => i.Tag).Should().Equal(MenuIconKeys);
        foreach (MenuItem item in items.Where(i => i.Tag is not null))
        {
            Control icon = item.Icon.Should().BeAssignableTo<Control>().Subject;
            icon.GetVisualDescendants().Should().Contain(
                c => c.IsVisible && ((c is Image && ((Image)c).Source != null) || (c is PathIcon && ((PathIcon)c).Data != null)),
                "the icon of {0} is drawn from its Tag", item.Header);
        }

        menu.Hide();
        window.Close();
    }

    /// <summary>"Copy Path" puts the full path of the entry on the clipboard, through the hook the view attaches.</summary>
    [AvaloniaFact]
    public void Copy_path_puts_the_full_path_of_the_entry_on_the_clipboard()
    {
        // Arrange
        string? copied = null;
        using StartScreenViewModel vm = new(
            new RelayCommand(() => { }), new RelayCommand(() => { }), () => RecentFiles, _ => { })
        {
            IsVisible = true,
        };
        Window window = new() { Width = 600, Height = 400, Content = new StartScreenView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Act
        vm.CopyPath = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        vm.RecentFileItems[1].CopyPathCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        copied.Should().Be(RecentFiles[1]);
        window.Close();
    }
}
