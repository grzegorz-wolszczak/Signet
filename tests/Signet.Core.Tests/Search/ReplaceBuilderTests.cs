using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="ReplaceTextBuilder"/> — building replacement text from a PCRE match.
/// </summary>
public sealed class ReplaceBuilderTests
{
    /// <summary>
    /// Finds the first match of <paramref name="pattern"/> in <paramref name="text"/>
    /// and builds its replacement text from <paramref name="replacement"/>.
    /// </summary>
    private static string Replace(string text, string pattern, string replacement)
    {
        var re = new Spcre(pattern);
        Spcre.MatchInfo info = re.GetFirstMatchInfo(text);
        info.Success.Should().BeTrue("the pattern should match");

        string matched = text.Substring(info.Offset.Start, info.Offset.Length);
        re.ReplaceText(matched, info.CaptureGroupsOffsets, replacement, out string result)
            .Should().BeTrue();
        return result;
    }

    [Fact]
    public void PlainText_WithoutBackslash_IsReturnedVerbatim()
    {
        Replace("abc", "b", "XYZ").Should().Be("XYZ");
    }

    [Fact]
    public void NumberedBackreferences()
    {
        Replace("John Smith", @"(\w+)\s+(\w+)", @"\2, \1").Should().Be("Smith, John");
    }

    [Fact]
    public void WholeMatchBackreferenceZero()
    {
        Replace("abc", @"b", @"[\0]").Should().Be("[b]");
    }

    [Fact]
    public void NamedBackreference_BraceAndAngle()
    {
        Replace("2026-09", @"(?<y>\d{4})-(?<m>\d{2})", @"\g<m>/\g{y}").Should().Be("09/2026");
    }

    [Fact]
    public void NumberedBackreference_InsideBraces()
    {
        Replace("ab", @"(a)(b)", @"\g{2}\g{1}").Should().Be("ba");
    }

    [Fact]
    public void CaseFolding_UpperUntilE()
    {
        Replace("hello world", @"(\w+) (\w+)", @"\U\1\E \2").Should().Be("HELLO world");
    }

    [Fact]
    public void CaseFolding_LowerUntilE()
    {
        Replace("HELLO", @"(\w+)", @"\L\1\E").Should().Be("hello");
    }

    [Fact]
    public void CaseFolding_SingleCharUpperAndLower()
    {
        Replace("john smith", @"(\w)(\w+) (\w)(\w+)", @"\u\1\2 \u\3\4").Should().Be("John Smith");
        Replace("HELLO", @"(H)(ELLO)", @"\l\1\2").Should().Be("hELLO");
    }

    [Fact]
    public void CaseFolding_InnerChangeIgnoredUntilE()
    {
        // \L inside \U is ignored until \E.
        Replace("abc", @"(abc)", @"\U\L\1\E").Should().Be("ABC");
    }

    [Fact]
    public void EscapedMetacharacters()
    {
        Replace("x", "x", @"a\tb\nc").Should().Be("a\tb\nc");
        Replace("x", "x", @"\\n").Should().Be(@"\n");
    }

    [Fact]
    public void HexEscape_TwoDigits()
    {
        Replace("x", "x", @"\x41\x42").Should().Be("AB");
    }

    [Fact]
    public void HexEscape_BracedBmpAndAstral()
    {
        Replace("x", "x", @"\x{41}").Should().Be("A");
        Replace("x", "x", @"\x{20AC}").Should().Be("\u20AC");          // EURO SIGN
        // Outside the BMP 6 digits are required (a length of 5 is invalid).
        Replace("x", "x", @"\x{01F600}").Should().Be("\U0001F600");    // GRINNING FACE
    }

    [Fact]
    public void HexEscape_FiveDigitBrace_IsInvalid()
    {
        Replace("x", "x", @"\x{1F600}").Should().Be(@"\x{1F600}");
    }

    [Fact]
    public void InvalidBackreference_IsEmittedLiterally()
    {
        Replace("ab", @"(a)(b)", @"\9").Should().Be(@"\9");
    }

    [Fact]
    public void UnknownEscape_IsEmittedLiterally()
    {
        Replace("x", "x", @"\q\z").Should().Be(@"\q\z");
    }

    [Fact]
    public void DanglingBackslash_IsEmittedLiterally()
    {
        Replace("x", "x", @"end\").Should().Be(@"end\");
    }

    [Fact]
    public void MalformedBracedHex_IsEmittedLiterally()
    {
        Replace("x", "x", @"\x{2}").Should().Be(@"\x{2}");
    }

    [Fact]
    public void UnsetOptionalGroup_ReplacesWithEmptyString()
    {
        Replace("b", @"(a)?(b)", @"[\1][\2]").Should().Be("[][b]");
    }

    [Fact]
    public void InvalidRegex_BuildFails()
    {
        var re = new Spcre("(unclosed");
        re.ReplaceText("x", new SpcreCapture[] { new(0, 1) }, "y", out string result)
            .Should().BeFalse();
        result.Should().BeEmpty();
    }
}
