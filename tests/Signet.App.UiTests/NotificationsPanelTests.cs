using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AutoFixture;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

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
        // Arrange
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar);

        // Act
        statusBar.ShowMessage("Book loaded", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("Delete Unused Stylesheet Selectors cancelled: part0000.html is not well-formed.",
            TimeSpan.FromSeconds(1), NotificationLevel.Warning);

        // Assert
        vm.HasUnreadWarnings.Should().BeTrue();

        var window = new Window { Width = 600, Height = 300, Content = new NotificationsView { DataContext = vm } };

        // Act
        Render(window);

        string[] texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToArray();

        // Assert
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

    private static Window ShowNotificationsTool(NotificationsViewModel viewModel)
    {
        Factory factory = new();
        NotificationsTool notifications = new() { ViewModel = viewModel };
        IToolDock toolDock = factory.CreateToolDock();
        toolDock.Alignment = Alignment.Bottom;
        toolDock.VisibleDockables = factory.CreateList<IDockable>(notifications);
        toolDock.ActiveDockable = notifications;
        IRootDock root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(toolDock);
        root.ActiveDockable = toolDock;
        root.DefaultDockable = toolDock;
        factory.InitLayout(root);

        var window = new Window { Width = 600, Height = 300, Content = new DockControl { Layout = root, Factory = factory } };
        Render(window);
        return window;
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
        // Act
        (Window window, NotificationsTool notifications, _) = ShowToolDock(pinNotifications: false);

        ToolTabStripItem tab = window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(t => t.DataContext == notifications);
        BellFollowsAttention(window, tab, notifications);

        ToolTabStripItem other = window.GetVisualDescendants().OfType<ToolTabStripItem>().Single(t => t.DataContext != notifications);

        // Assert
        other.GetVisualDescendants().OfType<NotificationBell>().Should().OnlyContain(b => !b.IsVisible, "only the Notifications tab has a bell");
        window.Close();
    }

    [AvaloniaFact]
    public void Collapsed_tab_shows_the_bell_with_the_unread_count()
    {
        // Act
        (Window window, NotificationsTool notifications, _) = ShowToolDock(pinNotifications: true);

        ToolPinItemControl pin = window.GetVisualDescendants().OfType<ToolPinItemControl>().Single(t => t.DataContext == notifications);

        // Act & Assert
        BellFollowsAttention(window, pin, notifications);
        window.Close();
    }

    [AvaloniaFact]
    public void Error_rows_are_drawn_with_the_error_color()
    {
        // Arrange
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar);

        // Act
        statusBar.ShowMessage("Book loaded", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("Add Nav to Reading Order is not available for EPUB 2.", TimeSpan.FromSeconds(1), NotificationLevel.Warning);

        var window = new Window { Width = 600, Height = 300, Content = new NotificationsView { DataContext = vm } };
        Render(window);

        TextBlock[] texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
        TextBlock error = texts.Single(t => t.Text?.StartsWith("Add Nav", StringComparison.Ordinal) == true);
        TextBlock info = texts.Single(t => t.Text == "Book loaded");

        // Assert
        ((ISolidColorBrush)error.Foreground!).Color.Should().Be(ErrorColor(window));
        ((ISolidColorBrush)info.Foreground!).Color.Should().NotBe(ErrorColor(window));
        window.Close();
    }

    [AvaloniaFact]
    public void Zero_entry_spacing_leaves_no_gap_between_rows()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        settings.NotificationsAppearance = settings.NotificationsAppearance with { EntrySpacing = 0 };
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar, settings);

        // Act
        statusBar.ShowMessage("First", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("Second", TimeSpan.FromSeconds(1));

        var window = new Window { Width = 600, Height = 300, Content = new NotificationsView { DataContext = vm } };
        Render(window);

        ListBoxItem[] rows = window.GetVisualDescendants().OfType<ListBoxItem>()
            .OrderBy(r => r.Bounds.Top).ToArray();

        // Assert
        rows.Should().HaveCount(2);
        (rows[1].Bounds.Top - rows[0].Bounds.Bottom).Should().Be(0);
        rows[0].Padding.Should().Be(new Thickness(0));
        rows[0].Margin.Should().Be(new Thickness(0));

        Border[] messageCells = rows.Select(row => row.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("messageCell"))).ToArray();
        messageCells[0].Bounds.Height.Should().Be(rows[0].Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void FontFallback_ToolHeaderFontDiffers_MatchesPreferencesPreview()
    {
        // Arrange
        Fixture fixture = new();
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        settings.NotificationsAppearance = settings.NotificationsAppearance with { EntrySpacing = 0 };
        using StatusBarService statusBar = new();
        NotificationsViewModel actualViewModel = new(statusBar, settings);
        NotificationsViewModel previewViewModel = new(statusBar, settings) { IsSample = true };
        StatusNotification[] entries =
        [
            new StatusNotification(fixture.Create<DateTime>(), fixture.Create<string>(), NotificationLevel.Info),
            new StatusNotification(fixture.Create<DateTime>(), fixture.Create<string>(), NotificationLevel.Warning)
        ];
        foreach (StatusNotification entry in entries)
        {
            actualViewModel.Add(entry);
            previewViewModel.Add(entry);
        }

        Application app = Application.Current!;
        UiDensityManager.ApplyToolHeader(app, new ToolHeaderFontAppearance("Courier New", 24));

        // Act
        Window actualWindow = ShowNotificationsTool(actualViewModel);
        var previewWindow = new Window
        {
            Width = 600,
            Height = 300,
            Content = new NotificationsView { DataContext = previewViewModel }
        };
        Render(previewWindow);

        // Assert
        SelectableTextBlock actualMessage = actualWindow.GetVisualDescendants().OfType<SelectableTextBlock>().First();
        SelectableTextBlock previewMessage = previewWindow.GetVisualDescendants().OfType<SelectableTextBlock>().First();
        actualMessage.FontFamily.Name.Should().Be(previewMessage.FontFamily.Name);
        actualMessage.FontSize.Should().Be(previewMessage.FontSize);

        ListBoxItem[] actualRows = actualWindow.GetVisualDescendants().OfType<NotificationsView>().Single()
            .GetVisualDescendants().OfType<ListBoxItem>().OrderBy(row => row.Bounds.Top).ToArray();
        ListBoxItem[] previewRows = previewWindow.GetVisualDescendants().OfType<ListBoxItem>()
            .OrderBy(row => row.Bounds.Top).ToArray();
        actualRows.Should().HaveCount(2);
        previewRows.Should().HaveCount(2);
        actualRows[0].Bounds.Height.Should().Be(previewRows[0].Bounds.Height);
        (actualRows[1].Bounds.Top - actualRows[0].Bounds.Bottom).Should().Be(0);
        (previewRows[1].Bounds.Top - previewRows[0].Bounds.Bottom).Should().Be(0);

        actualWindow.Close();
        previewWindow.Close();
        UiDensityManager.ApplyToolHeader(app, ToolHeaderFontAppearance.Default);
    }

    [AvaloniaFact]
    public void WrapRows_toggle_switches_the_message_text_wrapping()
    {
        // Arrange
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar);
        statusBar.ShowMessage("A message long enough to need wrapping or an ellipsis to fit the column", TimeSpan.FromSeconds(1));

        var window = new Window { Width = 300, Height = 200, Content = new NotificationsView { DataContext = vm } };
        Render(window);

        SelectableTextBlock Message() => window.GetVisualDescendants().OfType<SelectableTextBlock>().Single();

        // Assert
        vm.WrapRows.Should().BeFalse("off by default");
        Message().TextWrapping.Should().Be(TextWrapping.NoWrap);

        // Act
        vm.WrapRows = true;
        Render(window);

        // Assert
        Message().TextWrapping.Should().Be(TextWrapping.Wrap);
        window.Close();
    }

    [AvaloniaFact]
    public void Clear_on_the_sample_panel_does_not_clear_the_entries()
    {
        // Arrange
        using StatusBarService statusBar = new();
        NotificationsViewModel vm = new(statusBar) { IsSample = true };
        statusBar.ShowMessage("Book loaded", TimeSpan.FromSeconds(1));

        // Act
        vm.ClearCommand.Execute(null);

        // Assert
        vm.HasEntries.Should().BeTrue();
        vm.Entries.Should().HaveCount(1);
    }
}
