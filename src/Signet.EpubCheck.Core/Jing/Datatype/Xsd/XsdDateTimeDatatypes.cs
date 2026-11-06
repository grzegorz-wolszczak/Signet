using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

/// <summary>
/// <c>dateTime</c>, <c>time</c>, <c>date</c> and the <c>g…</c> types (Jing's <c>DateTimeDatatype</c>). The lexical space
/// is a regular expression made from a template; the value is an instant in milliseconds, as Jing gets it from a
/// non-lenient proleptic <c>GregorianCalendar</c>, whose field limits are kept: year 1 to 292278994 (to 292269055 before
/// Christ), hours to 23, a time zone from -13:00 to +14:00. A leap second (60) is kept apart.
/// </summary>
internal sealed class DateTimeDatatype : RegexDatatype, IOrderRelation
{
    private const string YearPattern = "-?([1-9][0-9]*)?[0-9]{4}";
    private const string MonthPattern = "[0-9]{2}";
    private const string DayOfMonthPattern = "[0-9]{2}";
    private const string TimePattern = "[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]*)?";
    private const string TimeZonePattern = "(Z|[+\\-][0-9][0-9]:[0-5][0-9])?";

    private const int DefaultYear = 2000; // any leap year will do
    private const int LeapSecond = 60;
    private const int LastSecond = 59;
    private const int LastMillisecond = 999;
    private const int MillisecondDigits = 3;
    private const int MaxYear = 292278994;
    private const int MaxYearBeforeChrist = 292269055;
    private const int MaxHour = 23;
    private const int MaxMinute = 59;
    private const int MonthsPerYear = 12;
    private const long MillisPerSecond = 1000;
    private const long MillisPerMinute = 60 * MillisPerSecond;
    private const long MillisPerHour = 60 * MillisPerMinute;
    private const long MillisPerDay = 24 * MillisPerHour;
    private const int MinZoneOffset = -13 * (int)MillisPerHour;
    private const int MaxZoneOffset = 14 * (int)MillisPerHour;
    private const long TimeZoneMax = 14 * MillisPerHour;

    private readonly string _template;

    private DateTimeDatatype(JingLocalizer localizer, XsdRegex regex, string template)
        : base(localizer, regex, MakeLexicalSpaceKey(template))
    {
        _template = template;
    }

    public override IOrderRelation? OrderRelation => this;

    // Jing's DateTimeDatatype inherits a length from TokenDatatype and a length facet then fails on the value (it is
    // not a string): the port gives it no length, so the facet is refused when the schema is read.
    public override IMeasure? Measure => null;

    /// <summary>
    /// A datatype for a template: <c>Y</c> a year with an optional minus, <c>M</c> a two-digit month, <c>D</c> a two-digit
    /// day, <c>t</c> a time; other characters stand for themselves; an optional time zone follows.
    /// </summary>
    public static DateTimeDatatype Create(JingLocalizer localizer, JingLocalizer regexLocalizer, string template) =>
        new(localizer, XsdRegex.Compile(MakePattern(template), regexLocalizer), template);

    public override object GetValue(string str, IValidationContext? vc)
    {
        bool negative = false;
        int year = DefaultYear;
        int month = 1;
        int day = 1;
        int hours = 0;
        int minutes = 0;
        int seconds = 0;
        int milliseconds = 0;
        int pos = 0;
        int len = str.Length;
        foreach (char templateChar in _template)
        {
            switch (templateChar)
            {
                case 'Y':
                    negative = str[pos] == '-';
                    int yearStartIndex = negative ? pos + 1 : pos;
                    pos = SkipDigits(str, yearStartIndex);
                    if (!int.TryParse(str.AsSpan(yearStartIndex, pos - yearStartIndex), NumberStyles.None, CultureInfo.InvariantCulture, out year))
                    {
                        throw CreateLexicallyInvalidException();
                    }

                    break;
                case 'M':
                    month = Parse2Digits(str, pos);
                    pos += 2;
                    break;
                case 'D':
                    day = Parse2Digits(str, pos);
                    pos += 2;
                    break;
                case 't':
                    hours = Parse2Digits(str, pos);
                    pos += 3;
                    minutes = Parse2Digits(str, pos);
                    pos += 3;
                    seconds = Parse2Digits(str, pos);
                    pos += 2;
                    if (pos < len && str[pos] == '.')
                    {
                        int end = SkipDigits(str, ++pos);
                        for (int j = 0; j < MillisecondDigits; j++)
                        {
                            milliseconds *= 10;
                            if (pos < end)
                            {
                                milliseconds += str[pos++] - '0';
                            }
                        }

                        pos = end;
                    }

                    break;
                default:
                    pos++;
                    break;
            }
        }

        bool hasTimeZone = pos < len;
        int tzOffset = hasTimeZone && str[pos] != 'Z' ? ParseTimeZone(str, pos) : 0;
        int leapMilliseconds = 0;
        if (seconds == LeapSecond)
        {
            leapMilliseconds = milliseconds + 1;
            milliseconds = LastMillisecond;
            seconds = LastSecond;
        }

        long? millis = CreateDate(tzOffset, negative, year, month, day, hours, minutes, seconds, milliseconds);
        return millis is { } m ? new DateTimeValue(m, leapMilliseconds, hasTimeZone) : throw CreateLexicallyInvalidException();
    }

    public bool IsLessThan(object obj1, object obj2)
    {
        DateTimeValue dt1 = (DateTimeValue)obj1;
        DateTimeValue dt2 = (DateTimeValue)obj2;
        long t1 = dt1.Millis;
        long t2 = dt2.Millis;
        if (dt1.HasTimeZone == dt2.HasTimeZone)
        {
            return IsLessThan(t1, dt1.LeapMilliseconds, t2, dt2.LeapMilliseconds);
        }

        return !dt2.HasTimeZone
            ? IsLessThan(t1, dt1.LeapMilliseconds, t2 - TimeZoneMax, dt2.LeapMilliseconds)
            : IsLessThan(t1 + TimeZoneMax, dt1.LeapMilliseconds, t2, dt2.LeapMilliseconds);
    }

    private static bool IsLessThan(long t1, int leapMillis1, long t2, int leapMillis2) =>
        t1 < t2 || (t1 == t2 && leapMillis1 < leapMillis2);

    private static string MakeLexicalSpaceKey(string template)
    {
        string key = string.Empty;
        if (template.Contains('Y', StringComparison.Ordinal))
        {
            key += "_y";
        }

        if (template.Contains('M', StringComparison.Ordinal))
        {
            key += "_m";
        }

        if (template.Contains('D', StringComparison.Ordinal))
        {
            key += "_d";
        }

        if (key.Length > 0)
        {
            key = "date" + key;
        }

        if (template.Contains('t', StringComparison.Ordinal))
        {
            key = key.Length > 0 ? key + "_time" : "time";
        }

        return key;
    }

    private static string MakePattern(string template)
    {
        StringBuilder pattern = new();
        foreach (char c in template)
        {
            pattern.Append(c switch
            {
                'Y' => YearPattern,
                'M' => MonthPattern,
                'D' => DayOfMonthPattern,
                't' => TimePattern,
                _ => c.ToString(),
            });
        }

        return pattern.Append(TimeZonePattern).ToString();
    }

    // The instant a non-lenient GregorianCalendar (proleptic, gregorianChange at the earliest date) gives, or null when
    // it refuses the fields.
    private static long? CreateDate(int tzOffset, bool negative, int year, int month, int day, int hours, int minutes, int seconds, int milliseconds)
    {
        if (year < 1 || year > (negative ? MaxYearBeforeChrist : MaxYear) || month < 1 || month > MonthsPerYear
            || hours > MaxHour || minutes > MaxMinute || seconds > LastSecond || tzOffset < MinZoneOffset || tzOffset > MaxZoneOffset)
        {
            return null;
        }

        long astronomicalYear = negative ? 1 - year : year;
        if (day < 1 || day > DaysInMonth(astronomicalYear, month))
        {
            return null;
        }

        return DaysFromCivil(astronomicalYear, month, day) * MillisPerDay
            + (hours * MillisPerHour) + (minutes * MillisPerMinute) + (seconds * MillisPerSecond) + milliseconds - tzOffset;
    }

    private static int DaysInMonth(long year, int month) => month switch
    {
        4 or 6 or 9 or 11 => 30,
        2 => IsLeapYear(year) ? 29 : 28,
        _ => 31,
    };

    private static bool IsLeapYear(long year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

    // Days from 1970-01-01 in the proleptic Gregorian calendar (H. Hinnant's days_from_civil).
    private static long DaysFromCivil(long y, int m, int d)
    {
        const int DaysPerEra = 146097;
        const int YearsPerEra = 400;
        const int EpochShift = 719468;
        y -= m <= 2 ? 1 : 0;
        long era = (y >= 0 ? y : y - (YearsPerEra - 1)) / YearsPerEra;
        long yoe = y - (era * YearsPerEra);
        long doy = ((153 * (m + (m > 2 ? -3 : 9))) + 2) / 5 + d - 1;
        long doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
        return (era * DaysPerEra) + doe - EpochShift;
    }

    private static int ParseTimeZone(string str, int i)
    {
        int sign = str[i] == '-' ? -1 : 1;
        int tzHours = int.Parse(str.AsSpan(i + 1, 2), CultureInfo.InvariantCulture);
        int tzMinutes = int.Parse(str.AsSpan(i + 4), CultureInfo.InvariantCulture);
        return (int)(((tzHours * 60) + tzMinutes) * MillisPerMinute) * sign;
    }

    private static int Parse2Digits(string str, int i) => ((str[i] - '0') * 10) + (str[i + 1] - '0');

    private static int SkipDigits(string str, int i)
    {
        while (i < str.Length && char.IsAsciiDigit(str[i]))
        {
            i++;
        }

        return i;
    }

    private sealed record DateTimeValue(long Millis, int LeapMilliseconds, bool HasTimeZone);
}

/// <summary><c>duration</c> (Jing's <c>DurationDatatype</c>, "partially implemented" in Jing's words).</summary>
internal sealed class DurationDatatype : RegexDatatype, IOrderRelation
{
    private const string Pattern = "-?P([0-9]+Y)?([0-9]+M)?([0-9]+D)?(T([0-9]+H)?([0-9]+M)?(([0-9]+(\\.[0-9]*)?|\\.[0-9]+)S)?)?";
    private const int MonthsPerYear = 12;
    private const int HoursPerDay = 24;
    private const int MinutesPerHour = 60;
    private const int SecondsPerMinute = 60;
    private const int SecondsPerDay = HoursPerDay * MinutesPerHour * SecondsPerMinute;
    private const int GregorianCycleMonths = 400 * MonthsPerYear;
    private const int GregorianCycleDays = (365 * 400) + 97;

    // The four months of reference XML Schema compares durations from (1696-09, 1697-02, 1903-03, 1903-07).
    private static readonly (int Year, int Month)[] s_referenceMonths = [(1696, 9), (1697, 2), (1903, 3), (1903, 7)];

    private DurationDatatype(JingLocalizer localizer, XsdRegex regex)
        : base(localizer, regex, "duration")
    {
    }

    public override IOrderRelation? OrderRelation => this;

    // As for the dates: no length.
    public override IMeasure? Measure => null;

    public static DurationDatatype Create(JingLocalizer localizer, JingLocalizer regexLocalizer) =>
        new(localizer, XsdRegex.Compile(Pattern, regexLocalizer));

    // There must be at least one component, and no "T" without a time component.
    public override bool LexicallyAllows(string str) => base.LexicallyAllows(str) && str[^1] is not ('P' or 'T');

    public override object GetValue(string str, IValidationContext? vc)
    {
        int t = str.IndexOf('T', StringComparison.Ordinal);
        if (t < 0)
        {
            t = str.Length;
        }

        string date = str[..t];
        string time = str[t..];
        return new DurationValue(str[0] == '-',
            IntegerField(date, 'Y'), IntegerField(date, 'M'), IntegerField(date, 'D'),
            IntegerField(time, 'H'), IntegerField(time, 'M'), DecimalField(time, 'S'));
    }

    public bool IsLessThan(object obj1, object obj2)
    {
        DurationValue d1 = (DurationValue)obj1;
        DurationValue d2 = (DurationValue)obj2;
        BigInteger months1 = d1.TotalMonths;
        BigInteger months2 = d2.TotalMonths;
        JavaBigDecimal seconds1 = d1.TotalSeconds;
        JavaBigDecimal seconds2 = d2.TotalSeconds;
        switch (months1.CompareTo(months2))
        {
            case < 0:
                if (seconds1 <= seconds2)
                {
                    return true;
                }

                break;
            case 0:
                return seconds1 < seconds2;
            default:
                if (seconds1 >= seconds2)
                {
                    return false;
                }

                break;
        }

        foreach ((int year, int month) in s_referenceMonths)
        {
            JavaBigDecimal total1 = DaysPlusSeconds(ComputeDays(months1, year, month), seconds1);
            JavaBigDecimal total2 = DaysPlusSeconds(ComputeDays(months2, year, month), seconds2);
            if (total1 >= total2)
            {
                return false;
            }
        }

        return true;
    }

    // The days a number of months spans from a year and month of reference.
    private static BigInteger ComputeDays(BigInteger months, int refYear, int refMonth)
    {
        if (months.IsZero)
        {
            return BigInteger.Zero;
        }

        if (months.Sign < 0)
        {
            return -ComputeDays(-months, refYear, refMonth);
        }

        BigInteger cycles = BigInteger.DivRem(months, GregorianCycleMonths, out BigInteger remainder);
        int monthIndex = refMonth - 1;
        int total = 0;
        for (int rem = (int)remainder; rem > 0; rem--)
        {
            total += DaysInMonth(refYear, monthIndex);
            if (++monthIndex == MonthsPerYear)
            {
                monthIndex = 0;
                refYear++;
            }
        }

        return (cycles * GregorianCycleDays) + total;
    }

    // monthIndex counts from 0, as Java's Calendar does.
    private static int DaysInMonth(int year, int monthIndex) => monthIndex switch
    {
        3 or 5 or 8 or 10 => 30,
        1 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
        _ => 31,
    };

    private static JavaBigDecimal DaysPlusSeconds(BigInteger days, JavaBigDecimal seconds) =>
        seconds + JavaBigDecimal.FromInteger(days * SecondsPerDay);

    private static BigInteger IntegerField(string str, char code)
    {
        int end = str.IndexOf(code, StringComparison.Ordinal);
        if (end < 0)
        {
            return BigInteger.Zero;
        }

        int start = end;
        while (char.IsAsciiDigit(str[start - 1]))
        {
            --start;
        }

        return BigInteger.Parse(str.AsSpan(start, end - start), CultureInfo.InvariantCulture);
    }

    private static JavaBigDecimal DecimalField(string str, char code)
    {
        int end = str.IndexOf(code, StringComparison.Ordinal);
        if (end < 0)
        {
            return JavaBigDecimal.FromInteger(BigInteger.Zero);
        }

        int start = end;
        while (!char.IsLetter(str[start - 1]))
        {
            --start;
        }

        return JavaBigDecimal.Parse(str[start..end]);
    }

    private sealed class DurationValue
    {
        public DurationValue(bool negative, BigInteger years, BigInteger months, BigInteger days, BigInteger hours, BigInteger minutes, JavaBigDecimal seconds)
        {
            int sign = negative ? -1 : 1;
            Years = years * sign;
            Months = months * sign;
            Days = days * sign;
            Hours = hours * sign;
            Minutes = minutes * sign;
            Seconds = negative ? -seconds : seconds;
        }

        public BigInteger Years { get; }

        public BigInteger Months { get; }

        public BigInteger Days { get; }

        public BigInteger Hours { get; }

        public BigInteger Minutes { get; }

        public JavaBigDecimal Seconds { get; }

        public BigInteger TotalMonths => (Years * MonthsPerYear) + Months;

        public JavaBigDecimal TotalSeconds =>
            Seconds + JavaBigDecimal.FromInteger((((Days * HoursPerDay) + Hours) * MinutesPerHour + Minutes) * SecondsPerMinute);

        public override bool Equals(object? obj) =>
            obj is DurationValue other && Years == other.Years && Months == other.Months && Days == other.Days
            && Hours == other.Hours && Minutes == other.Minutes && Seconds.CompareTo(other.Seconds) == 0;

        // Jing hashes the seconds with their scale (1S and 1.0S, which are equal, hash differently): the port leaves them out.
        public override int GetHashCode() => HashCode.Combine(Years, Months, Days, Hours, Minutes);
    }
}
