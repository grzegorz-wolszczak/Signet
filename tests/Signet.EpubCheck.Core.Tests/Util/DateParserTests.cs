using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

/// <summary>The dates of package documents: W3C's profile of ISO 8601, with the error texts of upstream's calendar.</summary>
public sealed class DateParserTests
{
    [Theory]
    [InlineData("2000", 2000)]
    [InlineData("2000-02", 2000)]
    [InlineData("2000-02-29", 2000)]
    [InlineData("2000-02-29T12:30", 2000)]
    [InlineData("2000-02-29T12:30:45", 2000)]
    [InlineData("2000-02-29T12:30:45.123", 2000)]
    [InlineData("2000-02-29T12:30:45.5Z", 2000)]
    [InlineData("2000-12-31T23:30:00-01:00", 2001)]
    [InlineData("2000-01-01T00:30:00+01:00", 1999)]
    [InlineData("1500-02-29", 1500)]
    public void Parse_ValidDate_GivesTheYear(string value, int year)
    {
        // Act & Assert
        DateParser.Parse(value).Should().Be(year);
    }

    [Fact]
    public void Parse_DateEndingWithTheDot_IsInvalidNotAnException()
    {
        // Act & Assert
        FluentActions.Invoking(() => DateParser.Parse("2017-06-14T00:00:01."))
            .Should().Throw<InvalidDateException>().WithMessage("No fraction of a second specified.");
    }

    // Upstream's DateParserTest (JUnit, added in 5.4.1): the dates that are valid and the ones that are not.
    [Theory]
    [InlineData("2011")]
    [InlineData("2011-02")]
    [InlineData("2011-02-12")]
    [InlineData("2011-03-01T13")]
    [InlineData("2011-02-01T13:00")]
    [InlineData("2011-02-01T13:00:00")]
    [InlineData("2011-02-01T13:00:00Z")]
    [InlineData("2011-02-01T13:00:00+01:00")]
    [InlineData("2011-02-01T13:00:00-03:00")]
    public void Parse_UpstreamsValidDates_AreAccepted(string value)
    {
        // Act & Assert
        FluentActions.Invoking(() => DateParser.Parse(value)).Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2011-")]
    [InlineData("2011-02-")]
    [InlineData("2011-02-01T")]
    [InlineData("2011-02-01T13:")]
    [InlineData("2011-02-01T13:00:")]
    [InlineData("2011-02-01T13:00:00T")]
    [InlineData("2011-02-01T13:00:00.")]
    [InlineData("2011-02-01T13:00:00+01")]
    [InlineData("2011-02-01T13:00:00+01:")]
    [InlineData("2011-02-01T13:00:00-03")]
    [InlineData("2011-02-01T13:00:00-03:")]
    [InlineData("2011-02-01T13:00:00-03:AA")]
    [InlineData("20a1")]
    [InlineData(" 2")]
    [InlineData("2011-02-29")]
    [InlineData("2011-02-01T13:00:00.123aqb")]
    [InlineData("1994-11-05T13:15:30Zab")]
    public void Parse_UpstreamsInvalidDates_AreRejected(string value)
    {
        // Act & Assert
        FluentActions.Invoking(() => DateParser.Parse(value)).Should().Throw<InvalidDateException>();
    }

    [Theory]
    [InlineData("2001-02-29", "2001-02-29 class java.lang.IllegalArgumentException MONTH")]
    [InlineData("2000-13-01", "2000-13-01 class java.lang.IllegalArgumentException MONTH")]
    [InlineData("2000-00-01", "2000-00-01 class java.lang.IllegalArgumentException MONTH")]
    [InlineData("2000-01-32", "2000-01-32 class java.lang.IllegalArgumentException DAY_OF_MONTH")]
    [InlineData("2000-01-01T24:00", "2000-01-01T24:00 class java.lang.IllegalArgumentException HOUR_OF_DAY")]
    [InlineData("2000-01-01T12:60", "2000-01-01T12:60 class java.lang.IllegalArgumentException MINUTE")]
    [InlineData("2000-01-01T12:00:60", "2000-01-01T12:00:60 class java.lang.IllegalArgumentException SECOND")]
    [InlineData("0000-01-01", "0000-01-01 class java.lang.IllegalArgumentException YEAR")]
    [InlineData("10000-01-01", "10000")]
    [InlineData("", "Empty Date")]
    [InlineData("2000-", "Incomplete date.")]
    [InlineData("2000-01-01T", "Incomplete date.")]
    [InlineData("2000/01/01", "[For input string: \"2000/01/01\"] is not an integer")]
    [InlineData("abcd", "[For input string: \"abcd\"] is not an integer")]
    [InlineData("2000-01X", "[For input string: \"01X\"] is not an integer")]
    [InlineData("2000-01-01T12:00:00Zjunk", "Unexpected field at the end of the date field: junk")]
    [InlineData("2000-01-01T12:00:00Z-", "Unexpected field at the end of the date field: -")]
    [InlineData("2000-01-01T12:00:00:", "only Z, + or - allowed")]
    [InlineData("2000-01-01T12:00:00+", "Missing hour field")]
    [InlineData("2000-01-01T12:00:00+01", "Missing minute field")]
    [InlineData("2000-01-01T12:00:00.1234x", "Invalid digit in the decimal fraction of a second: x")]
    public void Parse_InvalidDate_ThrowsWithUpstreamsText(string value, string message)
    {
        // Act & Assert
        FluentActions.Invoking(() => DateParser.Parse(value)).Should().Throw<InvalidDateException>().WithMessage(message);
    }
}
