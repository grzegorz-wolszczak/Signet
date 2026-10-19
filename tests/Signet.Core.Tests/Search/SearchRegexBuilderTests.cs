using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>Tests for <see cref="SearchRegexBuilder"/> — building the search regex from the Find &amp; Replace settings.</summary>
public sealed class SearchRegexBuilderTests
{
    [Fact]
    public void Normal_EscapesAndPrependsIgnoreCase()
    {
        // Act
        string result = SearchRegexBuilder.BuildSearchRegex(
            "a.b*c", SearchMode.Normal, SearchOptions.None, searchIsXml: false);

        // Assert
        result.Should().Be(@"(?i)a\.b\*c");
    }

    [Fact]
    public void CaseSensitive_EscapesWithoutIgnoreCase()
    {
        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex("(x)", SearchMode.CaseSensitive, SearchOptions.None, false)
            .Should().Be(@"\(x\)");
    }

    [Fact]
    public void Regex_PassesPatternThrough()
    {
        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex(@"(\d{4})", SearchMode.Regex, SearchOptions.None, false)
            .Should().Be(@"(\d{4})");
    }

    [Fact]
    public void Regex_DotAllMinimalUcp_PrependedInOrderWithUcpFirst()
    {
        // Arrange
        var options = new SearchOptions(DotAll: true, MinimalMatch: true, UnicodeProperty: true);

        // Act
        string result = SearchRegexBuilder.BuildSearchRegex("x+", SearchMode.Regex, options, false);

        // Assert
        result.Should().Be("(*UCP)(?U)(?s)x+");
    }

    [Fact]
    public void TextOnly_AppliedOnlyForXmlResources()
    {
        // Arrange
        var options = new SearchOptions(TextOnly: true);

        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex("word", SearchMode.Regex, options, searchIsXml: true)
            .Should().Be("<[^<>]*>(*SKIP)(*F)|word");

        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex("word", SearchMode.Regex, options, searchIsXml: false)
            .Should().Be("word");
    }

    [Fact]
    public void TextOnly_ComesBeforeIgnoreCaseInNormalMode()
    {
        // Arrange
        var options = new SearchOptions(TextOnly: true);

        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex("Word", SearchMode.Normal, options, searchIsXml: true)
            .Should().Be("(?i)<[^<>]*>(*SKIP)(*F)|Word");
    }

    [Fact]
    public void LineBreaksAreNormalizedToNewline()
    {
        // Act & Assert
        SearchRegexBuilder.BuildSearchRegex("a\r\nb", SearchMode.Regex, SearchOptions.None, false)
            .Should().Be("a\nb");
    }
}
