using AwesomeAssertions;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

public sealed class StepExpressionTests
{
    private static StepExpression Parse(string expression) => StepExpression.Parse(expression, new ParameterTypeRegistry());

    [Theory]
    [InlineData("no other errors or warnings are reported")]
    [InlineData("no errors or warnings are reported")]
    [InlineData("no warnings or errors are reported")]
    [InlineData("no other warning or error is reported")]
    public void Alternatives_And_OptionalParts_AreUnderstood(string text)
    {
        // Arrange
        StepExpression expression = Parse("no( other) warning(s)/error(s) or error(s)/warning(s) are/is reported");

        // Act & Assert
        expression.TryMatch(text, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("checking EPUB 'a'")]
    [InlineData("checking file 'a'")]
    [InlineData("checking document 'a'")]
    [InlineData("checking directory 'a'")]
    [InlineData("checking file set 'a'")]
    public void AnEscapedSpace_StaysInsideAnAlternative(string text)
    {
        // Arrange
        StepExpression expression = Parse("checking EPUB/file/document/directory/file\\ set {string}");

        // Act
        bool matched = expression.TryMatch(text, out object?[] values);

        // Assert
        matched.Should().BeTrue();
        values.Should().Equal("a");
    }

    [Theory]
    [InlineData("checking file name 'a'")]
    [InlineData("checking the file 'a'")]
    [InlineData("checking EPUB a")]
    public void ATextThatIsNotTheWholeExpression_DoesNotMatch(string text)
    {
        // Arrange
        StepExpression expression = Parse("checking EPUB/file/document/directory/file\\ set {string}");

        // Act & Assert
        expression.TryMatch(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("the error OPF-014 is reported", "Error", "OPF_014", 1)]
    [InlineData("fatal error RSC-016 is reported", "Fatal", "RSC_016", 1)]
    [InlineData("warning OPF-004a is reported 3 times", "Warning", "OPF_004a", 3)]
    [InlineData("usage HTM-060b is reported 12 times (and more)", "Usage", "HTM_060b", 12)]
    public void TheMessageStep_GivesSeverityIdAndQuantity(string text, string severity, string id, int quantity)
    {
        // Arrange
        StepExpression expression = Parse("(the ){severity} {messageId} is reported{messageQuantity}(  \\(){}");

        // Act
        bool matched = expression.TryMatch(text, out object?[] values);

        // Assert
        matched.Should().BeTrue();
        values[0]!.ToString().Should().Be(severity);
        values[1]!.ToString().Should().Be(id);
        values[2].Should().Be(quantity);
    }

    [Theory]
    [InlineData("it says 'don\\'t'", "don't")]
    [InlineData("it says \"quoted\"", "quoted")]
    public void AStringParameter_LosesItsQuotes_AndEscapes(string text, string expected)
    {
        // Arrange
        StepExpression expression = Parse("it says {string}");

        // Act
        bool matched = expression.TryMatch(text, out object?[] values);

        // Assert
        matched.Should().BeTrue();
        values.Should().Equal(expected);
    }

    [Fact]
    public void AnAnonymousParameter_TakesTheRest()
    {
        // Arrange
        StepExpression expression = Parse("running `{}`");

        // Act
        bool matched = expression.TryMatch("running `epubcheck a.epub -q`", out object?[] values);

        // Assert
        matched.Should().BeTrue();
        values.Should().Equal("epubcheck a.epub -q");
    }

    [Fact]
    public void ACaseInsensitiveParameter_MatchesAnyLetterCase()
    {
        // Arrange
        StepExpression expression = Parse("EPUBCheck configured to check a(n) {checkerMode}");

        // Act & Assert
        expression.TryMatch("EPUBCheck configured to check a Navigation Document", out _).Should().BeTrue();
        expression.TryMatch("EPUBCheck configured to check an XHTML Content DOCUMENT", out _).Should().BeTrue();
        expression.TryMatch("EPUBCheck configured to check a Package", out _).Should().BeFalse();
    }
}
