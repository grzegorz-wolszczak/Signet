using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class EpubUrlFragmentTests
{
    private const string Svg = "image/svg+xml";

    [Theory]
    [InlineData("t=١٢")] // ARABIC-INDIC digits: Java's \d is ASCII only
    [InlineData("xywh=١,2,3,4")]
    public void Parse_SvgMediaFragmentWithNonAsciiDigits_IsInvalid(string fragment)
    {
        // Act
        EpubUrlFragment parsed = EpubUrlFragment.Parse(fragment, Svg);

        // Assert
        parsed.IsMediaFragment.Should().BeTrue();
        parsed.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Parse_SvgMediaFragmentFollowedByANewLine_IsInvalid()
    {
        // Act (Java's matches() needs the whole string; .NET's "$" would accept a final new line)
        EpubUrlFragment parsed = EpubUrlFragment.Parse("t=1\n", Svg);

        // Assert
        parsed.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyXmlFragment_IsInvalid()
    {
        // Act
        EpubUrlFragment parsed = EpubUrlFragment.Parse(string.Empty, "application/xml");

        // Assert
        parsed.IsValid.Should().BeFalse();
        parsed.Exists.Should().BeTrue();
        parsed.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_SchemeNameWithNonAsciiLetters_IsAnId()
    {
        // Act (Java's \w is ASCII only, so this is not a scheme-based fragment)
        EpubUrlFragment parsed = EpubUrlFragment.Parse("élément(1)", "application/xhtml+xml");

        // Assert
        parsed.Scheme.Should().BeEmpty();
        parsed.Id.Should().Be("élément(1)");
    }
}
