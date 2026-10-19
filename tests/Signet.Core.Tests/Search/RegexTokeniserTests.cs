using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>Tests for <see cref="RegexTokeniser"/> — turning selected text into a regex.</summary>
public sealed class RegexTokeniserTests
{
    [Fact]
    public void TokeniseForRegex_PlainText_EscapesMetacharacters()
    {
        // Act
        string result = RegexTokeniser.TokeniseForRegex("a.b*c", includeNumerics: false);

        // Assert
        result.Should().Be(@"a\.b\*c");
    }

    [Fact]
    public void TokeniseForRegex_AlreadyEscaped_IsLeftUnchanged()
    {
        // Contains a backslash -> treated as already tokenised, not escaped again.
        // Act
        string result = RegexTokeniser.TokeniseForRegex(@"a\.b", includeNumerics: false);

        // Assert
        result.Should().Be(@"a\.b");
    }

    [Fact]
    public void TokeniseForRegex_RestoresReadablePunctuation()
    {
        // Act
        string result = RegexTokeniser.TokeniseForRegex("a<b>c/d;e:f&g=h", includeNumerics: false);

        // Assert
        result.Should().Be("a<b>c/d;e:f&g=h");
    }

    [Fact]
    public void TokeniseForRegex_CollapsesMultipleSpacesToWhitespaceClass()
    {
        // Act
        string result = RegexTokeniser.TokeniseForRegex("a   b", includeNumerics: false);

        // Assert
        result.Should().Be(@"a\s+b");
    }

    [Fact]
    public void TokeniseForRegex_IncludeNumerics_ReplacesDigitRuns()
    {
        // A single space is not a "run" (the threshold is 2+), so it stays literal.
        // Act
        string result = RegexTokeniser.TokeniseForRegex("item 42", includeNumerics: true);

        // Assert
        result.Should().Be(@"item \d+");
    }

    [Fact]
    public void TokeniseForRegex_ExcludeNumerics_KeepsDigitsLiteral()
    {
        // Act
        string result = RegexTokeniser.TokeniseForRegex("item 42", includeNumerics: false);

        // Assert
        result.Should().Be("item 42");
    }

    [Fact]
    public void TokeniseForRegex_EmptyText_ReturnsEmpty()
    {
        // Act & Assert
        RegexTokeniser.TokeniseForRegex(string.Empty, includeNumerics: false).Should().BeEmpty();
    }
}
