using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="Spcre"/> — the PCRE.NET-based regex wrapper.
/// </summary>
public sealed class SpcreTests
{
    [Fact]
    public void ValidPattern_IsValid_AndExposesPattern()
    {
        // Arrange
        var re = new Spcre(@"(\d+)");

        // Act & Assert
        re.IsValid.Should().BeTrue();

        // Act & Assert
        re.Error.Should().BeEmpty();

        // Act & Assert
        re.ErrorPosition.Should().Be(-1);

        // Act & Assert
        re.Pattern.Should().Be(@"(\d+)");
    }

    [Fact]
    public void InvalidPattern_ReportsErrorAndOffset()
    {
        // Arrange
        var re = new Spcre("foo(bar");

        // Act & Assert
        re.IsValid.Should().BeFalse();

        // Act & Assert
        re.Error.Should().Be("missing closing parenthesis");

        // Act & Assert
        re.ErrorPosition.Should().Be(7);

        // Act & Assert
        re.CaptureSubpatternCount.Should().Be(0);
    }

    [Fact]
    public void CaptureSubpatternCount_IsGroupCountPlusOne()
    {
        // Act & Assert
        new Spcre(@"(a)(b)(c)").CaptureSubpatternCount.Should().Be(4);

        // Act & Assert
        new Spcre(@"abc").CaptureSubpatternCount.Should().Be(1);
    }

    [Fact]
    public void GetCaptureStringNumber_ResolvesNamedGroups()
    {
        // Arrange
        var re = new Spcre(@"(?<year>\d{4})-(?<month>\d{2})");

        // Act & Assert
        re.GetCaptureStringNumber("year").Should().Be(1);

        // Act & Assert
        re.GetCaptureStringNumber("month").Should().Be(2);

        // Act & Assert
        re.GetCaptureStringNumber("missing").Should().Be(-1);
    }

    [Fact]
    public void GetFirstMatchInfo_ReturnsMatchAndCaptureOffsets()
    {
        // Arrange
        var re = new Spcre(@"(\d{4})-(\d{2})");

        // Act
        Spcre.MatchInfo info = re.GetFirstMatchInfo("x 2026-09 y");

        // Assert
        info.Success.Should().BeTrue();
        info.Offset.Should().Be(new SpcreCapture(2, 9));
        info.CaptureGroupsOffsets.Should().Equal(
            new SpcreCapture(0, 7),  // whole match, normalized
            new SpcreCapture(0, 4),  // 2026
            new SpcreCapture(5, 7)); // 09
    }

    [Fact]
    public void GetFirstMatchInfo_NoMatch_ReturnsNone()
    {
        // Act
        Spcre.MatchInfo info = new Spcre(@"\d+").GetFirstMatchInfo("abc");

        // Assert
        info.Success.Should().BeFalse();
        info.Offset.Should().Be(new SpcreCapture(-1, -1));
    }

    [Fact]
    public void GetFirstMatchInfo_WithOffset_SkipsEarlierMatches()
    {
        // Arrange
        var re = new Spcre(@"\d");

        // Act & Assert
        re.GetFirstMatchInfo("1 2 3", 2).Offset.Should().Be(new SpcreCapture(2, 3));

        // Act & Assert
        re.GetFirstMatchInfo("1 2 3", 100).Success.Should().BeFalse();
    }

    [Fact]
    public void GetEveryMatchInfo_ReturnsAllNonOverlappingMatches()
    {
        // Arrange
        var re = new Spcre(@"\d+");

        // Act
        List<SpcreCapture> offsets = re.GetEveryMatchInfo("a1 bb22 c333")
            .Select(m => m.Offset)
            .ToList();

        // Assert
        offsets.Should().Equal(
            new SpcreCapture(1, 2),
            new SpcreCapture(5, 7),
            new SpcreCapture(9, 12));
    }

    [Fact]
    public void GetEveryMatchInfo_NeverReturnsEmptyMatches()
    {
        // Arrange
        // PCRE2_NOTEMPTY — "a*" does not return empty matches between characters.
        var re = new Spcre(@"a*");

        // Act & Assert
        re.GetEveryMatchInfo("xaaxx").Select(m => m.Offset)
            .Should().Equal(new SpcreCapture(1, 3));
    }

    [Fact]
    public void GetLastMatchInfo_ReturnsFinalMatch()
    {
        // Arrange
        var re = new Spcre(@"\d+");

        // Act & Assert
        re.GetLastMatchInfo("a1 b22 c333").Offset.Should().Be(new SpcreCapture(8, 11));

        // Act & Assert
        re.GetLastMatchInfo("none").Success.Should().BeFalse();
    }

    [Fact]
    public void Supports_KeepOperator()
    {
        // Act
        Spcre.MatchInfo info = new Spcre(@"foo\Kbar").GetFirstMatchInfo("foobar");

        // Assert
        info.Offset.Should().Be(new SpcreCapture(3, 6));
        info.CaptureGroupsOffsets[0].Should().Be(new SpcreCapture(0, 3));
    }

    [Fact]
    public void Supports_BoundedLookbehind_RejectsUnbounded()
    {
        // Act & Assert
        new Spcre(@"(?<=\$)\d{2}").GetFirstMatchInfo("total $34").Offset
            .Should().Be(new SpcreCapture(7, 9));

        // Arrange
        var bad = new Spcre(@"(?<=\$\d*)\d{2}");

        // Assert
        bad.IsValid.Should().BeFalse();
        bad.Error.Should().Contain("lookbehind");
    }

    [Fact]
    public void Multiline_IsOnByDefault()
    {
        // Arrange
        // ^ and $ match at line boundaries (PCRE2_MULTILINE).
        var re = new Spcre(@"^b$");

        // Act & Assert
        re.GetFirstMatchInfo("a\nb\nc").Offset.Should().Be(new SpcreCapture(2, 3));
    }

    [Fact]
    public void DotAll_IsOffByDefault_ButHonorsInlineFlag()
    {
        // Act & Assert
        new Spcre(@"a.b").GetFirstMatchInfo("a\nb").Success.Should().BeFalse();

        // Act & Assert
        new Spcre(@"(?s)a.b").GetFirstMatchInfo("a\nb").Success.Should().BeTrue();
    }

    [Fact]
    public void CaseInsensitive_ViaInlineFlag()
    {
        // Act & Assert
        new Spcre(@"(?i)abc").GetFirstMatchInfo("ZABCZ").Offset.Should().Be(new SpcreCapture(1, 4));
    }

    [Fact]
    public void UnsetOptionalGroup_HasSentinelOffsets()
    {
        // Act
        Spcre.MatchInfo info = new Spcre(@"(a)?(b)").GetFirstMatchInfo("b");

        // Assert
        info.CaptureGroupsOffsets[1].Should().Be(new SpcreCapture(-1, -1));
        info.CaptureGroupsOffsets[2].Should().Be(new SpcreCapture(0, 1));
    }
}
