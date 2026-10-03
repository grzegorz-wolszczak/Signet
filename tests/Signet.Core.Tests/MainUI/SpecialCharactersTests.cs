using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Semantics;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of the data of the "Insert Special Character" dialog: the curated grid
/// (<see cref="SpecialCharacters"/>) and searching by Unicode name (<see cref="CodepointNames.Search"/>).
/// </summary>
public sealed class SpecialCharactersTests
{
    [Fact]
    public void Curated_grid_contains_the_em_dash_entry()
    {
        SpecialCharacterEntry emDash = SpecialCharacters.Entries.Single(e => e.Entity == "&mdash;");

        emDash.Insert.Should().Be("—");
        emDash.Group.Should().Be(SpecialCharacterGroup.Common);
    }

    [Fact]
    public void Curated_grid_covers_every_original_group()
    {
        SpecialCharacters.Entries.Select(e => e.Group).Distinct()
            .Should().BeEquivalentTo(new[]
            {
                SpecialCharacterGroup.Spaces,
                SpecialCharacterGroup.Common,
                SpecialCharacterGroup.Greek,
                SpecialCharacterGroup.Symbols,
            });
    }

    [Fact]
    public void DisplayText_falls_back_to_the_insert_text_and_unescapes_qt_ampersand()
    {
        SpecialCharacters.Entries.Single(e => e.Entity == "&rsquo;").DisplayText.Should().Be("’");
        SpecialCharacters.Entries.Single(e => e.Description == "ampersand").DisplayText.Should().Be("&");
    }

    [Fact]
    public void Search_finds_a_codepoint_by_words_in_its_name()
    {
        CodepointNames.Search("rightwards arrow").Select(r => r.Codepoint).Should().Contain(0x2192);
    }

    [Fact]
    public void Search_accepts_a_hex_or_u_plus_code()
    {
        CodepointNames.Search("U+2192").Select(r => r.Codepoint).Should().Contain(0x2192);
        CodepointNames.Search("2033").Select(r => r.Codepoint).Should().Contain(0x2033);
    }

    [Fact]
    public void Search_returns_nothing_for_a_blank_query()
    {
        CodepointNames.Search("   ").Should().BeEmpty();
    }
}
