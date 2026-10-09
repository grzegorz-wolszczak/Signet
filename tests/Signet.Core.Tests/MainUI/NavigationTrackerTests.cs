using System;
using System.Collections.Generic;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="NavigationTracker"/> — one user action (command) records at most one place: the one the caret
/// was at when the first jump of the command started, and only when the caret moved to another file or line.
/// </summary>
public sealed class NavigationTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static NavigationPlace At(string bookPath, int line) => new(bookPath, line * 10, line, DateTimeOffset.UnixEpoch);

    // A dispatcher stand-in: the end of the command runs when the test calls EndCommand.
    private sealed class Commands
    {
        private readonly List<Action> _queued = new();

        public bool Schedule(Action action)
        {
            _queued.Add(action);
            return true;
        }

        public void EndCommand()
        {
            List<Action> queued = new(_queued);
            _queued.Clear();
            queued.ForEach(a => a());
        }
    }

    [Theory]
    [AutoData]
    public void Opening_a_tab_and_moving_its_caret_in_one_command_records_only_the_place_left(string from, string to)
    {
        NavigationHistory history = new();
        Commands commands = new();
        NavigationTracker tracker = new(history, commands.Schedule, () => Now);
        tracker.UpdateCurrentPlace(At(from, 5));

        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(to, 1));
        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(to, 40));
        commands.EndCommand();

        history.BackPlaces.Should().Equal(At(from, 5) with { Time = Now });
    }

    [Theory]
    [AutoData]
    public void A_jump_that_stays_on_the_same_line_records_nothing(string file)
    {
        NavigationHistory history = new();
        Commands commands = new();
        NavigationTracker tracker = new(history, commands.Schedule);
        tracker.UpdateCurrentPlace(At(file, 5));

        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(file, 5) with { Offset = 52 });
        commands.EndCommand();

        history.CanGoBack.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Without_a_scheduler_the_next_jump_or_Flush_commits_the_pending_one(string a, string b, string c)
    {
        NavigationHistory history = new();
        NavigationTracker tracker = new(history, _ => false);
        tracker.UpdateCurrentPlace(At(a, 1));

        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(b, 1));
        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(c, 1));
        tracker.Flush();

        history.BackPlaces.Should().HaveCount(2);
        history.BackPlaces[0].BookPath.Should().Be(a);
        history.BackPlaces[1].BookPath.Should().Be(b);
    }

    [Theory]
    [AutoData]
    public void Jumps_while_suspended_are_not_recorded(string a, string b)
    {
        NavigationHistory history = new();
        Commands commands = new();
        NavigationTracker tracker = new(history, commands.Schedule);
        tracker.UpdateCurrentPlace(At(a, 1));

        using (tracker.Suspend())
        {
            tracker.NoteNavigation();
            tracker.UpdateCurrentPlace(At(b, 1));
        }

        commands.EndCommand();

        history.CanGoBack.Should().BeFalse();
        tracker.CurrentPlace.Should().Be(At(b, 1));
    }

    [Theory]
    [AutoData]
    public void A_jump_from_nowhere_records_nothing(string file)
    {
        NavigationHistory history = new();
        Commands commands = new();
        NavigationTracker tracker = new(history, commands.Schedule);

        tracker.NoteNavigation();
        tracker.UpdateCurrentPlace(At(file, 1));
        commands.EndCommand();

        history.CanGoBack.Should().BeFalse();
    }
}
