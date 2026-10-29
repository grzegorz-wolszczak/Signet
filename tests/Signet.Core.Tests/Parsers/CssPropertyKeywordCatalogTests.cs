using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

public sealed class CssPropertyKeywordCatalogTests
{
    [Theory]
    [InlineData("border-style", "none, hidden, dotted, dashed, solid, double, groove, ridge, inset, outset")]
    [InlineData("font-style", "normal, italic, oblique")]
    [InlineData("position", "static, relative, absolute, fixed, sticky")]
    public void ForProperty_KnownFiniteKeywords_ReturnsExpectedValues(string property, string expected)
    {
        // Act
        string? actual = CssPropertyKeywordCatalog.ForProperty(property);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ForProperty_Display_ReturnsLayoutKeywords()
    {
        // Act
        string? actual = CssPropertyKeywordCatalog.ForProperty("display");

        // Assert
        actual.Should().Contain("block");
        actual.Should().Contain("inline");
        actual.Should().Contain("grid");
        actual.Should().Contain("none");
    }

    [Fact]
    public void ForProperty_EpubPrefixedProperty_ReturnsItsKeywordValues()
    {
        // Act
        string? actual = CssPropertyKeywordCatalog.ForProperty("-epub-ruby-position");

        // Assert
        actual.Should().Be("over, under, inter-character");
    }

    [Fact]
    public void ForProperty_AdobeHyphenate_ReturnsItsKeywordValues()
    {
        // Act
        string? actual = CssPropertyKeywordCatalog.ForProperty("adobe-hyphenate");

        // Assert
        actual.Should().Be("none, explicit, auto");
    }

    [Fact]
    public void ForProperty_UnknownProperty_ReturnsNull()
    {
        // Act & Assert
        CssPropertyKeywordCatalog.ForProperty("--book-custom-property").Should().BeNull();
    }
}
