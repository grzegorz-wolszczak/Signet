using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

public sealed class EpubCssPropertyCatalogTests
{
    [Fact]
    public void Every_listed_property_is_known_to_the_validator()
    {
        // Act & Assert
        EpubCssPropertyCatalog.AllProperties.Should().OnlyContain(p => CssPropertyValidator.IsKnownProperty(p));
        EpubCssPropertyCatalog.CommonProperties.Should().OnlyContain(p => EpubCssPropertyCatalog.AllProperties.Contains(p));
    }

    [Theory]
    [InlineData("-epub-writing-mode")]
    [InlineData("-webkit-text-emphasis-style")]
    [InlineData("adobe-hyphenate")]
    [InlineData("font-stretch")]
    [InlineData("page-break-inside")]
    [InlineData("text-combine-upright")]
    [InlineData("speak-as")]
    public void All_lists_what_matters_in_an_ebook(string property)
    {
        // Act & Assert
        EpubCssPropertyCatalog.AllProperties.Should().Contain(property);
    }

    [Theory]
    [InlineData("animation-name")]
    [InlineData("transition")]
    [InlineData("scroll-snap-type")]
    [InlineData("cursor")]
    [InlineData("user-select")]
    [InlineData("container-type")]
    [InlineData("corner-shape")]
    [InlineData("window-drag")]
    public void All_leaves_out_web_only_and_experimental_properties(string property)
    {
        // Act & Assert
        EpubCssPropertyCatalog.AllProperties.Should().NotContain(property);
    }

    [Fact]
    public void All_is_sorted_and_without_duplicates()
    {
        // Act
        string[] all = EpubCssPropertyCatalog.AllProperties.ToArray();

        // Assert
        all.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder(System.StringComparer.OrdinalIgnoreCase);
    }
}
