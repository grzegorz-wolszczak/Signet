using System;
using System.Linq;
using Avalonia.Controls;
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

/// <summary>The "Notifications" panel view and the attention state of its dock tab.</summary>
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

    [AvaloniaFact]
    public void Tool_tab_needing_attention_is_drawn_with_the_warning_color()
    {
        Factory factory = new();
        NotificationsTool notifications = new();
        ValidationResultsTool validation = new();
        IToolDock toolDock = factory.CreateToolDock();
        toolDock.VisibleDockables = factory.CreateList<IDockable>(validation, notifications);
        toolDock.ActiveDockable = validation;
        IRootDock root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(toolDock);
        root.ActiveDockable = toolDock;
        root.DefaultDockable = toolDock;
        factory.InitLayout(root);
        var window = new Window { Width = 600, Height = 300, Content = new DockControl { Layout = root, Factory = factory } };
        Render(window);

        ToolTabStripItem tab = window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(t => t.DataContext == notifications);
        TextBlock title = tab.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == notifications.Title);
        Color normal = ((ISolidColorBrush)title.Foreground!).Color;

        notifications.NeedsAttention = true;
        Render(window);

        tab.Classes.Should().Contain(DockAttention.ClassName);
        window.TryFindResource("SignetWarningBrush", window.ActualThemeVariant, out object? warning).Should().BeTrue();
        Color expected = ((ISolidColorBrush)warning!).Color;
        expected.Should().NotBe(normal);
        ((ISolidColorBrush)title.Foreground!).Color.Should().Be(expected, "the tab text itself is drawn in the warning color");

        notifications.NeedsAttention = false;
        Render(window);
        tab.Classes.Should().NotContain(DockAttention.ClassName);
        ((ISolidColorBrush)title.Foreground!).Color.Should().Be(normal);
        window.Close();
    }

    [AvaloniaFact]
    public void Collapsed_tab_needing_attention_is_drawn_with_the_warning_color()
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
        factory.PinDockable(notifications);
        var window = new Window { Width = 600, Height = 300, Content = new DockControl { Layout = root, Factory = factory } };
        Render(window);

        notifications.NeedsAttention = true;
        Render(window);

        ToolPinItemControl pin = window.GetVisualDescendants().OfType<ToolPinItemControl>().Single(t => t.DataContext == notifications);
        pin.Classes.Should().Contain(DockAttention.ClassName);
        window.TryFindResource("SignetWarningBrush", window.ActualThemeVariant, out object? warning).Should().BeTrue();
        TextBlock title = pin.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == notifications.Title);
        ((ISolidColorBrush)title.Foreground!).Color.Should().Be(((ISolidColorBrush)warning!).Color);
        window.Close();
    }
}
