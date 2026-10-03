using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of <see cref="NotificationsViewModel"/> — the history of the status bar messages.</summary>
public sealed class NotificationsViewModelTests
{
    private static StatusNotification Info(string text) => new(DateTime.Now, text, NotificationLevel.Info);

    private static StatusNotification Warning(string text) => new(DateTime.Now, text, NotificationLevel.Warning);

    [Fact]
    public void Messages_shown_in_the_status_bar_are_recorded_newest_first()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);

        statusBar.ShowMessage("first", TimeSpan.FromSeconds(1));
        statusBar.ShowMessage("second", TimeSpan.FromSeconds(1), NotificationLevel.Warning);

        sut.Entries.Select(e => e.Message).Should().Equal("second", "first");
        sut.Entries[0].IsWarning.Should().BeTrue();
        sut.Entries[1].IsWarning.Should().BeFalse();
        sut.HasEntries.Should().BeTrue();
    }

    [Fact]
    public void Clearing_the_status_bar_is_not_recorded()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);

        statusBar.ShowMessage(string.Empty);
        statusBar.Clear();

        sut.Entries.Should().BeEmpty();
        sut.HasEntries.Should().BeFalse();
    }

    [Fact]
    public void Only_warnings_raise_the_unread_counter_and_the_warning_event()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);
        int raised = 0;
        sut.WarningArrived += (_, _) => raised++;

        sut.Add(Info("done"));
        sut.HasUnreadWarnings.Should().BeFalse();

        sut.Add(Warning("cancelled"));
        sut.Add(Warning("failed"));

        sut.UnreadWarnings.Should().Be(2);
        sut.HasUnreadWarnings.Should().BeTrue();
        raised.Should().Be(2);
    }

    [Fact]
    public void MarkAsRead_resets_the_counter_but_keeps_the_entries()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);
        sut.Add(Warning("cancelled"));

        sut.MarkAsRead();

        sut.UnreadWarnings.Should().Be(0);
        sut.Entries.Should().ContainSingle();
    }

    [Fact]
    public void Clear_removes_the_entries_and_the_unread_counter()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);
        sut.Add(Warning("cancelled"));
        sut.Add(Info("done"));

        sut.ClearCommand.Execute(null);

        sut.Entries.Should().BeEmpty();
        sut.HasEntries.Should().BeFalse();
        sut.UnreadWarnings.Should().Be(0);
    }

    [Fact]
    public void The_list_is_limited_and_drops_the_oldest_entries()
    {
        using StatusBarService statusBar = new();
        NotificationsViewModel sut = new(statusBar);

        for (int i = 0; i < NotificationsViewModel.MaxEntries + 5; i++)
        {
            sut.Add(Info("message " + i));
        }

        sut.Entries.Should().HaveCount(NotificationsViewModel.MaxEntries);
        sut.Entries[0].Message.Should().Be("message " + (NotificationsViewModel.MaxEntries + 4));
        sut.Entries[^1].Message.Should().Be("message 5");
    }

    [Fact]
    public void Entry_time_is_formatted_as_hours_minutes_seconds()
    {
        NotificationEntry entry = new(new StatusNotification(new DateTime(2026, 10, 3, 20, 34, 7), "x", NotificationLevel.Info));

        entry.TimeText.Should().Be("20:34:07");
    }
}
