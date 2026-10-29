using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

public sealed class CssDisplayApplicabilityTests
{
    [Theory]
    [InlineData("display", CssDisplayContext.Inline | CssDisplayContext.Block | CssDisplayContext.InlineBlock | CssDisplayContext.Flex | CssDisplayContext.Grid | CssDisplayContext.Table)]
    [InlineData("font-family", CssDisplayContext.Inline | CssDisplayContext.Block | CssDisplayContext.InlineBlock | CssDisplayContext.Flex | CssDisplayContext.Grid | CssDisplayContext.Table)]
    [InlineData("grid-template-columns", CssDisplayContext.Grid)]
    [InlineData("flex-grow", CssDisplayContext.Flex)]
    [InlineData("border-collapse", CssDisplayContext.Table)]
    [InlineData("text-align", CssDisplayContext.Block | CssDisplayContext.InlineBlock | CssDisplayContext.Flex | CssDisplayContext.Grid | CssDisplayContext.Table)]
    [InlineData("vertical-align", CssDisplayContext.Inline | CssDisplayContext.InlineBlock | CssDisplayContext.Table)]
    [InlineData("columns", CssDisplayContext.Block | CssDisplayContext.InlineBlock | CssDisplayContext.Flex | CssDisplayContext.Grid)]
    [InlineData("list-style-type", CssDisplayContext.Flex | CssDisplayContext.Grid)]
    [InlineData("ruby-position", CssDisplayContext.None)]
    [InlineData("-epub-ruby-position", CssDisplayContext.None)]
    [InlineData("--book-property", CssDisplayContext.None)]
    [InlineData("not-a-css-property", CssDisplayContext.None)]
    [InlineData("GRID-TEMPLATE-COLUMNS", CssDisplayContext.Grid)]
    public void ForProperty_KnownProperty_ReturnsExpectedDisplayContexts(string property, CssDisplayContext expected)
    {
        // Act
        CssDisplayContext actual = CssDisplayApplicability.ForProperty(property);

        // Assert
        actual.Should().Be(expected);
    }
}
