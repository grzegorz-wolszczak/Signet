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
        var re = new Spcre(@"(\d+)");

        re.IsValid.Should().BeTrue();
        re.Error.Should().BeEmpty();
        re.ErrorPosition.Should().Be(-1);
        re.Pattern.Should().Be(@"(\d+)");
    }

    [Fact]
    public void InvalidPattern_ReportsErrorAndOffset()
    {
        var re = new Spcre("foo(bar");

        re.IsValid.Should().BeFalse();
        re.Error.Should().Be("missing closing parenthesis");
        re.ErrorPosition.Should().Be(7);
        re.CaptureSubpatternCount.Should().Be(0);
    }

    [Fact]
    public void CaptureSubpatternCount_IsGroupCountPlusOne()
    {
        new Spcre(@"(a)(b)(c)").CaptureSubpatternCount.Should().Be(4);
        new Spcre(@"abc").CaptureSubpatternCount.Should().Be(1);
    }

    [Fact]
    public void GetCaptureStringNumber_ResolvesNamedGroups()
    {
        var re = new Spcre(@"(?<year>\d{4})-(?<month>\d{2})");

        re.GetCaptureStringNumber("year").Should().Be(1);
        re.GetCaptureStringNumber("month").Should().Be(2);
        re.GetCaptureStringNumber("missing").Should().Be(-1);
    }

    [Fact]
    public void GetFirstMatchInfo_ReturnsMatchAndCaptureOffsets()
    {
        var re = new Spcre(@"(\d{4})-(\d{2})");

        Spcre.MatchInfo info = re.GetFirstMatchInfo("x 2026-09 y");

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
        Spcre.MatchInfo info = new Spcre(@"\d+").GetFirstMatchInfo("abc");

        info.Success.Should().BeFalse();
        info.Offset.Should().Be(new SpcreCapture(-1, -1));
    }

    [Fact]
    public void GetFirstMatchInfo_WithOffset_SkipsEarlierMatches()
    {
        var re = new Spcre(@"\d");

        re.GetFirstMatchInfo("1 2 3", 2).Offset.Should().Be(new SpcreCapture(2, 3));
        re.GetFirstMatchInfo("1 2 3", 100).Success.Should().BeFalse();
    }

    [Fact]
    public void GetEveryMatchInfo_ReturnsAllNonOverlappingMatches()
    {
        var re = new Spcre(@"\d+");

        List<SpcreCapture> offsets = re.GetEveryMatchInfo("a1 bb22 c333")
            .Select(m => m.Offset)
            .ToList();

        offsets.Should().Equal(
            new SpcreCapture(1, 2),
            new SpcreCapture(5, 7),
            new SpcreCapture(9, 12));
    }

    [Fact]
    public void GetEveryMatchInfo_NeverReturnsEmptyMatches()
    {
        // PCRE2_NOTEMPTY — "a*" does not return empty matches between characters.
        var re = new Spcre(@"a*");

        re.GetEveryMatchInfo("xaaxx").Select(m => m.Offset)
            .Should().Equal(new SpcreCapture(1, 3));
    }

    [Fact]
    public void GetLastMatchInfo_ReturnsFinalMatch()
    {
        var re = new Spcre(@"\d+");

        re.GetLastMatchInfo("a1 b22 c333").Offset.Should().Be(new SpcreCapture(8, 11));
        re.GetLastMatchInfo("none").Success.Should().BeFalse();
    }

    [Fact]
    public void Supports_KeepOperator()
    {
        Spcre.MatchInfo info = new Spcre(@"foo\Kbar").GetFirstMatchInfo("foobar");

        info.Offset.Should().Be(new SpcreCapture(3, 6));
        info.CaptureGroupsOffsets[0].Should().Be(new SpcreCapture(0, 3));
    }

    [Fact]
    public void Supports_BoundedLookbehind_RejectsUnbounded()
    {
        new Spcre(@"(?<=\$)\d{2}").GetFirstMatchInfo("total $34").Offset
            .Should().Be(new SpcreCapture(7, 9));

        var bad = new Spcre(@"(?<=\$\d*)\d{2}");
        bad.IsValid.Should().BeFalse();
        bad.Error.Should().Contain("lookbehind");
    }

    [Fact]
    public void Multiline_IsOnByDefault()
    {
        // ^ and $ match at line boundaries (PCRE2_MULTILINE).
        var re = new Spcre(@"^b$");

        re.GetFirstMatchInfo("a\nb\nc").Offset.Should().Be(new SpcreCapture(2, 3));
    }

    [Fact]
    public void DotAll_IsOffByDefault_ButHonorsInlineFlag()
    {
        new Spcre(@"a.b").GetFirstMatchInfo("a\nb").Success.Should().BeFalse();
        new Spcre(@"(?s)a.b").GetFirstMatchInfo("a\nb").Success.Should().BeTrue();
    }

    [Fact]
    public void CaseInsensitive_ViaInlineFlag()
    {
        new Spcre(@"(?i)abc").GetFirstMatchInfo("ZABCZ").Offset.Should().Be(new SpcreCapture(1, 4));
    }

    [Fact]
    public void UnsetOptionalGroup_HasSentinelOffsets()
    {
        Spcre.MatchInfo info = new Spcre(@"(a)?(b)").GetFirstMatchInfo("b");

        info.CaptureGroupsOffsets[1].Should().Be(new SpcreCapture(-1, -1));
        info.CaptureGroupsOffsets[2].Should().Be(new SpcreCapture(0, 1));
    }
}
