using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontFamilyList"/> — <c>font-family</c> lists and the <c>font</c> shorthand.</summary>
public sealed class FontFamilyListTests
{
    [Fact]
    public void Parse_reads_quoted_unquoted_and_generic_names_with_offsets()
    {
        const string value = "\"KCaecilia\", Book  Antiqua, 'A, B', serif !important";

        IReadOnlyList<FontFamilyEntry> entries = FontFamilyList.Parse(value, 0, value.Length);

        entries.Select(e => e.Name).Should().Equal("KCaecilia", "Book Antiqua", "A, B", "serif");
        entries.Select(e => e.IsGeneric).Should().Equal(false, false, false, true);
        entries.Select(e => value[e.Start..e.End]).Should().Equal("\"KCaecilia\"", "Book  Antiqua", "'A, B'", "serif");
    }

    [Theory]
    [InlineData("italic bold 12px/1.5 \"KCaecilia\", serif", "italic", "bold")]
    [InlineData("12px \"KCaecilia\", serif", "normal", "normal")]
    [InlineData("oblique 600 1em / 1.2 \"KCaecilia\", serif", "oblique", "600")]
    [InlineData("small-caps 700 large/ 2 \"KCaecilia\", serif", "normal", "700")]
    [InlineData("bold 1em /1.2 \"KCaecilia\", serif", "normal", "bold")]
    public void ParseShorthand_finds_the_family_list(string value, string style, string weight)
    {
        FontShorthand? shorthand = FontFamilyList.ParseShorthand(value, 0, value.Length);

        shorthand.Should().NotBeNull();
        shorthand!.Style.Should().Be(style);
        shorthand.Weight.Should().Be(weight);
        value[shorthand.FamilyStart..shorthand.FamilyEnd].Should().Be("\"KCaecilia\", serif");
    }

    [Theory]
    [InlineData("caption")]
    [InlineData("var(--body-font)")]
    [InlineData("bold")]
    public void ParseShorthand_gives_up_on_system_fonts_variables_and_values_without_a_size(string value)
    {
        FontFamilyList.ParseShorthand(value, 0, value.Length).Should().BeNull();
    }

    [Fact]
    public void Without_keeps_the_remaining_entries_as_written()
    {
        const string value = "\"KCaecilia\", 'Georgia', serif";
        IReadOnlyList<FontFamilyEntry> entries = FontFamilyList.Parse(value, 0, value.Length);

        FontFamilyList.Without(value, entries, e => e.Name == "KCaecilia").Should().Be("'Georgia', serif");
        FontFamilyList.Without(value, entries, _ => true).Should().BeEmpty();
    }

    [Fact]
    public void Quote_escapes_the_quote_character()
    {
        FontFamilyList.Quote("My \"Font\"").Should().Be("\"My \\\"Font\\\"\"");
        FontFamilyList.Quote("Font", '\'').Should().Be("'Font'");
    }
}
