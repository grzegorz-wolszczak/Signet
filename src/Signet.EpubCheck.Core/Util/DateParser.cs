using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Util;

/// <summary>A date that does not follow ISO 8601's profile of W3C's note (upstream's <c>InvalidDateException</c>).</summary>
public sealed class InvalidDateException(string message) : Exception(message);

/// <summary>
/// Reads the dates of a package document (upstream's <c>DateParser</c>): the profile of ISO 8601 of
/// <c>http://www.w3.org/TR/NOTE-datetime</c>, <c>YYYY[-MM[-DD[Thh:mm[:ss[.s]]TZD]]]</c>. Upstream fills a
/// <c>java.util.GregorianCalendar</c> and asks it for the time with leniency off; the texts of the errors are those of
/// that calendar (the name of the field that is out of range), which this parser reproduces.
/// </summary>
public static class DateParser
{
    private const string Delimiters = "-T:.+Z";
    private const int MinYear = 1;
    private const int MaxYear = 292_278_994;
    private const int MaxFourDigitYear = 9999;
    private const int MillisecondDigits = 3;
    private const int JulianCutoverYear = 1582;
    private const int MonthsPerYear = 12;
    private const string IllegalArgument = "class java.lang.IllegalArgumentException";

    /// <summary>Parses <paramref name="value"/> and returns its year (the only part its caller uses).</summary>
    /// <exception cref="InvalidDateException">The date is not valid.</exception>
    public static int Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Fields fields = Read(value);
        if (fields.HasOffset)
        {
            // A calendar that was added to has computed its time already, without checking the fields.
            return LenientYear(fields);
        }

        Validate(fields, value);
        return fields.Year;
    }

    private static Fields Read(string value)
    {
        List<string> tokens = Tokenize(value);
        if (tokens.Count == 0)
        {
            throw new InvalidDateException("Empty Date");
        }

        Reader reader = new(tokens);
        Fields fields = new();
        try
        {
            fields.Year = reader.Int();
            if (!reader.CheckValueAndNext("-"))
            {
                return fields;
            }

            fields.Month = reader.Int() - 1;
            if (!reader.CheckValueAndNext("-"))
            {
                return fields;
            }

            fields.Day = reader.Int();
            if (!reader.CheckValueAndNext("T"))
            {
                return fields;
            }

            fields.Hour = reader.Int();
            if (!reader.CheckValueAndNext(":"))
            {
                return fields;
            }

            fields.Minute = reader.Int();
            if (!reader.HasMore)
            {
                return fields;
            }

            string token = reader.Next();
            if (token == ":")
            {
                if (!reader.HasMore)
                {
                    throw new InvalidDateException("No secondes specified");
                }

                fields.Second = reader.Int();
                if (!reader.HasMore)
                {
                    return fields;
                }

                token = reader.Next();
                if (token == ".")
                {
                    fields.Millisecond = ReadFraction(reader.Next());
                    if (!reader.HasMore)
                    {
                        return fields;
                    }

                    token = reader.Next();
                }
            }

            ReadZone(fields, reader, token);
        }
        catch (FormatException e)
        {
            throw new InvalidDateException("[" + e.Message + "] is not an integer");
        }

        return fields;
    }

    private static int ReadFraction(string digits)
    {
        string fraction = digits;
        while (fraction.Length < MillisecondDigits)
        {
            fraction += "0";
        }

        for (int i = MillisecondDigits; i < fraction.Length; i++)
        {
            if (!char.IsDigit(fraction[i]))
            {
                throw new InvalidDateException("Invalid digit in the decimal fraction of a second: " + fraction[i]);
            }
        }

        return ParseInt(fraction[..MillisecondDigits]);
    }

    private static void ReadZone(Fields fields, Reader reader, string token)
    {
        if (token != "Z")
        {
            if (token is not ("+" or "-"))
            {
                throw new InvalidDateException("only Z, + or - allowed");
            }

            if (!reader.HasMore)
            {
                throw new InvalidDateException("Missing hour field");
            }

            int hours = reader.Int();
            if (!reader.CheckValueAndNext(":"))
            {
                throw new InvalidDateException("Missing minute field");
            }

            int minutes = reader.Int();
            fields.HasOffset = true;
            int sign = token == "+" ? -1 : 1;
            fields.OffsetMinutes = sign * ((hours * 60) + minutes);
        }
        else if (reader.HasMore)
        {
            throw new InvalidDateException("Unexpected field at the end of the date field: " + reader.Next());
        }
    }

    // Java's GregorianCalendar with leniency off: every field set must be in its range, and a day that does not
    // exist in the month rolls over into the next one, which shows as a different month.
    private static void Validate(Fields fields, string value)
    {
        (string Name, int Value, int Min, int Max)[] ranges =
        [
            ("YEAR", fields.Year, MinYear, MaxYear),
            ("MONTH", fields.Month, 0, MonthsPerYear - 1),
            ("DAY_OF_MONTH", fields.Day, 1, 31),
            ("HOUR_OF_DAY", fields.Hour, 0, 23),
            ("MINUTE", fields.Minute, 0, 59),
            ("SECOND", fields.Second, 0, 59),
            ("MILLISECOND", fields.Millisecond, 0, 999),
        ];
        foreach ((string name, int number, int min, int max) in ranges)
        {
            if (number < min || number > max)
            {
                throw new InvalidDateException(value + " " + IllegalArgument + " " + name);
            }
        }

        if (fields.Day > DaysInMonth(fields.Year, fields.Month))
        {
            throw new InvalidDateException(value + " " + IllegalArgument + " MONTH");
        }

        if (fields.Year > MaxFourDigitYear)
        {
            throw new InvalidDateException(fields.Year.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static int LenientYear(Fields fields)
    {
        if (fields.Year > MaxFourDigitYear)
        {
            throw new InvalidDateException(fields.Year.ToString(CultureInfo.InvariantCulture));
        }

        DateTime date = new DateTime(Math.Max(fields.Year, MinYear), 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(fields.Month).AddDays(fields.Day - 1).AddHours(fields.Hour).AddMinutes(fields.Minute)
            .AddMinutes(fields.OffsetMinutes);
        return date.Year;
    }

    private static int DaysInMonth(int year, int month)
    {
        // Java's calendar is Julian before 1582 (every fourth year is a leap year).
        bool leap = year < JulianCutoverYear ? year % 4 == 0 : DateTime.IsLeapYear(Math.Min(year, MaxFourDigitYear));
        return month switch
        {
            1 => leap ? 29 : 28,
            3 or 5 or 8 or 10 => 30,
            _ => 31,
        };
    }

    // Java's StringTokenizer with the delimiters returned as tokens.
    private static List<string> Tokenize(string value)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        foreach (char c in value)
        {
            if (Delimiters.Contains(c, StringComparison.Ordinal))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                tokens.Add(c.ToString());
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    // Java's Integer.parseInt: digits only (a sign is a delimiter here), and an int.
    private static int ParseInt(string token)
    {
        if (token.Length == 0 || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            throw new FormatException("For input string: \"" + token + "\"");
        }

        return number;
    }

    private sealed class Reader(List<string> tokens)
    {
        private int _index;

        public bool HasMore => _index < tokens.Count;

        public string Next() => tokens[_index++];

        public int Int() => HasMore ? ParseInt(Next()) : throw new InvalidDateException("Incomplete date.");

        public bool CheckValueAndNext(string delimiter)
        {
            if (!HasMore)
            {
                return false;
            }

            string token = Next();
            if (token != delimiter)
            {
                throw new InvalidDateException("Unexpected: " + token);
            }

            return HasMore ? true : throw new InvalidDateException("Incomplete date.");
        }
    }

    private sealed class Fields
    {
        public int Year { get; set; }

        public int Month { get; set; }

        public int Day { get; set; } = 1;

        public int Hour { get; set; }

        public int Minute { get; set; }

        public int Second { get; set; }

        public int Millisecond { get; set; }

        public bool HasOffset { get; set; }

        public int OffsetMinutes { get; set; }
    }
}
