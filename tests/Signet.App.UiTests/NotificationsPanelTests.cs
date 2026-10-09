using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Signet.App.Docking;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>The "Notifications" panel view and the notification bell on its dock tab.</summary>
public sealed class NotificationsPanelTests
{
    private static void Render(Window window)
    {
        window.Show();
        for (int i = 0; i < 2; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaFact]
    public void Panel_lists_the_messages_and_marks_warnings_as_read_when_visible()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar);
        statusBar.ShowMessage("Book loaded", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("Delete Unused Stylesheet Selectors cancelled: part0000.html is not well-formed.",
            TimeSpan.FromSeconds(1), NotificationLevel.Warning);
        vm.HasUnreadWarnings.Should().BeTrue();

        var window = new Window { Width = 600, Height = 300, Content = new NotificationsView { DataContext = vm } };
        Render(window);

        string[] texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToArray();
        texts.Should().Contain("Book loaded");
        texts.Should().Contain(t => t.StartsWith("Delete Unused Stylesheet Selectors cancelled", StringComparison.Ordinal));
        vm.HasUnreadWarnings.Should().BeFalse("the list is on screen, so the warning was seen");
        window.Close();
    }

    private static (Window Window, NotificationsTool Notifications, IFactory Factory) ShowToolDock(bool pinNotifications)
    {
        Factory factory = new();
        NotificationsTool notifications = new();
        ValidationResultsTool validation = new();
        IToolDock toolDock = factory.CreateToolDock();
        toolDock.Alignment = Alignment.Bottom;
        toolDock.VisibleDockables = factory.CreateList<IDockable>(validation, notifications);
        toolDock.ActiveDockable = validation;
        IRootDock root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(toolDock);
        root.ActiveDockable = toolDock;
        root.DefaultDockable = toolDock;
        factory.InitLayout(root);
        if (pinNotifications)
        {
            factory.PinDockable(notifications);
        }

        var window = new Window { Width = 600, Height = 300, Content = new DockControl { Layout = root, Factory = factory } };
        Render(window);
        return (window, notifications, factory);
    }

    private static Color ErrorColor(Window window)
    {
        window.TryFindResource("SignetErrorBrush", window.ActualThemeVariant, out object? error).Should().BeTrue();
        return ((ISolidColorBrush)error!).Color;
    }

    // The bell after the title: no dot and no "[n]" while nothing is unread, the red dot and "[n]" otherwise; the
    // title keeps its text and color.
    private static void BellFollowsAttention(Window window, Control tab, NotificationsTool notifications)
    {
        NotificationBell bell = tab.GetVisualDescendants().OfType<NotificationBell>().Single();
        TextBlock title = tab.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == notifications.Title);
        Ellipse dot = bell.GetVisualDescendants().OfType<Ellipse>().Single();
        TextBlock count = bell.GetVisualDescendants().OfType<TextBlock>().Single();
        IBrush? titleBrush = title.Foreground;
        bell.IsEffectivelyVisible.Should().BeTrue("the Notifications tab always shows the bell");
        dot.IsVisible.Should().BeFalse();
        count.IsVisible.Should().BeFalse();

        notifications.AttentionCount = 4;
        Render(window);

        dot.IsVisible.Should().BeTrue();
        ((ISolidColorBrush)dot.Fill!).Color.Should().Be(ErrorColor(window));
        count.IsVisible.Should().BeTrue();
        count.Text.Should().Be("[4]");
        title.Text.Should().Be(notifications.Title, "the count is not appended to the title");
        title.Foreground.Should().BeSameAs(titleBrush, "the title keeps its color");

        notifications.AttentionCount = 0;
        Render(window);
        dot.IsVisible.Should().BeFalse();
        count.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Tool_tab_shows_the_bell_with_the_unread_count()
    {
        (Window window, NotificationsTool notifications, _) = ShowToolDock(pinNotifications: false);

        ToolTabStripItem tab = window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(t => t.DataContext == notifications);
        BellFollowsAttention(window, tab, notifications);

        ToolTabStripItem other = window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(t => t.DataContext != notifications);
        other.GetVisualDescendants().OfType<NotificationBell>().Should().OnlyContain(b => !b.IsVisible, "only the Notifications tab has a bell");
        window.Close();
    }

    [AvaloniaFact]
    public void Collapsed_tab_shows_the_bell_with_the_unread_count()
    {
        (Window window, NotificationsTool notifications, _) = ShowToolDock(pinNotifications: true);

        ToolPinItemControl pin = window.GetVisualDescendants().OfType<ToolPinItemControl>().Single(t => t.DataContext == notifications);
        BellFollowsAttention(window, pin, notifications);
        window.Close();
    }

    [AvaloniaFact]
    public void Error_rows_are_drawn_with_the_error_color()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar);
        statusBar.ShowMessage("Book loaded", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("Add Nav to Reading Order is not available for EPUB 2.", TimeSpan.FromSeconds(1), NotificationLevel.Warning);

        var window = new Window { Width = 600, Height = 300, Content = new NotificationsView { DataContext = vm } };
        Render(window);

        TextBlock[] texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
        TextBlock error = texts.Single(t => t.Text?.StartsWith("Add Nav", StringComparison.Ordinal) == true);
        TextBlock info = texts.Single(t => t.Text == "Book loaded");
        ((ISolidColorBrush)error.Foreground!).Color.Should().Be(ErrorColor(window));
        ((ISolidColorBrush)info.Foreground!).Color.Should().NotBe(ErrorColor(window));
        window.Close();
    }
}
