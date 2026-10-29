using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="SearchControls"/> — encoding/decoding the "Controls" string.</summary>
public sealed class SearchControlsTests
{
    [Fact]
    public void Build_ProducesTokensInOriginalOrder()
    {
        // Arrange
        var values = new SearchControlValues(
            SearchMode.Regex,
            SearchDirection.Up,
            LookWhere.AllHtmlFiles,
            Wrap: true,
            DotAll: true,
            MinimalMatch: false,
            AutoTokenise: false,
            UnicodeProperty: true,
            TextOnly: true);

        // Act & Assert
        SearchControls.Build(values).Should().Be("RX DA UN WR TO UP AH");
    }

    [Fact]
    public void Parse_EmptyString_ReturnsDefaults()
    {
        // Act & Assert
        SearchControls.Parse(string.Empty).Should().Be(SearchControlValues.Defaults);
    }

    [Theory]
    [InlineData("NL DN CF", SearchMode.Normal, SearchDirection.Down, LookWhere.CurrentFile)]
    [InlineData("RX WR UP SX", SearchMode.Regex, SearchDirection.Up, LookWhere.SelectedMiscXmlFiles)]
    [InlineData("CS DN OP", SearchMode.CaseSensitive, SearchDirection.Down, LookWhere.OpfFile)]
    public void Parse_DecodesModeDirectionScope(
        string controls, SearchMode mode, SearchDirection direction, LookWhere lookWhere)
    {
        // Act
        SearchControlValues values = SearchControls.Parse(controls);

        // Assert
        values.Mode.Should().Be(mode);
        values.Direction.Should().Be(direction);
        values.LookWhere.Should().Be(lookWhere);
    }

    [Fact]
    public void BuildThenParse_RoundTripsAllFlags()
    {
        // Arrange
        var original = new SearchControlValues(
            SearchMode.CaseSensitive,
            SearchDirection.Down,
            LookWhere.TabbedCssFiles,
            Wrap: false,
            DotAll: true,
            MinimalMatch: true,
            AutoTokenise: true,
            UnicodeProperty: true,
            TextOnly: false);

        // Act & Assert
        SearchControls.Parse(SearchControls.Build(original)).Should().Be(original);
    }

    [Fact]
    public void BuildThenParse_AllFilesScope_RoundTrips()
    {
        // Arrange
        SearchControlValues original = SearchControlValues.Defaults with { LookWhere = LookWhere.AllFiles };

        // Act
        SearchControlValues parsed = SearchControls.Parse(SearchControls.Build(original));

        // Assert
        parsed.Should().Be(original);
    }

    [Fact]
    public void ToSearchOptions_MapsRegexFlags()
    {
        // Arrange
        var values = SearchControlValues.Defaults with { DotAll = true, TextOnly = true };

        // Act & Assert
        SearchControls.ToSearchOptions(values)
            .Should().Be(new SearchOptions(DotAll: true, MinimalMatch: false, UnicodeProperty: false, TextOnly: true));
    }

    [Fact]
    public void BuildToolTip_ListsHumanReadableTokens()
    {
        // Act
        string tip = SearchControls.BuildToolTip("RX DN AH");

        // Assert
        tip.Should().Contain("RX - ").And.Contain("DN - ").And.Contain("AH - ");
    }
}
