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
        // Act & Assert
        // 266 rows in the data, but the code "rce" appears twice (last wins).
        MarcRelators.GetCodeMap().Should().HaveCount(265);

        // Act & Assert
        MarcRelators.GetName("aut").Should().Be("Author");

        // Act & Assert
        MarcRelators.GetCode("Author").Should().Be("aut");

        // Act & Assert
        MarcRelators.GetName("edt").Should().Be("Editor");

        // Act & Assert
        MarcRelators.GetDescriptionByCode("aut").Should().Contain("intellectual or artistic content");

        // Act & Assert
        MarcRelators.GetDescriptionByName("Author").Should().Be(MarcRelators.GetDescriptionByCode("aut"));

        // Act & Assert
        MarcRelators.IsRelatorCode("ill").Should().BeTrue();

        // Act & Assert
        MarcRelators.IsRelatorName("Illustrator").Should().BeTrue();
    }

    [Fact]
    public void MarcRelators_unknown_lookups_return_empty()
    {
        // Act & Assert
        MarcRelators.GetName("zzz").Should().BeEmpty();

        // Act & Assert
        MarcRelators.GetCode("Not A Relator").Should().BeEmpty();

        // Act & Assert
        MarcRelators.IsRelatorCode("zzz").Should().BeFalse();

        // Act & Assert
        MarcRelators.GetSortedNames().Should().BeInAscendingOrder(StringComparer.CurrentCulture);
    }

    // -------------------------------------------------------------- Language //

    [Fact]
    public void Language_cardinality_and_bidirectional_lookup()
    {
        // Act & Assert
        Language.GetLangMap().Should().NotBeEmpty();

        // Act & Assert
        Language.GetLanguageName("en").Should().Be("English");

        // Act & Assert
        Language.GetLanguageName("fr").Should().Be("French");

        // Act & Assert
        Language.GetLanguageCode("English").Should().Be("en");

        // Act & Assert
        Language.GetLanguageName("ar-DZ").Should().Be("Arabic - Algeria");

        // Act & Assert
        Language.GetSortedPrimaryLanguageNames().Should().Contain("English").And.BeInAscendingOrder(StringComparer.CurrentCulture);
    }

    [Fact]
    public void Language_unknown_lookup_returns_overwrite_value()
    {
        // Act & Assert
        Language.GetLanguageName("zz").Should().BeEmpty();

        // Act & Assert
        Language.GetLanguageName("zz", "fallback").Should().Be("fallback");

        // Act & Assert
        Language.GetLanguageCode("Klingon", "und").Should().Be("und");
    }

    // ----------------------------------------------------------- XMLEntities //

    [Fact]
    public void XmlEntities_name_and_description_lookup()
    {
        // Act & Assert
        XmlEntities.GetEntityName(160).Should().Be("nbsp");

        // Act & Assert
        XmlEntities.GetEntityName(38).Should().Be("amp");

        // Act & Assert
        XmlEntities.GetEntityDescription(160).Should().Be("no-break space");

        // Act & Assert
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
        // Act & Assert
        XmlEntities.GetEntityCode(input).Should().Be((ushort)expected);
    }

    // ------------------------------------------------------------- AriaRoles //

    [Fact]
    public void AriaRoles_lookup_epubtype_and_allowed_tags()
    {
        // Act & Assert
        AriaRoles.GetCodeMap().Should().HaveCount(43);

        // Act & Assert
        AriaRoles.GetName("doc-toc").Should().Be("Table of Contents");

        // Act & Assert
        AriaRoles.GetCode("Table of Contents").Should().Be("doc-toc");

        // Act & Assert
        AriaRoles.GetTitle("doc-toc", "pl").Should().Be("Table of Contents");

        // Act & Assert
        AriaRoles.GetTitle("not-a-role", "pl").Should().Be("not-a-role");

        // Act & Assert
        AriaRoles.EpubTypeMapping("doc-cover").Should().Be("cover-image");

        // Act & Assert
        AriaRoles.EpubTypeMapping("doc-pagelist").Should().Be("page-list");

        // Act & Assert
        AriaRoles.EpubTypeMapping("doc-example").Should().BeEmpty();

        // Act & Assert
        string.Join(",", AriaRoles.AllowedTags("doc-toc")).Should().Be("section,nav,div");

        // Act & Assert
        string.Join(",", AriaRoles.AllowedTags("doc-noteref")).Should().Be("a");

        // Act & Assert
        string.Join(",", AriaRoles.AllowedTags("doc-cover")).Should().Be("img");

        // Act & Assert
        string.Join(",", AriaRoles.AllowedTags("doc-chapter")).Should().Be("section,div");
    }

    // ------------------------------------------------------------- AriaClips //

    [Fact]
    public void AriaClips_templates_and_placeholder_substitution()
    {
        // Act & Assert
        AriaClips.GetAllCodes().Should().HaveCount(17);

        // Act & Assert
        AriaClips.GetName("chapter").Should().Be("Section: Chapter");

        // Act & Assert
        AriaClips.GetCode("Section: Chapter").Should().Be("chapter");

        // Act & Assert
        AriaClips.GetDescriptionByCode("pagebreak_hr").Should().Be("<hr epub:type=\"pagebreak\" role=\"doc-pagebreak\" />\n");

        // Act & Assert
        AriaClips.GetTitle("chapter", "pl").Should().Be("Section: Chapter");

        // Act
        string translated = AriaClips.TranslatePlaceholders(
            AriaClips.GetDescriptionByCode("footnotes"), "pl");

        // Assert
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
        // Act & Assert
        CodepointNames.GetName(cp).Should().Be(expected);
    }

    [Fact]
    public void CodepointNames_unassigned_codepoint_is_unknown()
    {
        // Act & Assert
        CodepointNames.GetName(0x10FFFF).Should().Be("Unknown");
    }
}
