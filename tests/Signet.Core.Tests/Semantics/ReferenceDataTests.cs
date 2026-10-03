using System;
using AwesomeAssertions;
using Signet.Core.Semantics;
using Xunit;
using Signet.Core.Tests.TestSupport;

namespace Signet.Core.Tests.Semantics;

/// <summary>
/// Spot checks of the built-in reference tables: <see cref="MarcRelators"/>,
/// <see cref="Language"/>, <see cref="XmlEntities"/>, <see cref="AriaRoles"/>,
/// <see cref="AriaClips"/>, <see cref="CodepointNames"/>.
/// </summary>
public sealed class ReferenceDataTests : EnglishUiCultureTest
{
    // ------------------------------------------------------------------ MARC //

    [Fact]
    public void MarcRelators_cardinality_and_bidirectional_lookup()
    {
        // 266 rows in the data, but the code "rce" appears twice (last wins).
        MarcRelators.GetCodeMap().Should().HaveCount(265);
        MarcRelators.GetName("aut").Should().Be("Author");
        MarcRelators.GetCode("Author").Should().Be("aut");
        MarcRelators.GetName("edt").Should().Be("Editor");
        MarcRelators.GetDescriptionByCode("aut").Should().Contain("intellectual or artistic content");
        MarcRelators.GetDescriptionByName("Author").Should().Be(MarcRelators.GetDescriptionByCode("aut"));
        MarcRelators.IsRelatorCode("ill").Should().BeTrue();
        MarcRelators.IsRelatorName("Illustrator").Should().BeTrue();
    }

    [Fact]
    public void MarcRelators_unknown_lookups_return_empty()
    {
        MarcRelators.GetName("zzz").Should().BeEmpty();
        MarcRelators.GetCode("Not A Relator").Should().BeEmpty();
        MarcRelators.IsRelatorCode("zzz").Should().BeFalse();
        MarcRelators.GetSortedNames().Should().BeInAscendingOrder(StringComparer.CurrentCulture);
    }

    // -------------------------------------------------------------- Language //

    [Fact]
    public void Language_cardinality_and_bidirectional_lookup()
    {
        Language.GetLangMap().Should().NotBeEmpty();
        Language.GetLanguageName("en").Should().Be("English");
        Language.GetLanguageName("fr").Should().Be("French");
        Language.GetLanguageCode("English").Should().Be("en");
        Language.GetLanguageName("ar-DZ").Should().Be("Arabic - Algeria");
        Language.GetSortedPrimaryLanguageNames().Should().Contain("English").And.BeInAscendingOrder(StringComparer.CurrentCulture);
    }

    [Fact]
    public void Language_unknown_lookup_returns_overwrite_value()
    {
        Language.GetLanguageName("zz").Should().BeEmpty();
        Language.GetLanguageName("zz", "fallback").Should().Be("fallback");
        Language.GetLanguageCode("Klingon", "und").Should().Be("und");
    }

    // ----------------------------------------------------------- XMLEntities //

    [Fact]
    public void XmlEntities_name_and_description_lookup()
    {
        XmlEntities.GetEntityName(160).Should().Be("nbsp");
        XmlEntities.GetEntityName(38).Should().Be("amp");
        XmlEntities.GetEntityDescription(160).Should().Be("no-break space");
        XmlEntities.GetAllCodes().Should().HaveCount(253);
    }

    [Theory]
    [InlineData("&nbsp;", 160)]
    [InlineData("&amp;", 38)]
    [InlineData("&#160;", 160)]
    [InlineData("&#xA0;", 160)]
    [InlineData("&#XA0;", 160)]
    [InlineData("nbsp", 0)]
    [InlineData("&notanentity;", 0)]
    [InlineData("&#xZZ;", 0)]
    public void XmlEntities_GetEntityCode_parses_named_and_numeric(string input, int expected)
    {
        XmlEntities.GetEntityCode(input).Should().Be((ushort)expected);
    }

    // ------------------------------------------------------------- AriaRoles //

    [Fact]
    public void AriaRoles_lookup_epubtype_and_allowed_tags()
    {
        AriaRoles.GetCodeMap().Should().HaveCount(43);
        AriaRoles.GetName("doc-toc").Should().Be("Table of Contents");
        AriaRoles.GetCode("Table of Contents").Should().Be("doc-toc");
        AriaRoles.GetTitle("doc-toc", "pl").Should().Be("Table of Contents");
        AriaRoles.GetTitle("not-a-role", "pl").Should().Be("not-a-role");
        AriaRoles.EpubTypeMapping("doc-cover").Should().Be("cover-image");
        AriaRoles.EpubTypeMapping("doc-pagelist").Should().Be("page-list");
        AriaRoles.EpubTypeMapping("doc-example").Should().BeEmpty();
        string.Join(",", AriaRoles.AllowedTags("doc-toc")).Should().Be("section,nav,div");
        string.Join(",", AriaRoles.AllowedTags("doc-noteref")).Should().Be("a");
        string.Join(",", AriaRoles.AllowedTags("doc-cover")).Should().Be("img");
        string.Join(",", AriaRoles.AllowedTags("doc-chapter")).Should().Be("section,div");
    }

    // ------------------------------------------------------------- AriaClips //

    [Fact]
    public void AriaClips_templates_and_placeholder_substitution()
    {
        AriaClips.GetAllCodes().Should().HaveCount(17);
        AriaClips.GetName("chapter").Should().Be("Section: Chapter");
        AriaClips.GetCode("Section: Chapter").Should().Be("chapter");
        AriaClips.GetDescriptionByCode("pagebreak_hr").Should().Be("<hr epub:type=\"pagebreak\" role=\"doc-pagebreak\" />\n");
        AriaClips.GetTitle("chapter", "pl").Should().Be("Section: Chapter");

        string translated = AriaClips.TranslatePlaceholders(
            AriaClips.GetDescriptionByCode("footnotes"), "pl");
        translated.Should().Contain("aria-label=\"Footnotes\"").And.NotContain("LABEL_FOR_FOOTNOTES");
    }

    // -------------------------------------------------------- CodepointNames //

    [Theory]
    [InlineData(-1, "EOF")]
    [InlineData(-5, "")]
    [InlineData(0, "NULL")]
    [InlineData(10, "NEW LINE")]
    [InlineData(0x0A0, "NO-BREAK SPACE")]
    [InlineData(0x41, "LATIN CAPITAL LETTER A")]
    [InlineData(0x105, "LATIN SMALL LETTER A WITH OGONEK")]
    [InlineData(0x1F600, "GRINNING FACE")]
    public void CodepointNames_GetName_matches_unicodedata(int cp, string expected)
    {
        CodepointNames.GetName(cp).Should().Be(expected);
    }

    [Fact]
    public void CodepointNames_unassigned_codepoint_is_unknown()
    {
        CodepointNames.GetName(0x10FFFF).Should().Be("Unknown");
    }
}
