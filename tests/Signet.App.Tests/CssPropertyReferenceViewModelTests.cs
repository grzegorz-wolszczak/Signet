using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
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

    [Fact]
    public void ShowAll_TooltipsCoverCatalogAndExplainCommonKeywordValues()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        CssPropertyReferenceViewModel viewModel = new();

        // Act
        viewModel.ShowAll = true;

        // Assert
        viewModel.InheritedProperties.Concat(viewModel.NonInheritedProperties)
            .Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.Tooltip.ToPlainText()));
        CssPropertyReferenceRow display = viewModel.NonInheritedProperties.Single(row => row.Name == "display");
        display.Tooltip.ToPlainText().Should().Contain("Controls box generation and participation in layout.");
        display.Tooltip.ToPlainText().Should().Contain("Keywords:");
        display.Tooltip.ToPlainText().Should().Contain("inline");
    }

    [Fact]
    public void OnLanguageChanged_RebuildsLocalizedPropertyTooltips()
    {
        // Arrange
        using UiCultureScope culture = new("pl");
        CssPropertyReferenceViewModel viewModel = new();
        string polishTooltip = viewModel.NonInheritedProperties.Single(row => row.Name == "display").Tooltip.ToPlainText();

        // Act
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        viewModel.OnLanguageChanged();

        // Assert
        string englishTooltip = viewModel.NonInheritedProperties.Single(row => row.Name == "display").Tooltip.ToPlainText();
        polishTooltip.Should().Contain("Określa sposób tworzenia pudełka");
        englishTooltip.Should().Contain("Controls box generation and participation in layout.");
        englishTooltip.Should().NotBe(polishTooltip);
    }

    [Theory]
    [InlineData("-epub-writing-mode", "Sets horizontal or vertical text flow")]
    [InlineData("-webkit-text-emphasis-style", "Shape of the emphasis marks")]
    [InlineData("adobe-hyphenate", "Adobe RMSDK")]
    [InlineData("max-width", "keeps images and tables within the page")]
    [InlineData("speak-as", "text-to-speech")]
    [InlineData("cue-before", "read aloud by text-to-speech")]
    public void ShowAll_EpubSpecificPropertiesHaveTheirOwnDescriptions(string property, string expected)
    {
        // Arrange
        using UiCultureScope culture = new("en");
        CssPropertyReferenceViewModel viewModel = new() { ShowAll = true };

        // Act
        CssPropertyReferenceRow row = viewModel.InheritedProperties.Concat(viewModel.NonInheritedProperties)
            .Single(r => r.Name == property);

        // Assert
        row.Tooltip.ToPlainText().Should().Contain(expected);
    }
}
