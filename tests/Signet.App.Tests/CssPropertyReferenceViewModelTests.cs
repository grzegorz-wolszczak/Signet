using AwesomeAssertions;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

public sealed class CssPropertyReferenceViewModelTests
{
    [Fact]
    public void Constructor_UsesCommonEpubPropertiesSeparatedByInheritance()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();

        // Act
        bool showAll = viewModel.ShowAll;

        // Assert
        showAll.Should().BeFalse();
        viewModel.InheritedProperties.Should().Contain("font-family");
        viewModel.InheritedProperties.Should().NotContain("margin");
        viewModel.NonInheritedProperties.Should().Contain("margin");
        viewModel.NonInheritedProperties.Should().NotContain("font-family");
    }

    [Fact]
    public void ShowAll_Enabled_IncludesFullEpubPropertyCatalog()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();

        // Act
        viewModel.ShowAll = true;

        // Assert
        viewModel.InheritedProperties.Should().Contain("-epub-ruby-position");
        viewModel.InheritedProperties.Should().Contain("fill");
        viewModel.NonInheritedProperties.Should().Contain("grid-template-columns");
        viewModel.NonInheritedProperties.Should().Contain("-webkit-column-break-after");
        viewModel.NonInheritedProperties.Should().Contain("padding-bottom");
    }

    [Fact]
    public void ShowAll_Disabled_RestoresCommonEpubPropertyCatalog()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();
        viewModel.ShowAll = true;

        // Act
        viewModel.ShowAll = false;

        // Assert
        viewModel.InheritedProperties.Should().Contain("font-family");
        viewModel.NonInheritedProperties.Should().Contain("margin");
        viewModel.NonInheritedProperties.Should().NotContain("grid-template-columns");
    }
}
