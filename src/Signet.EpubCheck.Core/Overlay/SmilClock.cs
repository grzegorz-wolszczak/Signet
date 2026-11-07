using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Overlay;

/// <summary>A time in milliseconds that may be unset (upstream's <c>ClipTime</c>).</summary>
/// <param name="Milliseconds">The time, or <c>null</c> when it is not set.</param>
internal readonly record struct ClipTime(double? Milliseconds)
{
    /// <summary>Whether the time is not set.</summary>
    public bool NotSet => Milliseconds is null;

    /// <summary>The time in milliseconds (0 when not set).</summary>
    public double TimeInMs => Milliseconds ?? 0;

    /// <summary>The sum of the two times.</summary>
    public ClipTime Add(ClipTime other) => new(TimeInMs + other.TimeInMs);

    /// <summary>The difference of the two times.</summary>
    public ClipTime Subtract(ClipTime other) => new(TimeInMs - other.TimeInMs);
}

/// <summary>
/// A SMIL clock value (upstream's <c>SmilClock</c>): the full (<c>1:02:03.5</c>), partial (<c>02:03.5</c>) and timecount
/// (<c>3.5s</c>, <c>10min</c>, <c>150ms</c>, <c>1h</c>, a number of seconds) syntaxes, with an optional <c>npt=</c>.
/// Clocks compare within a tolerance of 0 ms unless a call gives another.
/// </summary>
public sealed partial class SmilClock : IEquatable<SmilClock>, IComparable<SmilClock>
{
    private const long MillisecondsPerSecond = 1000;
    private const long MillisecondsPerMinute = 60 * MillisecondsPerSecond;
    private const long MillisecondsPerHour = 60 * MillisecondsPerMinute;

    private readonly ClipTime _time;

    /// <summary>Reads a clock value.</summary>
    /// <exception cref="FormatException">The text is not a clock value.</exception>
    public SmilClock(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = JavaStrings.Trim(value);
        Match m = TimecountPattern().Match(text);
        if (m.Success)
        {
            decimal number = ParseDecimal(m.Groups[2].Value);
            string unit = m.Groups[4].Value;
            _time = unit switch
            {
                "" or "s" => new ClipTime(Truncate(number * MillisecondsPerSecond)),
                "ms" => new ClipTime((double)number), // the fraction is not truncated
                "min" => new ClipTime((double)(number * MillisecondsPerMinute)),
                _ => new ClipTime(Truncate(number * MillisecondsPerHour)), // "h"
            };
            return;
        }

        m = FullClockPattern().Match(text);
        if (m.Success)
        {
            _time = new ClipTime(
                (ParseLong(m.Groups[2].Value) * MillisecondsPerHour)
                + (ParseLong(m.Groups[3].Value) * MillisecondsPerMinute)
                + (ParseLong(m.Groups[4].Value) * MillisecondsPerSecond)
                + (m.Groups[6].Success ? (double)(ParseDecimal(m.Groups[5].Value) * MillisecondsPerSecond) : 0));
            return;
        }

        m = PartialClockPattern().Match(text);
        if (m.Success)
        {
            _time = new ClipTime(
                (ParseLong(m.Groups[2].Value) * MillisecondsPerMinute)
                + (ParseLong(m.Groups[3].Value) * MillisecondsPerSecond)
                + (m.Groups[5].Success ? (double)(ParseDecimal(m.Groups[4].Value) * MillisecondsPerSecond) : 0));
            return;
        }

        throw new FormatException("Invalid SMIL clock value format: " + text);
    }

    /// <summary>A clock that is not set (0 ms).</summary>
    public SmilClock()
    {
        _time = new ClipTime((double?)null);
    }

    private SmilClock(ClipTime time)
    {
        _time = time;
    }

    /// <summary>Whether the clock is not set.</summary>
    public bool NotSet => _time.NotSet;

    /// <summary>The time in milliseconds.</summary>
    public double Milliseconds => _time.TimeInMs;

    /// <summary>The sum of this clock and <paramref name="other"/>, without losing precision.</summary>
    public SmilClock AddTime(SmilClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SmilClock(_time.Add(other._time));
    }

    /// <summary>The difference of this clock and <paramref name="other"/>.</summary>
    public SmilClock SubtractTime(SmilClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SmilClock(_time.Subtract(other._time));
    }

    /// <summary>Whether the two clocks differ by no more than <paramref name="toleranceMilliseconds"/>.</summary>
    public bool EqWithinTolerance(SmilClock other, long toleranceMilliseconds) => CompareTo(other, toleranceMilliseconds) == 0;

    /// <summary>-1, 0 or 1: this clock is before, within the tolerance of, or after <paramref name="other"/>.</summary>
    public int CompareTo(SmilClock? other, long toleranceMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Math.Abs(other.Milliseconds - Milliseconds) <= toleranceMilliseconds)
        {
            return 0;
        }

        return Milliseconds < other.Milliseconds ? -1 : 1;
    }

    /// <inheritdoc />
    public int CompareTo(SmilClock? other) => CompareTo(other, 0);

    /// <inheritdoc />
    public bool Equals(SmilClock? other) => other is not null && EqWithinTolerance(other, 0);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SmilClock);

    /// <inheritdoc />
    public override int GetHashCode() => Milliseconds.GetHashCode();

    /// <summary>The clock in the full syntax, <c>h:mm:ss</c> with milliseconds when there are some.</summary>
    public override string ToString()
    {
        double msec = Math.Round(Milliseconds, MidpointRounding.AwayFromZero) % MillisecondsPerSecond;
        long total = (long)Math.Round(Milliseconds - msec, MidpointRounding.AwayFromZero) / MillisecondsPerSecond;
        long sec = total % 60;
        total = (total - sec) / 60;
        long min = total % 60;
        long hr = (total - min) / 60;
        string time = string.Create(CultureInfo.InvariantCulture, $"{hr}:{min:00}:{sec:00}");
        return msec > 0 ? time + "." + ((long)msec).ToString("000", CultureInfo.InvariantCulture) : time;
    }

    /// <summary>Whether the two clocks are the same within the default tolerance.</summary>
    public static bool operator ==(SmilClock? left, SmilClock? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether the two clocks differ.</summary>
    public static bool operator !=(SmilClock? left, SmilClock? right) => !(left == right);

    /// <summary>Whether <paramref name="left"/> is before <paramref name="right"/>.</summary>
    public static bool operator <(SmilClock? left, SmilClock? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is after <paramref name="right"/>.</summary>
    public static bool operator >(SmilClock? left, SmilClock? right) => left is not null && left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is not after <paramref name="right"/>.</summary>
    public static bool operator <=(SmilClock? left, SmilClock? right) => !(left > right);

    /// <summary>Whether <paramref name="left"/> is not before <paramref name="right"/>.</summary>
    public static bool operator >=(SmilClock? left, SmilClock? right) => !(left < right);

    // Java's BigDecimal.longValue(): the integral part.
    private static double Truncate(decimal value) => (double)decimal.Truncate(value);

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : throw new FormatException("Invalid number: " + text);

    private static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : throw new FormatException("Invalid number: " + text);

    [GeneratedRegex(@"^(npt=)?([0-9]+([.][0-9]+)?)(h|min|s|ms)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimecountPattern();

    [GeneratedRegex(@"^(npt=)?([0-9]+):([0-5][0-9]):([0-5][0-9])([.]([0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex FullClockPattern();

    [GeneratedRegex(@"^(npt=)?([0-5][0-9]):([0-5][0-9])([.]([0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PartialClockPattern();
}
