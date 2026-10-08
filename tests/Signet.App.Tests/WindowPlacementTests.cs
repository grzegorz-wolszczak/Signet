using System.Collections.Generic;
using AwesomeAssertions;
using Avalonia;
using Signet.App.Infrastructure;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests of <see cref="WindowPlacement"/>: when a remembered window is fully visible (both corners on some monitor's
/// working area) and where it goes when it is not (centered on the primary monitor, shrunk to fit).
/// </summary>
public sealed class WindowPlacementTests
{
    // Two monitors side by side, 1920×1040 working areas (a 40 px taskbar at the bottom); the primary is on the left.
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    private static readonly PixelRect Secondary = new(1920, 0, 1920, 1040);
    private static readonly IReadOnlyList<PixelRect> TwoMonitors = new[] { Primary, Secondary };

    [Theory]
    [InlineData(100, 100, 800, 600)]
    [InlineData(1500, 100, 800, 600)]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(3000, 400, 840, 640)]
    public void A_window_whose_corners_lie_on_monitors_is_left_where_it_is(int x, int y, int width, int height)
    {
        PixelRect rect = new(x, y, width, height);

        WindowPlacement.IsFullyVisible(rect, TwoMonitors).Should().BeTrue();
        WindowPlacement.Correct(rect, TwoMonitors, Primary).Should().BeNull();
    }

    [Theory]
    [InlineData(-200, 100, 800, 600)]
    [InlineData(3500, 100, 800, 600)]
    [InlineData(100, 700, 800, 600)]
    [InlineData(5000, 5000, 800, 600)]
    [InlineData(100, -50, 800, 600)]
    public void A_window_with_a_corner_off_every_working_area_is_not_fully_visible(int x, int y, int width, int height)
    {
        WindowPlacement.IsFullyVisible(new PixelRect(x, y, width, height), TwoMonitors).Should().BeFalse();
    }

    [Fact]
    public void A_window_left_on_a_monitor_that_is_gone_is_centered_on_the_primary_one()
    {
        PixelRect rect = new(2500, 200, 800, 600);

        PixelRect? corrected = WindowPlacement.Correct(rect, new[] { Primary }, Primary);

        corrected.Should().Be(new PixelRect(560, 220, 800, 600));
    }

    [Fact]
    public void A_window_larger_than_the_primary_monitor_is_shrunk_to_fit_it()
    {
        PixelRect rect = new(0, 0, 3000, 2000);

        PixelRect? corrected = WindowPlacement.Correct(rect, new[] { Primary }, Primary);

        corrected.Should().Be(Primary);
    }

    [Fact]
    public void An_empty_rectangle_is_never_visible()
    {
        WindowPlacement.IsFullyVisible(new PixelRect(10, 10, 0, 0), TwoMonitors).Should().BeFalse();
    }
}
