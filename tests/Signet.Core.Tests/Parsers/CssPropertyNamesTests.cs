using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

public sealed class CssPropertyNamesTests
{
    [Fact]
    public void Standard_names_are_lowercase_unprefixed_and_listed_once()
    {
        // Act
        string[] names = CssPropertyNames.StandardProperties.ToArray();

        // Assert
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => !n.Any(char.IsUpper) && !n.StartsWith('-'));
    }

    [Fact]
    public void Epub_vendor_names_are_prefixed_or_adobe_and_not_standard()
    {
        // Act
        string[] names = CssPropertyNames.EpubVendorProperties.ToArray();

        // Assert
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.StartsWith('-') || n.StartsWith("adobe-", System.StringComparison.Ordinal));
        names.Should().NotContain(n => CssPropertyNames.IsStandard(n));
    }

    [Theory]
    [InlineData("text-align")]
    [InlineData("-epub-writing-mode")]
    [InlineData("adobe-hyphenate")]
    [InlineData("src")]
    public void The_validator_knows_every_listed_name_and_the_descriptors(string name)
    {
        // Act & Assert
        CssPropertyValidator.IsKnownProperty(name).Should().BeTrue();
    }

    [Fact]
    public void The_validator_still_reports_a_misspelled_property()
    {
        // Act & Assert
        CssPropertyValidator.IsKnownProperty("text-algin").Should().BeFalse();
    }
}
