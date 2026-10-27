using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// <see cref="ProgressDebouncer{T}"/> on a <see cref="HistoricalScheduler"/>, so the coalescing window is advanced
/// deterministically instead of waited on in real time.
/// </summary>
public sealed class ProgressDebouncerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void The_first_report_ever_goes_through_at_once()
    {
        // Arrange
        HistoricalScheduler scheduler = new();
        List<int> applied = new();
        using ProgressDebouncer<int> sut = new(Window, applied.Add, scheduler);

        // Act
        sut.Report(1);

        // Assert
        applied.Should().Equal(1);
    }

    [Fact]
    public void A_burst_within_the_window_is_coalesced_to_its_latest_value()
    {
        // Arrange
        HistoricalScheduler scheduler = new();
        List<int> applied = new();
        using ProgressDebouncer<int> sut = new(Window, applied.Add, scheduler);
        sut.Report(1);
        applied.Clear();

        // Act
        sut.Report(2);
        sut.Report(3);
        sut.Report(4);

        // Assert — nothing yet: all three landed inside the window of the first (already-applied) report.
        applied.Should().BeEmpty();

        // Act
        scheduler.AdvanceBy(Window);

        // Assert
        applied.Should().Equal(4);
    }

    [Fact]
    public void A_continuous_stream_still_flushes_at_the_window_s_cadence()
    {
        // Arrange
        HistoricalScheduler scheduler = new();
        List<int> applied = new();
        using ProgressDebouncer<int> sut = new(Window, applied.Add, scheduler);
        sut.Report(0);
        applied.Clear();

        // Act — one report every tenth of the window, continuously, for three windows.
        for (int i = 1; i <= 30; i++)
        {
            scheduler.AdvanceBy(TimeSpan.FromTicks(Window.Ticks / 10));
            sut.Report(i);
        }

        // Assert — flushed roughly every window, not starved by the continuous stream, and not every single value.
        applied.Should().NotBeEmpty();
        applied.Count.Should().BeLessThan(30);
        applied[^1].Should().BeLessThan(30, "the last couple of reports are still pending, not yet due");
    }

    [Fact]
    public void A_value_marked_flushNow_goes_through_even_inside_the_window_and_is_not_coalesced_away()
    {
        // Arrange
        HistoricalScheduler scheduler = new();
        List<int> applied = new();
        using ProgressDebouncer<int> sut = new(Window, applied.Add, scheduler);
        sut.Report(1);
        applied.Clear();

        // Act
        sut.Report(2, flushNow: true);
        sut.Report(3);

        // Assert — 2 went through at once (not coalesced into 3's later flush).
        applied.Should().Equal(2);

        // Act
        scheduler.AdvanceBy(Window);

        // Assert
        applied.Should().Equal(2, 3);
    }

    [Fact]
    public void Disposing_flushes_a_value_still_pending_instead_of_dropping_it()
    {
        // Arrange
        HistoricalScheduler scheduler = new();
        List<int> applied = new();
        ProgressDebouncer<int> sut = new(Window, applied.Add, scheduler);
        sut.Report(1);
        applied.Clear();
        sut.Report(2);

        // Act
        sut.Dispose();

        // Assert
        applied.Should().Equal(2);
    }
}
