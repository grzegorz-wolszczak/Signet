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

        SearchControls.Build(values).Should().Be("RX DA UN WR TO UP AH");
    }

    [Fact]
    public void Parse_EmptyString_ReturnsDefaults()
    {
        SearchControls.Parse(string.Empty).Should().Be(SearchControlValues.Defaults);
    }

    [Theory]
    [InlineData("NL DN CF", SearchMode.Normal, SearchDirection.Down, LookWhere.CurrentFile)]
    [InlineData("RX WR UP SX", SearchMode.Regex, SearchDirection.Up, LookWhere.SelectedMiscXmlFiles)]
    [InlineData("CS DN OP", SearchMode.CaseSensitive, SearchDirection.Down, LookWhere.OpfFile)]
    public void Parse_DecodesModeDirectionScope(
        string controls, SearchMode mode, SearchDirection direction, LookWhere lookWhere)
    {
        SearchControlValues values = SearchControls.Parse(controls);

        values.Mode.Should().Be(mode);
        values.Direction.Should().Be(direction);
        values.LookWhere.Should().Be(lookWhere);
    }

    [Fact]
    public void BuildThenParse_RoundTripsAllFlags()
    {
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

        SearchControls.Parse(SearchControls.Build(original)).Should().Be(original);
    }

    [Fact]
    public void ToSearchOptions_MapsRegexFlags()
    {
        var values = SearchControlValues.Defaults with { DotAll = true, TextOnly = true };

        SearchControls.ToSearchOptions(values)
            .Should().Be(new SearchOptions(DotAll: true, MinimalMatch: false, UnicodeProperty: false, TextOnly: true));
    }

    [Fact]
    public void BuildToolTip_ListsHumanReadableTokens()
    {
        string tip = SearchControls.BuildToolTip("RX DN AH");

        tip.Should().Contain("RX - ").And.Contain("DN - ").And.Contain("AH - ");
    }
}
