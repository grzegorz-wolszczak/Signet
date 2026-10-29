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
        viewModel.InheritedProperties.Should().Contain(property => property.Name == "font-family");
        viewModel.InheritedProperties.Should().NotContain(property => property.Name == "margin");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "margin");
        viewModel.NonInheritedProperties.Should().NotContain(property => property.Name == "font-family");
        viewModel.InheritedProperties.Should().ContainSingle(property => property.Name == "font-family")
            .Subject.Display.Should().Be("i, b, ib, f, g, t");
        viewModel.InheritedProperties.Should().ContainSingle(property => property.Name == "text-align")
            .Subject.Display.Should().Be("b, ib, f, g, t");
        viewModel.NonInheritedProperties.Should().ContainSingle(property => property.Name == "vertical-align")
            .Subject.Display.Should().Be("i, ib, t");
    }

    [Fact]
    public void ShowAll_Enabled_IncludesFullEpubPropertyCatalog()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new();

        // Act
        viewModel.ShowAll = true;

        // Assert
        viewModel.InheritedProperties.Should().Contain(property => property.Name == "-epub-ruby-position");
        viewModel.InheritedProperties.Should().Contain(property => property.Name == "fill");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "grid-template-columns");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "-webkit-column-break-after");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "padding-bottom");
        viewModel.NonInheritedProperties.Should().ContainSingle(property => property.Name == "grid-template-columns")
            .Subject.Display.Should().Be("g");
        viewModel.InheritedProperties.Should().ContainSingle(property => property.Name == "-epub-ruby-position")
            .Subject.Display.Should().Be("—");
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
        viewModel.InheritedProperties.Should().Contain(property => property.Name == "font-family");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "margin");
        viewModel.NonInheritedProperties.Should().NotContain(property => property.Name == "grid-template-columns");
    }

    [Fact]
    public void SearchText_FiltersBothListsByNameOrDisplayAndClearRestoresRows()
    {
        // Arrange
        CssPropertyReferenceViewModel viewModel = new() { ShowAll = true };

        // Act
        viewModel.SearchText = "f, g";

        // Assert
        viewModel.HasSearchText.Should().BeTrue();
        viewModel.InheritedProperties.Should().Contain(property => property.Name == "font-family");
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "margin");
        viewModel.InheritedProperties.Should().OnlyContain(property => property.Display.Contains("f, g"));
        viewModel.NonInheritedProperties.Should().OnlyContain(property => property.Display.Contains("f, g"));

        // Act
        viewModel.SearchText = "grid-template-columns";

        // Assert
        viewModel.InheritedProperties.Should().BeEmpty();
        viewModel.NonInheritedProperties.Should().ContainSingle(property => property.Name == "grid-template-columns");

        // Act
        viewModel.ClearSearchCommand.Execute(null);

        // Assert
        viewModel.SearchText.Should().BeEmpty();
        viewModel.HasSearchText.Should().BeFalse();
        viewModel.NonInheritedProperties.Should().Contain(property => property.Name == "margin");
    }
}
