using System;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Overlay;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Overlay;

public sealed class SmilClockTests
{
    [Theory]
    [InlineData("1", 1000)]
    [InlineData("1.5", 1500)]
    [InlineData("3s", 3000)]
    [InlineData("3.9s", 3900)]
    [InlineData("150ms", 150)]
    [InlineData("150.75ms", 150.75)]
    [InlineData("2min", 120000)]
    [InlineData("1.5min", 90000)]
    [InlineData("1h", 3600000)]
    [InlineData("0.5h", 1800000)]
    [InlineData("npt=4s", 4000)]
    [InlineData("  7s  ", 7000)]
    public void Parse_Timecount_IsInMilliseconds(string text, double expected)
    {
        // Act
        SmilClock clock = new(text);

        // Assert
        clock.Milliseconds.Should().Be(expected);
    }

    [Theory]
    [InlineData("0:00:01", 1000)]
    [InlineData("1:02:03", 3723000)]
    [InlineData("12:59:59.5", 46799500)]
    [InlineData("npt=0:01:00.250", 60250)]
    [InlineData("100:00:00", 360000000)]
    public void Parse_FullClock_IsInMilliseconds(string text, double expected)
    {
        // Act
        SmilClock clock = new(text);

        // Assert
        clock.Milliseconds.Should().Be(expected);
    }

    [Theory]
    [InlineData("02:03", 123000)]
    [InlineData("00:01.5", 1500)]
    [InlineData("59:59.999", 3599999)]
    [InlineData("npt=01:00", 60000)]
    public void Parse_PartialClock_IsInMilliseconds(string text, double expected)
    {
        // Act
        SmilClock clock = new(text);

        // Assert
        clock.Milliseconds.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60:00")]
    [InlineData("60:00")]
    [InlineData("1:2:3")]
    [InlineData("1x")]
    [InlineData("-1s")]
    [InlineData("1.s")]
    [InlineData("1 s")]
    public void Parse_NotAClock_Fails(string text)
    {
        // Act & Assert
        FluentActions.Invoking(() => new SmilClock(text)).Should().Throw<FormatException>();
    }

    [Fact]
    public void NotSet_ClockWithoutAValue_IsZero()
    {
        // Act
        SmilClock clock = new();

        // Assert
        clock.NotSet.Should().BeTrue();
        clock.Milliseconds.Should().Be(0);
        new SmilClock("0s").NotSet.Should().BeFalse();
    }

    [Fact]
    public void AddTime_AndSubtractTime_KeepTheFractionsOfMilliseconds()
    {
        // Arrange
        SmilClock a = new("150.75ms");
        SmilClock b = new("1s");

        // Act & Assert
        a.AddTime(b).Milliseconds.Should().Be(1150.75);
        b.SubtractTime(a).Milliseconds.Should().Be(849.25);
        new SmilClock().AddTime(a).Milliseconds.Should().Be(150.75);
    }

    [Fact]
    public void Compare_UsesATolerance()
    {
        // Arrange
        SmilClock a = new("10s");
        SmilClock b = new("10.5s");

        // Act & Assert
        a.CompareTo(b).Should().Be(-1);
        b.CompareTo(a).Should().Be(1);
        a.CompareTo(a).Should().Be(0);
        a.CompareTo(b, 1000).Should().Be(0);
        a.EqWithinTolerance(b, 1000).Should().BeTrue();
        a.EqWithinTolerance(b, 100).Should().BeFalse();
        a.Equals(new SmilClock("10000ms")).Should().BeTrue();
        (a < b).Should().BeTrue();
        (b > a).Should().BeTrue();
        (a == new SmilClock("10s")).Should().BeTrue();
        (a != b).Should().BeTrue();
        (a <= new SmilClock("10s")).Should().BeTrue();
        (b >= a).Should().BeTrue();
    }

    [Theory]
    [InlineData("0s", "0:00:00")]
    [InlineData("3723s", "1:02:03")]
    [InlineData("3723.5s", "1:02:03.500")]
    [InlineData("1500ms", "0:00:01.500")]
    public void ToString_IsTheFullClock(string text, string expected)
    {
        // Act
        string clock = new SmilClock(text).ToString();

        // Assert
        clock.Should().Be(expected);
    }
}
