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
        string result = RegexTokeniser.TokeniseForRegex("a.b*c", includeNumerics: false);

        result.Should().Be(@"a\.b\*c");
    }

    [Fact]
    public void TokeniseForRegex_AlreadyEscaped_IsLeftUnchanged()
    {
        // Contains a backslash -> treated as already tokenised, not escaped again.
        string result = RegexTokeniser.TokeniseForRegex(@"a\.b", includeNumerics: false);

        result.Should().Be(@"a\.b");
    }

    [Fact]
    public void TokeniseForRegex_RestoresReadablePunctuation()
    {
        string result = RegexTokeniser.TokeniseForRegex("a<b>c/d;e:f&g=h", includeNumerics: false);

        result.Should().Be("a<b>c/d;e:f&g=h");
    }

    [Fact]
    public void TokeniseForRegex_CollapsesMultipleSpacesToWhitespaceClass()
    {
        string result = RegexTokeniser.TokeniseForRegex("a   b", includeNumerics: false);

        result.Should().Be(@"a\s+b");
    }

    [Fact]
    public void TokeniseForRegex_IncludeNumerics_ReplacesDigitRuns()
    {
        // A single space is not a "run" (the threshold is 2+), so it stays literal.
        string result = RegexTokeniser.TokeniseForRegex("item 42", includeNumerics: true);

        result.Should().Be(@"item \d+");
    }

    [Fact]
    public void TokeniseForRegex_ExcludeNumerics_KeepsDigitsLiteral()
    {
        string result = RegexTokeniser.TokeniseForRegex("item 42", includeNumerics: false);

        result.Should().Be("item 42");
    }

    [Fact]
    public void TokeniseForRegex_EmptyText_ReturnsEmpty()
    {
        RegexTokeniser.TokeniseForRegex(string.Empty, includeNumerics: false).Should().BeEmpty();
    }
}
